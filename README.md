# MiniEmu

Lanzá y manejá emuladores de Android desde la bandeja de Windows.

MiniEmu es un port para Windows de [MiniSim](https://github.com/okwasniewski/MiniSim),
de Oskar Kwaśniewski. Cubre la parte de Android: los simuladores de iOS solo
existen en macOS.

## Instalar

En PowerShell:

```powershell
irm https://raw.githubusercontent.com/programadormartinez/miniemu/main/install.ps1 | iex
```

Instala un solo `.exe` (x64 o ARM64) en `%LOCALAPPDATA%\Programs\MiniEmu`, sin
pedir administrador ni .NET instalado. Verifica el SHA-256 que publica GitHub,
agrega un acceso directo al menú Inicio y lo deja arrancando con Windows.
Para actualizar, corré el mismo comando.

Necesitás el Android SDK, que viene con Android Studio. MiniEmu lo busca en
`androidHome` (ajustes), `ANDROID_HOME`, `ANDROID_SDK_ROOT` y
`%LOCALAPPDATA%\Android\Sdk`, en ese orden.

## Usar

Click en el ícono de la bandeja, o **Alt + Shift + E** desde cualquier lado.
Con el menú abierto, los emuladores se eligen con 1…9 y 0.

| En cada emulador | Encendido | Apagado |
| --- | --- | --- |
| Iniciar / mostrar su ventana (click en el nombre) | ✓ | ✓ |
| Copiar nombre | ✓ | ✓ |
| Copiar ID de ADB | ✓ | |
| Arranque en frío (`-no-snapshot`), iniciar sin audio (`-no-audio`) | | ✓ |
| Activar o desactivar TalkBack | ✓ | |
| Pegar el portapapeles en el dispositivo (solo ASCII) | ✓ | |
| Abrir logcat (en Windows Terminal, si está) | ✓ | |
| Subir archivos a `/sdcard/Download` | ✓ | |
| Eliminar emulador (Shift + click: sin confirmar) | ✓ | ✓ |

Los dispositivos físicos conectados por USB o Wi-Fi aparecen en su propia sección,
con las acciones que necesitan un dispositivo encendido.

## Ajustes

**Ajustes** abre `%APPDATA%\MiniEmu\settings.json` en Notepad. Los
cambios se aplican la próxima vez que abrís el menú.

```jsonc
{
  "androidHome": null,              // ruta del SDK; null = detectarla
  "hotkey": "Alt+Shift+E",          // Ctrl, Alt, Shift, Win + una tecla
  "launchParameters": ["-no-boot-anim", "-gpu host"],  // se agregan a cada arranque
  "commands": [
    {
      "name": "Metro: adb reverse 8081",
      "command": "\"$adb_path\" -s $adb_id reverse tcp:8081 tcp:8081",
      "needsRunningDevice": true
    },
    {
      "name": "Iniciar borrando datos",
      "command": "\"$android_home_path\\emulator\\emulator.exe\" @$device_name -wipe-data",
      "bootsDevice": true
    }
  ]
}
```

Los comandos propios corren con `cmd.exe`. Variables: `$device_name`,
`$adb_path`, `$adb_id` (solo con el dispositivo encendido) y
`$android_home_path`. Poné las rutas entre comillas: pueden tener espacios.
`needsRunningDevice` muestra el comando solo con el dispositivo encendido;
`bootsDevice`, solo con el emulador apagado.

## Desinstalar

```powershell
irm https://raw.githubusercontent.com/programadormartinez/miniemu/main/uninstall.ps1 | iex
```

Tus ajustes quedan en `%APPDATA%\MiniEmu`. Para borrarlos también, corré antes
`$env:MINIEMU_PURGE = '1'`.

## Compilar

Con el SDK de .NET 10:

```powershell
dotnet publish src/MiniEmu/MiniEmu.csproj -c Release -r win-x64 -o out
```

Cada tag `vX.Y.Z` publica una release con `MiniEmu-win-x64.exe` y
`MiniEmu-win-arm64.exe` desde GitHub Actions.

## Licencia

MIT. Basado en MiniSim © 2023 Oskar Kwaśniewski, también MIT.
