# VisibilidadParada: plugin para Civil 3D 2027

Un solo comando, **VISIBILIDAD**, con un cuadro de diálogo al estilo de la *Comprobación de visibilidad* de Civil 3D:

1. Eliges el alineamiento y el perfil de rasante (de una lista o señalándolos en pantalla).
2. Configuras los parámetros: velocidad de diseño, tp, a, alturas de ojo y objeto, criterio de longitud de curva, umbral de A, longitud mínima absoluta y distancia de adelantamiento.
3. Pulsas **Analizar** y ves la tabla de curvas verticales con su veredicto CUMPLE / NO CUMPLE, además del informe HTML y los CSV para Excel.

No se ingresa una "visibilidad mínima" como en Civil 3D: el análisis es por curvas, y la Dp se calcula en cada PVI con la fórmula de la DG-2018 a partir de V, tp, a y las pendientes de entrada y salida.

Como opción, en la pestaña *Superficie (opcional)* se puede activar además la comprobación de la DVP en cada progresiva del eje contra la superficie del corredor, en ambos sentidos, para detectar también los taludes en curvas horizontales.

Los comandos de línea **VISCURVAS** y **VISPARADA** siguen disponibles para quien prefiera trabajar sin ventana; hacen lo mismo pidiendo los datos uno por uno.

## 1. Compilar

1. Instala **Visual Studio 2026** con la carga de trabajo "Desarrollo de escritorio de .NET". AutoCAD/Civil 3D 2027 exige .NET 10 y VS 2022 no sirve para esta versión.
2. Abre `VisibilidadParada.csproj`.
3. Si Civil 3D no está en `C:\Program Files\Autodesk\AutoCAD 2027`, corrige la propiedad `C3DPath` del `.csproj`. Las referencias usadas son:
   - `accoremgd.dll`, `acdbmgd.dll`, `acmgd.dll` (carpeta raíz)
   - `ACA\AecBaseMgd.dll`
   - `C3D\AeccDbMgd.dll`
   - `AdWindows.dll` (carpeta raíz), para la pestaña de la cinta
4. Compila en **Release | x64**. Obtendrás `bin\x64\Release\VisibilidadParada.dll` o `bin\Release\VisibilidadParada.dll`. Al terminar, la compilación copia sola el plugin al paquete de carga automática (ver sección 2).

## 2. Instalar en Civil 3D (pestaña ARBA)

Al cargarse, el plugin crea en la cinta una pestaña **ARBA** (al final, después de las de Civil 3D) con el panel **Visibilidad** y el botón *Visibilidad de parada*, que ejecuta VISIBILIDAD.

**Instalación automática (recomendada).** Con Civil 3D cerrado, abre PowerShell en esta carpeta y ejecuta:

```powershell
.\instalar.ps1
```

El script compila el proyecto y copia la DLL y `Bundle\PackageContents.xml` a `%APPDATA%\Autodesk\ApplicationPlugins\VisibilidadParada.bundle`. Esa carpeta es de confianza para AutoCAD, así que no aparece la advertencia de seguridad y el plugin se carga solo cada vez que abres Civil 3D. Si PowerShell bloquea el script, ejecútalo con `powershell -ExecutionPolicy Bypass -File .\instalar.ps1`.

Compilar desde Visual Studio hace lo mismo: el `.csproj` tiene un paso posterior a la compilación que copia los archivos al paquete. Si Civil 3D está abierto, la copia falla porque la DLL está en uso; ciérralo y vuelve a compilar. Para desactivar ese paso, pon `InstalarEnBundle` en `false` en el `.csproj`.

**Carga manual (para probar).** Escribe `NETLOAD` y selecciona `VisibilidadParada.dll`. La pestaña ARBA aparece al instante, pero solo dura esa sesión.

**Desinstalar.** Borra la carpeta `VisibilidadParada.bundle` de `%APPDATA%\Autodesk\ApplicationPlugins`.

### Poner otros plugins en la pestaña ARBA

La pestaña se identifica por el Id `ARBA_PESTANA` y el título `ARBA`. Cualquier otro plugin puede añadir sus botones a la misma pestaña sin duplicarla:

- Si el otro plugin referencia `VisibilidadParada.dll`, usa directamente `CintaArba.ObtenerPanel("MI_PANEL", "Mi panel")` y `CintaArba.AgregarBoton(...)` desde su `IExtensionApplication.Initialize`.
- Si prefieres que sea independiente, copia `Civil\Cinta.cs` al otro proyecto y cambia el contenido de `CrearBotonesVisibilidad()` por tus propios botones. Como `ObtenerPestana()` busca primero una pestaña con ese Id o título, todos los plugins terminan en la misma pestaña ARBA, cada uno con su panel.

Cada plugin lleva su propio `PackageContents.xml` con `LoadOnAutoCADStartup="True"` para que su panel aparezca al abrir Civil 3D y no solo al ejecutar un comando.

