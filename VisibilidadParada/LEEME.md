# VisibilidadParada: plugin para Civil 3D 2027

Este plugin agrega dos comandos:

- **VISCURVAS**: lee la geometría del perfil (PVI y curvas verticales) y verifica cada curva por visibilidad de parada y, opcionalmente, de adelantamiento. Reproduce el análisis típico en Excel (Pe, Ps, A, tipo, Dp de ida y regreso, L por Dp > L y Dp < L) y lo compara con la curva proyectada. No necesita superficie y es instantáneo.
- **VISPARADA**: hace todo lo anterior y además verifica la DVP en cada progresiva del eje, en ambos sentidos, midiendo la visibilidad disponible contra la superficie del corredor. Así detecta también los taludes en curvas horizontales.

Ambos generan **un solo informe HTML** y el detalle en CSV para Excel.

## 1. Compilar

1. Instala **Visual Studio 2026** con la carga de trabajo "Desarrollo de escritorio de .NET". AutoCAD/Civil 3D 2027 exige .NET 10 y VS 2022 no sirve para esta versión.
2. Abre `VisibilidadParada.csproj`.
3. Si Civil 3D no está en `C:\Program Files\Autodesk\AutoCAD 2027`, corrige la propiedad `C3DPath` del `.csproj`. Las referencias usadas son:
   - `accoremgd.dll`, `acdbmgd.dll`, `acmgd.dll` (carpeta raíz)
   - `ACA\AecBaseMgd.dll`
   - `C3D\AeccDbMgd.dll`
4. Compila en **Release | x64**. Obtendrás `bin\x64\Release\VisibilidadParada.dll` o `bin\Release\VisibilidadParada.dll`.

## 2. Cargar en Civil 3D

- Para cargarlo manualmente, escribe `NETLOAD` y selecciona `VisibilidadParada.dll`.
- Para cargarlo siempre, agrégalo a la carga automática. Una opción es crear un paquete en `%APPDATA%\Autodesk\ApplicationPlugins`; otra es poner `(command "NETLOAD" "ruta\\VisibilidadParada.dll")` en `acaddoc.lsp`. Si la DLL no está en una ruta de confianza, AutoCAD mostrará una advertencia de seguridad. Agrega la carpeta en `TRUSTEDPATHS`.

## 3. Uso

Escribe `VISCURVAS` o `VISPARADA`. **No hay nada precargado.** Cada ejecución empieza desde cero, así que debes seleccionar el perfil e ingresar todos los valores. Enter vacío no se acepta. Donde aplica, el mensaje muestra el valor de referencia de la DG-2018 solo como guía; igual tienes que escribirlo.

**Selección de objetos**

1. **Alineamiento**: se selecciona en planta.
2. **Perfil de rasante**: haz clic sobre la línea del perfil en la vista de perfil. Si prefieres elegirlo de una lista numerada, escribe `L` (opción `Lista`). El plugin comprueba que el perfil pertenezca al alineamiento seleccionado; si no, lo vuelve a pedir.
3. **Superficie de obstrucción** (solo VISPARADA): usa la superficie del corredor con taludes (Top + daylight). Si seleccionas el terreno natural, las cotas de ojo y objeto saldrán mal en rellenos.

**Datos que se piden**

| Solicitud | Referencia mostrada | Comentario |
|---|---|---|
| Velocidad de diseño (km/h) | — | Opción `Archivo` para cargar velocidades por tramo (ver `velocidades_ejemplo.csv`). Luego pide la velocidad para progresivas no cubiertas |
| tp (s) / a (m/s²) | 2.5 / 3.4 | Fórmula DG-2018 / AASHTO |
| Altura ojo / objeto (m) | 1.07 / 0.15 | |
| Longitud de curva vertical | — | `Formula`: usa L de Dp < L si resulta ≥ Dp; si no, la de Dp > L (selección normativa). `Maximo`: toma la mayor de ambas (más conservador; es lo que hace una hoja que toma el máximo) |
| A para exigir curva (%) | — | Los PVI sin curva con A igual o mayor se marcan NO CUMPLE (falta curva). Usa el umbral de tu norma y tipo de vía |
| Longitud mínima absoluta (m) | — | 0 = no aplicar. La L exigida es la mayor entre la de visibilidad y esta |
| Da (m) | — | 0 = no evaluar adelantamiento. También puede venir por tramo (cuarta columna del archivo de velocidades) |
| Altura objeto adelantamiento (m) | 1.30 | Solo si se evalúa Da |
| *Solo VISPARADA:* | | |
| Desfase carril creciente / decreciente (m) | — | Centro de carril. Positivo = derecha del eje en el sentido de las progresivas (por ejemplo +1.65 / −1.65) |
| Intervalo (m) | — | Separación entre progresivas evaluadas |
| Sentido | — | Ambos / Creciente / Decreciente |
| Criterio de pendiente | — | `Desfavorable`: menor pendiente (más en bajada) dentro de la DVP. `Promedio`: pendiente media en esa longitud |
| Precisión | — | Normal: muestreo 1 m / búsqueda 5 m. Fina: 0.5 / 2.5 m. Rápida: 2 / 10 m |
| Dibujar sectores | — | Polilíneas en las capas `VIS-PARADA-DEF-CRECIENTE` (roja) y `VIS-PARADA-DEF-DECRECIENTE` (magenta) |

**Resultado de la verificación**

Al terminar, la línea de comandos muestra cada PVI con su comprobación y un veredicto final. Por ejemplo:

