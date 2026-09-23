import os, re
path_s = r'D:\Proyectos C#\Verificador DP y DA\ArbaMcp\Servidor.cs'
with open(path_s, 'r', encoding='utf-8') as f:
    content = f.read()

exec_patch = '''
            try
            {
                Task<object> tareaPrincipal;
                if (herramienta.EjecutarAsync != null)
                {
                    tareaPrincipal = herramienta.EjecutarAsync(args);
                }
                else
                {
                    tareaPrincipal = HiloPrincipal.Ejecutar(() => herramienta.Ejecutar(args));
                }

                var terminada = await Task.WhenAny(tareaPrincipal, Task.Delay(TimeSpan.FromSeconds(timeoutS)));
                if (terminada != tareaPrincipal)
                    return Error("Tiempo agotado (" + timeoutS + " s). Civil 3D puede estar ocupado o con un cuadro de diǭlogo abierto.");
                object resultado = await tareaPrincipal;
                Historial.Registrar("MCP ✓ " + nombre + " OK (" + reloj.ElapsedMilliseconds + " ms)");
                return JsonSerializer.Serialize(new { ok = true, tool = nombre, ms = reloj.ElapsedMilliseconds, result = resultado }, Json);
            }
'''

content = re.sub(r'try\s*\{\s*var tarea = HiloPrincipal\.Ejecutar\(\(\) => herramienta\.Ejecutar\(args\)\);\s*var terminada = await Task\.WhenAny\(tarea, Task\.Delay\(TimeSpan\.FromSeconds\(timeoutS\)\)\);\s*if \(terminada != tarea\)\s*return Error\("Tiempo agotado[^"]*"\);\s*object resultado = await tarea;\s*Historial\.Registrar\("[^"]*"\);\s*return JsonSerializer\.Serialize\(new \{ ok = true, tool = nombre, ms = reloj\.ElapsedMilliseconds, result = resultado \}, Json\);\s*\}', exec_patch, content)

with open(path_s, 'w', encoding='utf-8') as f:
    f.write(content)

path_h = r'D:\Proyectos C#\Verificador DP y DA\ArbaMcp\Herramientas.cs'
with open(path_h, 'r', encoding='utf-8') as f:
    content_h = f.read()

content_h = content_h.replace('public Func<JsonElement, object> Ejecutar;', 'public Func<JsonElement, object> Ejecutar;\n        public Func<JsonElement, Task<object>> EjecutarAsync;')

with open(path_h, 'w', encoding='utf-8') as f:
    f.write(content_h)
print("Soporte EjecutarAsync añadido.")
