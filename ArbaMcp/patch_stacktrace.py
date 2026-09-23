import os, re

path = r'D:\Proyectos C#\Verificador DP y DA\ArbaMcp\Servidor.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('Historial.Registrar("MCP ← " + nombre + " ERROR: " + raiz.Message);', 'Historial.Registrar("MCP ← " + nombre + " ERROR: " + raiz.Message + " " + raiz.StackTrace);')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
