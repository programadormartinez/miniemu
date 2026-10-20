# MiniEmu — desinstalador, en PowerShell:
#
#   irm https://raw.githubusercontent.com/programadormartinez/miniemu/main/uninstall.ps1 | iex
#
# Saca la app, el acceso directo y el inicio automático. Tus ajustes quedan en
# %APPDATA%\MiniEmu salvo que antes definas $env:MINIEMU_PURGE = '1'.

& {
    $ErrorActionPreference = 'Stop'

    Get-Process -Name MiniEmu -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300

    Remove-Item -Recurse -Force (Join-Path $env:LOCALAPPDATA 'Programs\MiniEmu') -ErrorAction SilentlyContinue
    Remove-Item -Force (Join-Path ([Environment]::GetFolderPath('Programs')) 'MiniEmu.lnk') -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MiniEmu' -ErrorAction SilentlyContinue

    $settings = Join-Path $env:APPDATA 'MiniEmu'
    if ($env:MINIEMU_PURGE -eq '1') {
        Remove-Item -Recurse -Force $settings -ErrorAction SilentlyContinue
        Write-Host "`n  MiniEmu y sus ajustes fueron eliminados.`n"
    } else {
        Write-Host "`n  MiniEmu fue desinstalado. Tus ajustes siguen en $settings`n"
    }
}
