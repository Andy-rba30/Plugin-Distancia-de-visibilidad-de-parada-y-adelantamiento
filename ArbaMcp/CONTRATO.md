# Contrato del servidor local para el puente MCP

El plugin **ArbaMcp** abre, al cargarse en Civil 3D, un servidor HTTP mínimo que escucha **solo en 127.0.0.1**.
No usa http.sys ni necesita permisos de administrador.

- Puerto: variable de entorno `ARBA_MCP_PORT` (por defecto **8765**). `ARBA_MCP=0` desactiva el servidor.
- Comando `ARBAMCP` (botón *Conexión IA* de la pestaña ARBA): muestra si está activo, el puerto y las últimas líneas del historial.
- Todas las llamadas a la API de Civil 3D se ejecutan en el hilo principal. Si Civil 3D está ocupado
  (comando activo o cuadro de diálogo modal abierto) la petición espera; pasado `timeout_s` responde error.

## Seguridad (desde la versión 1.1.0)

Toda petición debe cumplir estas reglas; si no, se rechaza antes de procesar nada:

| Regla | Si falla |
|---|---|
| Cabecera `X-Arba-Token` con el token de la sesión. El plugin lo genera al iniciar y lo guarda en `%LOCALAPPDATA%\ArbaMcp\token` (permisos solo para tu usuario). Cambia en cada arranque de Civil 3D. Aplica a todas las rutas, `/ping` incluida | 401 |
| Sin cabecera `Origin` (evita llamadas desde navegadores) | 403 |
| Cabecera `Host` exactamente `127.0.0.1:<puerto>` o `localhost:<puerto>` | 400 |
| En `POST`, `Content-Type: application/json` | 415 |

El puente Python lee el token del archivo al arrancar y lo vuelve a leer si recibe 401.

## Rutas

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/ping` | `{"ok":true,"servidor":"ArbaMcp","puerto":8765}` |
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
| `ejecutar_comando` | comando*, timeout_s | Espera a que el comando termine y devuelve `terminado`, `cancelado`, `fallido`, `el comando no se inició (¿nombre incorrecto?)` o `timeout con ESC`. Si hay un comando activo en Civil 3D responde error sin enviar nada. Cada orden va entre `_.UNDO _BE` y `_.UNDO _E`, así un solo Ctrl+Z la revierte. Si se agota `timeout_s` (60 por defecto) envía dos ESC para cancelar |
| `leer_historial` | ultimas_n | líneas con hora: comandos iniciados/terminados, llamadas MCP, mensajes |
| `capturar_pantalla` | ruta | ruta del PNG, ancho, alto (ventana principal de Civil 3D, con diálogos) |
| `listar_pvis` | alineamiento*, perfil* | n, progresiva, cota, pe_pct, ps_pct, a_pct, tiene_curva, tipo_curva, longitud_curva |
| `leer_variable` | nombre* | variable, valor, tipo |

`*` = obligatorio.

## Pruebas rápidas desde PowerShell (con Civil 3D abierto)

```powershell
$t = Get-Content "$env:LOCALAPPDATA\ArbaMcp\token" -Raw
curl.exe -H "X-Arba-Token: $t" http://127.0.0.1:8765/tools
curl.exe -X POST http://127.0.0.1:8765/execute -H "X-Arba-Token: $t" -H "Content-Type: application/json" -d "{\"tool\":\"ping\"}"
curl.exe -X POST http://127.0.0.1:8765/execute -H "X-Arba-Token: $t" -H "Content-Type: application/json" -d "{\"tool\":\"listar_alineamientos\"}"
curl.exe -X POST http://127.0.0.1:8765/execute -H "X-Arba-Token: $t" -H "Content-Type: application/json" -d "{\"tool\":\"listar_pvis\",\"args\":{\"alineamiento\":\"Eje principal\",\"perfil\":\"Rasante\"}}"
curl.exe -X POST http://127.0.0.1:8765/execute -H "X-Arba-Token: $t" -H "Content-Type: application/json" -d "{\"tool\":\"ejecutar_comando\",\"args\":{\"comando\":\"REGEN\"}}"
```

Sin token responde 401; con cabecera `Origin`, 403; `POST` sin `Content-Type: application/json`, 415.
El script `pruebas\probar_servidor.py` ejecuta estas comprobaciones de una vez.

## Añadir herramientas desde otro plugin de la pestaña ARBA

Cualquier plugin que referencie `ArbaMcp.dll` puede registrar sus propias herramientas desde su `Initialize`:

```csharp
ArbaMcp.Herramientas.Registrar(new ArbaMcp.Herramienta
{
    Nombre = "mi_herramienta",
    Descripcion = "...",
    Parametros = { new ArbaMcp.Parametro { name = "x", type = "number", description = "...", required = true } },
    Ejecutar = args => new { resultado = 1 }   // se ejecuta en el hilo principal
});
```

El puente las verá en `GET /tools` sin cambios en Python.
