# Compila el plugin e instala el paquete de carga automatica en
# %APPDATA%\Autodesk\ApplicationPlugins\VisibilidadParada.bundle
# Uso (PowerShell, en esta carpeta):  .\instalar.ps1
param([string]$Configuracion = "Release")

$ErrorActionPreference = "Stop"
$raiz = $PSScriptRoot

if (Get-Process -Name "acad" -ErrorAction SilentlyContinue) {
    Write-Host "Cierra Civil 3D antes de instalar (la DLL esta en uso)." -ForegroundColor Yellow
    exit 1
}

Write-Host "Compilando ($Configuracion)..." -ForegroundColor Cyan
dotnet build "$raiz\VisibilidadParada.csproj" -c $Configuracion -p:InstalarEnBundle=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = Get-ChildItem "$raiz\bin" -Recurse -Filter "VisibilidadParada.dll" |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $dll) { throw "No se encontro VisibilidadParada.dll en bin\. Fallo la compilacion?" }

$bundle = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\VisibilidadParada.bundle"
New-Item -ItemType Directory -Force -Path (Join-Path $bundle "Contents") | Out-Null
Copy-Item (Join-Path $raiz "Bundle\PackageContents.xml") $bundle -Force
Copy-Item $dll.FullName (Join-Path $bundle "Contents") -Force
$pdb = [System.IO.Path]::ChangeExtension($dll.FullName, ".pdb")
if (Test-Path $pdb) { Copy-Item $pdb (Join-Path $bundle "Contents") -Force }

Write-Host ""
Write-Host "Instalado en: $bundle" -ForegroundColor Green
Write-Host "Abre Civil 3D: la pestana ARBA aparecera en la cinta con los botones del plugin."
