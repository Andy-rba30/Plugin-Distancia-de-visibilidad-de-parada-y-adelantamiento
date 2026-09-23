using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(VisibilidadParada.Civil.Aplicacion))]

namespace VisibilidadParada.Civil
{
    /// <summary>
    /// Punto de entrada del plugin. Al cargarse la DLL (NETLOAD o carga automática)
    /// crea la pestaña ARBA en la cinta y agrega los botones de los comandos.
    /// </summary>
    public class Aplicacion : IExtensionApplication
    {
        public void Initialize()
        {
            // Si la cinta ya existe (NETLOAD manual), se crea de inmediato.
            // Si el plugin se carga al arrancar Civil 3D, hay que esperar a que la cinta se inicialice.
            if (ComponentManager.Ribbon != null)
                CintaArba.CrearBotonesVisibilidad();
            else
            {
                ComponentManager.ItemInitialized += AlInicializarCinta;
                AcApp.Idle += AlEstarInactivo;
            }

            // Servidor local para el puente MCP (127.0.0.1, puerto ARBA_MCP_PORT o 8765)
            try { VisibilidadParada.Mcp.Servidor.Iniciar(); } catch { }

            // Al cambiar de espacio de trabajo la cinta se reconstruye: se vuelve a crear la pestaña.
            AcApp.SystemVariableChanged += (s, e) =>
            {
                if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase))
                    CintaArba.CrearBotonesVisibilidad();
            };
        }

        public void Terminate()
        {
            try { VisibilidadParada.Mcp.Servidor.Detener(); } catch { }
        }

        private static void AlInicializarCinta(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= AlInicializarCinta;
            AcApp.Idle -= AlEstarInactivo;
            CintaArba.CrearBotonesVisibilidad();
        }

        private static void AlEstarInactivo(object sender, EventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            AcApp.Idle -= AlEstarInactivo;
            ComponentManager.ItemInitialized -= AlInicializarCinta;
            CintaArba.CrearBotonesVisibilidad();
        }
    }

    /// <summary>
    /// Pestaña ARBA de la cinta. Es compartida: cualquier otro plugin puede llamar a
    /// ObtenerPestana() / ObtenerPanel() / AgregarBoton() para colocar sus comandos aquí.
    /// Todas las operaciones son idempotentes (no duplican pestañas, paneles ni botones).
    /// </summary>
    public static class CintaArba
    {
        public const string IdPestana = "ARBA_PESTANA";
        public const string TituloPestana = "ARBA";

        private static readonly Color ColorVisibilidad = Color.FromRgb(0x1F, 0x4E, 0x79);

        /// <summary>Devuelve la pestaña ARBA, creándola si no existe. Null si la cinta aún no está lista.</summary>
        public static RibbonTab ObtenerPestana()
        {
            var cinta = ComponentManager.Ribbon;
            if (cinta == null) return null;

            foreach (var t in cinta.Tabs)
                if (t.Id == IdPestana || string.Equals(t.Title, TituloPestana, StringComparison.OrdinalIgnoreCase))
                    return t;

            // Se agrega al final de la cinta y no se activa: las pestañas propias de Civil 3D
            // son las de uso diario; ARBA es para herramientas puntuales.
            var pestana = new RibbonTab { Id = IdPestana, Title = TituloPestana, Name = TituloPestana, IsVisible = true };
            cinta.Tabs.Add(pestana);
            return pestana;
        }

        /// <summary>Devuelve un panel de la pestaña ARBA por Id, creándolo si no existe.</summary>
        public static RibbonPanel ObtenerPanel(string id, string titulo)
        {
            var pestana = ObtenerPestana();
            if (pestana == null) return null;

            foreach (var p in pestana.Panels)
                if (p.Source != null && p.Source.Id == id)
                    return p;

            var panel = new RibbonPanel { Source = new RibbonPanelSource { Id = id, Title = titulo, Name = titulo } };
            pestana.Panels.Add(panel);
            return panel;
        }

        /// <summary>
        /// Agrega un botón grande que ejecuta un comando de AutoCAD. Si ya existe un botón con ese Id, lo devuelve.
        /// </summary>
        public static RibbonButton AgregarBoton(RibbonPanel panel, string id, string texto, string comando,
                                                string descripcion, Color colorIcono, string letrasIcono)
        {
            if (panel == null) return null;

            foreach (var item in panel.Source.Items)
                if (item is RibbonButton existente && existente.Id == id)
                    return existente;

            var boton = new RibbonButton
            {
                Id = id,
                Name = texto.Replace("\n", " "),
                Text = texto,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                Image = Icono(letrasIcono, colorIcono, 16),
                LargeImage = Icono(letrasIcono, colorIcono, 32),
                CommandParameter = comando,
                CommandHandler = new ComandoCinta(comando),
            };

            boton.ToolTip = new RibbonToolTip
            {
                Title = texto.Replace("\n", " "),
                Command = comando,
                Content = descripcion,
                IsHelpEnabled = false,
            };

            panel.Source.Items.Add(boton);
            return boton;
        }

        /// <summary>Crea el panel "Visibilidad" con los botones de este plugin.</summary>
        internal static void CrearBotonesVisibilidad()
        {
            try
            {
                var panel = ObtenerPanel("ARBA_VISIBILIDAD", "Visibilidad");
                if (panel == null) return;

                AgregarBoton(panel, "ARBA_BTN_VISIBILIDAD", "Visibilidad\nde parada", "VISIBILIDAD",
                    "Verifica las curvas verticales del perfil por visibilidad de parada y adelantamiento, y muestra la tabla de CUMPLE / NO CUMPLE.",
                    ColorVisibilidad, "Dp");
            }
            catch (System.Exception ex)
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc != null) doc.Editor.WriteMessage("\nVisibilidadParada: no se pudo crear la pestaña ARBA: " + ex.Message);
            }
        }

        /// <summary>Icono generado en tiempo de ejecución: cuadro redondeado con dos letras.</summary>
        private static BitmapSource Icono(string letras, Color fondo, int tam)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(fondo), null, new Rect(0, 0, tam, tam), tam / 6.0, tam / 6.0);
                var texto = new FormattedText(letras, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    tam * 0.45, Brushes.White, 1.0);
                dc.DrawText(texto, new Point((tam - texto.Width) / 2.0, (tam - texto.Height) / 2.0));
            }
            var bmp = new RenderTargetBitmap(tam, tam, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
    }

    /// <summary>Envía el comando a la línea de comandos del documento activo.</summary>
    internal class ComandoCinta : ICommand
    {
        private readonly string _comando;
        public ComandoCinta(string comando) { _comando = comando; }

        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            doc.SendStringToExecute("_." + _comando + " ", true, false, true);
        }
    }
}
