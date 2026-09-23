using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using VisibilidadParada.Nucleo;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivAlignment = Autodesk.Civil.DatabaseServices.Alignment;
using CivProfile = Autodesk.Civil.DatabaseServices.Profile;
using CivSurface = Autodesk.Civil.DatabaseServices.Surface;

[assembly: CommandClass(typeof(VisibilidadParada.Civil.Comando))]

namespace VisibilidadParada.Civil
{
    public class Comando
    {
        // Parámetros de la ejecución actual (se reinician en cada comando: nada queda precargado)
        private static Parametros P = new Parametros();

        private const string CapaCreciente = "VIS-PARADA-DEF-CRECIENTE";
        private const string CapaDecreciente = "VIS-PARADA-DEF-DECRECIENTE";

        [CommandMethod("VISPARADA")]
        public void VisParada()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            P = new Parametros();

            try
            {
                // 1. Selección de objetos -------------------------------------------------
                var peAl = new PromptEntityOptions("\nSeleccione el alineamiento: ");
                peAl.SetRejectMessage("\nDebe ser un alineamiento de Civil 3D.");
                peAl.AddAllowedClass(typeof(CivAlignment), false);
                var rAl = ed.GetEntity(peAl);
                if (rAl.Status != PromptStatus.OK) return;

                ObjectId idPerfil = ElegirPerfil(ed, db, rAl.ObjectId);
                if (idPerfil.IsNull) return;

                var peSu = new PromptEntityOptions("\nSeleccione la superficie de obstrucción (corredor con taludes): ");
                peSu.SetRejectMessage("\nDebe ser una superficie de Civil 3D.");
                peSu.AddAllowedClass(typeof(CivSurface), false);
                var rSu = ed.GetEntity(peSu);
                if (rSu.Status != PromptStatus.OK) return;

                // 2. Parámetros ------------------------------------------------------------
                if (!PedirParametros(ed, true)) return;

                // 3. Archivo de salida ------------------------------------------------------
                string dir = "";
                try { dir = Path.GetDirectoryName(db.Filename) ?? ""; } catch { }
                var pso = new PromptSaveFileOptions("Guardar informe de visibilidad de parada")
                {
                    Filter = "Informe HTML (*.html)|*.html",
                    InitialFileName = "Informe_DVP.html",
                    InitialDirectory = dir
                };
                var rFile = ed.GetFileNameForSave(pso);
                if (rFile.Status != PromptStatus.OK) return;
                string rutaHtml = rFile.StringResult;
                if (!rutaHtml.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) rutaHtml += ".html";
                string rutaCsv = Path.ChangeExtension(rutaHtml, ".csv");

                // 4. Cálculo -----------------------------------------------------------------
                var reloj = Stopwatch.StartNew();
                var datos = new DatosInforme { P = P, Dibujo = Path.GetFileName(db.Filename) };
                bool cancelado = false;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var al = (CivAlignment)tr.GetObject(rAl.ObjectId, OpenMode.ForRead);
                    var pr = (CivProfile)tr.GetObject(idPerfil, OpenMode.ForRead);
                    var su = (CivSurface)tr.GetObject(rSu.ObjectId, OpenMode.ForRead);

                    datos.Eje = al.Name; datos.Perfil = pr.Name; datos.Superficie = su.Name;
                    datos.Curvas = CurvasVerticales.Analizar(GeometriaPerfil.LeerPvis(pr), P);

                    double sMin = Math.Max(al.StartingStation, pr.StartingStation);
                    double sMax = Math.Min(al.EndingStation, pr.EndingStation);
                    if (sMax - sMin < 1.0)
                    {
                        ed.WriteMessage("\nEl perfil no cubre un rango útil del alineamiento.");
                        return;
                    }
                    datos.SMin = sMin; datos.SMax = sMax;

                    var eje = new EjeCivil(al);
                    var an = new Analizador(eje, new RasanteCivil(pr), new SuperficieCivil(su), P, sMin, sMax);

                    var sentidos = new List<int>();
                    if (P.Sentido != SentidoAnalisis.Decreciente) sentidos.Add(1);
                    if (P.Sentido != SentidoAnalisis.Creciente) sentidos.Add(-1);

                    var progs = Analizador.Progresivas(sMin, sMax, P.Intervalo);
                    var res = new List<ResultadoPunto>(progs.Count * sentidos.Count);

                    ed.WriteMessage($"\nEvaluando {progs.Count} progresivas × {sentidos.Count} sentido(s). Pulse ESC para cancelar.");
                    var pm = new ProgressMeter();
                    pm.SetLimit(progs.Count * sentidos.Count);
                    pm.Start("Verificando visibilidad de parada");
                    try
                    {
                        foreach (int sen in sentidos)
                        {
                            foreach (double s in progs)
                            {
                                if (Interrumpido()) { cancelado = true; break; }
                                res.Add(an.Evaluar(s, sen));
                                pm.MeterProgress();
                            }
                            if (cancelado) break;
                        }
                    }
                    finally { pm.Stop(); }

                    if (cancelado)
                    {
                        ed.WriteMessage("\nAnálisis cancelado por el usuario.");
                        return;
                    }

                    datos.Resultados = res;
                    datos.Sectores = Analizador.Sectores(res, P);
                    datos.MuestrasFuera = an.MuestrasFuera;
                    datos.PuntosSinSuperficie = an.PuntosSinSuperficie;

                    if (P.Dibujar && datos.Sectores.Count > 0)
                        DibujarSectores(tr, db, eje, datos.Sectores, sMin, sMax);

                    tr.Commit();
                }

