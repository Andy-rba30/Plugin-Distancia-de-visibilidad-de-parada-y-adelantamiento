# Compila el plugin e instala el paquete de carga automatica en
# %APPDATA%\Autodesk\ApplicationPlugins\ArbaMcp.bundle
# Uso (PowerShell, en esta carpeta):  .\instalar.ps1
param([string]$Configuracion = "Release")

$ErrorActionPreference = "Stop"
$raiz = $PSScriptRoot

if (Get-Process -Name "acad" -ErrorAction SilentlyContinue) {
    Write-Host "Cierra Civil 3D antes de instalar (la DLL esta en uso)." -ForegroundColor Yellow
    exit 1
}

Write-Host "Compilando ($Configuracion)..." -ForegroundColor Cyan
dotnet build "$raiz\ArbaMcp.csproj" -c $Configuracion -p:InstalarEnBundle=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = Get-ChildItem "$raiz\bin" -Recurse -Filter "ArbaMcp.dll" |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $dll) { throw "No se encontro ArbaMcp.dll en bin\. Fallo la compilacion?" }

$bundle = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\ArbaMcp.bundle"
New-Item -ItemType Directory -Force -Path (Join-Path $bundle "Contents") | Out-Null
Copy-Item (Join-Path $raiz "Bundle\PackageContents.xml") $bundle -Force
Copy-Item $dll.FullName (Join-Path $bundle "Contents") -Force
$pdb = [System.IO.Path]::ChangeExtension($dll.FullName, ".pdb")
if (Test-Path $pdb) { Copy-Item $pdb (Join-Path $bundle "Contents") -Force }

Write-Host ""
Write-Host "Instalado en: $bundle" -ForegroundColor Green
Write-Host "Abre Civil 3D: en la pestana ARBA aparecera el boton Conexion IA."