## 3. Uso

Pulsa el botón de la pestaña ARBA o escribe `VISIBILIDAD`. Se abre una ventana con cuatro pestañas:

**General**

- **Alineamiento** y **Perfil de rasante**: listas desplegables con los objetos del dibujo. El botón *Seleccionar en pantalla* oculta la ventana para señalar el objeto; si señalas un perfil, el alineamiento se ajusta solo. Se preselecciona el primer perfil que no sea de terreno (EG).
- **Rango de progresivas**: por defecto todo el perfil. Se puede acotar en metros o en formato km (2+350.00).
- **Informe**: ruta del HTML. Los CSV se guardan al lado con los sufijos `_curvas.csv` y, si hay superficie, `_progresivas.csv`.

**Parámetros**

| Campo | Referencia mostrada | Comentario |
|---|---|---|
| Velocidad de diseño (km/h) | — | Opción de cargar velocidades por tramo desde archivo (ver `velocidades_ejemplo.csv`). La velocidad ingresada se usa en las progresivas no cubiertas |
| tp (s) / a (m/s²) | 2.5 / 3.4 | Fórmula DG-2018 / AASHTO |
| Altura ojo / objeto (m) | 1.07 / 0.15 | |
| Longitud de curva | — | *Fórmula*: usa L de Dp < L si resulta ≥ Dp; si no, la de Dp > L. *Máximo*: la mayor de ambas (más conservador) |
| A para exigir curva (%) | — | Los PVI sin curva con A igual o mayor se marcan NO CUMPLE (falta curva) |
| Longitud mínima absoluta (m) | — | 0 = no aplicar. La L exigida es la mayor entre la de visibilidad y esta |
| Da (m) | — | 0 = no evaluar adelantamiento. También puede venir por tramo (cuarta columna del archivo de velocidades) |
| Altura objeto adelantamiento (m) | 1.30 | |

Los campos vienen precargados con los valores de referencia de la DG-2018; edítalos según tu norma y tipo de vía. El botón **Sugerir según DG-2018** rellena a partir de la velocidad de diseño los tres criterios que no salen de la geometría: A ≥ 1 % (carreteras pavimentadas; usa 2 % en afirmadas), longitud mínima absoluta 0.6·V (criterio AASHTO) y Da de la Tabla 205.03 (por ejemplo 410 m a 60 km/h y 540 m a 80 km/h).

**Superficie (opcional)**

Desactivada por defecto. Al activarla se pide la superficie de obstrucción (corredor con taludes, Top + daylight; no el terreno natural), los desfases de carril (positivo = derecha del eje en el sentido de las progresivas, por ejemplo +1.65 / −1.65), el intervalo entre progresivas, el sentido, el criterio de pendiente (*Desfavorable*: menor pendiente dentro de la DVP; *Promedio*: pendiente media), la precisión (Normal 1 m / 5 m, Fina 0.5 / 2.5, Rápida 2 / 10) y si se dibujan en planta los sectores deficientes (capas `VIS-PARADA-DEF-CRECIENTE`, roja, y `VIS-PARADA-DEF-DECRECIENTE`, magenta). Este análisis tarda más; el botón *Cancelar análisis* lo interrumpe.

**Resultados**

Un recuadro verde (CUMPLE) o rojo (NO CUMPLE) con los motivos, y la tabla de curvas verticales: PVI, ubicación, tipo, V, Pe, Ps, A, Dp calculada, L mínima exigida, L de proyecto, K mínima y de proyecto, estado por visibilidad de parada, Da, L requerida por adelantamiento, si permite adelantar y el texto de la verificación (Lp contra Lmín y Kp contra Kmín). Las filas que no cumplen van en rojo. Si se activó la superficie, debajo aparece la tabla de sectores del eje sin visibilidad suficiente. Los botones *Abrir informe HTML* y *Abrir carpeta* llevan a los archivos generados.

**Comandos de línea**: `VISCURVAS` (solo curvas) y `VISPARADA` (curvas más superficie) piden los mismos datos uno por uno en la línea de comandos, sin valores por defecto, y generan los mismos informes. ESC cancela.

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
  Motor.cs        Ejecuta el análisis (curvas y, opcionalmente, superficie), dibuja sectores y escribe los informes
  VentanaVisibilidad.xaml/.cs  Cuadro de diálogo del comando VISIBILIDAD (selección, parámetros, resultados)
  Comando.cs      Comandos VISIBILIDAD (ventana), VISPARADA y VISCURVAS (línea de comandos)
  Cinta.cs        Pestaña ARBA de la cinta (compartible con otros plugins) y arranque del plugin
Bundle/   PackageContents.xml para la carga automática (ApplicationPlugins)
instalar.ps1  Compila e instala el paquete de carga automática
```
