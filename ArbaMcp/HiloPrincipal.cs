using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ArbaMcp
{
    /// <summary>
    /// Cola de trabajos que se ejecutan en el hilo principal de AutoCAD (evento Idle).
    /// El servidor HTTP corre en otro hilo y nunca toca la API de AutoCAD directamente.
    /// </summary>
    internal static class HiloPrincipal
    {
        /// <summary>
        /// Trabajo encolado. Mientras está en cola puede descartarse (por ejemplo, cuando el servidor
        /// ya respondió "tiempo agotado"): así no se ejecuta a destiempo cuando Civil 3D se libere.
        /// </summary>
        internal sealed class Pendiente
        {
            private const int EnCola = 0, Ejecutando = 1, Descartado = 2;
            private int _estado = EnCola;

            internal Func<object> Funcion;
            internal readonly TaskCompletionSource<object> Resultado =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<object> Tarea => Resultado.Task;

            /// <summary>Descarta el trabajo si aún no empezó. Devuelve true si se descartó (no se ejecutará).</summary>
            public bool Descartar() => Interlocked.CompareExchange(ref _estado, Descartado, EnCola) == EnCola;

            /// <summary>Lo reclama el hilo principal justo antes de ejecutarlo. False si ya fue descartado.</summary>
            internal bool Reclamar() => Interlocked.CompareExchange(ref _estado, Ejecutando, EnCola) == EnCola;
        }

        private static readonly ConcurrentQueue<Pendiente> Cola = new ConcurrentQueue<Pendiente>();
        private static bool _enganchado;

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public static void Iniciar()
        {
            if (_enganchado) return;
            AcApp.Idle += AlEstarInactivo;
            _enganchado = true;
        }

        public static void Detener()
        {
            if (!_enganchado) return;
            AcApp.Idle -= AlEstarInactivo;
            _enganchado = false;
        }

        /// <summary>Encola una función y devuelve una tarea que se completa cuando el hilo principal la ejecuta.</summary>
        public static Task<object> Ejecutar(Func<object> funcion) => Encolar(funcion).Tarea;

        /// <summary>Como Ejecutar, pero devuelve el trabajo para poder descartarlo si sigue en cola.</summary>
        public static Pendiente Encolar(Func<object> funcion)
        {
            var t = new Pendiente { Funcion = funcion };
            Cola.Enqueue(t);
            Despertar();
            return t;
        }

        /// <summary>Envía un mensaje vacío a la ventana principal para que el bucle de mensajes dispare Idle cuanto antes.</summary>
        private static void Despertar()
        {
            try
            {
                var w = AcApp.MainWindow;
                if (w != null) PostMessage(w.Handle, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        private static void AlEstarInactivo(object sender, EventArgs e)
        {
            // Se atiende un trabajo por vez para no bloquear la interfaz con ráfagas largas
            if (!Cola.TryDequeue(out var t)) return;
            if (t.Reclamar())
            {
                try { t.Resultado.TrySetResult(t.Funcion()); }
                catch (System.Exception ex) { t.Resultado.TrySetException(ex); }
            }
            else
            {
                // El servidor ya respondió "tiempo agotado": se salta para no ejecutarlo a destiempo
                t.Resultado.TrySetCanceled();
            }
            if (!Cola.IsEmpty) Despertar();
        }
    }
}
