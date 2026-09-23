using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace VisibilidadParada.Mcp
{
    /// <summary>
    /// Cola de trabajos que se ejecutan en el hilo principal de AutoCAD (evento Idle).
    /// El servidor HTTP corre en otro hilo y nunca toca la API de AutoCAD directamente.
    /// </summary>
    internal static class HiloPrincipal
    {
        private class Trabajo
        {
            public Func<object> Funcion;
            public TaskCompletionSource<object> Resultado;
        }

        private static readonly ConcurrentQueue<Trabajo> Cola = new ConcurrentQueue<Trabajo>();
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
        public static Task<object> Ejecutar(Func<object> funcion)
        {
            var t = new Trabajo
            {
                Funcion = funcion,
                Resultado = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            Cola.Enqueue(t);
            Despertar();
            return t.Resultado.Task;
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
            try { t.Resultado.TrySetResult(t.Funcion()); }
            catch (System.Exception ex) { t.Resultado.TrySetException(ex); }
            if (!Cola.IsEmpty) Despertar();
        }
    }
}
