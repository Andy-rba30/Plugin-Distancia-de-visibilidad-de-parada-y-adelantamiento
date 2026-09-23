import os, re

path = r'D:\Proyectos C#\Verificador DP y DA\ArbaMcp\Herramientas.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace the old ejecutar_comando block with a new async one.
old_pattern = r'Registrar\(new Herramienta\s*\{\s*Nombre = "ejecutar_comando",\s*Descripcion = "Envía un comando[^"]*",\s*Parametros = new List<Parametro>\s*\{\s*new Parametro \{ name = "comando", type = "string", description = "Texto del comando[^"]*", required = true \}\s*\},\s*Ejecutar = args =>\s*\{\s*string comando = "";\s*if \(args.TryGetProperty\("comando", out var c\)\) comando = c.GetString\(\) \?\? "";\s*if \(string.IsNullOrWhiteSpace\(comando\)\) return "Comando vacío";\s*var doc = AcApp.DocumentManager.MdiActiveDocument;\s*if \(doc != null\)\s*doc.SendStringToExecute\(comando \+ "\\n", false, false, false\);\s*return "Comando '" \+ comando \+ "' enviado de forma asíncrona. Consulta leer_historial.";\s*\}\s*\}\);'

new_code = '''Registrar(new Herramienta
            {
                Nombre = "ejecutar_comando",
                Descripcion = "Envía un comando a la línea de comandos del dibujo activo de forma síncrona. Retorna cuando termina o falla (tiempo límite 60s).",
                Parametros = new List<Parametro>
                {
                    new Parametro { name = "comando", type = "string", description = "Texto del comando, por ejemplo 'REGEN' o '_.ZOOM E'", required = true }
                },
                EjecutarAsync = async args =>
                {
                    string comando = "";
                    if (args.TryGetProperty("comando", out var c)) comando = c.GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(comando)) throw new Exception("Comando vacío");

                    var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                    
                    // Extraer primer token
                    string cmdParse = comando.Trim().Split(new[] { ' ', '\\n', '\\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    cmdParse = cmdParse.TrimStart('_', '.').ToUpperInvariant();

                    Document doc = AcApp.DocumentManager.MdiActiveDocument;
                    if (doc == null) throw new Exception("No hay documento activo");

                    bool started = false;

                    // Handlers locales
                    void OnCommandWillStart(object s, CommandEventArgs e)
                    {
                        if (e.GlobalCommandName.ToUpperInvariant() == cmdParse) started = true;
                    }
                    void OnCommandEnded(object s, CommandEventArgs e)
                    {
                        if (e.GlobalCommandName.ToUpperInvariant() == cmdParse) {
                            doc.SendStringToExecute("_.UNDO _E\\n", false, false, false);
                            tcs.TrySetResult("terminado");
                        }
                    }
                    void OnCommandCancelled(object s, CommandEventArgs e)
                    {
                        if (e.GlobalCommandName.ToUpperInvariant() == cmdParse) {
                            doc.SendStringToExecute("_.UNDO _E\\n", false, false, false);
                            tcs.TrySetResult("cancelado");
                        }
                    }
                    void OnCommandFailed(object s, CommandEventArgs e)
                    {
                        if (e.GlobalCommandName.ToUpperInvariant() == cmdParse) {
                            doc.SendStringToExecute("_.UNDO _E\\n", false, false, false);
                            tcs.TrySetResult("fallido");
                        }
                    }

                    await HiloPrincipal.Ejecutar(() =>
                    {
                        try {
                            if (Convert.ToInt32(AcApp.GetSystemVariable("CMDACTIVE")) > 0) {
                                tcs.TrySetResult(new { ok = false, error = "hay un comando activo en Civil 3D" });
                                return true;
                            }
                            
                            doc.CommandWillStart += OnCommandWillStart;
                            doc.CommandEnded += OnCommandEnded;
                            doc.CommandCancelled += OnCommandCancelled;
                            doc.CommandFailed += OnCommandFailed;
                            
                            doc.SendStringToExecute("_.UNDO _BE\\n" + comando + "\\n", true, false, false);
                        } catch (Exception ex) { tcs.TrySetException(ex); }
                        return true;
                    });

                    // Validar inicio temprano
                    for (int i = 0; i < 30; i++) {
                        if (tcs.Task.IsCompleted) break;
                        if (started) break;
                        await Task.Delay(100);
                    }
                    if (!started && !tcs.Task.IsCompleted) {
                        await HiloPrincipal.Ejecutar(() => {
                            doc.SendStringToExecute("_.UNDO _E\\n", false, false, false);
                            tcs.TrySetResult("el comando no se inició (¿nombre incorrecto?)");
                            return true;
                        });
                    }

                    var completada = await Task.WhenAny(tcs.Task, Task.Delay(60000));
                    
                    await HiloPrincipal.Ejecutar(() =>
                    {
                        doc.CommandWillStart -= OnCommandWillStart;
                        doc.CommandEnded -= OnCommandEnded;
                        doc.CommandCancelled -= OnCommandCancelled;
                        doc.CommandFailed -= OnCommandFailed;
                        
                        if (completada != tcs.Task && !tcs.Task.IsCompleted) {
                            // Timeout
                            doc.SendStringToExecute("\\x1B\\x1B", false, false, false);
                            tcs.TrySetResult("timeout con ESC");
                        }
                        return true;
                    });

                    return await tcs.Task;
                }
            });'''

if not re.search(r'Nombre = "ejecutar_comando"', content):
    print("NO SE ENCONTRÓ LA HERRAMIENTA ejecutar_comando")
else:
    content = re.sub(old_pattern, new_code, content, flags=re.DOTALL)
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)
    print("ejecutar_comando reescrito.")
