using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MiniEmu;

enum DeviceKind { Emulator, Physical }

/// <summary>Un emulador (AVD) o un dispositivo físico conectado por adb.</summary>
/// <param name="Serial">El ID de adb, por ejemplo emulator-5554. Null si el emulador está apagado.</param>
sealed record Device(string Name, DeviceKind Kind, string? Serial)
{
    public bool IsRunning => Serial is not null;
}

sealed record ProcessResult(int ExitCode, string Output, string Error);

static class Proc
{
    public static Task<ProcessResult> RunAsync(string file, IEnumerable<string> args, TimeSpan? timeout = null)
    {
        var info = new ProcessStartInfo(file);
        // ArgumentList quotea cada argumento por separado: rutas con espacios y
        // nombres raros no pueden inyectar nada.
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return RunAsync(info, timeout);
    }

    public static async Task<ProcessResult> RunAsync(ProcessStartInfo info, TimeSpan? timeout = null)
    {
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"No pude iniciar {Path.GetFileName(info.FileName)}.");
        // Leer las dos salidas a la vez: si una se llena, el proceso se traba.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        using var cancel = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(cancel.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { /* ya terminó */ }
            throw new TimeoutException($"{Path.GetFileName(info.FileName)} no respondió a tiempo.");
        }
        return new ProcessResult(process.ExitCode, await output, await error);
    }
}

/// <summary>El Android SDK instalado y todo lo que MiniEmu hace con emulator.exe y adb.exe.</summary>
sealed partial class AndroidSdk
{
    const string TalkBackOn = "com.google.android.marvin.talkback/com.google.android.marvin.talkback.TalkBackService";
    // Igual que MiniSim: apuntar a un servicio que no existe apaga TalkBack sin
    // borrar la lista entera.
    const string TalkBackOff = "com.android.talkback/com.google.android.marvin.talkback.TalkBackService";

    public string Home { get; }
    public string Emulator => Path.Combine(Home, "emulator", "emulator.exe");
    public string Adb => Path.Combine(Home, "platform-tools", "adb.exe");

    AndroidSdk(string home) => Home = home;

    /// <summary>
    /// Busca el SDK: la ruta de los ajustes si está; si no, ANDROID_HOME,
    /// ANDROID_SDK_ROOT y la ruta por defecto de Android Studio.
    /// </summary>
    public static AndroidSdk? Locate(string? configured, out string? problem)
    {
        problem = null;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var sdk = new AndroidSdk(configured.Trim());
            if (File.Exists(sdk.Emulator)) return sdk;
            problem = $"La ruta androidHome de los ajustes ({configured}) no tiene emulator\\emulator.exe.";
            return null;
        }

