import sys
from PIL import Image

src_path = r'C:\IA\pyrevit-ext\IA-Tools.extension\ARBA.tab\IA.panel\IniciarIA.pushbutton\icon.png'
dest_32 = r'D:\Proyectos C#\Verificador DP y DA\ArbaMcp\Recursos\ARBA_BTN_MCP.png'
dest_16 = r'D:\Proyectos C#\Verificador DP y DA\ArbaMcp\Recursos\ARBA_BTN_MCP_16.png'

try:
    with Image.open(src_path) as img:
        img_32 = img.resize((32, 32), Image.Resampling.LANCZOS)
        img_32.save(dest_32, format='PNG')
        
        img_16 = img.resize((16, 16), Image.Resampling.LANCZOS)
        img_16.save(dest_16, format='PNG')
    print('Iconos creados exitosamente.')
except Exception as e:
    print(f'Error: {e}')
    sys.exit(1)
