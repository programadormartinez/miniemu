using System.Text.Json;

namespace MiniEmu;

/// <summary>Un comando propio que aparece en el submenú de cada dispositivo.</summary>
sealed class CustomCommand
{
    public string Name { get; set; } = "";

    /// <summary>
    /// Se ejecuta con cmd.exe. Variables: $device_name, $adb_path, $adb_id, $android_home_path.
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary>Solo se muestra con el dispositivo encendido (y entonces $adb_id tiene valor).</summary>
    public bool NeedsRunningDevice { get; set; }

    /// <summary>Solo se muestra con el emulador apagado; no se espera a que termine.</summary>
    public bool BootsDevice { get; set; }
}

/// <summary>Ajustes guardados en %APPDATA%\MiniEmu\settings.json.</summary>
sealed class Settings
{
    /// <summary>Ruta del Android SDK. Vacío: ANDROID_HOME, ANDROID_SDK_ROOT o %LOCALAPPDATA%\Android\Sdk.</summary>
    public string? AndroidHome { get; set; }

    public string Hotkey { get; set; } = "Alt+Shift+E";

    /// <summary>Flags que se agregan a cada arranque, por ejemplo "-no-boot-anim" o "-gpu host".</summary>
    public List<string> LaunchParameters { get; set; } = [];

    public List<CustomCommand> Commands { get; set; } = [];

    public static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniEmu");

    public static string FilePath => Path.Combine(FolderPath, "settings.json");

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Lee los ajustes; si no existen, crea el archivo con los valores por defecto.</summary>
    public static Settings Load()
    {
        if (!File.Exists(FilePath))
        {
            var defaults = new Settings();
            defaults.Save();
            return defaults;
        }
        return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(FolderPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
