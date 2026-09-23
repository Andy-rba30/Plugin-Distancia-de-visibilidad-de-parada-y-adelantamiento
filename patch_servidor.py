import os, re
path = r'D:\Proyectos C#\Verificador DP y DA\ArbaMcp\Servidor.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Add TokenActual and generate it in Iniciar
content = content.replace('public static string UltimoError { get; private set; } = "";', 'public static string UltimoError { get; private set; } = "";\n        public static string TokenActual { get; private set; }')

iniciar_patch = '''
        public static void Iniciar()
        {
            if (Activo) return;
            if (Environment.GetEnvironmentVariable("ARBA_MCP") == "0") return;

            TokenActual = Guid.NewGuid().ToString("N");
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
'''
content = re.sub(r'public static void Iniciar\(\)\s*\{\s*if \(Activo\) return;\s*if \(Environment.GetEnvironmentVariable\("ARBA_MCP"\) == "0"\) return;', iniciar_patch, content)

# Header validation in Atender
atender_patch = '''
                    int largo = 0;
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var l in lineas.Skip(1)) {
                        if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(l.Substring(15).Trim(), out largo);
                        int idx = l.IndexOf(':');
                        if (idx > 0) headers[l.Substring(0, idx).Trim()] = l.Substring(idx + 1).Trim();
                    }

                    if (headers.ContainsKey("Origin")) { await Responder(ns, 403, Error("Forbidden")); return; }
                    
                    if (!headers.TryGetValue("Host", out string host) || (host != "127.0.0.1:" + Puerto && host != "localhost:" + Puerto)) { await Responder(ns, 400, Error("Bad Request")); return; }
                    
                    if (!headers.TryGetValue("X-Arba-Token", out string tokenReq) || tokenReq != TokenActual) { await Responder(ns, 401, Error("Unauthorized")); return; }
                    
                    if (metodo == "POST" && (!headers.TryGetValue("Content-Type", out string ct) || !ct.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))) { await Responder(ns, 415, Error("Unsupported Media Type")); return; }
'''
content = re.sub(r'int largo = 0;\s*foreach \(var l in lineas\.Skip\(1\)\)\s*if \(l\.StartsWith\("Content-Length:", StringComparison\.OrdinalIgnoreCase\)\)\s*int\.TryParse\(l\.Substring\(15\)\.Trim\(\), out largo\);', atender_patch, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
print("Servidor.cs modificado.")
