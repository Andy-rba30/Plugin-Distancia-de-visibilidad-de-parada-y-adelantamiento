using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(ArbaMcp.Aplicacion))]
[assembly: CommandClass(typeof(ArbaMcp.Comandos))]

namespace ArbaMcp
{
    /// <summary>
    /// Conector entre Civil 3D y un agente de IA (puente MCP). Al cargarse arranca el servidor local
    /// y agrega el botón "Conexión IA" en la pestaña ARBA de la cinta.
    /// </summary>
    public class Aplicacion : IExtensionApplication
    {
        public void Initialize()
        {
            try { Servidor.Iniciar(); } catch { }

            if (ComponentManager.Ribbon != null)
                CrearBoton();
            else
            {
                ComponentManager.ItemInitialized += AlInicializarCinta;
                AcApp.Idle += AlEstarInactivo;
            }

            AcApp.SystemVariableChanged += (s, e) =>
            {
                if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase)) CrearBoton();
            };
        }

        public void Terminate()
        {
            try { Servidor.Detener(); } catch { }
        }

        private static void AlInicializarCinta(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= AlInicializarCinta;
            AcApp.Idle -= AlEstarInactivo;
            CrearBoton();
        }

        private static void AlEstarInactivo(object sender, EventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            AcApp.Idle -= AlEstarInactivo;
            ComponentManager.ItemInitialized -= AlInicializarCinta;
            CrearBoton();
        }

        private static void CrearBoton()
        {
            try
            {
                var panel = CintaArba.ObtenerPanel("ARBA_IA", "IA");
                CintaArba.AgregarBoton(panel, "ARBA_BTN_MCP", "Conexión\nIA", "ARBAMCP",
                    "Muestra el estado del servidor local para el puente MCP (puerto 8765) y lo reinicia si está caído.",
                    Color.FromRgb(0x2E, 0x7D, 0x32), "IA");
            }
            catch { }
        }
    }

    public class Comandos
    {
        /// <summary>Estado del servidor local; lo reinicia si está caído.</summary>
        [CommandMethod("ARBAMCP")]
        public void ArbaMcpEstado()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc?.Editor;
            if (!Servidor.Activo)
            {
                ed?.WriteMessage("\nServidor MCP: INACTIVO. " + Servidor.UltimoError + "\nSe intenta iniciar de nuevo...");
                Servidor.Iniciar();
            }
            if (Servidor.Activo)
            {
                ed?.WriteMessage("\nServidor MCP activo en http://127.0.0.1:" + Servidor.Puerto + "/");
                ed?.WriteMessage("\nPrueba desde PowerShell:  curl.exe http://127.0.0.1:" + Servidor.Puerto + "/tools");
            }
            else ed?.WriteMessage("\nNo se pudo iniciar: " + Servidor.UltimoError);
            if (ed != null) foreach (var l in Historial.Ultimas(10)) ed.WriteMessage("\n  " + l);

            string estado = Servidor.Activo
                ? "Conectado. El servidor local para la IA está activo en\nhttp://127.0.0.1:" + Servidor.Puerto + "/\n\nYa puedes usar el agente con Civil 3D (el puente MCP debe estar en marcha)."
                : "No se pudo iniciar el servidor local:\n" + Servidor.UltimoError + "\n\n¿Otro programa usa el puerto " + Servidor.Puerto + "? Cámbialo con la variable de entorno ARBA_MCP_PORT.";
            AcApp.ShowAlertDialog(estado);
        }
    }

    /// <summary>
    /// Pestaña ARBA de la cinta, compartida entre plugins: se busca por Id antes de crearla,
    /// así cada plugin agrega su panel sin duplicar la pestaña.
    /// </summary>
    public static class CintaArba
    {
        public const string IdPestana = "ARBA_PESTANA";
        public const string TituloPestana = "ARBA";

        public static RibbonTab ObtenerPestana()
        {
            var cinta = ComponentManager.Ribbon;
            if (cinta == null) return null;
            foreach (var t in cinta.Tabs)
                if (t.Id == IdPestana || string.Equals(t.Title, TituloPestana, StringComparison.OrdinalIgnoreCase))
                    return t;
            var pestana = new RibbonTab { Id = IdPestana, Title = TituloPestana, Name = TituloPestana, IsVisible = true };
            cinta.Tabs.Add(pestana);
            return pestana;
        }

        public static RibbonPanel ObtenerPanel(string id, string titulo)
        {
            var pestana = ObtenerPestana();
            if (pestana == null) return null;
            foreach (var p in pestana.Panels)
                if (p.Source != null && p.Source.Id == id) return p;
            var panel = new RibbonPanel { Source = new RibbonPanelSource { Id = id, Title = titulo, Name = titulo } };
            pestana.Panels.Add(panel);
            return panel;
        }

        public static RibbonButton AgregarBoton(RibbonPanel panel, string id, string texto, string comando,
                                                string descripcion, Color colorIcono, string letrasIcono)
        {
            if (panel == null) return null;
            foreach (var item in panel.Source.Items)
                if (item is RibbonButton existente && existente.Id == id) return existente;

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
            boton.ToolTip = new RibbonToolTip { Title = texto.Replace("\n", " "), Command = comando, Content = descripcion, IsHelpEnabled = false };
            panel.Source.Items.Add(boton);
            return boton;
        }

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
