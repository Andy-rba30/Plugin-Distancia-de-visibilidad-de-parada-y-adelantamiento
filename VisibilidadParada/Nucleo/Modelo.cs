using System;
using System.Collections.Generic;

namespace VisibilidadParada.Nucleo
{
    /// <summary>Punto 3D simple (independiente de AutoCAD).</summary>
    public readonly struct P3
    {
        public readonly double X, Y, Z;
        public P3(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    /// <summary>Eje en planta: devuelve X,Y para una progresiva y un desfase (+ derecha).</summary>
    public interface IEje
    {
        bool Ubicar(double progresiva, double desfase, out double x, out double y);
    }

    /// <summary>Rasante (perfil longitudinal) del eje.</summary>
    public interface IRasante
    {
        double CotaEn(double progresiva);      // m
        double PendienteEn(double progresiva); // decimal, signo segun progresivas crecientes
    }

    /// <summary>Superficie de obstrucción (corredor con taludes). NaN si el punto está fuera.</summary>
    public interface ISuperficie
    {
        double CotaEn(double x, double y);
    }

    public enum SentidoAnalisis { Ambos, Creciente, Decreciente }
    public enum CriterioPendiente { Desfavorable, Promedio }
    public enum EstadoPunto { Cumple, NoCumple, NoEvaluable }

    public enum CriterioLongitud { Formula, Maximo }

    public class TramoVelocidad
    {
        public double Inicio, Fin, V;
        public double Da = double.NaN;   // distancia de visibilidad de adelantamiento del tramo (opcional)
    }

    public class Parametros
    {
        // Velocidad
        public double VelocidadDiseno = 60.0;                  // km/h (si no hay tramos)
        public List<TramoVelocidad> TramosVelocidad = new List<TramoVelocidad>();
        public string ArchivoVelocidades = "";

        // Fórmula DG-2018 / AASHTO: Dp = 0.278·V·tp + V² / (254·((a/9.81) ± i))
        public double TiempoPercepcion = 2.5;                  // s
        public double Desaceleracion = 3.4;                    // m/s²

        // Geometría de la visual
        public double AlturaOjo = 1.07;                        // m
        public double AlturaObjeto = 0.15;                     // m
        public double DesfaseCreciente = 1.65;                 // m, centro de carril (+ derecha del eje)
        public double DesfaseDecreciente = -1.65;              // m

        // Muestreo
        public double Intervalo = 10.0;                        // m entre progresivas evaluadas
        public double PasoBusqueda = 5.0;                      // m, avance del objeto
        public double PasoMuestreo = 1.0;                      // m, muestreo de la visual contra la superficie
        public double FactorBusqueda = 1.5;                    // busca visibilidad disponible hasta DVP × factor
        public double Tolerancia = 0.005;                      // m

        public SentidoAnalisis Sentido = SentidoAnalisis.Ambos;
        public CriterioPendiente Criterio = CriterioPendiente.Desfavorable;
        public bool Dibujar = true;

        // Curvas verticales
        public CriterioLongitud CriterioL = CriterioLongitud.Formula; // Formula: según caso Dp<L / Dp>L. Maximo: el mayor de ambos
        public double UmbralA = 1.0;
        public double LongitudMinima = 0.0;                    // m, longitud mínima absoluta de curva (0 = no aplicar)                           // % de diferencia algebraica a partir del cual se exige curva
        public double DistanciaAdelanto = 0.0;                 // m (0 = no evaluar adelantamiento)
        public double AlturaObjetoAdelanto = 1.30;             // m (vehículo que viene en sentido contrario)

        public double DaEn(double progresiva)
        {
            foreach (var t in TramosVelocidad)
                if (progresiva >= t.Inicio - 1e-6 && progresiva <= t.Fin + 1e-6 && !double.IsNaN(t.Da) && t.Da > 0) return t.Da;
            return DistanciaAdelanto;
        }

        public double VelocidadEn(double progresiva)
        {
            foreach (var t in TramosVelocidad)
                if (progresiva >= t.Inicio - 1e-6 && progresiva <= t.Fin + 1e-6) return t.V;
            return VelocidadDiseno;
        }
    }

    public class ResultadoPunto
    {
        public double Progresiva;
        public int Sentido;                 // +1 creciente, -1 decreciente
        public double Velocidad;            // km/h
        public double PendienteUsada;       // decimal, signo en el sentido de circulación (+ subida)
        public double DvpRequerida;         // m
        public double VisDisponible = double.NaN; // m
        public bool DisponibleEsMinimo;     // true => la disponible es "≥" (se llegó al límite de búsqueda)
        public EstadoPunto Estado;
        public string Nota = "";

        public double Deficit => Estado == EstadoPunto.NoCumple ? DvpRequerida - VisDisponible : 0.0;
    }

    public class Sector
    {
        public int Sentido;
        public double Inicio, Fin;
        public int Puntos;
        public double DeficitMax;
        public double DisponibleMin = double.MaxValue;
        public double DvpMax;
        public double Desfase;
    }

    public static class Formato
    {
        public static string Prog(double s)
        {
            bool neg = s < 0;
            s = Math.Abs(s);
            int km = (int)Math.Floor(s / 1000.0);
            double m = s - km * 1000.0;
            if (m >= 999.995) { km++; m = 0; }
            return (neg ? "-" : "") + km.ToString() + "+" +
                   m.ToString("000.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string SentidoTxt(int sentido) => sentido > 0 ? "Creciente" : "Decreciente";
    }
}
