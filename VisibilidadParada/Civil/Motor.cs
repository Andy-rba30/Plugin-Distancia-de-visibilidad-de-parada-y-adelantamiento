using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using VisibilidadParada.Nucleo;
using CivAlignment = Autodesk.Civil.DatabaseServices.Alignment;
using CivProfile = Autodesk.Civil.DatabaseServices.Profile;
using CivSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace VisibilidadParada.Civil
{
    /// <summary>Todo lo que hace falta para una corrida del análisis.</summary>
    internal class OpcionesAnalisis
    {
        public ObjectId Alineamiento, Perfil;
        public ObjectId Superficie = ObjectId.Null;          // nula => solo curvas verticales
        public double Inicio = double.NaN, Fin = double.NaN; // NaN => todo el perfil
        public Parametros P = new Parametros();
        public string RutaHtml = "";
        public bool ConSuperficie => !Superficie.IsNull;
    }

    internal class ResultadoEjecucion
    {
        public DatosInforme Datos;
        public bool Cancelado;
        public string Error;
        public string RutaHtml = "", RutaCsvCurvas = "", RutaCsvPuntos = "";
    }

    /// <summary>
    /// Ejecuta el análisis (curvas verticales y, opcionalmente, visibilidad contra superficie)
    /// y escribe los informes. Lo usan tanto la ventana VISIBILIDAD como los comandos de línea.
    /// </summary>
    internal static class Motor
    {
        public const string CapaCreciente = "VIS-PARADA-DEF-CRECIENTE";
        public const string CapaDecreciente = "VIS-PARADA-DEF-DECRECIENTE";

        public static ResultadoEjecucion Ejecutar(Database db, OpcionesAnalisis o,
                                                  Action<int, int> progreso, Func<bool> cancelar)
        {
            var P = o.P;
            var r = new ResultadoEjecucion { RutaHtml = o.RutaHtml };
            var reloj = Stopwatch.StartNew();
            string dibujo = "";
            try { dibujo = Path.GetFileName(db.Filename); } catch { }
            var datos = new DatosInforme { P = P, Dibujo = dibujo };

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var al = (CivAlignment)tr.GetObject(o.Alineamiento, OpenMode.ForRead);
                var pr = (CivProfile)tr.GetObject(o.Perfil, OpenMode.ForRead);
                datos.Eje = al.Name; datos.Perfil = pr.Name;

                double sMinPerfil = Math.Max(al.StartingStation, pr.StartingStation);
                double sMaxPerfil = Math.Min(al.EndingStation, pr.EndingStation);
                double sMin = double.IsNaN(o.Inicio) ? sMinPerfil : Math.Max(sMinPerfil, o.Inicio);
                double sMax = double.IsNaN(o.Fin) ? sMaxPerfil : Math.Min(sMaxPerfil, o.Fin);
                if (sMax - sMin < 1.0)
                {
                    r.Error = "El rango de progresivas no cubre un tramo útil del perfil.";
                    return r;
                }
                datos.SMin = sMin; datos.SMax = sMax;

                // Curvas verticales (siempre)
                datos.Curvas = CurvasVerticales.Analizar(GeometriaPerfil.LeerPvis(pr), P);
                datos.Curvas.RemoveAll(c => c.Progresiva < sMin - 1e-6 || c.Progresiva > sMax + 1e-6);

                // Visibilidad a lo largo del eje contra la superficie (opcional)
                if (o.ConSuperficie)
                {
                    var su = (CivSurface)tr.GetObject(o.Superficie, OpenMode.ForRead);
                    datos.Superficie = su.Name;

                    var eje = new EjeCivil(al);
                    var an = new Analizador(eje, new RasanteCivil(pr), new SuperficieCivil(su), P, sMinPerfil, sMaxPerfil);

                    var sentidos = new List<int>();
                    if (P.Sentido != SentidoAnalisis.Decreciente) sentidos.Add(1);
                    if (P.Sentido != SentidoAnalisis.Creciente) sentidos.Add(-1);

                    var progs = Analizador.Progresivas(sMin, sMax, P.Intervalo);
                    var res = new List<ResultadoPunto>(progs.Count * sentidos.Count);
                    int total = progs.Count * sentidos.Count, k = 0;

                    foreach (int sen in sentidos)
                    {
                        foreach (double s in progs)
                        {
                            if (cancelar != null && cancelar()) { r.Cancelado = true; return r; }
                            res.Add(an.Evaluar(s, sen));
                            progreso?.Invoke(++k, total);
                        }
                    }

                    datos.Resultados = res;
                    datos.Sectores = Analizador.Sectores(res, P);
                    datos.MuestrasFuera = an.MuestrasFuera;
                    datos.PuntosSinSuperficie = an.PuntosSinSuperficie;

                    if (P.Dibujar && datos.Sectores.Count > 0)
                        DibujarSectores(tr, db, eje, datos.Sectores, sMin, sMax, P);
                }

                tr.Commit();
            }

            datos.Duracion = reloj.Elapsed;
            r.Datos = datos;
            EscribirInformes(r);
            return r;
        }

        /// <summary>Escribe el HTML y los CSV junto a él: &lt;base&gt;_curvas.csv y, si hay superficie, &lt;base&gt;_progresivas.csv.</summary>
        private static void EscribirInformes(ResultadoEjecucion r)
        {
            string html = r.RutaHtml;
            if (string.IsNullOrWhiteSpace(html)) return;
            if (!html.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) html += ".html";
            string dir = Path.GetDirectoryName(html) ?? "";
            string baseNombre = Path.GetFileNameWithoutExtension(html);
            if (dir.Length > 0) Directory.CreateDirectory(dir);

            r.RutaHtml = html;
            r.RutaCsvCurvas = Path.Combine(dir, baseNombre + "_curvas.csv");
            Informe.EscribirHtml(html, r.Datos);
            Informe.EscribirCsvCurvas(r.RutaCsvCurvas, r.Datos);
            if (r.Datos.Resultados != null)
            {
                r.RutaCsvPuntos = Path.Combine(dir, baseNombre + "_progresivas.csv");
                Informe.EscribirCsv(r.RutaCsvPuntos, r.Datos);
            }
        }

        public static void AbrirArchivo(string ruta)
        {
            try { Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true }); } catch { }
        }

        // -------------------------------------------------------------------------------
        private static void DibujarSectores(Transaction tr, Database db, IEje eje, List<Sector> sectores,
                                            double sMin, double sMax, Parametros P)
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
