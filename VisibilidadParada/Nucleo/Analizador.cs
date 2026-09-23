using System;
using System.Collections.Generic;
using System.Linq;

namespace VisibilidadParada.Nucleo
{
    /// <summary>
    /// Cálculo de la distancia de visibilidad de parada requerida (por pendiente y sentido)
    /// y de la visibilidad disponible (visual 3D contra la superficie de obstrucción).
    /// </summary>
    public class Analizador
    {
        private readonly IEje _eje;
        private readonly IRasante _rasante;
        private readonly ISuperficie _sup;
        private readonly Parametros _p;
        private readonly double _sMin, _sMax;

        /// <summary>Muestras de la visual que cayeron fuera de la superficie (se asumen libres).</summary>
        public long MuestrasFuera { get; private set; }
        /// <summary>Puntos de ojo/objeto sin cota de superficie (se usó la rasante).</summary>
        public long PuntosSinSuperficie { get; private set; }

        public Analizador(IEje eje, IRasante rasante, ISuperficie sup, Parametros p, double sMin, double sMax)
        {
            _eje = eje; _rasante = rasante; _sup = sup; _p = p;
            _sMin = sMin; _sMax = sMax;
        }

        // ------------------------------------------------------------------
        // DVP requerida
        // ------------------------------------------------------------------

        /// <summary>Dp = 0.278·V·tp + V² / (254·((a/9.81) + i)), i en decimal (+ subida, − bajada).</summary>
        public static double Dvp(double v, double tp, double a, double i)
        {
            double f = a / 9.81 + i;
            if (f < 0.05) f = 0.05; // protección ante pendientes extremas
            return 0.278 * v * tp + v * v / (254.0 * f);
        }

        /// <summary>Pendiente a usar en la distancia 'longitud' recorrida desde s en el sentido dado.</summary>
        public double PendienteVentana(double s, int sentido, double longitud)
        {
            double fin = Limitar(s + sentido * longitud);
            double L = Math.Abs(fin - s);
            if (L < 0.5) return SeguroPendiente(s) * sentido;

            if (_p.Criterio == CriterioPendiente.Promedio)
            {
                double z0 = _rasante.CotaEn(s), z1 = _rasante.CotaEn(fin);
                return (z1 - z0) / L; // ya está en el sentido de circulación
            }

            // Desfavorable: la menor pendiente (más en bajada) en el sentido de circulación
            double paso = Math.Min(2.0, L / 4.0);
            double iMin = double.MaxValue;
            for (double d = 0; d <= L + 1e-9; d += paso)
            {
                double st = s + sentido * Math.Min(d, L);
                double i = SeguroPendiente(st) * sentido;
                if (i < iMin) iMin = i;
            }
            return iMin;
        }

        public (double dvp, double i) DvpRequerida(double s, int sentido)
        {
            double v = _p.VelocidadEn(s);
            double i = 0.0;
            double d = Dvp(v, _p.TiempoPercepcion, _p.Desaceleracion, 0.0);
            for (int k = 0; k < 25; k++)
            {
                double iN = PendienteVentana(s, sentido, d);
                double dN = Dvp(v, _p.TiempoPercepcion, _p.Desaceleracion, iN);
                i = iN;
                bool conv = Math.Abs(dN - d) < 0.05;
                d = dN;
                if (conv) break;
            }
            return (d, i);
        }

        // ------------------------------------------------------------------
        // Visibilidad disponible
        // ------------------------------------------------------------------

        private bool Punto(double s, double desfase, double altura, out P3 p)
        {
            p = default;
            if (!_eje.Ubicar(s, desfase, out double x, out double y)) return false;
            double z = _sup.CotaEn(x, y);
            if (double.IsNaN(z))
            {
                PuntosSinSuperficie++;
                try { z = _rasante.CotaEn(s); } catch { return false; }
                if (double.IsNaN(z)) return false;
            }
            p = new P3(x, y, z + altura);
            return true;
        }

        /// <summary>true si ninguna muestra de la superficie corta la visual ojo→objeto.</summary>
        public bool Visible(P3 ojo, P3 obj)
        {
            double dx = obj.X - ojo.X, dy = obj.Y - ojo.Y, dz = obj.Z - ojo.Z;
            double L = Math.Sqrt(dx * dx + dy * dy);
            if (L < 1e-6) return true;
            int n = Math.Max(2, (int)Math.Ceiling(L / _p.PasoMuestreo));
            for (int k = 1; k < n; k++)
            {
                double t = (double)k / n;
                double zs = _sup.CotaEn(ojo.X + t * dx, ojo.Y + t * dy);
                if (double.IsNaN(zs)) { MuestrasFuera++; continue; }
                if (zs > ojo.Z + t * dz + _p.Tolerancia) return false;
            }
            return true;
        }

        private bool VisibleA(P3 ojo, double s, int sentido, double desfase, double L, out bool valido)
        {
            valido = Punto(s + sentido * L, desfase, _p.AlturaObjeto, out P3 obj);
            return !valido || Visible(ojo, obj);
        }