                reloj.Stop();
                datos.Duracion = reloj.Elapsed;

                // 5. Informe --------------------------------------------------------------
                string rutaCsvCurvas = Path.Combine(Path.GetDirectoryName(rutaHtml) ?? "",
                    Path.GetFileNameWithoutExtension(rutaHtml) + "_curvas.csv");
                Informe.EscribirHtml(rutaHtml, datos);
                Informe.EscribirCsv(rutaCsv, datos);
                Informe.EscribirCsvCurvas(rutaCsvCurvas, datos);

                MostrarVeredicto(ed, datos);
                ed.WriteMessage($"\nInforme: {rutaHtml}\nDetalle CSV: {rutaCsv}\nCurvas CSV: {rutaCsvCurvas}");
                try { Process.Start(new ProcessStartInfo(rutaHtml) { UseShellExecute = true }); } catch { }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError en VISPARADA: {ex.Message}");
            }
        }

        /// <summary>Solo el análisis de curvas verticales (lee la geometría del perfil; no necesita superficie).</summary>
        [CommandMethod("VISCURVAS")]
        public void VisCurvas()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            P = new Parametros();
            try
            {
                var peAl = new PromptEntityOptions("\nSeleccione el alineamiento: ");
                peAl.SetRejectMessage("\nDebe ser un alineamiento de Civil 3D.");
                peAl.AddAllowedClass(typeof(CivAlignment), false);
                var rAl = ed.GetEntity(peAl);
                if (rAl.Status != PromptStatus.OK) return;

                ObjectId idPerfil = ElegirPerfil(ed, db, rAl.ObjectId);
                if (idPerfil.IsNull) return;

                if (!PedirParametros(ed, false)) return;

                string dir = "";
                try { dir = Path.GetDirectoryName(db.Filename) ?? ""; } catch { }
                var pso = new PromptSaveFileOptions("Guardar informe de curvas verticales")
                {
                    Filter = "Informe HTML (*.html)|*.html",
                    InitialFileName = "Informe_Curvas_Verticales.html",
                    InitialDirectory = dir
                };
                var rFile = ed.GetFileNameForSave(pso);
                if (rFile.Status != PromptStatus.OK) return;
                string rutaHtml = rFile.StringResult;
                if (!rutaHtml.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) rutaHtml += ".html";
                string rutaCsv = Path.ChangeExtension(rutaHtml, ".csv");

                var reloj = Stopwatch.StartNew();
                var datos = new DatosInforme { P = P, Dibujo = Path.GetFileName(db.Filename) };
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var al = (CivAlignment)tr.GetObject(rAl.ObjectId, OpenMode.ForRead);
                    var pr = (CivProfile)tr.GetObject(idPerfil, OpenMode.ForRead);
                    datos.Eje = al.Name; datos.Perfil = pr.Name;
                    datos.SMin = Math.Max(al.StartingStation, pr.StartingStation);
                    datos.SMax = Math.Min(al.EndingStation, pr.EndingStation);
                    datos.Curvas = CurvasVerticales.Analizar(GeometriaPerfil.LeerPvis(pr), P);
                    tr.Commit();
                }
                datos.Duracion = reloj.Elapsed;

                Informe.EscribirHtml(rutaHtml, datos);
                Informe.EscribirCsvCurvas(rutaCsv, datos);
                MostrarVeredicto(ed, datos);
                ed.WriteMessage($"\nInforme: {rutaHtml}\nCSV: {rutaCsv}");
                try { Process.Start(new ProcessStartInfo(rutaHtml) { UseShellExecute = true }); } catch { }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError en VISCURVAS: {ex.Message}");
            }
        }

        private static void MostrarVeredicto(Editor ed, DatosInforme d)
        {
            if (d.Curvas != null)
            {
                ed.WriteMessage("\n\nVerificación de curvas verticales:");
                foreach (var c in d.Curvas)
                    ed.WriteMessage($"\n  PVI {c.N,3}  {Formato.Prog(c.Progresiva),10}  {(c.Convexa ? "Convexa" : "Cóncava"),-8}  {c.Verificacion}");
            }
            var v = Veredicto.De(d);
            ed.WriteMessage("\n\n========================================");
            ed.WriteMessage($"\n  VERIFICACIÓN: {(v.Cumple ? "CUMPLE" : "NO CUMPLE")}");
            ed.WriteMessage("\n========================================");
            if (v.CurvasCumplen != null) ed.WriteMessage($"\n  Curvas verticales: {(v.CurvasCumplen.Value ? "CUMPLEN" : "NO CUMPLEN")}");
            if (v.VisibilidadCumple != null) ed.WriteMessage($"\n  Visibilidad de parada en el eje: {(v.VisibilidadCumple.Value ? "CUMPLE" : "NO CUMPLE")}");
            foreach (var m in v.Motivos) ed.WriteMessage("\n  - " + m);
        }

        // -------------------------------------------------------------------------------
        /// <summary>
        /// El usuario selecciona el perfil en la vista de perfil (o lo elige de la lista con [Lista]).
        /// No se preselecciona ningún perfil.
        /// </summary>
        private static ObjectId ElegirPerfil(Editor ed, Database db, ObjectId idAlineamiento)
        {
            while (true)
            {
                var pe = new PromptEntityOptions("\nSeleccione el perfil de rasante en la vista de perfil");
                pe.SetRejectMessage("\nDebe ser un perfil de Civil 3D.");
                pe.AddAllowedClass(typeof(CivProfile), false);
                pe.Keywords.Add("Lista");
                pe.AppendKeywordsToMessage = true;
                var r = ed.GetEntity(pe);

                if (r.Status == PromptStatus.Keyword) return ElegirPerfilDeLista(ed, db, idAlineamiento);
                if (r.Status != PromptStatus.OK) return ObjectId.Null;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var pr = (CivProfile)tr.GetObject(r.ObjectId, OpenMode.ForRead);
                    bool delEje = pr.AlignmentId.Equals(idAlineamiento);
                    string nombre = pr.Name;
                    tr.Commit();
                    if (delEje)
                    {
                        ed.WriteMessage($"\nPerfil seleccionado: {nombre}");
                        return r.ObjectId;
                    }
                    ed.WriteMessage($"\nEl perfil \"{nombre}\" no pertenece al alineamiento seleccionado. Intente de nuevo.");
                }
            }
        }

        private static ObjectId ElegirPerfilDeLista(Editor ed, Database db, ObjectId idAlineamiento)
        {
            var lista = new List<(ObjectId id, string nombre, string tipo)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var al = (CivAlignment)tr.GetObject(idAlineamiento, OpenMode.ForRead);
                foreach (ObjectId id in al.GetProfileIds())
                    if (tr.GetObject(id, OpenMode.ForRead) is CivProfile pr)
                        lista.Add((id, pr.Name, pr.ProfileType.ToString()));
                tr.Commit();
            }
            if (lista.Count == 0)
            {
                ed.WriteMessage("\nEl alineamiento no tiene perfiles.");
                return ObjectId.Null;
            }

            ed.WriteMessage("\nPerfiles del alineamiento:");
            for (int i = 0; i < lista.Count; i++)
                ed.WriteMessage($"\n  {i + 1}. {lista[i].nombre}  ({lista[i].tipo})");

            var pio = new PromptIntegerOptions($"\nNúmero del perfil de rasante (1 a {lista.Count})")
            {
                LowerLimit = 1, UpperLimit = lista.Count, AllowNone = false
            };
            var r = ed.GetInteger(pio);
            if (r.Status != PromptStatus.OK) return ObjectId.Null;
            ed.WriteMessage($"\nPerfil seleccionado: {lista[r.Value - 1].nombre}");
            return lista[r.Value - 1].id;
        }

        /// <summary>Todos los valores los ingresa el usuario: no hay valores por defecto.</summary>
        private static bool PedirParametros(Editor ed, bool visibilidad)
        {
            // Velocidad: valor único o archivo por tramos
            while (true)
            {
                string msg = P.TramosVelocidad.Count > 0
                    ? $"\nSe cargaron {P.TramosVelocidad.Count} tramos. Velocidad para progresivas fuera de los tramos (km/h)"
                    : "\nVelocidad de diseño (km/h)";
                var o = new PromptDoubleOptions(msg)
                {
                    UseDefaultValue = false, AllowNone = false,
                    AllowNegative = false, AllowZero = false
                };
                if (P.TramosVelocidad.Count == 0) o.Keywords.Add("Archivo");
                o.AppendKeywordsToMessage = true;
                var r = ed.GetDouble(o);

                if (r.Status == PromptStatus.Keyword && r.StringResult == "Archivo")
                {
                    var pof = new PromptOpenFileOptions("Archivo de velocidades por tramo (prog_inicio;prog_fin;V[;Da])")
                    { Filter = "CSV o texto (*.csv;*.txt)|*.csv;*.txt" };
                    var rf = ed.GetFileNameForOpen(pof);
                    if (rf.Status != PromptStatus.OK) continue;
                    var tramos = Informe.LeerVelocidades(rf.StringResult);
                    if (tramos.Count == 0) { ed.WriteMessage("\nNo se leyeron tramos válidos."); continue; }
                    P.TramosVelocidad = tramos;
                    P.ArchivoVelocidades = rf.StringResult;
                    continue;
                }
                if (r.Status == PromptStatus.OK) { P.VelocidadDiseno = r.Value; break; }
                return false;
            }

            if (!Doble(ed, "Tiempo de percepción-reacción tp (s) (ref. DG-2018: 2.5)", out P.TiempoPercepcion)) return false;
            if (!Doble(ed, "Tasa de desaceleración a (m/s²) (ref. DG-2018: 3.4)", out P.Desaceleracion)) return false;
            if (!Doble(ed, "Altura del ojo (m) (ref. DG-2018: 1.07)", out P.AlturaOjo)) return false;
            if (!Doble(ed, "Altura del objeto (m) (ref. DG-2018: 0.15)", out P.AlturaObjeto)) return false;

            // Curvas verticales
            string cl = Palabra(ed, "\nLongitud de curva vertical", new[] { "Formula", "Maximo" });
            if (cl == null) return false;
            P.CriterioL = (CriterioLongitud)Enum.Parse(typeof(CriterioLongitud), cl);
            if (!Doble(ed, "Diferencia algebraica A (%) a partir de la cual se exige curva vertical", out P.UmbralA, false, true)) return false;
            if (!Doble(ed, "Longitud mínima absoluta de curva vertical (m, 0 = no aplicar)", out P.LongitudMinima, false, true)) return false;

            if (P.TramosVelocidad.Any(t => !double.IsNaN(t.Da)))
            {
                ed.WriteMessage("\nEl archivo de velocidades trae Da por tramo.");
                if (!Doble(ed, "Da para progresivas fuera de los tramos (m, 0 = no evaluar)", out P.DistanciaAdelanto, false, true)) return false;
            }
            else if (!Doble(ed, "Distancia de visibilidad de adelantamiento Da (m, 0 = no evaluar)", out P.DistanciaAdelanto, false, true)) return false;

            if (P.DistanciaAdelanto > 0 || P.TramosVelocidad.Any(t => !double.IsNaN(t.Da)))
                if (!Doble(ed, "Altura del objeto para adelantamiento (m) (ref. DG-2018: 1.30)", out P.AlturaObjetoAdelanto)) return false;

            if (!visibilidad) return true;

            if (!Doble(ed, "Desfase del carril en sentido CRECIENTE (+ derecha del eje, m)", out P.DesfaseCreciente, true, true)) return false;
            if (!Doble(ed, "Desfase del carril en sentido DECRECIENTE (− izquierda del eje, m)", out P.DesfaseDecreciente, true, true)) return false;
            if (!Doble(ed, "Intervalo entre progresivas evaluadas (m)", out P.Intervalo)) return false;

            string s = Palabra(ed, "\nSentido de análisis", new[] { "Ambos", "Creciente", "Decreciente" });
            if (s == null) return false;
            P.Sentido = (SentidoAnalisis)Enum.Parse(typeof(SentidoAnalisis), s);

            string c = Palabra(ed, "\nCriterio de pendiente", new[] { "Desfavorable", "Promedio" });
            if (c == null) return false;
            P.Criterio = (CriterioPendiente)Enum.Parse(typeof(CriterioPendiente), c);

            string pr = Palabra(ed, "\nPrecisión de muestreo", new[] { "Normal", "Fina", "Rapida" });
            if (pr == null) return false;
            switch (pr)
            {
                case "Fina": P.PasoMuestreo = 0.5; P.PasoBusqueda = 2.5; break;
                case "Rapida": P.PasoMuestreo = 2.0; P.PasoBusqueda = 10.0; break;
                default: P.PasoMuestreo = 1.0; P.PasoBusqueda = 5.0; break;
            }

            string d = Palabra(ed, "\n¿Dibujar sectores deficientes en planta?", new[] { "Si", "No" });
            if (d == null) return false;
            P.Dibujar = d == "Si";
            return true;
        }

        /// <summary>Pide un número obligatorio (Enter vacío no se acepta).</summary>
        private static bool Doble(Editor ed, string msg, out double valor, bool negativo = false, bool cero = false)
        {
            valor = double.NaN;
            var o = new PromptDoubleOptions("\n" + msg)
            {
                UseDefaultValue = false, AllowNone = false,
                AllowNegative = negativo, AllowZero = cero
            };
            var r = ed.GetDouble(o);
            if (r.Status != PromptStatus.OK) return false;
            valor = r.Value;
            return true;
        }

        /// <summary>Pide una opción obligatoria (sin opción por defecto).</summary>
        private static string Palabra(Editor ed, string msg, string[] opciones)
        {
            var o = new PromptKeywordOptions(msg) { AllowNone = false };
            foreach (var k in opciones) o.Keywords.Add(k);
            o.AppendKeywordsToMessage = true;
            var r = ed.GetKeywords(o);
            return r.Status == PromptStatus.OK && !string.IsNullOrEmpty(r.StringResult) ? r.StringResult : null;
        }

        private static bool Interrumpido()
        {
            try { return HostApplicationServices.Current.UserBreak(); } catch { return false; }
        }

        // -------------------------------------------------------------------------------
        private static void DibujarSectores(Transaction tr, Database db, IEje eje, List<Sector> sectores,
                                            double sMin, double sMax)
        {
            var idCapaC = Capa(tr, db, CapaCreciente, 1);   // rojo
            var idCapaD = Capa(tr, db, CapaDecreciente, 6); // magenta

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            double medio = P.Intervalo / 2.0;
            foreach (var sec in sectores)
            {
                double a = Math.Max(sMin, sec.Inicio - medio);
                double b = Math.Min(sMax, sec.Fin + medio);
                double paso = Math.Max(0.5, P.Intervalo / 4.0);

                var pl = new Polyline();
                int k = 0;
                for (double s = a; s <= b + 1e-6; s += paso)
                {
                    double st = Math.Min(s, b);
                    if (eje.Ubicar(st, sec.Desfase, out double x, out double y))
                        pl.AddVertexAt(k++, new Point2d(x, y), 0, 0.6, 0.6);
                }
                if (eje.Ubicar(b, sec.Desfase, out double xf, out double yf) &&
                    (k == 0 || pl.GetPoint2dAt(k - 1).GetDistanceTo(new Point2d(xf, yf)) > 0.01))
                    pl.AddVertexAt(k++, new Point2d(xf, yf), 0, 0.6, 0.6);

                if (k < 2) { pl.Dispose(); continue; }
                pl.LayerId = sec.Sentido > 0 ? idCapaC : idCapaD;
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
            }
        }

        private static ObjectId Capa(Transaction tr, Database db, string nombre, short aci)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(nombre)) return lt[nombre];
            lt.UpgradeOpen();
            var ltr = new LayerTableRecord { Name = nombre, Color = Color.FromColorIndex(ColorMethod.ByAci, aci) };
            var id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }
    }
}
