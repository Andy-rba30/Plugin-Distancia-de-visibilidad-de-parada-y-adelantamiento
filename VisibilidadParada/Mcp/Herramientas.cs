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
using VisibilidadParada.Civil;
using VisibilidadParada.Nucleo;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivAlignment = Autodesk.Civil.DatabaseServices.Alignment;
using CivProfile = Autodesk.Civil.DatabaseServices.Profile;
using CivSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace VisibilidadParada.Mcp
{
    internal class Parametro
    {
        public string name { get; set; }
        public string type { get; set; }          // string | number | boolean
        public string description { get; set; }
        public bool required { get; set; }
    }

    internal class Herramienta
    {
        public string Nombre;
        public string Descripcion;
        public List<Parametro> Parametros = new List<Parametro>();
        public Func<JsonElement, object> Ejecutar;
    }

    /// <summary>
    /// Registro de herramientas expuestas por MCP. Otros plugins de la pestaña ARBA pueden llamar a
    /// Herramientas.Registrar(...) desde su Initialize para añadir las suyas.
    /// </summary>
    internal static class Herramientas
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
                        plugin = "VisibilidadParada",
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
                Descripcion = "Envía un comando a la línea de comandos del dibujo activo (se ejecuta de forma asíncrona; consulta leer_historial para ver si terminó). Para comandos con diálogo usa el prefijo '-' cuando exista versión de línea de comandos.",
                Parametros = { P("comando", "string", "Texto del comando, por ejemplo 'REGEN' o '_.ZOOM E'", true) },
                Ejecutar = a =>
                {
                    var doc = DocActivo();
                    string cmd = Requerido(a, "comando").Trim();
                    doc.SendStringToExecute(cmd + " ", true, false, true);
                    return new { enviado = cmd };
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

            Registrar(new Herramienta
            {
                Nombre = "analizar_visibilidad",
                Descripcion = "Verifica las curvas verticales de un perfil por visibilidad de parada y adelantamiento (DG-2018) y devuelve el veredicto CUMPLE / NO CUMPLE con la tabla completa. Si se indica una superficie, comprueba además la DVP a lo largo del eje. Escribe el informe HTML y los CSV.",
                Parametros =
                {
                    P("alineamiento", "string", "Nombre del alineamiento", true),
                    P("perfil", "string", "Nombre del perfil de rasante", true),
                    P("superficie", "string", "Nombre de la superficie de obstrucción (opcional; activa la comprobación a lo largo del eje)"),
                    P("velocidad", "number", "Velocidad de diseño km/h (por defecto 60)"),
                    P("tp", "number", "Tiempo de percepción-reacción s (2.5)"),
                    P("a", "number", "Desaceleración m/s² (3.4)"),
                    P("altura_ojo", "number", "Altura del ojo m (1.07)"),
                    P("altura_objeto", "number", "Altura del objeto m (0.15)"),
                    P("criterio_longitud", "string", "'formula' (según caso Dp<L / Dp>L) o 'maximo' (la mayor de ambas)"),
                    P("umbral_a", "number", "Diferencia algebraica % que exige curva (1)"),
                    P("longitud_minima", "number", "Longitud mínima absoluta de curva m (0 = no aplicar). Referencia 0.6·V"),
                    P("da", "number", "Distancia de visibilidad de adelantamiento m (0 = no evaluar). Tabla 205.03 DG-2018"),
                    P("altura_objeto_da", "number", "Altura del objeto para adelantamiento m (1.30)"),
                    P("inicio", "number", "Progresiva inicial del rango (opcional)"),
                    P("fin", "number", "Progresiva final del rango (opcional)"),
                    P("ruta_informe", "string", "Ruta del HTML a generar (por defecto junto al DWG)"),
                    P("desfase_creciente", "number", "Solo con superficie: desfase del carril creciente m (1.65)"),
                    P("desfase_decreciente", "number", "Solo con superficie: desfase del carril decreciente m (-1.65)"),
                    P("intervalo", "number", "Solo con superficie: intervalo entre progresivas m (10)"),
                    P("sentido", "string", "Solo con superficie: 'ambos', 'creciente' o 'decreciente'"),
                    P("criterio_pendiente", "string", "Solo con superficie: 'desfavorable' o 'promedio'"),
                    P("precision", "string", "Solo con superficie: 'normal', 'fina' o 'rapida'"),
                    P("dibujar", "boolean", "Solo con superficie: dibujar sectores deficientes en planta (true)")
                },
                Ejecutar = AnalizarVisibilidad
            });
        }

        // ------------------------------------------------------------------ análisis
        private static object AnalizarVisibilidad(JsonElement a)
        {
            var doc = DocActivo();
            var o = new OpcionesAnalisis();
            var par = o.P;

            par.VelocidadDiseno = Num(a, "velocidad", 60);
            par.TiempoPercepcion = Num(a, "tp", par.TiempoPercepcion);
            par.Desaceleracion = Num(a, "a", par.Desaceleracion);
            par.AlturaOjo = Num(a, "altura_ojo", par.AlturaOjo);
            par.AlturaObjeto = Num(a, "altura_objeto", par.AlturaObjeto);
            par.CriterioL = (Str(a, "criterio_longitud", "formula") ?? "formula").Trim().ToLowerInvariant().StartsWith("max") ? CriterioLongitud.Maximo : CriterioLongitud.Formula;
            par.UmbralA = Num(a, "umbral_a", 1.0);
            par.LongitudMinima = Num(a, "longitud_minima", 0);
            par.DistanciaAdelanto = Num(a, "da", 0);
            par.AlturaObjetoAdelanto = Num(a, "altura_objeto_da", par.AlturaObjetoAdelanto);
            o.Inicio = Num(a, "inicio", double.NaN);
            o.Fin = Num(a, "fin", double.NaN);

            string superficie = Str(a, "superficie");
            if (!string.IsNullOrWhiteSpace(superficie))
            {
                par.DesfaseCreciente = Num(a, "desfase_creciente", par.DesfaseCreciente);
                par.DesfaseDecreciente = Num(a, "desfase_decreciente", par.DesfaseDecreciente);
                par.Intervalo = Num(a, "intervalo", par.Intervalo);
                string s = (Str(a, "sentido", "ambos") ?? "ambos").Trim().ToLowerInvariant();
                par.Sentido = s.StartsWith("crec") ? SentidoAnalisis.Creciente : s.StartsWith("decr") ? SentidoAnalisis.Decreciente : SentidoAnalisis.Ambos;
                par.Criterio = (Str(a, "criterio_pendiente", "desfavorable") ?? "").Trim().ToLowerInvariant().StartsWith("prom") ? CriterioPendiente.Promedio : CriterioPendiente.Desfavorable;
                string pr = (Str(a, "precision", "normal") ?? "normal").Trim().ToLowerInvariant();
                if (pr.StartsWith("fin")) { par.PasoMuestreo = 0.5; par.PasoBusqueda = 2.5; }
                else if (pr.StartsWith("rap") || pr.StartsWith("ráp")) { par.PasoMuestreo = 2.0; par.PasoBusqueda = 10.0; }
                else { par.PasoMuestreo = 1.0; par.PasoBusqueda = 5.0; }
                par.Dibujar = Bool(a, "dibujar", true);
            }

            string ruta = Str(a, "ruta_informe");
            if (string.IsNullOrWhiteSpace(ruta))
            {
                string dir = "";
                try { dir = Path.GetDirectoryName(doc.Database.Filename) ?? ""; } catch { }
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Path.GetTempPath();
                ruta = Path.Combine(dir, "Informe_Visibilidad.html");
            }
            o.RutaHtml = ruta;

            ResultadoEjecucion res;
            using (doc.LockDocument())
            {
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    o.Alineamiento = BuscarAlineamiento(tr, Requerido(a, "alineamiento"));
                    o.Perfil = BuscarPerfil(tr, o.Alineamiento, Requerido(a, "perfil"));
                    if (!string.IsNullOrWhiteSpace(superficie)) o.Superficie = BuscarSuperficie(tr, superficie);
                    tr.Commit();
                }
                res = Motor.Ejecutar(doc.Database, o, null, null);
            }

            if (res.Error != null) throw new InvalidOperationException(res.Error);
            var d = res.Datos;
            var v = Veredicto.De(d);

            return new
            {
                veredicto = new
                {
                    cumple = v.Cumple,
                    texto = v.Cumple ? "CUMPLE" : "NO CUMPLE",
                    curvas_cumplen = v.CurvasCumplen,
                    visibilidad_eje_cumple = v.VisibilidadCumple,
                    motivos = v.Motivos
                },
                datos = new
                {
                    dibujo = d.Dibujo, eje = d.Eje, perfil = d.Perfil, superficie = string.IsNullOrEmpty(d.Superficie) ? null : d.Superficie,
                    rango_inicio = N(d.SMin), rango_fin = N(d.SMax),
                    velocidad = par.VelocidadDiseno, tp = par.TiempoPercepcion, a = par.Desaceleracion,
                    altura_ojo = par.AlturaOjo, altura_objeto = par.AlturaObjeto,
                    criterio_longitud = par.CriterioL.ToString().ToLowerInvariant(), umbral_a = par.UmbralA,
                    longitud_minima = par.LongitudMinima, da = par.DistanciaAdelanto, altura_objeto_da = par.AlturaObjetoAdelanto,
                    constante_convexa_parada = CurvasVerticales.ConstanteConvexa(par.AlturaOjo, par.AlturaObjeto),
                    constante_convexa_adelanto = CurvasVerticales.ConstanteConvexa(par.AlturaOjo, par.AlturaObjetoAdelanto),
                    duracion_s = Math.Round(d.Duracion.TotalSeconds, 2)
                },
                resumen = new
                {
                    pvi_analizados = d.Curvas.Count,
                    cumplen = d.Curvas.Count(c => c.Cumple),
                    curvas_cortas = d.Curvas.Count(c => c.Estado == EstadoCurva.NoCumple),
                    sin_curva_requerida = d.Curvas.Count(c => c.Estado == EstadoCurva.SinCurvaRequerida)
                },
                curvas = d.Curvas.Select(c => new
                {
                    n = c.N,
                    ubicacion = Formato.Prog(c.Progresiva),
                    progresiva = N(c.Progresiva),
                    tipo = c.Convexa ? "Convexa" : "Cóncava",
                    v = c.V,
                    pe_pct = N(c.Pe * 100), ps_pct = N(c.Ps * 100), a_pct = N(c.A),
                    dp_ida_pe = N(c.DpIdaPe), dp_ida_ps = N(c.DpIdaPs), dp_reg_pe = N(c.DpRegPe), dp_reg_ps = N(c.DpRegPs),
                    dp = N(c.Dp),
                    l_calc_dp_mayor_l = N(c.LDpMayor), l_calc_dp_menor_l = N(c.LDpMenor),
                    l_visibilidad = N(c.LVisibilidad), l_confort = N(c.LConfort),
                    l_min_exigida = N(c.LReq), k_min = N(c.KReq),
                    tiene_curva = c.TieneCurva, tipo_entidad = c.TipoEntidad,
                    l_proyecto = N(c.LProyecto), k_proyecto = N(c.KProyecto),
                    estado = CurvasVerticales.EstadoTxt(c.Estado),
                    cumple = c.Cumple,
                    verificacion = c.Verificacion,
                    da = N(c.Da), l_calc_da_mayor_l = N(c.LDaMayor), l_calc_da_menor_l = N(c.LDaMenor), l_req_adelanto = N(c.LDaReq),
                    permite_adelantar = c.PermiteAdelantar,
                    nota = c.Nota
                }).ToList(),
                eje = d.Resultados == null ? null : new
                {
                    puntos_evaluados = d.Resultados.Count,
                    cumplen = d.Resultados.Count(r => r.Estado == EstadoPunto.Cumple),
                    no_cumplen = d.Resultados.Count(r => r.Estado == EstadoPunto.NoCumple),
                    no_evaluables = d.Resultados.Count(r => r.Estado == EstadoPunto.NoEvaluable),
                    muestras_fuera_superficie = d.MuestrasFuera,
                    sectores = d.Sectores.Select(s => new
                    {
                        sentido = Formato.SentidoTxt(s.Sentido),
                        inicio = Formato.Prog(s.Inicio), fin = Formato.Prog(s.Fin),
                        longitud = N(s.Fin - s.Inicio), dvp_max = N(s.DvpMax),
                        disponible_min = N(s.DisponibleMin), deficit_max = N(s.DeficitMax)
                    }).ToList()
                },
                archivos = new { html = res.RutaHtml, csv_curvas = res.RutaCsvCurvas, csv_progresivas = string.IsNullOrEmpty(res.RutaCsvPuntos) ? null : res.RutaCsvPuntos }
            };
        }

        // ------------------------------------------------------------------ Win32
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    }
}