        public ResultadoPunto Evaluar(double s, int sentido)
        {
            var r = new ResultadoPunto { Progresiva = s, Sentido = sentido, Velocidad = _p.VelocidadEn(s) };
            try
            {
                var (dvp, i) = DvpRequerida(s, sentido);
                r.DvpRequerida = dvp;
                r.PendienteUsada = i;

                double desfase = sentido > 0 ? _p.DesfaseCreciente : _p.DesfaseDecreciente;
                double distFin = sentido > 0 ? _sMax - s : s - _sMin;

                if (!Punto(s, desfase, _p.AlturaOjo, out P3 ojo))
                {
                    r.Estado = EstadoPunto.NoEvaluable;
                    r.Nota = "No se pudo ubicar el ojo";
                    return r;
                }

                double limite = Math.Min(dvp * _p.FactorBusqueda, distFin);

                // Distancias a probar: múltiplos del paso + la DVP exacta + el límite
                var Ls = new SortedSet<double>();
                for (double L = _p.PasoBusqueda; L < limite - 1e-6; L += _p.PasoBusqueda) Ls.Add(Math.Round(L, 3));
                if (dvp <= limite) Ls.Add(Math.Round(dvp, 3));
                if (limite > 0.01) Ls.Add(Math.Round(limite, 3));

                double lVis = 0.0, lBloq = double.NaN;
                foreach (double L in Ls)
                {
                    if (VisibleA(ojo, s, sentido, desfase, L, out _)) lVis = L;
                    else { lBloq = L; break; }
                }

                if (!double.IsNaN(lBloq))
                {
                    // Refinar el punto de ocultamiento entre lVis y lBloq
                    double a = lVis, b = lBloq;
                    while (b - a > 0.10)
                    {
                        double m = 0.5 * (a + b);
                        if (VisibleA(ojo, s, sentido, desfase, m, out _)) a = m; else b = m;
                    }
                    r.VisDisponible = a;
                    r.DisponibleEsMinimo = false;
                    r.Estado = a + 1e-6 >= dvp ? EstadoPunto.Cumple : EstadoPunto.NoCumple;
                }
                else
                {
                    r.VisDisponible = limite;
                    r.DisponibleEsMinimo = true;
                    if (distFin + 1e-6 >= dvp) r.Estado = EstadoPunto.Cumple;
                    else
                    {
                        r.Estado = EstadoPunto.NoEvaluable;
                        r.Nota = "Fin del eje/perfil antes de la DVP";
                    }
                }
            }
            catch (Exception ex)
            {
                r.Estado = EstadoPunto.NoEvaluable;
                r.Nota = "Error: " + ex.Message;
            }
            return r;
        }

        // ------------------------------------------------------------------
        // Utilidades
        // ------------------------------------------------------------------

        private double Limitar(double s) => Math.Max(_sMin, Math.Min(_sMax, s));

        private double SeguroPendiente(double s)
        {
            try { return _rasante.PendienteEn(Limitar(s)); } catch { return 0.0; }
        }

        public static List<double> Progresivas(double sMin, double sMax, double intervalo)
        {
            var lista = new List<double>();
            double primera = Math.Ceiling(sMin / intervalo - 1e-9) * intervalo;
            if (primera - sMin > 1e-6) lista.Add(sMin);
            for (double s = primera; s < sMax - 1e-6; s += intervalo) lista.Add(s);
            lista.Add(sMax);
            return lista;
        }

        /// <summary>Agrupa puntos consecutivos que no cumplen en sectores.</summary>
        public static List<Sector> Sectores(List<ResultadoPunto> res, Parametros p)
        {
            var sectores = new List<Sector>();
            foreach (int sentido in new[] { 1, -1 })
            {
                var lista = res.Where(r => r.Sentido == sentido).OrderBy(r => r.Progresiva).ToList();
                Sector act = null;
                foreach (var r in lista)
                {
                    if (r.Estado == EstadoPunto.NoCumple)
                    {
                        if (act == null)
                        {
                            act = new Sector
                            {
                                Sentido = sentido, Inicio = r.Progresiva,
                                Desfase = sentido > 0 ? p.DesfaseCreciente : p.DesfaseDecreciente
                            };
                            sectores.Add(act);
                        }
                        act.Fin = r.Progresiva;
                        act.Puntos++;
                        act.DeficitMax = Math.Max(act.DeficitMax, r.Deficit);
                        act.DisponibleMin = Math.Min(act.DisponibleMin, r.VisDisponible);
                        act.DvpMax = Math.Max(act.DvpMax, r.DvpRequerida);
                    }
                    else act = null; // un punto que cumple o no evaluable corta el sector
                }
            }
            return sectores.OrderBy(x => x.Sentido > 0 ? 0 : 1).ThenBy(x => x.Inicio).ToList();
        }
    }
}
