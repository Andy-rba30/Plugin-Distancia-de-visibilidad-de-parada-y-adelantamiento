using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.ApplicationServices;
using VisibilidadParada.Nucleo;
using CivAlignment = Autodesk.Civil.DatabaseServices.Alignment;
using CivProfile = Autodesk.Civil.DatabaseServices.Profile;
using CivSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace VisibilidadParada.Civil
{
    /// <summary>Elemento de las listas desplegables (alineamientos, perfiles, superficies).</summary>
    public class ObjetoDibujo
    {
        public ObjectId Id { get; set; }
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public override string ToString() => Nombre;
    }

    /// <summary>Fila de la tabla de curvas verticales.</summary>
    public class FilaCurva
    {
        public int N { get; set; }
        public string Ubicacion { get; set; }
        public string Tipo { get; set; }
        public string V { get; set; }
        public string Pe { get; set; }
        public string Ps { get; set; }
        public string A { get; set; }
        public string Dp { get; set; }
        public string LReq { get; set; }
        public string LProyecto { get; set; }
        public string KReq { get; set; }
        public string KProyecto { get; set; }
        public string Estado { get; set; }
        public string Da { get; set; }
        public string LDaReq { get; set; }
        public string Adelanta { get; set; }
        public string Verificacion { get; set; }
        public bool Cumple { get; set; }
    }

    /// <summary>Fila de la tabla de sectores deficientes (solo con superficie).</summary>
    public class FilaSector
    {
        public string Sentido { get; set; }
        public string Inicio { get; set; }
        public string Fin { get; set; }
        public string Longitud { get; set; }
        public string DvpMax { get; set; }
        public string DisponibleMin { get; set; }
        public string DeficitMax { get; set; }
    }

    public partial class VentanaVisibilidad : Window
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly Document _doc;
        private bool _cancelar;
        private bool _ocupado;
        private ResultadoEjecucion _ultimo;
        private List<TramoVelocidad> _tramos = new List<TramoVelocidad>();

        public VentanaVisibilidad(Document doc)
        {
            InitializeComponent();
            _doc = doc;
            ValoresPorDefecto();
            CargarAlineamientos();
            CargarSuperficies();
            RutaPorDefecto();
        }

        // ------------------------------------------------------------------ valores de la norma
        // Tabla 205.03 DG-2018: distancia de visibilidad de adelantamiento (m) según velocidad de diseño (km/h)
        private static readonly double[] TablaV = { 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130 };
        private static readonly double[] TablaDa = { 130, 200, 270, 345, 410, 485, 540, 615, 670, 730, 775, 815 };

        /// <summary>Da de la Tabla 205.03 para la velocidad dada (interpola entre valores tabulados).</summary>
        public static double DaSegunNorma(double v)
        {
            if (v <= TablaV[0]) return TablaDa[0];
            int n = TablaV.Length;
            if (v >= TablaV[n - 1]) return TablaDa[n - 1];
            for (int i = 1; i < n; i++)
                if (v <= TablaV[i])
                {
                    double t = (v - TablaV[i - 1]) / (TablaV[i] - TablaV[i - 1]);
                    return TablaDa[i - 1] + t * (TablaDa[i] - TablaDa[i - 1]);
                }
            return TablaDa[n - 1];
        }

        /// <summary>Longitud mínima absoluta de curva vertical de referencia: 0.6·V (AASHTO), en metros.</summary>
        public static double LMinimaSegunNorma(double v) => Math.Ceiling(0.6 * v);

        private void SugerirSegunV(double v)
        {
            txtUmbralA.Text = "1";
            txtLMin.Text = Num(LMinimaSegunNorma(v));
            txtDa.Text = Num(Math.Round(DaSegunNorma(v)));
        }

        private void Sugerir_Click(object sender, RoutedEventArgs e)
        {
            if (!Leer(txtV, "Velocidad de diseño", out double v)) return;
            SugerirSegunV(v);
            txtEstado.Text = "Para V = " + Num(v) + " km/h: A ≥ 1 % (pavimentada), L mínima = 0.6·V = " + txtLMin.Text +
                             " m, Da = " + txtDa.Text + " m (Tabla 205.03). Ajusta según tu tipo de vía.";
        }

        // ------------------------------------------------------------------ carga inicial
        private void ValoresPorDefecto()
        {
            var p = new Parametros();
            txtV.Text = "60";
            txtTp.Text = Num(p.TiempoPercepcion);
            txtA.Text = Num(p.Desaceleracion);
            txtOjo.Text = Num(p.AlturaOjo);
            txtObjeto.Text = Num(p.AlturaObjeto);
            SugerirSegunV(60);
            txtObjetoDa.Text = Num(p.AlturaObjetoAdelanto);
            txtDesfC.Text = Num(p.DesfaseCreciente);
            txtDesfD.Text = Num(p.DesfaseDecreciente);
            txtIntervalo.Text = Num(p.Intervalo);
        }

        private void CargarAlineamientos()
        {
            var lista = new List<ObjetoDibujo>();
            var db = _doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in CivilApplication.ActiveDocument.GetAlignmentIds())
                    if (tr.GetObject(id, OpenMode.ForRead) is CivAlignment al)
                        lista.Add(new ObjetoDibujo { Id = id, Nombre = al.Name, Tipo = "Alineamiento" });
                tr.Commit();
            }
            lista.Sort((a, b) => string.Compare(a.Nombre, b.Nombre, StringComparison.CurrentCultureIgnoreCase));
            cbAlineamiento.ItemsSource = lista;
            if (lista.Count == 1) cbAlineamiento.SelectedIndex = 0;
            if (lista.Count == 0) txtEstado.Text = "El dibujo no tiene alineamientos.";
        }

        private void CargarPerfiles(ObjectId idAl, ObjectId seleccionar)
        {
            var lista = new List<ObjetoDibujo>();
            if (!idAl.IsNull)
            {
                var db = _doc.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var al = (CivAlignment)tr.GetObject(idAl, OpenMode.ForRead);
                    foreach (ObjectId id in al.GetProfileIds())
                        if (tr.GetObject(id, OpenMode.ForRead) is CivProfile pr)
                            lista.Add(new ObjetoDibujo { Id = id, Nombre = pr.Name + "  (" + pr.ProfileType + ")", Tipo = pr.ProfileType.ToString() });
                    tr.Commit();
                }
            }
            cbPerfil.ItemsSource = lista;
            if (!seleccionar.IsNull)
                cbPerfil.SelectedItem = lista.FirstOrDefault(x => x.Id == seleccionar);
            else if (lista.Count > 0)
            {
                // Se prefiere el primer perfil que no sea de terreno (EG)
                var fg = lista.FirstOrDefault(x => !x.Tipo.StartsWith("EG", StringComparison.OrdinalIgnoreCase));
                cbPerfil.SelectedItem = fg ?? lista[0];
            }
        }

        private void CargarSuperficies()
        {
            var lista = new List<ObjetoDibujo>();
            var db = _doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in CivilApplication.ActiveDocument.GetSurfaceIds())
                    if (tr.GetObject(id, OpenMode.ForRead) is CivSurface su)
                        lista.Add(new ObjetoDibujo { Id = id, Nombre = su.Name, Tipo = "Superficie" });
                tr.Commit();
            }
            lista.Sort((a, b) => string.Compare(a.Nombre, b.Nombre, StringComparison.CurrentCultureIgnoreCase));
            cbSuperficie.ItemsSource = lista;
        }

        private void RutaPorDefecto()
        {
            string dir = "";
            try { dir = Path.GetDirectoryName(_doc.Database.Filename) ?? ""; } catch { }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            txtRuta.Text = Path.Combine(dir, "Informe_Visibilidad.html");
        }

        private void ActualizarRango()
        {
            if (cbAlineamiento == null || cbPerfil == null || txtInicio == null) return;
            if (!(cbAlineamiento.SelectedItem is ObjetoDibujo a) || !(cbPerfil.SelectedItem is ObjetoDibujo p)) return;
            try
            {
                var db = _doc.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var al = (CivAlignment)tr.GetObject(a.Id, OpenMode.ForRead);
                    var pr = (CivProfile)tr.GetObject(p.Id, OpenMode.ForRead);
                    double sMin = Math.Max(al.StartingStation, pr.StartingStation);
                    double sMax = Math.Min(al.EndingStation, pr.EndingStation);
                    txtInicio.Text = sMin.ToString("0.00", Inv);
                    txtFin.Text = sMax.ToString("0.00", Inv);
                    txtEstado.Text = "Perfil " + pr.Name + ": " + Formato.Prog(sMin) + " – " + Formato.Prog(sMax);
                    tr.Commit();
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------ eventos de selección
        // Nota: los manejadores enlazados desde el XAML pueden dispararse durante InitializeComponent,
        // antes de que existan los controles declarados más abajo. Por eso comprueban null.
        private void Alineamiento_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (cbPerfil == null) return;
            var idAl = cbAlineamiento.SelectedItem is ObjetoDibujo o ? o.Id : ObjectId.Null;
            CargarPerfiles(idAl, ObjectId.Null);
        }

        private void Perfil_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (txtInicio == null || txtFin == null || txtEstado == null) return;
            ActualizarRango();
        }

        private void Todo_Changed(object sender, RoutedEventArgs e)
        {
            if (txtInicio == null || txtFin == null) return;
            bool todo = chkTodo.IsChecked == true;
            txtInicio.IsEnabled = !todo;
            txtFin.IsEnabled = !todo;
        }

        private void Tramos_Changed(object sender, RoutedEventArgs e)
        {
            if (txtArchivoTramos == null || btnArchivoTramos == null || txtTramosInfo == null) return;
            bool usar = chkTramos.IsChecked == true;
            txtArchivoTramos.IsEnabled = usar;
            btnArchivoTramos.IsEnabled = usar;
            if (!usar) { _tramos.Clear(); txtTramosInfo.Text = ""; }
        }

        private void Superficie_Changed(object sender, RoutedEventArgs e)
        {
            if (pnlSuperficie == null) return;
            pnlSuperficie.IsEnabled = chkSuperficie.IsChecked == true;
        }

        private void SelAlineamiento_Click(object sender, RoutedEventArgs e)
        {
            var id = SeleccionarEnPantalla(typeof(CivAlignment), "Seleccione el alineamiento: ", "Debe ser un alineamiento de Civil 3D.");
            if (id.IsNull) return;
            var item = (cbAlineamiento.ItemsSource as List<ObjetoDibujo>)?.FirstOrDefault(x => x.Id == id);
            if (item != null) cbAlineamiento.SelectedItem = item;
        }

        private void SelPerfil_Click(object sender, RoutedEventArgs e)
        {
            var id = SeleccionarEnPantalla(typeof(CivProfile), "Seleccione el perfil de rasante en la vista de perfil: ", "Debe ser un perfil de Civil 3D.");
            if (id.IsNull) return;
            ObjectId idAl = ObjectId.Null;
            using (var tr = _doc.Database.TransactionManager.StartTransaction())
            {
                var pr = (CivProfile)tr.GetObject(id, OpenMode.ForRead);
                idAl = pr.AlignmentId;
                tr.Commit();
            }
            var al = (cbAlineamiento.ItemsSource as List<ObjetoDibujo>)?.FirstOrDefault(x => x.Id == idAl);
            if (al != null && !ReferenceEquals(cbAlineamiento.SelectedItem, al)) cbAlineamiento.SelectedItem = al;
            CargarPerfiles(idAl, id);
        }

        private void SelSuperficie_Click(object sender, RoutedEventArgs e)
        {
            var id = SeleccionarEnPantalla(typeof(CivSurface), "Seleccione la superficie de obstrucción (corredor con taludes): ", "Debe ser una superficie de Civil 3D.");
            if (id.IsNull) return;
            var item = (cbSuperficie.ItemsSource as List<ObjetoDibujo>)?.FirstOrDefault(x => x.Id == id);
            if (item != null) { cbSuperficie.SelectedItem = item; chkSuperficie.IsChecked = true; }
        }

        /// <summary>Oculta la ventana, pide una entidad en pantalla y vuelve a mostrarla.</summary>
        private ObjectId SeleccionarEnPantalla(Type clase, string mensaje, string rechazo)
        {
            var ed = _doc.Editor;
            EditorUserInteraction ui = null;
            try
            {
                ui = ed.StartUserInteraction(this);
                var peo = new PromptEntityOptions("\n" + mensaje);
                peo.SetRejectMessage("\n" + rechazo);
                peo.AddAllowedClass(clase, false);
                var r = ed.GetEntity(peo);
                return r.Status == PromptStatus.OK ? r.ObjectId : ObjectId.Null;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Selección", MessageBoxButton.OK, MessageBoxImage.Warning);
                return ObjectId.Null;
            }
            finally { ui?.End(); }
        }

        private void ElegirRuta_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar informe de visibilidad",
                Filter = "Informe HTML (*.html)|*.html",
                FileName = Path.GetFileName(txtRuta.Text),
                DefaultExt = ".html",
                AddExtension = true
            };
            try { dlg.InitialDirectory = Path.GetDirectoryName(txtRuta.Text); } catch { }
            if (dlg.ShowDialog(this) == true) txtRuta.Text = dlg.FileName;
        }

        private void ElegirTramos_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Archivo de velocidades por tramo (prog_inicio;prog_fin;V[;Da])",
                Filter = "CSV o texto (*.csv;*.txt)|*.csv;*.txt|Todos (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var tramos = Informe.LeerVelocidades(dlg.FileName);
                if (tramos.Count == 0)
                {
                    MessageBox.Show(this, "No se leyeron tramos válidos del archivo.", "Velocidades por tramo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _tramos = tramos;
                txtArchivoTramos.Text = dlg.FileName;
                bool conDa = tramos.Any(t => !double.IsNaN(t.Da));
                txtTramosInfo.Text = "Se cargaron " + tramos.Count + " tramo(s)" + (conDa ? " con Da por tramo" : "") +
                                     ". La velocidad de diseño de arriba se usa en las progresivas no cubiertas.";
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Velocidades por tramo", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ------------------------------------------------------------------ análisis
        private void Analizar_Click(object sender, RoutedEventArgs e)
        {
            if (_ocupado) return;
            if (!ArmarOpciones(out var o)) return;

            _cancelar = false;
            Ocupado(true);
            ResultadoEjecucion res = null;
            try
            {
                txtEstado.Text = o.ConSuperficie ? "Evaluando visibilidad contra la superficie..." : "Verificando curvas verticales...";
                Bombear();
                res = Motor.Ejecutar(_doc.Database, o, Progreso, () => { Bombear(); return _cancelar; });
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error en el análisis", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { Ocupado(false); }

            if (res == null) return;
            if (res.Error != null)
            {
                txtEstado.Text = res.Error;
                MessageBox.Show(this, res.Error, "Análisis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (res.Cancelado)
            {
                txtEstado.Text = "Análisis cancelado.";
                return;
            }

            _ultimo = res;
            MostrarResultados(res);
            tabs.SelectedItem = tabResultados;
            if (chkAbrir.IsChecked == true) Motor.AbrirArchivo(res.RutaHtml);
        }

        private void Progreso(int hecho, int total)
        {
            if (hecho % 10 != 0 && hecho != total) return;
            barra.Maximum = total;
            barra.Value = hecho;
            txtEstado.Text = "Evaluando progresiva " + hecho + " de " + total + "...";
            Bombear();
        }

        /// <summary>Procesa los mensajes pendientes de la ventana (repintado y clic en Cancelar) durante el cálculo.</summary>
        private void Bombear()
        {
            try { Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { })); } catch { }
        }

        private void Ocupado(bool si)
        {
            _ocupado = si;
            btnAnalizar.IsEnabled = !si;
            btnCerrar.IsEnabled = !si;
            btnCancelar.Visibility = si ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            barra.Visibility = si ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            barra.Value = 0;
            Cursor = si ? System.Windows.Input.Cursors.Wait : null;
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e) { _cancelar = true; txtEstado.Text = "Cancelando..."; }

        private void Cerrar_Click(object sender, RoutedEventArgs e) { if (!_ocupado) Close(); }

        private void AbrirHtml_Click(object sender, RoutedEventArgs e)
        {
            if (_ultimo != null && File.Exists(_ultimo.RutaHtml)) Motor.AbrirArchivo(_ultimo.RutaHtml);
        }

        private void AbrirCarpeta_Click(object sender, RoutedEventArgs e)
        {
            if (_ultimo == null) return;
            string dir = Path.GetDirectoryName(_ultimo.RutaHtml);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Motor.AbrirArchivo(dir);
        }

        // ------------------------------------------------------------------ lectura del formulario
        private bool ArmarOpciones(out OpcionesAnalisis o)
        {
            o = new OpcionesAnalisis();
            var P = o.P;

            if (!(cbAlineamiento.SelectedItem is ObjetoDibujo al))
                return Falla("Elige un alineamiento.", 0, cbAlineamiento);
            if (!(cbPerfil.SelectedItem is ObjetoDibujo pr))
                return Falla("Elige el perfil de rasante.", 0, cbPerfil);
            o.Alineamiento = al.Id;
            o.Perfil = pr.Id;

            if (chkTodo.IsChecked != true)
            {
                if (!Leer(txtInicio, "Progresiva de inicio", out double ini, true, true)) return false;
                if (!Leer(txtFin, "Progresiva final", out double fin, true, true)) return false;
                if (fin <= ini) return Falla("La progresiva final debe ser mayor que la inicial.", 0, txtFin);
                o.Inicio = ini; o.Fin = fin;
            }

            string ruta = txtRuta.Text.Trim();
            if (ruta.Length == 0) return Falla("Indica el archivo del informe HTML.", 0, txtRuta);
            o.RutaHtml = ruta;

            // Parámetros
            if (!Leer(txtV, "Velocidad de diseño", out P.VelocidadDiseno)) return false;
            if (chkTramos.IsChecked == true)
            {
                if (_tramos.Count == 0) return Falla("Carga el archivo de velocidades por tramo o desmarca la opción.", 1, txtArchivoTramos);
                P.TramosVelocidad = _tramos;
                P.ArchivoVelocidades = txtArchivoTramos.Text;
            }
            if (!Leer(txtTp, "Tiempo de percepción-reacción", out P.TiempoPercepcion)) return false;
            if (!Leer(txtA, "Tasa de desaceleración", out P.Desaceleracion)) return false;
            if (!Leer(txtOjo, "Altura del ojo", out P.AlturaOjo)) return false;
            if (!Leer(txtObjeto, "Altura del objeto", out P.AlturaObjeto)) return false;
            P.CriterioL = rbMaximo.IsChecked == true ? CriterioLongitud.Maximo : CriterioLongitud.Formula;
            if (!Leer(txtUmbralA, "Diferencia algebraica que exige curva", out P.UmbralA, false, true)) return false;
            if (!Leer(txtLMin, "Longitud mínima absoluta", out P.LongitudMinima, false, true)) return false;
            if (!Leer(txtDa, "Distancia de visibilidad de adelantamiento", out P.DistanciaAdelanto, false, true)) return false;
            if (!Leer(txtObjetoDa, "Altura del objeto para adelantamiento", out P.AlturaObjetoAdelanto)) return false;

            // Superficie (opcional)
            if (chkSuperficie.IsChecked == true)
            {
                if (!(cbSuperficie.SelectedItem is ObjetoDibujo su))
                    return Falla("Elige la superficie de obstrucción o desmarca la comprobación contra superficie.", 2, cbSuperficie);
                o.Superficie = su.Id;
                if (!Leer(txtDesfC, "Desfase del carril creciente", out P.DesfaseCreciente, true, true)) return false;
                if (!Leer(txtDesfD, "Desfase del carril decreciente", out P.DesfaseDecreciente, true, true)) return false;
                if (!Leer(txtIntervalo, "Intervalo entre progresivas", out P.Intervalo)) return false;
                P.Sentido = (SentidoAnalisis)cbSentido.SelectedIndex;
                P.Criterio = (CriterioPendiente)cbCriterio.SelectedIndex;
                switch (cbPrecision.SelectedIndex)
                {
                    case 1: P.PasoMuestreo = 0.5; P.PasoBusqueda = 2.5; break;
                    case 2: P.PasoMuestreo = 2.0; P.PasoBusqueda = 10.0; break;
                    default: P.PasoMuestreo = 1.0; P.PasoBusqueda = 5.0; break;
                }
                P.Dibujar = chkDibujar.IsChecked == true;
            }
            return true;
        }

        private bool Falla(string msg, int pestana, Control foco)
        {
            tabs.SelectedIndex = pestana;
            MessageBox.Show(this, msg, "Datos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            foco?.Focus();
            return false;
        }

        /// <summary>Lee un número del cuadro; acepta coma o punto decimal y progresivas "2+350.00".</summary>
        private bool Leer(TextBox tb, string nombre, out double v, bool negativo = false, bool cero = false)
        {
            v = double.NaN;
            string s = (tb.Text ?? "").Trim().Replace(" ", "").Replace(',', '.');
            bool ok;
            if (s.Contains("+"))
            {
                var pp = s.Split('+');
                double km = 0, m = 0;
                ok = pp.Length == 2 &&
                     double.TryParse(pp[0], NumberStyles.Float, Inv, out km) &&
                     double.TryParse(pp[1], NumberStyles.Float, Inv, out m);
                if (ok) v = km * 1000.0 + m;
            }
            else ok = double.TryParse(s, NumberStyles.Float, Inv, out v);

            if (!ok || double.IsNaN(v) || double.IsInfinity(v))
                return Falla(nombre + ": ingresa un valor numérico.", PestanaDe(tb), tb);
            if (!negativo && v < 0)
                return Falla(nombre + ": no puede ser negativo.", PestanaDe(tb), tb);
            if (!cero && Math.Abs(v) < 1e-12)
                return Falla(nombre + ": debe ser distinto de cero.", PestanaDe(tb), tb);
            return true;
        }

        private int PestanaDe(Control c)
        {
            DependencyObject d = c;
            while (d != null && !(d is TabItem)) d = VisualTreeHelper.GetParent(d);
            return d is TabItem ti ? tabs.Items.IndexOf(ti) : 0;
        }

        private static string Num(double v) => v.ToString("0.###", Inv);
        private static string F(double v, int dec = 2) => double.IsNaN(v) ? "—" : v.ToString("F" + dec, Inv);

        // ------------------------------------------------------------------ resultados
        private void MostrarResultados(ResultadoEjecucion res)
        {
            var d = res.Datos;
            var v = Veredicto.De(d);

            txtVeredicto.Text = v.Cumple ? "VERIFICACIÓN: CUMPLE" : "VERIFICACIÓN: NO CUMPLE";
            var colorTexto = v.Cumple ? Color.FromRgb(0x1B, 0x7A, 0x2F) : Color.FromRgb(0xB3, 0x26, 0x1E);
            var colorFondo = v.Cumple ? Color.FromRgb(0xE8, 0xF5, 0xEC) : Color.FromRgb(0xFD, 0xEC, 0xEA);
            txtVeredicto.Foreground = new SolidColorBrush(colorTexto);
            bordeVeredicto.BorderBrush = new SolidColorBrush(colorTexto);
            bordeVeredicto.Background = new SolidColorBrush(colorFondo);

            var lineas = new List<string>();
            if (v.CurvasCumplen != null) lineas.Add("Curvas verticales: " + (v.CurvasCumplen.Value ? "CUMPLEN" : "NO CUMPLEN"));
            if (v.VisibilidadCumple != null) lineas.Add("Visibilidad de parada en el eje: " + (v.VisibilidadCumple.Value ? "CUMPLE" : "NO CUMPLE"));
            lineas.AddRange(v.Motivos.Select(m => "• " + m));
            int cumplen = d.Curvas.Count(c => c.Cumple);
            lineas.Add("PVI analizados: " + d.Curvas.Count + "  ·  cumplen: " + cumplen + "  ·  no cumplen: " + (d.Curvas.Count - cumplen) +
                       "  ·  rango " + Formato.Prog(d.SMin) + " – " + Formato.Prog(d.SMax));
            txtMotivos.Text = string.Join("\n", lineas);

            gridCurvas.ItemsSource = d.Curvas.Select(c => new FilaCurva
            {
                N = c.N,
                Ubicacion = Formato.Prog(c.Progresiva),
                Tipo = c.Convexa ? "Convexa" : "Cóncava",
                V = F(c.V, 0),
                Pe = F(c.Pe * 100.0),
                Ps = F(c.Ps * 100.0),
                A = F(c.A),
                Dp = F(c.Dp, 0),
                LReq = F(c.LReq, 0),
                LProyecto = c.TieneCurva ? F(c.LProyecto) : "sin curva",
                KReq = F(c.KReq),
                KProyecto = c.TieneCurva ? F(c.KProyecto) : "—",
                Estado = CurvasVerticales.EstadoTxt(c.Estado),
                Da = F(c.Da, 0),
                LDaReq = F(c.LDaReq, 0),
                Adelanta = !c.Convexa ? "No aplica (cóncava)"
                         : c.PermiteAdelantar == null ? (d.P.DistanciaAdelanto > 0 || d.P.TramosVelocidad.Count > 0 ? "—" : "Da = 0: no evaluado")
                         : c.PermiteAdelantar.Value ? "Sí" : "No",
                Verificacion = c.Verificacion,
                Cumple = c.Cumple
            }).ToList();

            bool haySectores = d.Resultados != null;
            lblSectores.Visibility = haySectores ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            gridSectores.Visibility = haySectores ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (haySectores)
            {
                lblSectores.Text = d.Sectores.Count == 0
                    ? "Visibilidad a lo largo del eje: sin sectores deficientes (" + d.Resultados.Count + " puntos evaluados)"
                    : "Sectores del eje sin visibilidad de parada suficiente (" + d.Sectores.Count + ")";
                gridSectores.ItemsSource = d.Sectores.Select(s => new FilaSector
                {
                    Sentido = Formato.SentidoTxt(s.Sentido),
                    Inicio = Formato.Prog(s.Inicio),
                    Fin = Formato.Prog(s.Fin),
                    Longitud = F(s.Fin - s.Inicio),
                    DvpMax = F(s.DvpMax),
                    DisponibleMin = F(s.DisponibleMin),
                    DeficitMax = F(s.DeficitMax)
                }).ToList();
            }

            btnAbrirHtml.IsEnabled = true;
            btnAbrirCarpeta.IsEnabled = true;
            txtArchivos.Text = "Informe: " + Path.GetFileName(res.RutaHtml) + "  ·  " + Path.GetFileName(res.RutaCsvCurvas) +
                               (string.IsNullOrEmpty(res.RutaCsvPuntos) ? "" : "  ·  " + Path.GetFileName(res.RutaCsvPuntos));
            txtEstado.Text = "Análisis terminado en " + d.Duracion.TotalSeconds.ToString("0.0", Inv) + " s. Informe: " + res.RutaHtml;
        }
    }
}