```
  PVI   2    0+060.98  Cóncava   Lp = 57.26 m < Lmín = 146 m (Kp = 12.12 < Kmín = 30.90); faltan 88.75 m → NO CUMPLE
  PVI   4    0+413.02  Convexa   Lp = 211.50 m ≥ Lmín = 95 m (Kp = 90.00 ≥ Kmín = 40.43) → CUMPLE
  ...
========================================
  VERIFICACIÓN: NO CUMPLE
========================================
  Curvas verticales: NO CUMPLEN
  - 1 curva(s) vertical(es) con longitud menor a la mínima: PVI 2 (0+060.98).
  - 3 PVI sin curva vertical que la requieren: PVI 17 (3+985.06), 18 (4+209.44), 19 (4+264.93).
```

El informe HTML abre con el mismo veredicto en un recuadro verde (CUMPLE) o rojo (NO CUMPLE) y sus motivos. La tabla de curvas incluye las columnas **Estado** y **Verificación**, con la comparación Lp contra Lmín y Kp contra Kmín. En VISPARADA el veredicto incluye también la visibilidad a lo largo del eje, con los sectores que no cumplen.

Archivos generados: VISPARADA crea `Informe_DVP.html`, `Informe_DVP.csv` (detalle por progresiva) e `Informe_DVP_curvas.csv`. VISCURVAS crea el HTML y un CSV con la tabla de curvas. ESC cancela.

## 4. Qué calcula

**Curvas verticales** (ambos comandos). Se leen del perfil los PVI (progresiva y cota) y las entidades no tangentes (inicio, fin y tipo). Para cada PVI interior:

- Pe y Ps se calculan con las cotas y progresivas de los PVI. A = |Ps − Pe|. Es convexa si Ps < Pe.
- Dp se calcula de ida (Pe, Ps) y de regreso (−Ps, −Pe). Se toma el mayor, redondeado hacia arriba.
- Convexas: L = A·Dp²/404 (Dp < L) o L = 2·Dp − 404/A (Dp > L). El 404 sale de 200·(√1.07 + √0.15)² y se recalcula si cambias las alturas.
- Cóncavas, por faros: L = A·Dp²/(120 + 3.5·Dp) o L = 2·Dp − (120 + 3.5·Dp)/A. Se informa también la L de confort A·V²/395 como referencia.
- Adelantamiento, solo convexas: mismas fórmulas con Da y 946 = 200·(√1.07 + √1.30)².
- Se compara la L requerida con la L proyectada en Civil 3D, y se marcan los PVI sin curva cuya A supera el umbral.

**Visibilidad a lo largo del eje** (solo VISPARADA).

**DVP requerida**, en cada progresiva y sentido:

`Dp = 0.278·V·tp + V² / (254·((a/9.81) ± i))`

Aquí `i` es positiva en subida y negativa en bajada, vista en el sentido de circulación. La pendiente se busca dentro de la propia DVP y el cálculo se itera hasta converger. Por eso cada tramo y cada sentido tienen su propio valor sin que tengas que agruparlos a mano.

**Visibilidad disponible**: el ojo se ubica sobre el centro del carril, a la cota de la superficie más 1.07 m, de modo que incluye el peralte. El objeto se avanza a lo largo del eje, a la cota de la superficie más 0.15 m. La visual recta en 3D se muestrea contra la superficie. El primer punto donde la superficie supera la visual define el ocultamiento, y la distancia se refina por bisección a 0.10 m. Así se detectan tanto las crestas verticales como los taludes en el interior de las curvas horizontales.

**Resultado** por punto: Cumple, No cumple o No evaluable. Un punto es no evaluable, por ejemplo, cuando el eje o el perfil terminan antes de completar la DVP. Los puntos consecutivos que no cumplen se agrupan en sectores.

## 5. Validación

El análisis de curvas se validó contra una hoja Excel de proyecto (Ve = 80 km/h, 15 curvas, Da = 410 m). Con el criterio `Maximo` coinciden los 15 valores de Dp, tipo de curva, L requerida por parada y L por adelantamiento. Los valores intermedios coinciden al cuarto decimal (por ejemplo, 110.3878 y 113.4285 m en la curva 2).

El núcleo de visibilidad se probó con casos de solución analítica conocida:

- **Curva vertical convexa** (A = 8 %, L = 150 m, h1 = 1.07, h2 = 0.15). Visibilidad teórica 87.06 m; el plugin da 86.99 m.
- **Curva horizontal** (R = 200 m, talud a 6 m del centro del carril interior). Visibilidad teórica 98.64 m; el plugin da 98.59 m.
- Fórmula de DVP y efecto de la pendiente: coinciden exactamente.

**Antes de usarlo en un proyecto**, contrasta un par de sectores con la herramienta nativa *Comprobación de visibilidad* de Civil 3D, usando la misma DVP, alturas y desfases.

## 6. Limitaciones

- Solo detecta obstrucciones que existan en la superficie. Vegetación, muros, barreras, señales o edificaciones que no estén modelados no se detectan.
- Si en una curva horizontal la visual pasa fuera de los límites de la superficie, esas muestras se consideran libres. El informe advierte cuántas hubo.
- No considera ecuaciones de progresiva: trabaja con progresivas continuas.
- Asume unidades del dibujo en metros.
- La DVP calculada es la de la fórmula, sin redondear. Si tu norma exige usar los valores tabulados (redondeados), compárala con la tabla correspondiente.

## Estructura del código

```
Nucleo/   Cálculo e informe, sin dependencias de Autodesk
  Modelo.cs       Parámetros, resultados, interfaces
  Analizador.cs   DVP requerida, visibilidad disponible, sectores
  CurvasVerticales.cs  Verificación de curvas verticales por Dp y Da
  Informe.cs      HTML, CSV y lectura del archivo de velocidades
Civil/    Conexión con Civil 3D
  Adaptadores.cs  Alineamiento, perfil (incluida la lectura de PVI y curvas) y superficie
  Comando.cs      Comandos VISPARADA y VISCURVAS, solicitudes, dibujo de sectores
```
