using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivAlignment = Autodesk.Civil.DatabaseServices.Alignment;
using CivProfile = Autodesk.Civil.DatabaseServices.Profile;
using CivSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace ArbaMcp
{
    public class Parametro
    {
        public string name { get; set; }
        public string type { get; set; }          // string | number | boolean
        public string description { get; set; }
        public bool required { get; set; }
    }

    public class Herramienta
    {
        public string Nombre;
        public string Descripcion;
        public List<Parametro> Parametros = new List<Parametro>();
        public Func<JsonElement, object> Ejecutar;
        public Func<JsonElement, Task<object>> EjecutarAsync;
    }

    /// <summary>
    /// Registro de herramientas expuestas por MCP. Otros plugins de la pestaña ARBA pueden llamar a
    /// Herramientas.Registrar(...) desde su Initialize para añadir las suyas.
    /// </summary>
    public static class Herramientas
    {
        private static readonly List<Herramienta> Lista = new List<Herramienta>();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Registrar(Herramienta h)
        {
            lock (Lista)
            {
                Lista.RemoveAll(x => x.Nombre == h.Nombre);
                Lista.Add(h);
            }
        }

        public static Herramienta Buscar(string nombre)
        {
            lock (Lista) return Lista.FirstOrDefault(h => string.Equals(h.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
        }

        public static object Describir()
        {
            lock (Lista)
                return Lista.Select(h => new { name = h.Nombre, description = h.Descripcion, parameters = h.Parametros }).ToList();
        }

        private static Parametro P(string nombre, string tipo, string desc, bool req = false)
            => new Parametro { name = nombre, type = tipo, description = desc, required = req };

        // ------------------------------------------------------------------ lectura de argumentos
        private static bool Tiene(JsonElement a, string n) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null;

        private static string Str(JsonElement a, string n, string def = null)
        {
            if (!Tiene(a, n)) return def;
            var v = a.GetProperty(n);
            return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
        }

        private static double Num(JsonElement a, string n, double def)
        {
            if (!Tiene(a, n)) return def;
            var v = a.GetProperty(n);
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString().Replace(',', '.'), NumberStyles.Float, Inv, out double d)) return d;
            throw new ArgumentException("El parámetro '" + n + "' debe ser numérico.");
        }

        private static bool Bool(JsonElement a, string n, bool def)
        {
            if (!Tiene(a, n)) return def;
            var v = a.GetProperty(n);
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            if (v.ValueKind == JsonValueKind.String) return v.GetString().Trim().ToLowerInvariant() is "1" or "si" or "sí" or "true" or "yes";
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble() != 0;
            return def;
        }

        private static string Requerido(JsonElement a, string n)
        {
            string s = Str(a, n);
            if (string.IsNullOrWhiteSpace(s)) throw new ArgumentException("Falta el parámetro obligatorio '" + n + "'.");
            return s;
        }

        private static double? N(double v) => double.IsNaN(v) || double.IsInfinity(v) ? (double?)null : Math.Round(v, 4);

        // ------------------------------------------------------------------ acceso al dibujo
        private static Document DocActivo()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("No hay ningún dibujo abierto en Civil 3D.");
            return doc;
        }

        private static ObjectId BuscarAlineamiento(Transaction tr, string nombre)
        {
            foreach (ObjectId id in CivilApplication.ActiveDocument.GetAlignmentIds())
                if (tr.GetObject(id, OpenMode.ForRead) is CivAlignment al && string.Equals(al.Name, nombre, StringComparison.OrdinalIgnoreCase))
                    return id;
            throw new ArgumentException("No existe el alineamiento '" + nombre + "'. Usa listar_alineamientos.");
        }

        private static ObjectId BuscarPerfil(Transaction tr, ObjectId idAl, string nombre)
        {
            var al = (CivAlignment)tr.GetObject(idAl, OpenMode.ForRead);
            foreach (ObjectId id in al.GetProfileIds())
                if (tr.GetObject(id, OpenMode.ForRead) is CivProfile pr && string.Equals(pr.Name, nombre, StringComparison.OrdinalIgnoreCase))
                    return id;
            throw new ArgumentException("El alineamiento '" + al.Name + "' no tiene un perfil llamado '" + nombre + "'. Usa listar_perfiles.");
        }

        private static ObjectId BuscarSuperficie(Transaction tr, string nombre)
        {
            foreach (ObjectId id in CivilApplication.ActiveDocument.GetSurfaceIds())
                if (tr.GetObject(id, OpenMode.ForRead) is CivSurface su && string.Equals(su.Name, nombre, StringComparison.OrdinalIgnoreCase))
                    return id;
            throw new ArgumentException("No existe la superficie '" + nombre + "'. Usa listar_superficies.");
        }

        // ------------------------------------------------------------------ registro inicial
        static Herramientas()
        {
            Registrar(new Herramienta
            {
                Nombre = "ping",
                Descripcion = "Comprueba que el plugin responde. Devuelve versión, dibujo activo y hora.",
                Ejecutar = a =>
                {
                    var doc = AcApp.DocumentManager.MdiActiveDocument;
                    string nombre = null;
                    try { nombre = doc?.Database?.Filename; } catch { }
                    return new
                    {
                        plugin = "ArbaMcp",
                        version = typeof(Herramientas).Assembly.GetName().Version?.ToString(),
                        puerto = Servidor.Puerto,
                        dibujo = nombre,
                        hay_dibujo = doc != null,
                        hora = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };
                }
            });

            Registrar(new Herramienta
            {
                Nombre = "listar_alineamientos",
                Descripcion = "Lista los alineamientos del dibujo activo con sus progresivas inicial y final y sus perfiles.",
                Ejecutar = a =>
                {
                    var doc = DocActivo();
                    var lista = new List<object>();
                    using (doc.LockDocument())
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in CivilApplication.ActiveDocument.GetAlignmentIds())
                        {
                            if (!(tr.GetObject(id, OpenMode.ForRead) is CivAlignment al)) continue;
                            var perfiles = new List<string>();
                            foreach (ObjectId pid in al.GetProfileIds())
                                if (tr.GetObject(pid, OpenMode.ForRead) is CivProfile pr) perfiles.Add(pr.Name);
                            lista.Add(new
                            {
                                nombre = al.Name,
                                inicio = N(al.StartingStation),
                                fin = N(al.EndingStation),
                                longitud = N(al.Length),
                                perfiles
                            });
                        }
                        tr.Commit();
                    }
                    return lista;
                }
            });

            Registrar(new Herramienta
            {
                Nombre = "listar_perfiles",
                Descripcion = "Lista los perfiles de un alineamiento: nombre, tipo (EG terreno, FG rasante), progresivas y número de PVI.",
                Parametros = { P("alineamiento", "string", "Nombre del alineamiento", true) },
                Ejecutar = a =>
                {
                    var doc = DocActivo();
                    string nombreAl = Requerido(a, "alineamiento");
                    var lista = new List<object>();
                    using (doc.LockDocument())
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var idAl = BuscarAlineamiento(tr, nombreAl);
                        var al = (CivAlignment)tr.GetObject(idAl, OpenMode.ForRead);
                        foreach (ObjectId pid in al.GetProfileIds())
                        {
                            if (!(tr.GetObject(pid, OpenMode.ForRead) is CivProfile pr)) continue;
                            int nPvi = 0;
                            try { nPvi = pr.PVIs.Count; } catch { }
                            lista.Add(new
                            {
                                nombre = pr.Name,
                                tipo = pr.ProfileType.ToString(),
                                inicio = N(pr.StartingStation),
                                fin = N(pr.EndingStation),
                                pvis = nPvi
                            });
                        }
                        tr.Commit();
                    }
                    return lista;
                }
            });

            Registrar(new Herramienta
            {
                Nombre = "listar_superficies",
                Descripcion = "Lista las superficies del dibujo activo.",
                Ejecutar = a =>
                {
                    var doc = DocActivo();
                    var lista = new List<object>();
                    using (doc.LockDocument())
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in CivilApplication.ActiveDocument.GetSurfaceIds())
                            if (tr.GetObject(id, OpenMode.ForRead) is CivSurface su)
                                lista.Add(new { nombre = su.Name, tipo = su.GetType().Name });
                        tr.Commit();
                    }
                    return lista;
                }
            });

            Registrar(new Herramienta
            {
                Nombre = "abrir_dibujo",
                Descripcion = "Abre un archivo DWG en Civil 3D y lo deja como dibujo activo.",
                Parametros = { P("ruta", "string", "Ruta completa del DWG", true) },
                Ejecutar = a =>
                {
                    string ruta = Requerido(a, "ruta");
                    if (!File.Exists(ruta)) throw new FileNotFoundException("No existe el archivo: " + ruta);
                    var doc = AcApp.DocumentManager.Open(ruta, false);
                    AcApp.DocumentManager.MdiActiveDocument = doc;
                    return new { abierto = doc.Name };
                }
            });

            Registrar(new Herramienta
            {
                Nombre = "ejecutar_comando",
                Descripcion = "Envía un comando a la línea de comandos del dibujo activo y espera a que termine. Devuelve 'terminado', 'cancelado', 'fallido', 'el comando no se inició' o 'timeout con ESC'. Cada orden va en un grupo de UNDO (un solo Ctrl+Z la revierte).",
                Parametros =
                {
                    P("comando", "string", "Texto del comando, por ejemplo 'REGEN' o '_.ZOOM E'", true),
                    P("timeout_s", "number", "Segundos máximos de espera (por defecto 60). Si se agota, se envían dos ESC para cancelar")
                },
                EjecutarAsync = async a =>
                {
                    string comando = Requerido(a, "comando").Trim();
                    int timeoutMs = (int)(Math.Max(1, Num(a, "timeout_s", 60)) * 1000);
                    var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                    
                    string cmdParse = comando.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    cmdParse = cmdParse.TrimStart('_', '.').ToUpperInvariant();

                    Document doc = DocActivo();
                    int iniciado = 0; // se escribe en el hilo principal y se lee en el del servidor
                    int grupoCerrado = 0;
                    Historial.Registrar("ejecutar_comando: '" + comando + "' con tiempo máximo " + (timeoutMs / 1000) + " s");

                    // Cierra el grupo de UNDO una sola vez, venga de donde venga (fin, cancelación, fallo o tiempo agotado)
                    void CerrarGrupo(string prefijo)
                    {
                        if (System.Threading.Interlocked.Exchange(ref grupoCerrado, 1) == 0)
                            doc.SendStringToExecute(prefijo + "_.UNDO _E\n", false, false, false);
                    }

                    void OnCommandWillStart(object s, CommandEventArgs e)
                    {
                        if (e.GlobalCommandName.ToUpperInvariant() == cmdParse) System.Threading.Interlocked.Exchange(ref iniciado, 1);
                    }
                    void OnCommandEnded(object s, CommandEventArgs e)
                    {
                        if (e.GlobalCommandName.ToUpperInvariant() == cmdParse) {
                            CerrarGrupo("");
                            tcs.TrySetResult("terminado");
                        }
                    }
                    void OnCommandCancelled(object s, CommandEventArgs e)
                    {
                        if (e.GlobalCommandName.ToUpperInvariant() == cmdParse) {
                            CerrarGrupo("");
                            tcs.TrySetResult("cancelado");
                        }
                    }
                    void OnCommandFailed(object s, CommandEventArgs e)
                    {
                        if (e.GlobalCommandName.ToUpperInvariant() == cmdParse) {
                            CerrarGrupo("");
                            tcs.TrySetResult("fallido");
                        }
                    }

                    await HiloPrincipal.Ejecutar(() =>
                    {
                        try {
                            if (Convert.ToInt32(AcApp.GetSystemVariable("CMDACTIVE")) > 0)
                                throw new InvalidOperationException("Hay un comando activo en Civil 3D; termínalo o cancélalo antes de enviar otro.");
                            
                            doc.CommandWillStart += OnCommandWillStart;
                            doc.CommandEnded += OnCommandEnded;
                            doc.CommandCancelled += OnCommandCancelled;
                            doc.CommandFailed += OnCommandFailed;
                            
                            doc.SendStringToExecute("_.UNDO _BE\n" + comando + "\n", true, false, false);
                        } catch (Exception ex) { tcs.TrySetException(ex); }
                        return true;
                    });

                    for (int i = 0; i < 30; i++) {
                        if (tcs.Task.IsCompleted) break;
                        if (System.Threading.Volatile.Read(ref iniciado) == 1) break;
                        await Task.Delay(100);
                    }
                    if (System.Threading.Volatile.Read(ref iniciado) == 0 && !tcs.Task.IsCompleted) {
                        await HiloPrincipal.Ejecutar(() => {
                            CerrarGrupo("");
                            tcs.TrySetResult("el comando no se inició (¿nombre incorrecto?)");
                            return true;
                        });
                    }

                    var completada = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));

                    if (completada != tcs.Task && !tcs.Task.IsCompleted)
                    {
                        // Mientras un comando espera entrada del usuario, el evento Idle no llega y un trabajo
                        // encolado en HiloPrincipal no se atendería. SendStringToExecute solo encola teclas,
                        // así que los ESC y el cierre del grupo se envían directamente desde este hilo.
                        Historial.Registrar("ejecutar_comando: tiempo agotado a los " + (timeoutMs / 1000) + " s, se envían ESC");
                        CerrarGrupo("\x1B\x1B");
                        tcs.TrySetResult("timeout con ESC");
                    }

                    // Los manejadores se desenganchan en el hilo principal cuando llegue Idle; no hace falta esperar
                    _ = HiloPrincipal.Ejecutar(() =>
                    {
                        doc.CommandWillStart -= OnCommandWillStart;
                        doc.CommandEnded -= OnCommandEnded;
                        doc.CommandCancelled -= OnCommandCancelled;
                        doc.CommandFailed -= OnCommandFailed;
                        return true;
                    });

                    return await tcs.Task;
                }
            });

            Registrar(new Herramienta
            {
                Nombre = "leer_historial",
                Descripcion = "Devuelve las últimas líneas del historial del plugin: comandos iniciados y terminados, llamadas MCP y mensajes.",
                Parametros = { P("ultimas_n", "number", "Cantidad de líneas (por defecto 50)") },
                Ejecutar = a => Historial.Ultimas((int)Num(a, "ultimas_n", 50))
            });

            Registrar(new Herramienta
            {
                Nombre = "capturar_pantalla",
                Descripcion = "Guarda una captura PNG de la ventana principal de Civil 3D (incluye cuadros de diálogo abiertos) y devuelve la ruta.",
                Parametros = { P("ruta", "string", "Ruta del PNG a crear (por defecto en la carpeta temporal)") },
                Ejecutar = a =>
                {
                    string ruta = Str(a, "ruta");
                    if (string.IsNullOrWhiteSpace(ruta))
                        ruta = Path.Combine(Path.GetTempPath(), "arba_captura_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                    var h = AcApp.MainWindow.Handle;
                    if (!GetWindowRect(h, out RECT r)) throw new InvalidOperationException("No se pudo obtener el rectángulo de la ventana.");
                    int w = Math.Max(1, r.Right - r.Left), alto = Math.Max(1, r.Bottom - r.Top);
                    using (var bmp = new Bitmap(w, alto))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, alto));
                        Directory.CreateDirectory(Path.GetDirectoryName(ruta) ?? ".");
                        bmp.Save(ruta, ImageFormat.Png);
                    }
                    return new { ruta, ancho = w, alto };
                }
            });

            RegistrarAdicionales();
        }

        // ------------------------------------------------------------------ herramientas genéricas adicionales
        private static void RegistrarAdicionales()
        {
            Registrar(new Herramienta
            {
                Nombre = "listar_pvis",
                Descripcion = "Devuelve la geometría vertical de un perfil: cada PVI con progresiva, cota, pendientes de entrada y salida, y la curva vertical que lo contiene (tipo y longitud) si existe.",
                Parametros =
                {
                    P("alineamiento", "string", "Nombre del alineamiento", true),
                    P("perfil", "string", "Nombre del perfil", true)
                },
                Ejecutar = a =>
                {
                    var doc = DocActivo();
                    var lista = new List<object>();
                    using (doc.LockDocument())
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var idAl = BuscarAlineamiento(tr, Requerido(a, "alineamiento"));
                        var idPr = BuscarPerfil(tr, idAl, Requerido(a, "perfil"));
                        var pr = (CivProfile)tr.GetObject(idPr, OpenMode.ForRead);

                        var curvas = new List<(double ini, double fin, string tipo)>();
                        foreach (Autodesk.Civil.DatabaseServices.ProfileEntity ent in pr.Entities)
                        {
                            string tipo = ent.EntityType.ToString();
                            if (tipo.IndexOf("Tangent", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            curvas.Add((ent.StartStation, ent.EndStation, tipo));
                        }

                        var pvis = new List<(double s, double z)>();
                        foreach (Autodesk.Civil.DatabaseServices.ProfilePVI pvi in pr.PVIs) pvis.Add((pvi.Station, pvi.Elevation));
                        pvis.Sort((x, y) => x.s.CompareTo(y.s));

                        for (int i = 0; i < pvis.Count; i++)
                        {
                            double? pe = i > 0 && pvis[i].s - pvis[i - 1].s > 1e-6 ? (pvis[i].z - pvis[i - 1].z) / (pvis[i].s - pvis[i - 1].s) * 100 : (double?)null;
                            double? ps = i < pvis.Count - 1 && pvis[i + 1].s - pvis[i].s > 1e-6 ? (pvis[i + 1].z - pvis[i].z) / (pvis[i + 1].s - pvis[i].s) * 100 : (double?)null;
                            string tipoCurva = null; double? lCurva = null;
                            foreach (var c in curvas)
                                if (pvis[i].s > c.ini + 1e-4 && pvis[i].s < c.fin - 1e-4) { tipoCurva = c.tipo; lCurva = c.fin - c.ini; break; }
                            lista.Add(new
                            {
                                n = i + 1,
                                progresiva = N(pvis[i].s),
                                cota = N(pvis[i].z),
                                pe_pct = pe.HasValue ? N(pe.Value) : null,
                                ps_pct = ps.HasValue ? N(ps.Value) : null,
                                a_pct = pe.HasValue && ps.HasValue ? N(Math.Abs(ps.Value - pe.Value)) : null,
                                tiene_curva = tipoCurva != null,
                                tipo_curva = tipoCurva,
                                longitud_curva = lCurva.HasValue ? N(lCurva.Value) : null
                            });
                        }
                        tr.Commit();
                    }
                    return lista;
                }
            });

            Registrar(new Herramienta
            {
                Nombre = "leer_variable",
                Descripcion = "Lee una variable de sistema de AutoCAD (por ejemplo DWGNAME, CMDACTIVE, INSUNITS, SECURELOAD).",
                Parametros = { P("nombre", "string", "Nombre de la variable de sistema", true) },
                Ejecutar = a =>
                {
                    string n = Requerido(a, "nombre");
                    object v = AcApp.GetSystemVariable(n);
                    return new { variable = n.ToUpperInvariant(), valor = v?.ToString(), tipo = v?.GetType().Name };
                }
            });
        }

        // ------------------------------------------------------------------ Win32
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    }
}


