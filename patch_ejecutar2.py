import os

path = r'D:\Proyectos C#\Verificador DP y DA\ArbaMcp\Herramientas.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

start_idx = content.find('Nombre = "ejecutar_comando",')
if start_idx == -1:
    print("Not found")
    import sys; sys.exit(1)

# Find the end of this block
start_block = content.rfind('Registrar(new Herramienta', 0, start_idx)
end_block = content.find('});', start_idx) + 3

new_code = '''Registrar(new Herramienta
            {
                Nombre = "ejecutar_comando",
                Descripcion = "Envía un comando a la línea de comandos del dibujo activo de forma síncrona. Retorna cuando termina o falla (tiempo límite 60s).",
                Parametros = { P("comando", "string", "Texto del comando, por ejemplo 'REGEN' o '_.ZOOM E'", true) },
                EjecutarAsync = async a =>
                {
                    string comando = Requerido(a, "comando").Trim();
                    var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                    
                    string cmdParse = comando.Split(new[] { ' ', '\\n', '\\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    cmdParse = cmdParse.TrimStart('_', '.').ToUpperInvariant();

                    Document doc = DocActivo();
                    bool started = false;

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
                            doc.SendStringToExecute("\\x1B\\x1B", false, false, false);
                            tcs.TrySetResult("timeout con ESC");
                        }
                        return true;
                    });

                    return await tcs.Task;
                }
            });'''

content = content[:start_block] + new_code + content[end_block:]

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
print("done")
