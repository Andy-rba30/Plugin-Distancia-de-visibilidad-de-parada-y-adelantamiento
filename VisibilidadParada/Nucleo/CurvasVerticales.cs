using System;
using System.Collections.Generic;

namespace VisibilidadParada.Nucleo
{
    /// <summary>PVI leído del perfil de Civil 3D.</summary>
    public class PviDato
    {
        public int N;
        public double Progresiva, Cota;
        public bool TieneCurva;
        public double LProyecto;          // longitud de la curva vertical proyectada (m)
        public string TipoEntidad = "";   // tipo de subentidad según Civil 3D
    }

    public enum EstadoCurva { Cumple, NoCumple, SinCurvaRequerida, SinCurvaAceptable }

    public class ResultadoCurva
    {
        public int N;
        public double Progresiva, V;
        public double Pe, Ps;             // decimal
        public double A;                  // %
        public bool Convexa;

        // Ida: Pe1 = Pe, Ps1 = Ps. Regreso: Pe2 = −Ps, Ps2 = −Pe
        public double DpIdaPe, DpIdaPs, DpRegPe, DpRegPs;
        public double Dp;                 // máximo de los cuatro, redondeado hacia arriba

        public double LDpMayor, LDpMenor; // L para Dp > L y para Dp < L
        public double LVisibilidad;       // longitud mínima por visibilidad de parada (redondeada)
        public double LReq;               // longitud mínima exigida = máx(L visibilidad, L mínima absoluta)
        public double LConfort = double.NaN; // cóncavas: A·V²/395 (referencial)

        public double Da = double.NaN, LDaMayor = double.NaN, LDaMenor = double.NaN, LDaReq = double.NaN;
        public bool? PermiteAdelantar;

        public bool TieneCurva;
        public string TipoEntidad = "";
        public double LProyecto = double.NaN, KProyecto = double.NaN, KReq = double.NaN;

        public EstadoCurva Estado;
        public string Verificacion = "";   // texto de la comprobación, p. ej. "Lp = 120.00 m ≥ Lmín = 114 m → CUMPLE"
        public string Nota = "";

        public bool Cumple => Estado == EstadoCurva.Cumple || Estado == EstadoCurva.SinCurvaAceptable;
    }

    public static class CurvasVerticales
    {
        /// <summary>Constante de curva convexa: 200·(√h1 + √h2)², redondeada (404 para 1.07/0.15; 946 para 1.07/1.30).</summary>
        public static double ConstanteConvexa(double h1, double h2)
            => Math.Round(200.0 * Math.Pow(Math.Sqrt(h1) + Math.Sqrt(h2), 2));

        /// <summary>Constante de curva cóncava (faros a 0.60 m, 1°): 120 + 3.5·D.</summary>
        public static double ConstanteConcava(double d) => 120.0 + 3.5 * d;

        private static double Seleccionar(double lMenor, double lMayor, double d, CriterioLongitud c)
        {
            double L = c == CriterioLongitud.Maximo
                ? Math.Max(lMenor, lMayor)
                : (lMenor >= d ? lMenor : lMayor); // caso D < L válido solo si L resulta ≥ D
            return Math.Max(L, 0.0);
        }

        private static double Techo(double x)
        {
            double r = Math.Ceiling(x - 1e-9);
            return r <= 0 ? 0.0 : r; // evita "-0"
        }

