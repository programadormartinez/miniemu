# MiniEmu — instalador de un comando, en PowerShell:
#
#   irm https://raw.githubusercontent.com/programadormartinez/miniemu/main/install.ps1 | iex
#
# Qué hace, en orden:
#   1. busca la última release de MiniEmu en GitHub, por HTTPS;
#   2. baja el .exe de tu arquitectura (x64 o ARM64) y verifica su SHA-256;
#   3. lo instala en %LOCALAPPDATA%\Programs\MiniEmu, sin pedir administrador;
#   4. agrega un acceso directo en el menú Inicio, lo deja arrancando con
#      Windows y lo abre.
#
# Para desinstalar:
#
#   irm https://raw.githubusercontent.com/programadormartinez/miniemu/main/uninstall.ps1 | iex
#
# Todo está dentro de un bloque: iex lo parsea entero antes de ejecutar, así
# que una descarga cortada a la mitad no ejecuta medio script.

& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'  # la barra de progreso hace lentísimo a Invoke-WebRequest
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    $repo = if ($env:MINIEMU_REPO) { $env:MINIEMU_REPO } else { 'programadormartinez/miniemu' }
    $rid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
    $assetName = "MiniEmu-$rid.exe"
    $dir = Join-Path $env:LOCALAPPDATA 'Programs\MiniEmu'
    $exe = Join-Path $dir 'MiniEmu.exe'

    Write-Host "`n  MiniEmu para Windows`n"

    Write-Host '  · Buscando la última versión'
    $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" `
        -Headers @{ 'User-Agent' = 'MiniEmu-installer'; 'Accept' = 'application/vnd.github+json' }
    $asset = $release.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
    if (-not $asset) { throw "La release $($release.tag_name) no tiene $assetName." }

    Write-Host "  · Descargando MiniEmu $($release.tag_name) ($rid)"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $download = "$exe.download"
    Invoke-WebRequest $asset.browser_download_url -OutFile $download -UseBasicParsing

    # GitHub publica el SHA-256 de cada asset; si está, tiene que coincidir.
    if ($asset.digest -match '^sha256:([0-9a-f]{64})$') {
        $actual = (Get-FileHash $download -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $Matches[1]) {
            Remove-Item $download -Force
            throw 'El archivo descargado no coincide con el SHA-256 que publicó GitHub.'
        }
        Write-Host '  · SHA-256 verificado'
    }

    Write-Host "  · Instalando en $dir"
    Get-Process -Name MiniEmu -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300
    Move-Item -Force $download $exe

    $startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'MiniEmu.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startMenu)
    $shortcut.TargetPath = $exe
    $shortcut.Description = 'Lanzá emuladores de Android desde la bandeja'
    $shortcut.Save()

    Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MiniEmu' -Value "`"$exe`""

    Start-Process $exe

    Write-Host "`n  MiniEmu $($release.tag_name) está instalado."
    Write-Host '  Buscá el ícono en la bandeja del sistema (abajo a la derecha) o abrí el menú con Alt + Shift + E.'
    Write-Host '  Para actualizar, corré el mismo comando otra vez.'
    Write-Host ''
}
