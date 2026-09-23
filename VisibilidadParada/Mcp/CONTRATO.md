# Contrato del servidor local para el puente MCP

El plugin abre, al cargarse en Civil 3D, un servidor HTTP mínimo que escucha **solo en 127.0.0.1**.
No usa http.sys ni necesita permisos de administrador.

- Puerto: variable de entorno `ARBA_MCP_PORT` (por defecto **8765**). `ARBA_MCP=0` desactiva el servidor.
- Comando `ARBAMCP` en Civil 3D: muestra si está activo, el puerto y las últimas líneas del historial.
- Todas las llamadas a la API de Civil 3D se ejecutan en el hilo principal. Si Civil 3D está ocupado
  (comando activo o cuadro de diálogo modal abierto) la petición espera; pasado `timeout_s` responde error.

## Rutas

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/ping` | `{"ok":true,"servidor":"VisibilidadParada MCP","puerto":8765}` |
| GET | `/tools` | Lista de herramientas con nombre, descripción y parámetros (nombre, tipo, descripción, requerido) |
| POST | `/execute` | Ejecuta una herramienta. Cuerpo: `{"tool":"nombre","args":{...},"timeout_s":120}` |

Respuesta de `/execute`:

```json
{"ok": true,  "tool": "ping", "ms": 3, "result": { ... }}
{"ok": false, "error": "mensaje"}
```

Los tipos de parámetro son `string`, `number` o `boolean`. El puente debe leer `/tools` al arrancar y
registrar cada herramienta de forma dinámica, de modo que al añadir herramientas en C# no haya que tocar Python.

## Herramientas de la primera versión

| Herramienta | Parámetros | Devuelve |
|---|---|---|
| `ping` | — | plugin, versión, puerto, dibujo activo, hora |
| `listar_alineamientos` | — | nombre, inicio, fin, longitud, perfiles[] |
| `listar_perfiles` | alineamiento* | nombre, tipo (EG/FG), inicio, fin, pvis |
| `listar_superficies` | — | nombre, tipo |
| `abrir_dibujo` | ruta* | abierto |
| `ejecutar_comando` | comando* | enviado (asíncrono: comprobar con `leer_historial`) |
| `leer_historial` | ultimas_n | líneas con hora: comandos iniciados/terminados, llamadas MCP, mensajes |
| `capturar_pantalla` | ruta | ruta del PNG, ancho, alto (ventana principal de Civil 3D, con diálogos) |
| `analizar_visibilidad` | alineamiento*, perfil*, superficie, velocidad, tp, a, altura_ojo, altura_objeto, criterio_longitud, umbral_a, longitud_minima, da, altura_objeto_da, inicio, fin, ruta_informe, y con superficie: desfase_creciente, desfase_decreciente, intervalo, sentido, criterio_pendiente, precision, dibujar | veredicto {cumple, texto, motivos}, datos, resumen, curvas[] (tabla completa por PVI), eje (solo con superficie), archivos {html, csv_curvas, csv_progresivas} |

`*` = obligatorio.

## Pruebas rápidas desde PowerShell (con Civil 3D abierto)

```powershell
curl.exe http://127.0.0.1:8765/tools
curl.exe -X POST http://127.0.0.1:8765/execute -H "Content-Type: application/json" -d "{\"tool\":\"ping\"}"
curl.exe -X POST http://127.0.0.1:8765/execute -H "Content-Type: application/json" -d "{\"tool\":\"listar_alineamientos\"}"
curl.exe -X POST http://127.0.0.1:8765/execute -H "Content-Type: application/json" -d "{\"tool\":\"analizar_visibilidad\",\"args\":{\"alineamiento\":\"Eje principal\",\"perfil\":\"Rasante\",\"velocidad\":80,\"da\":540,\"longitud_minima\":48}}"
```

## Añadir herramientas desde otro plugin de la pestaña ARBA

Desde el `Initialize` de otro plugin que referencie `VisibilidadParada.dll`:

```csharp
VisibilidadParada.Mcp.Herramientas.Registrar(new VisibilidadParada.Mcp.Herramienta
{
    Nombre = "mi_herramienta",
    Descripcion = "...",
    Parametros = { new VisibilidadParada.Mcp.Parametro { name = "x", type = "number", description = "...", required = true } },
    Ejecutar = args => new { resultado = 1 }   // se ejecuta en el hilo principal
});
```

(Las clases son `internal`; para usarlas desde otra DLL hay que hacerlas `public` o añadir `InternalsVisibleTo`.)
