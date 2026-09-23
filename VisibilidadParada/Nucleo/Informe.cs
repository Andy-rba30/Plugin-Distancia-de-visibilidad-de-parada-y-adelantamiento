using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace VisibilidadParada.Nucleo
{
    public class DatosInforme
    {
        public string Dibujo = "", Eje = "", Perfil = "", Superficie = "";
        public double SMin, SMax;
        public Parametros P;
        public List<ResultadoPunto> Resultados;   // null si solo se analizaron curvas
        public List<Sector> Sectores;
        public List<ResultadoCurva> Curvas;       // null si no se analizaron curvas
        public long MuestrasFuera, PuntosSinSuperficie;
        public TimeSpan Duracion;
    }

    /// <summary>Veredicto de la verificación: CUMPLE / NO CUMPLE con su justificación.</summary>
    public class Veredicto
    {
        public bool? CurvasCumplen, VisibilidadCumple;
        public List<string> Motivos = new List<string>();
        public bool Cumple => (CurvasCumplen ?? true) && (VisibilidadCumple ?? true);

        public static Veredicto De(DatosInforme d)
        {
            var v = new Veredicto();
            if (d.Curvas != null)
            {
                var cortas = d.Curvas.Where(c => c.Estado == EstadoCurva.NoCumple).ToList();
                var faltan = d.Curvas.Where(c => c.Estado == EstadoCurva.SinCurvaRequerida).ToList();
                v.CurvasCumplen = cortas.Count == 0 && faltan.Count == 0;
                if (cortas.Count > 0)
                    v.Motivos.Add($"{cortas.Count} curva(s) vertical(es) con longitud menor a la mínima: PVI " +
                                  string.Join(", ", cortas.Select(c => $"{c.N} ({Formato.Prog(c.Progresiva)})")) + ".");
                if (faltan.Count > 0)
                    v.Motivos.Add($"{faltan.Count} PVI sin curva vertical que la requieren: PVI " +
                                  string.Join(", ", faltan.Select(c => $"{c.N} ({Formato.Prog(c.Progresiva)})")) + ".");
            }
            if (d.Resultados != null)
            {
                v.VisibilidadCumple = d.Sectores.Count == 0;
                if (d.Sectores.Count > 0)
                    v.Motivos.Add($"{d.Sectores.Count} sector(es) del eje sin visibilidad de parada suficiente: " +
                                  string.Join(", ", d.Sectores.Select(s => $"{Formato.Prog(s.Inicio)}–{Formato.Prog(s.Fin)} ({Formato.SentidoTxt(s.Sentido).ToLower()})")) + ".");
            }
            return v;
        }
    }

    public static class Informe
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string F(double v, int dec = 2) => double.IsNaN(v) ? "—" : v.ToString("F" + dec, Inv);
        private static string H(string s) => WebUtility.HtmlEncode(s ?? "");
        private static string Pct(double i) => (i * 100.0).ToString("0.00", Inv) + " %";

        public static void EscribirHtml(string ruta, DatosInforme d)
        {
            var p = d.P;
            var sb = new StringBuilder();
            bool hayVis = d.Resultados != null;
            bool hayCur = d.Curvas != null;
            int nC = hayVis ? d.Resultados.Count(r => r.Estado == EstadoPunto.Cumple) : 0;
            int nN = hayVis ? d.Resultados.Count(r => r.Estado == EstadoPunto.NoCumple) : 0;
            int nE = hayVis ? d.Resultados.Count(r => r.Estado == EstadoPunto.NoEvaluable) : 0;
            int cC = hayCur ? d.Curvas.Count(r => r.Cumple) : 0;
            int cN = hayCur ? d.Curvas.Count(r => r.Estado == EstadoCurva.NoCumple) : 0;
            int cS = hayCur ? d.Curvas.Count(r => r.Estado == EstadoCurva.SinCurvaRequerida) : 0;

            sb.AppendLine("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.AppendLine("<title>Verificación de visibilidad de parada y curvas verticales</title><style>");
            sb.AppendLine(@"body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1d1d1f;background:#fff}
h1{font-size:22px;margin:0 0 4px} h2{font-size:17px;margin:28px 0 8px;border-bottom:2px solid #1f4e79;padding-bottom:4px;color:#1f4e79}
.sub{color:#555;font-size:13px} table{border-collapse:collapse;font-size:12.5px;width:100%}
th,td{border:1px solid #c9ced6;padding:4px 7px;text-align:right} th{background:#1f4e79;color:#fff;position:sticky;top:0}
td.t,th.t{text-align:left} tr:nth-child(even) td{background:#f5f7fa}
.ok{color:#1b7a2f;font-weight:600}.no{color:#b3261e;font-weight:700}.ne{color:#8a6d00}
.kpi{display:flex;gap:12px;flex-wrap:wrap}.kpi div{border:1px solid #c9ced6;border-radius:8px;padding:10px 16px;min-width:130px}
.kpi b{display:block;font-size:22px}.wrap{overflow-x:auto;max-height:70vh}
.veredicto{border-radius:10px;padding:14px 18px;margin:16px 0;border:2px solid}
.veredicto.si{background:#e8f5ec;border-color:#1b7a2f}.veredicto.nop{background:#fdecea;border-color:#b3261e}
.veredicto .grande{font-size:24px;font-weight:800;letter-spacing:.5px}.veredicto.si .grande{color:#1b7a2f}.veredicto.nop .grande{color:#b3261e}
.veredicto ul{margin:8px 0 0 18px;padding:0;font-size:13.5px}
.nota{font-size:12.5px;color:#444;line-height:1.5} .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:4px 24px;font-size:13px}
@media print{.wrap{max-height:none;overflow:visible} th{position:static}}");
            sb.AppendLine("</style></head><body>");

            sb.AppendLine(hayVis
                ? "<h1>Verificación de distancia de visibilidad de parada (DVP)</h1>"
                : "<h1>Verificación de curvas verticales por visibilidad</h1>");
            sb.AppendLine($"<div class=\"sub\">Generado el {DateTime.Now:dd/MM/yyyy HH:mm} · Dibujo: {H(d.Dibujo)} · Tiempo de cálculo: {d.Duracion.TotalSeconds:F1} s</div>");

            var ver = Veredicto.De(d);
            sb.AppendLine($"<div class=\"veredicto {(ver.Cumple ? "si" : "nop")}\"><div class=\"grande\">VERIFICACIÓN: {(ver.Cumple ? "CUMPLE" : "NO CUMPLE")}</div><ul>");
            if (ver.CurvasCumplen != null)
                sb.AppendLine($"<li>Curvas verticales por visibilidad: <b>{(ver.CurvasCumplen.Value ? "CUMPLEN" : "NO CUMPLEN")}</b></li>");
            if (ver.VisibilidadCumple != null)
                sb.AppendLine($"<li>Distancia de visibilidad de parada a lo largo del eje: <b>{(ver.VisibilidadCumple.Value ? "CUMPLE en todo el tramo evaluado" : "NO CUMPLE")}</b></li>");
            foreach (var m in ver.Motivos) sb.AppendLine($"<li>{H(m)}</li>");
            if (hayVis && nE > 0)
                sb.AppendLine($"<li>{nE} punto(s) no evaluables (fin de eje o sin datos); revíselos en el detalle.</li>");
            sb.AppendLine("</ul></div>");

            sb.AppendLine("<h2>Datos del análisis</h2><div class=\"grid\">");
            sb.AppendLine($"<div>Eje: <b>{H(d.Eje)}</b></div><div>Perfil (rasante): <b>{H(d.Perfil)}</b></div>");
            sb.AppendLine($"<div>Superficie de obstrucción: <b>{(hayVis ? H(d.Superficie) : "— (no aplica)")}</b></div>");
            sb.AppendLine($"<div>Rango: <b>{Formato.Prog(d.SMin)} – {Formato.Prog(d.SMax)}</b></div>");
            string vel = p.TramosVelocidad.Count > 0
                ? $"por tramos ({p.TramosVelocidad.Count}) desde {H(Path.GetFileName(p.ArchivoVelocidades))}; fuera de ellos {F(p.VelocidadDiseno, 0)} km/h"
                : $"{F(p.VelocidadDiseno, 0)} km/h";
            sb.AppendLine($"<div>Velocidad de diseño: <b>{vel}</b></div>");
            sb.AppendLine($"<div>tp = <b>{F(p.TiempoPercepcion, 2)} s</b> · a = <b>{F(p.Desaceleracion, 2)} m/s²</b></div>");
            sb.AppendLine($"<div>Altura de ojo / objeto: <b>{F(p.AlturaOjo)} m / {F(p.AlturaObjeto)} m</b></div>");
            if (hayVis)
            {
                sb.AppendLine($"<div>Desfase carril creciente / decreciente: <b>{F(p.DesfaseCreciente)} m / {F(p.DesfaseDecreciente)} m</b></div>");
                sb.AppendLine($"<div>Intervalo de evaluación: <b>{F(p.Intervalo, 1)} m</b> · Sentido: <b>{p.Sentido}</b></div>");
                sb.AppendLine($"<div>Criterio de pendiente: <b>{(p.Criterio == CriterioPendiente.Desfavorable ? "más desfavorable en la DVP" : "promedio en la DVP")}</b></div>");
                sb.AppendLine($"<div>Muestreo de la visual: <b>{F(p.PasoMuestreo, 2)} m</b> · Paso de búsqueda: <b>{F(p.PasoBusqueda, 2)} m</b></div>");
            }
            if (hayCur)
            {
                sb.AppendLine($"<div>Longitud de curva: <b>{(p.CriterioL == CriterioLongitud.Formula ? "según caso D&lt;L / D&gt;L" : "mayor de ambas fórmulas")}</b></div>");
                sb.AppendLine($"<div>Umbral de A para exigir curva: <b>{F(p.UmbralA)} %</b></div>");
                sb.AppendLine($"<div>Longitud mínima absoluta de curva: <b>{(p.LongitudMinima > 0 ? F(p.LongitudMinima) + " m" : "no aplicada")}</b></div>");
                string da = p.DistanciaAdelanto > 0 || p.TramosVelocidad.Any(t => !double.IsNaN(t.Da))
                    ? $"{F(p.DistanciaAdelanto, 0)} m" + (p.TramosVelocidad.Any(t => !double.IsNaN(t.Da)) ? " (o la del tramo)" : "") + $" · objeto {F(p.AlturaObjetoAdelanto)} m"
                    : "no evaluada";
                sb.AppendLine($"<div>Visibilidad de adelantamiento Da: <b>{da}</b></div>");
            }
            sb.AppendLine("</div>");

            sb.AppendLine("<h2>Resumen</h2>");
            if (hayVis)
            {
                sb.AppendLine("<p class=\"sub\">Visibilidad de parada a lo largo del eje</p><div class=\"kpi\">");
                sb.AppendLine($"<div>Puntos evaluados<b>{d.Resultados.Count}</b></div>");
                sb.AppendLine($"<div>Cumplen<b class=\"ok\">{nC}</b></div>");
                sb.AppendLine($"<div>No cumplen<b class=\"no\">{nN}</b></div>");
                sb.AppendLine($"<div>No evaluables<b class=\"ne\">{nE}</b></div>");
                sb.AppendLine($"<div>Sectores deficientes<b class=\"{(d.Sectores.Count > 0 ? "no" : "ok")}\">{d.Sectores.Count}</b></div>");
                sb.AppendLine("</div>");
            }
            if (hayCur)
            {
                sb.AppendLine("<p class=\"sub\">Curvas verticales (PVI interiores del perfil)</p><div class=\"kpi\">");
                sb.AppendLine($"<div>PVI analizados<b>{d.Curvas.Count}</b></div>");
                sb.AppendLine($"<div>Cumplen<b class=\"ok\">{cC}</b></div>");
                sb.AppendLine($"<div>Curvas cortas<b class=\"{(cN > 0 ? "no" : "ok")}\">{cN}</b></div>");
                sb.AppendLine($"<div>Sin curva y la requieren<b class=\"{(cS > 0 ? "no" : "ok")}\">{cS}</b></div>");
                sb.AppendLine("</div>");
            }

            if (hayCur) EscribirSeccionCurvas(sb, d);

            if (hayVis) {
            if (d.MuestrasFuera > 0 || d.PuntosSinSuperficie > 0)
            {
                sb.AppendLine("<p class=\"nota\"><b>Advertencia:</b> ");
                if (d.MuestrasFuera > 0)
                    sb.Append($"{d.MuestrasFuera:N0} muestras de visual cayeron fuera de la superficie y se consideraron libres de obstrucción (revise en curvas horizontales que la superficie cubra el interior de la curva). ");
                if (d.PuntosSinSuperficie > 0)
                    sb.Append($"{d.PuntosSinSuperficie:N0} ubicaciones de ojo/objeto no tuvieron cota de superficie y se usó la cota de la rasante en el eje.");
                sb.AppendLine("</p>");
            }

            sb.AppendLine("<h2>Sectores que no cumplen</h2>");
            if (d.Sectores.Count == 0)
                sb.AppendLine("<p class=\"ok\">No se encontraron sectores con visibilidad de parada insuficiente.</p>");
            else
            {
                sb.AppendLine("<div class=\"wrap\"><table><tr><th class=\"t\">N°</th><th class=\"t\">Sentido</th><th>Desde</th><th>Hasta</th><th>Longitud aprox. (m)</th><th>DVP requerida máx. (m)</th><th>Visibilidad disponible mín. (m)</th><th>Déficit máx. (m)</th></tr>");
                int k = 1;
                foreach (var s in d.Sectores)
                {
                    double lon = Math.Max(s.Fin - s.Inicio, 0) + p.Intervalo;
                    sb.AppendLine($"<tr><td class=\"t\">{k++}</td><td class=\"t\">{Formato.SentidoTxt(s.Sentido)}</td><td>{Formato.Prog(s.Inicio)}</td><td>{Formato.Prog(s.Fin)}</td><td>{F(lon, 0)}</td><td>{F(s.DvpMax)}</td><td>{F(s.DisponibleMin)}</td><td class=\"no\">{F(s.DeficitMax)}</td></tr>");
                }
                sb.AppendLine("</table></div>");
            }

            sb.AppendLine("<h2>Detalle por progresiva</h2><div class=\"wrap\"><table>");
            sb.AppendLine("<tr><th>Progresiva</th><th class=\"t\">Sentido</th><th>V (km/h)</th><th>Pendiente usada</th><th>DVP requerida (m)</th><th>Visibilidad disponible (m)</th><th>Margen (m)</th><th class=\"t\">Estado</th><th class=\"t\">Nota</th></tr>");
            foreach (var r in d.Resultados.OrderBy(x => x.Progresiva).ThenBy(x => -x.Sentido))
            {
                string disp = double.IsNaN(r.VisDisponible) ? "—" : (r.DisponibleEsMinimo ? "≥ " : "") + F(r.VisDisponible);
                string margen = double.IsNaN(r.VisDisponible) ? "—" : (r.DisponibleEsMinimo ? "≥ " : "") + F(r.VisDisponible - r.DvpRequerida);
                string est = r.Estado == EstadoPunto.Cumple ? "<span class=\"ok\">CUMPLE</span>"
                           : r.Estado == EstadoPunto.NoCumple ? "<span class=\"no\">NO CUMPLE</span>"
                           : "<span class=\"ne\">No evaluable</span>";
                sb.AppendLine($"<tr><td>{Formato.Prog(r.Progresiva)}</td><td class=\"t\">{Formato.SentidoTxt(r.Sentido)}</td><td>{F(r.Velocidad, 0)}</td><td>{Pct(r.PendienteUsada)}</td><td>{F(r.DvpRequerida)}</td><td>{disp}</td><td>{margen}</td><td class=\"t\">{est}</td><td class=\"t\">{H(r.Nota)}</td></tr>");
            }
            sb.AppendLine("</table></div>");
            } // hayVis

            sb.AppendLine("<h2>Método</h2><div class=\"nota\">");
            if (hayCur)
                sb.AppendLine("<p>Curvas verticales: las pendientes de entrada (Pe) y salida (Ps) se calculan con las progresivas y cotas de los PVI del perfil. " +
                              "Dp se evalúa de ida (Pe, Ps) y de regreso (−Ps, −Pe) y se toma el mayor, redondeado hacia arriba. " +
                              "Convexas: L = A·Dp²/C (Dp &lt; L) o L = 2·Dp − C/A (Dp &gt; L), con C = 200·(√h1+√h2)². " +
                              "Cóncavas (faros): L = A·Dp²/(120+3.5·Dp) o L = 2·Dp − (120+3.5·Dp)/A. " +
                              "Adelantamiento (solo convexas): mismas fórmulas con Da y objeto de 1.30 m. " +
                              "La longitud exigida es la mayor entre la de visibilidad y la mínima absoluta ingresada. " +
                              "La longitud de confort en cóncavas (A·V²/395) es referencial y no interviene en el estado.</p>");
            if (hayVis) {
            sb.AppendLine("<p>DVP requerida: Dp = 0.278·V·tp + V² / (254·((a/9.81) ± i)), con i positiva en subida y negativa en bajada, evaluada en el sentido de circulación. " +
                          "La pendiente se toma sobre toda la longitud de la DVP y el cálculo se itera hasta converger.</p>");
            sb.AppendLine("<p>Visibilidad disponible: desde el ojo, ubicado sobre el centro del carril indicado, se avanza el objeto a lo largo del eje y se traza la visual 3D recta. " +
                          "La visual se muestrea contra la superficie; el primer punto en que la superficie supera la visual define el ocultamiento. " +
                          "La distancia informada se mide a lo largo del eje y se refina por bisección a 0.10 m. " +
                          "Las cotas de ojo y objeto se toman de la superficie en su ubicación (incluye peralte) más la altura correspondiente.</p>");
            sb.AppendLine("<p>Limitaciones: solo se detectan obstrucciones modeladas en la superficie (no vegetación, muros, señales ni edificaciones que no estén en ella). " +
                          "Las obstrucciones de ancho menor al paso de muestreo pueden no detectarse. " +
                          "Los valores deben contrastarse con la norma vigente del proyecto.</p>");
            } // hayVis método
            sb.AppendLine("</div>");
            sb.AppendLine("</body></html>");

            File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(true));
        }

        private static void EscribirSeccionCurvas(StringBuilder sb, DatosInforme d)
        {
            var p = d.P;
            bool hayDa = d.Curvas.Any(c => !double.IsNaN(c.Da));
            sb.AppendLine("<h2>Análisis de curvas verticales por visibilidad</h2>");
            sb.AppendLine("<div class=\"wrap\"><table>");
            sb.Append("<tr><th rowspan=\"2\">N°</th><th rowspan=\"2\">Ubicación</th><th rowspan=\"2\">Ve</th><th rowspan=\"2\">Pe</th><th rowspan=\"2\">Ps</th><th rowspan=\"2\">A</th><th rowspan=\"2\" class=\"t\">Tipo</th>");
            sb.Append("<th colspan=\"4\">Ida</th><th colspan=\"4\">Regreso</th><th rowspan=\"2\">Dp</th>");
            sb.Append("<th rowspan=\"2\">L calc. Dp&gt;L</th><th rowspan=\"2\">L calc. Dp&lt;L</th><th rowspan=\"2\">L confort (cóncava)</th><th rowspan=\"2\">L por visibilidad</th><th rowspan=\"2\">L mín. exigida</th><th rowspan=\"2\">K req.</th>");
            sb.Append("<th rowspan=\"2\">L proyecto</th><th rowspan=\"2\">K proyecto</th><th rowspan=\"2\" class=\"t\">Estado</th><th rowspan=\"2\" class=\"t\">Verificación</th>");
            if (hayDa) sb.Append("<th rowspan=\"2\">Da</th><th rowspan=\"2\">L calc. Da&gt;L</th><th rowspan=\"2\">L calc. Da&lt;L</th><th rowspan=\"2\">L req. adelant.</th><th rowspan=\"2\" class=\"t\">¿Permite adelantar?</th>");
            sb.AppendLine("<th rowspan=\"2\" class=\"t\">Nota</th></tr>");
            sb.AppendLine("<tr><th>Pe 1</th><th>Ps 1</th><th>Dp (Pe 1)</th><th>Dp (Ps 1)</th><th>Pe 2</th><th>Ps 2</th><th>Dp (Pe 2)</th><th>Dp (Ps 2)</th></tr>");
            foreach (var c in d.Curvas)
            {
                string est = c.Cumple
                    ? $"<span class=\"ok\">{CurvasVerticales.EstadoTxt(c.Estado)}</span>"
                    : $"<span class=\"no\">{CurvasVerticales.EstadoTxt(c.Estado)}</span>";
                sb.Append($"<tr><td>{c.N}</td><td>{Formato.Prog(c.Progresiva)}</td><td>{F(c.V, 0)}</td><td>{Pct(c.Pe)}</td><td>{Pct(c.Ps)}</td><td>{F(c.A)} %</td><td class=\"t\">{(c.Convexa ? "Convexa" : "Cóncava")}</td>");
                sb.Append($"<td>{Pct(c.Pe)}</td><td>{Pct(c.Ps)}</td><td>{F(c.DpIdaPe)}</td><td>{F(c.DpIdaPs)}</td><td>{Pct(-c.Ps)}</td><td>{Pct(-c.Pe)}</td><td>{F(c.DpRegPe)}</td><td>{F(c.DpRegPs)}</td><td><b>{F(c.Dp, 0)}</b></td>");
                sb.Append($"<td>{F(c.LDpMayor)}</td><td>{F(c.LDpMenor)}</td><td>{F(c.LConfort)}</td><td>{F(c.LVisibilidad, 0)}</td><td><b>{F(c.LReq, 0)}</b></td><td>{F(c.KReq)}</td>");
                sb.Append($"<td>{(c.TieneCurva ? F(c.LProyecto) : "—")}</td><td>{(c.TieneCurva ? F(c.KProyecto) : "—")}</td><td class=\"t\">{est}</td><td class=\"t\">{H(c.Verificacion)}</td>");
                if (hayDa)
                {
                    string pa = c.PermiteAdelantar == null ? "—" : c.PermiteAdelantar.Value ? "Sí" : "No";
                    sb.Append($"<td>{F(c.Da, 0)}</td><td>{F(c.LDaMayor)}</td><td>{F(c.LDaMenor)}</td><td>{F(c.LDaReq, 0)}</td><td class=\"t\">{pa}</td>");
                }
                string nota = c.Nota;
                if (c.TieneCurva && !string.IsNullOrEmpty(c.TipoEntidad)) nota = (nota.Length > 0 ? nota + "; " : "") + c.TipoEntidad;
                sb.AppendLine($"<td class=\"t\">{H(nota)}</td></tr>");
            }
            sb.AppendLine("</table></div>");
        }

        public static void EscribirCsvCurvas(string ruta, DatosInforme d)
        {
            var cul = CultureInfo.CurrentCulture;
            string sep = cul.TextInfo.ListSeparator;
            if (string.IsNullOrEmpty(sep)) sep = ";";
            string N(double v, int dec = 3) => double.IsNaN(v) ? "" : v.ToString("F" + dec, cul);
            string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(sep, "N", "Ubicacion", "Progresiva (m)", "Ve (km/h)", "Pe (%)", "Ps (%)", "A (%)", "Tipo", "tp (s)", "a (m/s2)",
                "Pe1 (%)", "Ps1 (%)", "Dp Pe1", "Dp Ps1", "Pe2 (%)", "Ps2 (%)", "Dp Pe2", "Dp Ps2", "Dp",
                "L calc Dp>L", "L calc Dp<L", "L confort", "L visibilidad", "L min exigida", "K req", "L proyecto", "K proyecto", "Estado", "Verificacion",
                "Da", "L calc Da>L", "L calc Da<L", "L req adelant", "Permite adelantar", "Nota"));
            foreach (var c in d.Curvas)
            {
                sb.AppendLine(string.Join(sep, c.N, Q(Formato.Prog(c.Progresiva)), N(c.Progresiva), N(c.V, 0),
                    N(c.Pe * 100, 3), N(c.Ps * 100, 3), N(c.A, 3), c.Convexa ? "Convexa" : "Concava",
                    N(d.P.TiempoPercepcion, 2), N(d.P.Desaceleracion, 2),
                    N(c.Pe * 100, 3), N(c.Ps * 100, 3), N(c.DpIdaPe), N(c.DpIdaPs),
                    N(-c.Ps * 100, 3), N(-c.Pe * 100, 3), N(c.DpRegPe), N(c.DpRegPs), N(c.Dp, 0),
                    N(c.LDpMayor), N(c.LDpMenor), N(c.LConfort), N(c.LVisibilidad, 0), N(c.LReq, 0), N(c.KReq),
                    c.TieneCurva ? N(c.LProyecto) : "", c.TieneCurva ? N(c.KProyecto) : "",
                    Q(CurvasVerticales.EstadoTxt(c.Estado)), Q(c.Verificacion),
                    N(c.Da, 0), N(c.LDaMayor), N(c.LDaMenor), N(c.LDaReq, 0),
                    c.PermiteAdelantar == null ? "" : c.PermiteAdelantar.Value ? "Si" : "No",
                    Q(c.Nota)));
            }
            File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>CSV con separador de lista y decimal de la configuración regional (abre directo en Excel).</summary>
        public static void EscribirCsv(string ruta, DatosInforme d)
        {
            var cul = CultureInfo.CurrentCulture;
            string sep = cul.TextInfo.ListSeparator;
            if (string.IsNullOrEmpty(sep)) sep = ";";
            string N(double v, int dec = 3) => double.IsNaN(v) ? "" : v.ToString("F" + dec, cul);
            string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(sep, "Progresiva", "Progresiva (m)", "Sentido", "V (km/h)", "Pendiente usada (%)",
                "DVP requerida (m)", "Visibilidad disponible (m)", "Disponible es minimo", "Margen (m)", "Estado", "Nota"));
            foreach (var r in d.Resultados.OrderBy(x => x.Progresiva).ThenBy(x => -x.Sentido))
            {
                sb.AppendLine(string.Join(sep,
                    Q(Formato.Prog(r.Progresiva)), N(r.Progresiva), Formato.SentidoTxt(r.Sentido), N(r.Velocidad, 0),
                    N(r.PendienteUsada * 100.0, 3), N(r.DvpRequerida), N(r.VisDisponible),
                    r.DisponibleEsMinimo ? "Si" : "No",
                    double.IsNaN(r.VisDisponible) ? "" : N(r.VisDisponible - r.DvpRequerida),
                    r.Estado == EstadoPunto.Cumple ? "CUMPLE" : r.Estado == EstadoPunto.NoCumple ? "NO CUMPLE" : "No evaluable",
                    Q(r.Nota)));
            }
            File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>Lee tramos de velocidad: "prog_inicio;prog_fin;V[;Da]" por línea. Ignora encabezados y líneas con #.</summary>
        public static List<TramoVelocidad> LeerVelocidades(string ruta)
        {
            var lista = new List<TramoVelocidad>();
            foreach (var linea0 in File.ReadAllLines(ruta))
            {
                string linea = linea0.Trim();
                if (linea.Length == 0 || linea.StartsWith("#")) continue;
                string[] partes = linea.Split(new[] { ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length < 3) partes = linea.Split(',');
                if (partes.Length < 3) continue;
                if (!Num(partes[0], out double a) || !Num(partes[1], out double b) || !Num(partes[2], out double v)) continue;
                var t = new TramoVelocidad { Inicio = Math.Min(a, b), Fin = Math.Max(a, b), V = v };
                if (partes.Length >= 4 && Num(partes[3], out double da) && da > 0) t.Da = da;
                lista.Add(t);
            }
            return lista;
        }

        private static bool Num(string s, out double v)
        {
            s = s.Trim().Replace(" ", "");
            // admite progresiva "2+340.5"
            if (s.Contains("+"))
            {
                var pp = s.Split('+');
                if (pp.Length == 2 && Num(pp[0], out double km) && Num(pp[1], out double m)) { v = km * 1000 + m; return true; }
                v = 0; return false;
            }
            return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
