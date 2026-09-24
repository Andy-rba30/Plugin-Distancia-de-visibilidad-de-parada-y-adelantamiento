using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ArbaMcp
{
    /// <summary>
    /// Servidor HTTP mínimo (sin http.sys, sin permisos de administrador) que escucha solo en 127.0.0.1.
    /// Contrato:
    ///   GET  /tools                 → lista de herramientas con parámetros
    ///   GET  /ping                  → estado
    ///   POST /execute {tool, args}  → {"ok":true,"result":...} o {"ok":false,"error":"..."}
    /// Puerto: variable de entorno ARBA_MCP_PORT (por defecto 8765). ARBA_MCP=0 desactiva el servidor.
    /// </summary>
    internal static class Servidor
    {
        public const int PuertoPorDefecto = 8765;
        public static int Puerto { get; private set; }
        public static bool Activo => _oyente != null;
        public static string UltimoError { get; private set; } = "";
        public static string TokenActual { get; private set; }

        private static TcpListener _oyente;
        private static CancellationTokenSource _cts;

        internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };

        
        public static void Iniciar()
        {
            if (Activo) return;
            if (Environment.GetEnvironmentVariable("ARBA_MCP") == "0") return;

            // 32 bytes del generador criptográfico, en hexadecimal (64 caracteres); un Guid no está pensado como secreto
            TokenActual = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            try {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArbaMcp");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "token");
                File.WriteAllText(file, TokenActual);
                var fi = new FileInfo(file);
                var sec = fi.GetAccessControl();
                sec.SetAccessRuleProtection(true, false);
                var id = System.Security.Principal.WindowsIdentity.GetCurrent().User;
                sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(id, System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
                fi.SetAccessControl(sec);
            } catch { }


            Puerto = PuertoPorDefecto;
            if (int.TryParse(Environment.GetEnvironmentVariable("ARBA_MCP_PORT"), out int p) && p > 0 && p < 65536) Puerto = p;

            try
            {
                _cts = new CancellationTokenSource();
                _oyente = new TcpListener(IPAddress.Loopback, Puerto);
                _oyente.Start();
                HiloPrincipal.Iniciar();
                Historial.Iniciar();
                Task.Run(() => Bucle(_cts.Token));
                Historial.Registrar("Servidor MCP escuchando en http://127.0.0.1:" + Puerto + "/");
            }
            catch (System.Exception ex)
            {
                UltimoError = ex.Message;
                _oyente = null;
                Historial.Registrar("No se pudo iniciar el servidor MCP: " + ex.Message);
            }
        }

        public static void Detener()
        {
            try { _cts?.Cancel(); _oyente?.Stop(); } catch { }
            _oyente = null;
            HiloPrincipal.Detener();
        }

        private static async Task Bucle(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient cliente;
                try { cliente = await _oyente.AcceptTcpClientAsync(); }
                catch { break; }
                _ = Task.Run(() => Atender(cliente));
            }
        }

        private static async Task Atender(TcpClient cliente)
        {
            using (cliente)
            {
                try
                {
                    cliente.ReceiveTimeout = 10000;
                    var ns = cliente.GetStream();

                    // 1. Cabeceras
                    var acumulado = new MemoryStream();
                    var tmp = new byte[8192];
                    int finCab = -1;
                    while (finCab < 0)
                    {
                        int n = await ns.ReadAsync(tmp, 0, tmp.Length);
                        if (n <= 0) return;
                        acumulado.Write(tmp, 0, n);
                        finCab = Buscar(acumulado.GetBuffer(), (int)acumulado.Length, "\r\n\r\n");
                        if (acumulado.Length > 1 << 20) { await Responder(ns, 413, Error("Petición demasiado grande")); return; }
                    }

                    string cabeceras = Encoding.ASCII.GetString(acumulado.GetBuffer(), 0, finCab);
                    var lineas = cabeceras.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    var partes = lineas[0].Split(' ');
                    if (partes.Length < 2) { await Responder(ns, 400, Error("Petición inválida")); return; }
                    string metodo = partes[0].ToUpperInvariant();
                    string ruta = partes[1];
                    int qs = ruta.IndexOf('?');
                    if (qs >= 0) ruta = ruta.Substring(0, qs);

                    
                    int largo = 0;
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var l in lineas.Skip(1)) {
                        if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(l.Substring(15).Trim(), out largo);
                        int idx = l.IndexOf(':');
                        if (idx > 0) headers[l.Substring(0, idx).Trim()] = l.Substring(idx + 1).Trim();
                    }

                    // 2. Cuerpo
                    int inicioCuerpo = finCab + 4;
                    var cuerpo = new MemoryStream();
                    cuerpo.Write(acumulado.GetBuffer(), inicioCuerpo, (int)acumulado.Length - inicioCuerpo);
                    while (cuerpo.Length < largo)
                    {
                        int n = await ns.ReadAsync(tmp, 0, tmp.Length);
                        if (n <= 0) break;
                        cuerpo.Write(tmp, 0, n);
                    }
                    string textoCuerpo = Encoding.UTF8.GetString(cuerpo.GetBuffer(), 0, (int)Math.Min(cuerpo.Length, largo));

                    // Las validaciones van DESPUÉS de leer el cuerpo: si se cierra la conexión con bytes
                    // sin leer, Windows envía un reset y el cliente ve un error en vez del 401/403/415.
                    if (headers.ContainsKey("Origin")) { await Responder(ns, 403, Error("Forbidden")); return; }
                    
                    if (!headers.TryGetValue("Host", out string host) || (host != "127.0.0.1:" + Puerto && host != "localhost:" + Puerto)) { await Responder(ns, 400, Error("Bad Request")); return; }
                    
                    if (!headers.TryGetValue("X-Arba-Token", out string tokenReq) || tokenReq != TokenActual) { await Responder(ns, 401, Error("Unauthorized")); return; }
                    
                    if (metodo == "POST" && (!headers.TryGetValue("Content-Type", out string ct) || !ct.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))) { await Responder(ns, 415, Error("Unsupported Media Type")); return; }

                    // 3. Enrutado
                    if (metodo == "OPTIONS") { await Responder(ns, 204, ""); return; }
                    if (metodo == "GET" && (ruta == "/tools" || ruta == "/tools/"))
                    {
                        await Responder(ns, 200, JsonSerializer.Serialize(new { ok = true, tools = Herramientas.Describir() }, Json));
                        return;
                    }
                    if (metodo == "GET" && (ruta == "/ping" || ruta == "/"))
                    {
                        await Responder(ns, 200, JsonSerializer.Serialize(new { ok = true, servidor = "ArbaMcp", puerto = Puerto }, Json));
                        return;
                    }
                    if (metodo == "POST" && (ruta == "/execute" || ruta == "/execute/"))
                    {
                        await Responder(ns, 200, await Ejecutar(textoCuerpo));
                        return;
                    }
                    await Responder(ns, 404, Error("Ruta no encontrada: " + metodo + " " + ruta));
                }
                catch (System.Exception ex)
                {
                    try { await Responder(cliente.GetStream(), 500, Error(ex.Message)); } catch { }
                }
            }
        }

        /// <summary>Ejecuta {tool, args, timeout_s} en el hilo principal y devuelve el JSON de respuesta.</summary>
        private static async Task<string> Ejecutar(string cuerpo)
        {
            string nombre = "";
            JsonElement args = default;
            int timeoutS = 120;
            try
            {
                using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(cuerpo) ? "{}" : cuerpo))
                {
                    var raiz = doc.RootElement;
                    if (raiz.TryGetProperty("tool", out var t)) nombre = t.GetString() ?? "";
                    if (raiz.TryGetProperty("args", out var a)) args = a.Clone();
                    if (raiz.TryGetProperty("timeout_s", out var to) && to.TryGetInt32(out int ts) && ts > 0) timeoutS = ts;
                }
            }
            catch (System.Exception ex) { return Error("JSON inválido: " + ex.Message); }

            var herramienta = Herramientas.Buscar(nombre);
            if (herramienta == null) return Error("Herramienta desconocida: '" + nombre + "'. Consulta GET /tools.");

            Historial.Registrar("MCP → " + nombre);
            var reloj = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Task<object> tareaPrincipal;
                HiloPrincipal.Pendiente pendiente = null;
                if (herramienta.EjecutarAsync != null)
                {
                    tareaPrincipal = herramienta.EjecutarAsync(args);
                }
                else
                {
                    pendiente = HiloPrincipal.Encolar(() => herramienta.Ejecutar(args));
                    tareaPrincipal = pendiente.Tarea;
                }

                var terminada = await Task.WhenAny(tareaPrincipal, Task.Delay(TimeSpan.FromSeconds(timeoutS + 5)));
                if (terminada != tareaPrincipal)
                {
                    string detalle;
                    if (pendiente == null)
                        detalle = "el comando pudo haberse enviado; consulta leer_historial.";
                    else if (pendiente.Descartar())
                        detalle = "la acción seguía en cola y se ha descartado: no se ejecutó.";
                    else
                        detalle = "Civil 3D empezó a ejecutarla y sigue ocupado; terminará por su cuenta.";
                    Historial.Registrar("MCP ← " + nombre + " TIEMPO AGOTADO (" + timeoutS + " s): " + detalle);
                    return Error("Tiempo agotado (" + timeoutS + " s): " + detalle + " Civil 3D puede estar ocupado o con un cuadro de diálogo abierto.");
                }
                object resultado = await tareaPrincipal;
                Historial.Registrar("MCP ✓ " + nombre + " OK (" + reloj.ElapsedMilliseconds + " ms)");
                return JsonSerializer.Serialize(new { ok = true, tool = nombre, ms = reloj.ElapsedMilliseconds, result = resultado }, Json);
            }
            catch (System.Exception ex)
            {
                var raiz = ex; while (raiz.InnerException != null) raiz = raiz.InnerException;
                Historial.Registrar("MCP ← " + nombre + " ERROR: " + raiz.Message + " " + raiz.StackTrace);
                return Error(raiz.GetType().Name + ": " + raiz.Message);
            }
        }

        private static string Error(string msg) => JsonSerializer.Serialize(new { ok = false, error = msg }, Json);

        private static async Task Responder(NetworkStream ns, int codigo, string json)
        {
            string estado = codigo == 200 ? "OK" : codigo == 204 ? "No Content" : codigo == 400 ? "Bad Request" : codigo == 404 ? "Not Found" : codigo == 413 ? "Payload Too Large" : "Internal Server Error";
            var cuerpo = Encoding.UTF8.GetBytes(json ?? "");
            var cab = "HTTP/1.1 " + codigo + " " + estado + "\r\n" +
                      "Content-Type: application/json; charset=utf-8\r\n" +
                      "Content-Length: " + cuerpo.Length + "\r\n" +
                      "Connection: close\r\n\r\n";
            var bytesCab = Encoding.ASCII.GetBytes(cab);
            await ns.WriteAsync(bytesCab, 0, bytesCab.Length);
            if (cuerpo.Length > 0) await ns.WriteAsync(cuerpo, 0, cuerpo.Length);
            await ns.FlushAsync();
        }

        private static int Buscar(byte[] datos, int largo, string patron)
        {
            var p = Encoding.ASCII.GetBytes(patron);
            for (int i = 0; i <= largo - p.Length; i++)
            {
                int k = 0;
                while (k < p.Length && datos[i + k] == p[k]) k++;
                if (k == p.Length) return i;
            }
            return -1;
        }
    }

    /// <summary>Registro de comandos ejecutados y mensajes del plugin, consultable por MCP.</summary>
    internal static class Historial
    {
        private static readonly List<string> Lineas = new List<string>();
        private static readonly object Cerrojo = new object();
        private static bool _iniciado;

        public static void Registrar(string texto)
        {
            lock (Cerrojo)
            {
                Lineas.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + texto);
                if (Lineas.Count > 1000) Lineas.RemoveRange(0, Lineas.Count - 1000);
            }
        }

        public static List<string> Ultimas(int n)
        {
            lock (Cerrojo) return Lineas.Skip(Math.Max(0, Lineas.Count - n)).ToList();
        }

        public static void Iniciar()
        {
            if (_iniciado) return;
            _iniciado = true;
            try
            {
                foreach (Document d in AcApp.DocumentManager) Enganchar(d);
                AcApp.DocumentManager.DocumentCreated += (s, e) => Enganchar(e.Document);
                AcApp.DocumentManager.DocumentToBeDestroyed += (s, e) => Desenganchar(e.Document);
            }
            catch (System.Exception ex) { Registrar("No se pudieron enganchar los eventos de documento: " + ex.Message); }
        }

        // Manejadores con nombre para poder desengancharlos cuando el dibujo se cierra
        private static void AlIniciarComando(object s, CommandEventArgs e) => Registrar("Comando inicia: " + e.GlobalCommandName);
        private static void AlTerminarComando(object s, CommandEventArgs e) => Registrar("Comando termina: " + e.GlobalCommandName);
        private static void AlCancelarComando(object s, CommandEventArgs e) => Registrar("Comando cancelado: " + e.GlobalCommandName);
        private static void AlFallarComando(object s, CommandEventArgs e) => Registrar("Comando falló: " + e.GlobalCommandName);

        private static void Enganchar(Document d)
        {
            if (d == null) return;
            d.CommandWillStart += AlIniciarComando;
            d.CommandEnded += AlTerminarComando;
            d.CommandCancelled += AlCancelarComando;
            d.CommandFailed += AlFallarComando;
        }

        private static void Desenganchar(Document d)
        {
            if (d == null) return;
            d.CommandWillStart -= AlIniciarComando;
            d.CommandEnded -= AlTerminarComando;
            d.CommandCancelled -= AlCancelarComando;
            d.CommandFailed -= AlFallarComando;
        }
    }
}

