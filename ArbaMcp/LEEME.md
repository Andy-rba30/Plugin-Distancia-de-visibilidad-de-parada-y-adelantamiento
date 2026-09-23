# ArbaMcp: conector entre Civil 3D 2027 y un agente de IA

Plugin independiente de las herramientas de cálculo. Al cargarse en Civil 3D abre un servidor HTTP local en `http://127.0.0.1:8765/` (solo accesible desde tu máquina) y agrega el botón **Conexión IA** en la pestaña ARBA. Un puente MCP externo, en Python, traduce las herramientas del agente a peticiones a ese servidor, igual que el conector de Revit.

## Instalar

Con Civil 3D cerrado, en PowerShell dentro de esta carpeta:

```powershell
powershell -ExecutionPolicy Bypass -File .\instalar.ps1
```

Compila e instala el paquete en `%APPDATA%\Autodesk\ApplicationPlugins\ArbaMcp.bundle`. Requiere Visual Studio 2026 con .NET 10 y Civil 3D 2027 en la ruta indicada en `C3DPath` del `.csproj`. Compilar desde Visual Studio también instala.

## Seguridad

El servidor solo acepta peticiones con la cabecera `X-Arba-Token`. El token se genera en cada arranque de Civil 3D y se guarda en `%LOCALAPPDATA%\ArbaMcp\token`, legible solo por tu usuario; el puente Python lo lee de ahí. Además rechaza peticiones con cabecera `Origin`, con `Host` distinto de 127.0.0.1 o localhost, y `POST` que no sean JSON. Detalle en `CONTRATO.md`.

## Comprobar

Abre Civil 3D y pulsa **Conexión IA** en la pestaña ARBA, o escribe `ARBAMCP`. Verás un aviso con el estado y el puerto. Desde PowerShell:

```powershell
$t = Get-Content "$env:LOCALAPPDATA\ArbaMcp\token" -Raw
curl.exe -H "X-Arba-Token: $t" http://127.0.0.1:8765/ping
curl.exe -H "X-Arba-Token: $t" http://127.0.0.1:8765/tools
curl.exe -X POST http://127.0.0.1:8765/execute -H "X-Arba-Token: $t" -H "Content-Type: application/json" -d "{\"tool\":\"listar_alineamientos\"}"
```

## Icono del botón

Copia el PNG de tu icono a `Recursos\ARBA_BTN_MCP.png` (32×32) y recompila. Ver `Recursos\LEEME.md`.

## Configurar

- `ARBA_MCP_PORT`: puerto (por defecto 8765).
- `ARBA_MCP=0`: desactiva el servidor.

## Herramientas

`ping`, `listar_alineamientos`, `listar_perfiles`, `listar_pvis`, `listar_superficies`, `abrir_dibujo`, `ejecutar_comando` (espera a que el comando termine, con grupo de UNDO y tiempo límite), `leer_historial`, `leer_variable`, `capturar_pantalla`. El contrato completo, con parámetros y respuestas, está en `CONTRATO.md`. Otros plugins pueden registrar sus propias herramientas (ver el final de ese archivo).

## Estructura

```
Cinta.cs          Arranque del plugin, botón Conexión IA, comando ARBAMCP, pestaña ARBA compartida
Servidor.cs       HTTP mínimo en 127.0.0.1 (GET /ping, GET /tools, POST /execute) e historial
HiloPrincipal.cs  Cola que ejecuta cada herramienta en el hilo principal de AutoCAD
Herramientas.cs   Registro e implementación de las herramientas
Bundle/           PackageContents.xml para la carga automática
instalar.ps1      Compila e instala
CONTRATO.md       Contrato para escribir el puente MCP
Recursos/         Iconos de los botones
pruebas/          probar_servidor.py: comprueba seguridad y ejecutar_comando con Civil 3D abierto
```

El puente Python está en la carpeta `PuenteMcp` del repositorio (escucha en `localhost:8001/mcp`; Antigravity se conecta ahí).
