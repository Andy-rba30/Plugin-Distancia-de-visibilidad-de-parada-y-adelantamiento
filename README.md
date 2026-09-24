# Plugin de distancia de visibilidad de parada y adelantamiento

Plugin para Civil 3D 2027 (.NET 10). Un solo comando, **VISIBILIDAD**, en la pestaña **ARBA** de la cinta: abre un cuadro de diálogo donde eliges el alineamiento y el perfil, configuras los parámetros (V, tp, a, alturas de ojo y objeto, Da) y obtienes la tabla de curvas verticales con su veredicto CUMPLE / NO CUMPLE por visibilidad de parada y adelantamiento, más el informe HTML y CSV. Como opción, comprueba también la DVP a lo largo del eje contra la superficie del corredor.

El código, las instrucciones de compilación y uso, y los informes de ejemplo están en la carpeta [`VisibilidadParada`](VisibilidadParada/). Lee [`VisibilidadParada/LEEME.md`](VisibilidadParada/LEEME.md) para empezar.

## Conector para agentes de IA

El plugin ArbaMcp (servidor local en Civil 3D) y el puente MCP en Python viven en su propio repositorio: [civil3d-mcp](https://github.com/Andy-rba30/civil3d-mcp). Este repo contiene solo el plugin de visibilidad.