        public static List<ResultadoCurva> Analizar(List<PviDato> pvis, Parametros p)
        {
            var res = new List<ResultadoCurva>();
            if (pvis == null || pvis.Count < 3) return res;

            double cParada = ConstanteConvexa(p.AlturaOjo, p.AlturaObjeto);
            double cAdel = ConstanteConvexa(p.AlturaOjo, p.AlturaObjetoAdelanto);

            for (int k = 1; k < pvis.Count - 1; k++)
            {
                var a = pvis[k - 1]; var b = pvis[k]; var c = pvis[k + 1];
                double dx1 = b.Progresiva - a.Progresiva, dx2 = c.Progresiva - b.Progresiva;
                if (dx1 <= 1e-6 || dx2 <= 1e-6) continue;

                var r = new ResultadoCurva
                {
                    N = b.N,
                    Progresiva = b.Progresiva,
                    Pe = (b.Cota - a.Cota) / dx1,
                    Ps = (c.Cota - b.Cota) / dx2,
                    V = p.VelocidadEn(b.Progresiva),
                    TieneCurva = b.TieneCurva,
                    TipoEntidad = b.TipoEntidad
                };
                r.A = Math.Abs(r.Ps - r.Pe) * 100.0;
                r.Convexa = r.Ps < r.Pe;

                double tp = p.TiempoPercepcion, ac = p.Desaceleracion, V = r.V;
                r.DpIdaPe = Analizador.Dvp(V, tp, ac, r.Pe);
                r.DpIdaPs = Analizador.Dvp(V, tp, ac, r.Ps);
                r.DpRegPe = Analizador.Dvp(V, tp, ac, -r.Ps);
                r.DpRegPs = Analizador.Dvp(V, tp, ac, -r.Pe);
                r.Dp = Techo(Math.Max(Math.Max(r.DpIdaPe, r.DpIdaPs), Math.Max(r.DpRegPe, r.DpRegPs)));

                if (r.A < 1e-6)
                {
                    r.LDpMayor = r.LDpMenor = r.LReq = 0;
                    r.Nota = "PVI sin cambio de pendiente";
                }
                else
                {
                    double C = r.Convexa ? cParada : ConstanteConcava(r.Dp);
                    r.LDpMenor = r.A * r.Dp * r.Dp / C;
                    r.LDpMayor = 2.0 * r.Dp - C / r.A;
                    r.LVisibilidad = Techo(Seleccionar(r.LDpMenor, r.LDpMayor, r.Dp, p.CriterioL));
                    r.LReq = Math.Max(r.LVisibilidad, p.LongitudMinima);
                    r.KReq = r.LReq / r.A;
                    if (r.LVisibilidad <= 0)
                        r.Nota = "La visibilidad de parada no condiciona la longitud" + (p.LongitudMinima > 0 ? "; rige la L mínima absoluta" : "");
                    else if (p.LongitudMinima > r.LVisibilidad)
                        r.Nota = "Rige la L mínima absoluta";

                    if (!r.Convexa) r.LConfort = r.A * V * V / 395.0;

                    double da = p.DaEn(b.Progresiva);
                    if (r.Convexa && da > 0)
                    {
                        r.Da = da;
                        r.LDaMenor = r.A * da * da / cAdel;
                        r.LDaMayor = 2.0 * da - cAdel / r.A;
                        r.LDaReq = Techo(Seleccionar(r.LDaMenor, r.LDaMayor, da, p.CriterioL));
                    }
                }

                var inv = System.Globalization.CultureInfo.InvariantCulture;
                if (b.TieneCurva)
                {
                    r.LProyecto = b.LProyecto;
                    if (r.A > 1e-6) r.KProyecto = b.LProyecto / r.A;
                    bool ok = b.LProyecto + 0.005 >= r.LReq;
                    r.Estado = ok ? EstadoCurva.Cumple : EstadoCurva.NoCumple;
                    r.Verificacion = ok
                        ? string.Format(inv, "Lp = {0:F2} m ≥ Lmín = {1:F0} m (Kp = {2:F2} ≥ Kmín = {3:F2}) → CUMPLE", r.LProyecto, r.LReq, r.KProyecto, r.KReq)
                        : string.Format(inv, "Lp = {0:F2} m < Lmín = {1:F0} m (Kp = {2:F2} < Kmín = {3:F2}); faltan {4:F2} m → NO CUMPLE", r.LProyecto, r.LReq, r.KProyecto, r.KReq, r.LReq - r.LProyecto);
                    if (!double.IsNaN(r.LDaReq)) r.PermiteAdelantar = b.LProyecto + 0.005 >= r.LDaReq;
                }
                else
                {
                    bool requiere = r.A >= p.UmbralA;
                    r.Estado = requiere ? EstadoCurva.SinCurvaRequerida : EstadoCurva.SinCurvaAceptable;
                    r.Verificacion = requiere
                        ? (r.LReq > 0
                            ? string.Format(inv, "Sin curva vertical; A = {0:F2} % ≥ {1:F2} % requiere curva de Lmín = {2:F0} m → NO CUMPLE", r.A, p.UmbralA, r.LReq)
                            : string.Format(inv, "Sin curva vertical; A = {0:F2} % ≥ {1:F2} % requiere curva (la visibilidad no fija longitud mínima) → NO CUMPLE", r.A, p.UmbralA))
                        : string.Format(inv, "Sin curva vertical; A = {0:F2} % < {1:F2} %, no requiere curva → CUMPLE", r.A, p.UmbralA);
                    if (!double.IsNaN(r.LDaReq)) r.PermiteAdelantar = false;
                }
                res.Add(r);
            }
            return res;
        }

        public static string EstadoTxt(EstadoCurva e)
        {
            switch (e)
            {
                case EstadoCurva.Cumple: return "CUMPLE";
                case EstadoCurva.NoCumple: return "NO CUMPLE (curva corta)";
                case EstadoCurva.SinCurvaRequerida: return "NO CUMPLE (falta curva)";
                default: return "CUMPLE (no requiere curva)";
            }
        }
    }
}