        string?[] candidates =
        [
            Environment.GetEnvironmentVariable("ANDROID_HOME"),
            Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"),
        ];
        foreach (var home in candidates)
        {
            if (string.IsNullOrWhiteSpace(home)) continue;
            var sdk = new AndroidSdk(home);
            if (File.Exists(sdk.Emulator)) return sdk;
        }
        problem = "No encontré el Android SDK. Instalá Android Studio o poné la ruta en androidHome (menú → Ajustes).";
        return null;
    }

    // Los nombres de AVD solo admiten letras, números, punto, guion y guion bajo.
    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex AvdName();

    public async Task<IReadOnlyList<Device>> GetDevicesAsync()
    {
        var avdsTask = Proc.RunAsync(Emulator, ["-list-avds"]);
        var adbTask = Proc.RunAsync(Adb, ["devices", "-l"]);

        var avds = await avdsTask;
        if (avds.ExitCode != 0)
            throw new InvalidOperationException($"emulator -list-avds falló: {avds.Error.Trim()}");
        // emulator a veces mezcla líneas de log ("INFO | Storing crashdata…"): las descarta el regex.
        var names = Lines(avds.Output).Where(line => AvdName().IsMatch(line)).ToList();

        ProcessResult? adb = null;
        try { adb = await adbTask; }
        catch (Exception) { /* sin adb no hay dispositivos encendidos, pero los AVD se pueden lanzar igual */ }

        var physical = new List<Device>();
        var emulatorSerials = new List<string>();
        if (adb is { ExitCode: 0 })
        {
            foreach (var line in Lines(adb.Output).Skip(1))
            {
                var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2 || tokens[1] != "device") continue; // offline / unauthorized
                var serial = tokens[0];
                if (serial.StartsWith("emulator-", StringComparison.Ordinal))
                {
                    emulatorSerials.Add(serial);
                    continue;
                }
                // adb devices -l: "<serial> device product:x model:Pixel_7 device:y transport_id:1"
                var model = tokens.Skip(2)
                    .Select(t => t.Split(':', 2))
                    .FirstOrDefault(kv => kv.Length == 2 && kv[0] == "model")?[1];
                physical.Add(new Device(model?.Replace('_', ' ') ?? serial, DeviceKind.Physical, serial));
            }
        }

        // Cada emulador encendido nos dice qué AVD es.
        var running = await Task.WhenAll(emulatorSerials.Select(async serial =>
        {
            try
            {
                var result = await Proc.RunAsync(Adb, ["-s", serial, "emu", "avd", "name"], TimeSpan.FromSeconds(5));
                return (Name: Lines(result.Output).FirstOrDefault(), Serial: serial);
            }
            catch (Exception)
            {
                return (Name: (string?)null, Serial: serial);
            }
        }));
        var serialByName = running
            .Where(r => r.Name is not null)
            .GroupBy(r => r.Name!)
            .ToDictionary(g => g.Key, g => g.First().Serial);

        var emulators = names.Select(name =>
            new Device(name, DeviceKind.Emulator, serialByName.GetValueOrDefault(name)));

        return physical.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Concat(emulators.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Arranca el emulador sin esperarlo: queda vivo aunque MiniEmu se cierre.</summary>
    public Process Launch(string avd, IEnumerable<string> extraArgs)
    {
        var info = new ProcessStartInfo(Emulator)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Emulator)!,
        };
        info.ArgumentList.Add("@" + avd);
        foreach (var arg in extraArgs) info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new InvalidOperationException("No pude iniciar el emulador.");
    }

    public async Task<bool> ToggleTalkBackAsync(string serial)
    {
        var current = await Proc.RunAsync(Adb, ["-s", serial, "shell", "settings", "get", "secure", "enabled_accessibility_services"]);
        bool turnOn = current.Output.Trim() != TalkBackOn;
        var result = await Proc.RunAsync(Adb,
            ["-s", serial, "shell", "settings", "put", "secure", "enabled_accessibility_services", turnOn ? TalkBackOn : TalkBackOff]);
        if (result.ExitCode != 0) throw new InvalidOperationException($"adb no pudo cambiar TalkBack: {result.Error.Trim()}");
        return turnOn;
    }

    /// <summary>Escribe el texto en el campo enfocado del dispositivo con "input text".</summary>
    public async Task PasteTextAsync(string serial, string text)
    {
        if (text.Any(c => c > 127))
            throw new InvalidOperationException("adb input text solo acepta texto ASCII (sin tildes, eñes ni emojis).");

        var escaped = new StringBuilder();
        foreach (var c in text)
        {
            if (c is ' ' or '\t' or '\r' or '\n') escaped.Append("%s"); // input text usa %s como espacio
            else if ("\\'\"`$&|;<>()*?!#~{}[]^".Contains(c)) escaped.Append('\\').Append(c); // lo interpreta el sh del dispositivo
            else escaped.Append(c);
        }
        var result = await Proc.RunAsync(Adb, ["-s", serial, "shell", "input", "text", escaped.ToString()]);
        if (result.ExitCode != 0) throw new InvalidOperationException($"adb input text falló: {result.Error.Trim()}");
    }

    /// <summary>Abre logcat en Windows Terminal si está, o en una consola común.</summary>
    public void OpenLogcat(string serial)
    {
        var terminal = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        ProcessStartInfo info;
        if (File.Exists(terminal))
        {
            // Windows Terminal entiende los colores ANSI de "-v color".
            info = new ProcessStartInfo(terminal);
            foreach (var arg in new[] { "--title", $"logcat {serial}", Adb, "-s", serial, "logcat", "-v", "color" })
                info.ArgumentList.Add(arg);
        }
        else
        {
            info = new ProcessStartInfo(Adb);
            foreach (var arg in new[] { "-s", serial, "logcat", "-v", "threadtime" }) info.ArgumentList.Add(arg);
        }
        info.UseShellExecute = false;
        info.CreateNoWindow = false;
        Process.Start(info);
    }

    /// <summary>Sube archivos a la carpeta de descargas del dispositivo y devuelve esa carpeta.</summary>
    public async Task<string> UploadAsync(string serial, IReadOnlyList<string> files)
    {
        string? destination = null;
        foreach (var folder in new[] { "/sdcard/Download", "/sdcard/Downloads" })
        {
            var test = await Proc.RunAsync(Adb, ["-s", serial, "shell", "test", "-d", folder]);
            if (test.ExitCode == 0) { destination = folder; break; }
        }
        if (destination is null)
            throw new InvalidOperationException("No encontré la carpeta de descargas del dispositivo (/sdcard/Download).");

        foreach (var file in files)
        {
            var target = $"{destination}/{Path.GetFileName(file)}";
            var push = await Proc.RunAsync(Adb, ["-s", serial, "push", file, target], TimeSpan.FromMinutes(10));
            if (push.ExitCode != 0)
                throw new InvalidOperationException($"adb push falló con {Path.GetFileName(file)}: {push.Error.Trim()}");

            // Para que aparezca en la galería y en Archivos sin reiniciar. Si falla, no importa.
            var uri = "'file://" + target.Replace("'", "'\\''") + "'";
            await Proc.RunAsync(Adb, ["-s", serial, "shell", "am", "broadcast", "-a",
                "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", uri]);
        }
        return destination;
    }

    public Task KillAsync(string serial) => Proc.RunAsync(Adb, ["-s", serial, "emu", "kill"]);

    /// <summary>
    /// Borra el AVD: su .ini y su carpeta .avd. Es lo que hace avdmanager, pero
    /// sin necesitar Java ni las cmdline-tools.
    /// </summary>
    public static async Task DeleteAvdAsync(string name)
    {
        var avdHome = AvdHome();
        var ini = Path.Combine(avdHome, name + ".ini");
        var folder = Path.Combine(avdHome, name + ".avd");
        if (File.Exists(ini))
        {
            var path = File.ReadLines(ini).FirstOrDefault(l => l.StartsWith("path=", StringComparison.Ordinal));
            if (path is not null) folder = path["path=".Length..].Trim();
        }
        if (!File.Exists(ini) && !Directory.Exists(folder))
            throw new InvalidOperationException($"No encontré los archivos de {name} en {avdHome}.");

        // Recién apagado, el emulador puede tardar unos segundos en soltar sus archivos.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
                if (File.Exists(ini)) File.Delete(ini);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                await Task.Delay(500);
            }
            catch (UnauthorizedAccessException) when (attempt < 20)
            {
                await Task.Delay(500);
            }
        }
    }

    static string AvdHome()
    {
        var avdHome = Environment.GetEnvironmentVariable("ANDROID_AVD_HOME");
        if (!string.IsNullOrWhiteSpace(avdHome)) return avdHome;
        var userHome = Environment.GetEnvironmentVariable("ANDROID_USER_HOME");
        if (!string.IsNullOrWhiteSpace(userHome)) return Path.Combine(userHome, "avd");
        var sdkHome = Environment.GetEnvironmentVariable("ANDROID_SDK_HOME");
        if (!string.IsNullOrWhiteSpace(sdkHome)) return Path.Combine(sdkHome, ".android", "avd");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "avd");
    }

    /// <summary>
    /// Corre un comando propio con cmd.exe. Los que arrancan el dispositivo no se
    /// esperan (devuelve null); el resto, hasta 2 minutos.
    /// </summary>
    public async Task<ProcessResult?> RunCustomAsync(CustomCommand command, Device device)
    {
        var line = command.Command
            .Replace("$device_name", device.Name)
            .Replace("$adb_path", Adb)
            .Replace("$adb_id", device.Serial ?? "")
            .Replace("$android_home_path", Home);
        // /s: cmd saca solo las comillas de afuera y deja intactas las del comando.
        var info = new ProcessStartInfo("cmd.exe") { Arguments = $"/d /s /c \"{line}\"" };

        if (command.BootsDevice)
        {
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            Process.Start(info)?.Dispose();
            return null;
        }
        return await Proc.RunAsync(info, TimeSpan.FromMinutes(2));
    }

    static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
}
