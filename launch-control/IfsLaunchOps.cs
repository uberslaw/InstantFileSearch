using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using LaunchControl.Standard.Host;

namespace InstantFileSearch.LaunchControl;

/// <summary>
/// Repo-local helpers for Instant File Search Launch Control.
/// Status / Start / Stop / Theme live in LaunchControl.Standard; this file is the
/// compiled-app ExtraActions (rebuild, run config, CLI, publish, tests, folders).
/// </summary>
internal static class IfsLaunchOps
{
    private const int MaxLoggedLines = 250;
    private static int _busy;
    private static readonly object OpsLock = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string LastConfiguration { get; private set; } = "Release";

    public static string LogDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InstantFileSearch",
        "logs");

    public static string OpsLogPath => Path.Combine(LogDir, "ops.log");
    public static string CrashLogPath => Path.Combine(LogDir, "launch-control.log");
    public static string PidFilePath => Path.Combine(LogDir, "ifs.pid");

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InstantFileSearch",
        "launch_control.json");

    public static void EnsureLogDir() => Directory.CreateDirectory(LogDir);

    public static void TouchLog(string path)
    {
        try
        {
            EnsureLogDir();
            if (!File.Exists(path))
                File.WriteAllText(path, "");
        }
        catch { /* ignore */ }
    }

    public static string FindRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = start;
            for (var i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
            {
                if (LooksLikeRoot(dir))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName ?? "";
            }
        }

        return Directory.GetCurrentDirectory();
    }

    public static bool LooksLikeRoot(string dir) =>
        File.Exists(Path.Combine(dir, "InstantFileSearch.slnx"))
        || File.Exists(Path.Combine(dir, "src", "InstantFileSearch", "InstantFileSearch.csproj"));

    public static string ReadProductVersion(string root)
    {
        var csproj = UiProject(root);
        if (!File.Exists(csproj))
            return "1.0.0";
        try
        {
            var text = File.ReadAllText(csproj);
            var match = Regex.Match(text, @"<Version>\s*([^<]+?)\s*</Version>");
            if (match.Success)
            {
                var value = match.Groups[1].Value.Trim();
                if (value.Length > 0)
                    return value;
            }
        }
        catch { /* keep default */ }

        return "1.0.0";
    }

    public static string UiProject(string root) =>
        Path.Combine(root, "src", "InstantFileSearch", "InstantFileSearch.csproj");

    public static string TestProject(string root) =>
        Path.Combine(root, "tests", "InstantFileSearch.Tests", "InstantFileSearch.Tests.csproj");

    public static string BuiltExe(string root, string configuration) =>
        Path.Combine(root, "src", "InstantFileSearch", "bin", configuration, "net8.0-windows", "InstantFileSearch.exe");

    public static string DistExe(string root) =>
        Path.Combine(root, "dist", "InstantFileSearch.exe");

    public static void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return;
            var doc = JsonSerializer.Deserialize<LcSettings>(File.ReadAllText(SettingsPath), Json);
            if (doc is null)
                return;
            if (IsConfiguration(doc.LastConfiguration))
                LastConfiguration = doc.LastConfiguration!;
        }
        catch { /* keep default */ }
    }

    public static void SaveLastConfiguration(string configuration)
    {
        if (!IsConfiguration(configuration))
            return;
        LastConfiguration = string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase)
            ? "Debug"
            : "Release";
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
                new LcSettings { LastConfiguration = LastConfiguration }, Json));
        }
        catch { /* ignore */ }
    }

    private static bool IsConfiguration(string? value) =>
        string.Equals(value, "Release", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "Debug", StringComparison.OrdinalIgnoreCase);

    public static (string FileName, string Arguments) ResolveStart(
        string root, string configuration, bool refuseIfRunning)
    {
        if (refuseIfRunning)
        {
            var pids = FindProductPids(root);
            if (pids.Count > 0)
                throw new InvalidOperationException(
                    $"Already running PID {string.Join(", ", pids)}. Stop first, or use Restart.");
        }

        var config = IsConfiguration(configuration) ? configuration : "Release";
        var built = BuiltExe(root, config);
        if (File.Exists(built))
            return (built, "");

        if (string.Equals(config, "Release", StringComparison.OrdinalIgnoreCase))
        {
            var dist = DistExe(root);
            if (File.Exists(dist))
                return (dist, "");
        }

        var dotnet = FindDotnet()
            ?? throw new FileNotFoundException(
                "dotnet.exe was not found. Install the .NET 8 SDK, or Rebuild so InstantFileSearch.exe exists.");
        var project = UiProject(root);
        if (!File.Exists(project))
            throw new FileNotFoundException("Instant File Search WPF project was not found.", project);
        return (dotnet, $"run --project \"{project}\" -c {config}");
    }

    /// <summary>
    /// InstantFileSearch.exe whose image path is under this repo (bin / dist).
    /// Does not match a copy of the app from some other folder.
    /// </summary>
    public static IReadOnlyList<int> FindProductPids(string root)
    {
        var pids = new List<int>();
        string prefix;
        try
        {
            prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        }
        catch
        {
            return pids;
        }

        Process[] processes;
        try { processes = Process.GetProcessesByName("InstantFileSearch"); }
        catch { processes = []; }

        foreach (var process in processes)
        {
            try
            {
                var path = TryGetImagePath(process);
                if (string.IsNullOrEmpty(path))
                    continue;
                var full = Path.GetFullPath(path);
                if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    pids.Add(process.Id);
            }
            catch
            {
                /* skip inaccessible processes — do not match by name alone */
            }
            finally
            {
                process.Dispose();
            }
        }

        try
        {
            if (File.Exists(PidFilePath)
                && int.TryParse(File.ReadAllText(PidFilePath).Trim().Split('\n', '\r')[0], out var tracked)
                && tracked > 0
                && ServiceRuntime.IsProcessAlive(tracked)
                && !pids.Contains(tracked)
                && PidFileMatchesProcess(tracked, prefix))
            {
                pids.Add(tracked);
            }
        }
        catch { /* stale pid file */ }

        return pids;
    }

    private static bool PidFileMatchesProcess(int pid, string prefix)
    {
        using var process = Process.GetProcessById(pid);
        var name = process.ProcessName;
        if (name.Equals("InstantFileSearch", StringComparison.OrdinalIgnoreCase))
        {
            var image = TryGetImagePath(process);
            if (string.IsNullOrEmpty(image))
                return false;
            return Path.GetFullPath(image).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        if (!name.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var written = File.GetLastWriteTimeUtc(PidFilePath);
            var started = process.StartTime.ToUniversalTime();
            return started >= written.AddSeconds(-15) && started <= written.AddMinutes(10);
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetImagePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    public static void Rebuild(LaunchControlWindow window, string root, string configuration)
    {
        SaveLastConfiguration(configuration);
        var project = UiProject(root);
        if (!File.Exists(project))
        {
            window.AppendLog($"Missing {project}", "ERROR");
            return;
        }

        var dotnet = FindDotnet();
        if (dotnet is null)
        {
            window.AppendLog("dotnet.exe was not found. Install the .NET 8 SDK.", "ERROR");
            return;
        }

        RunLogged(window, root, dotnet,
            $"build \"{project}\" -c {configuration} --nologo",
            $"Rebuild {configuration}",
            onSuccess: () =>
            {
                var exe = BuiltExe(root, configuration);
                if (File.Exists(exe))
                    window.AppendLog($"Built {exe}", "OK");
                else
                    window.AppendLog($"Build exit 0 but {exe} is missing.", "WARN");
            });
    }

    public static void RunTests(LaunchControlWindow window, string root)
    {
        var project = TestProject(root);
        if (!File.Exists(project))
        {
            window.AppendLog($"Missing {project}", "ERROR");
            return;
        }

        var dotnet = FindDotnet();
        if (dotnet is null)
        {
            window.AppendLog("dotnet.exe was not found. Install the .NET 8 SDK.", "ERROR");
            return;
        }

        RunLogged(window, root, dotnet,
            $"test \"{project}\" --nologo",
            "Run tests");
    }

    public static void Publish(LaunchControlWindow window, string root)
    {
        var script = Path.Combine(root, "publish.ps1");
        if (!File.Exists(script))
        {
            window.AppendLog($"Missing {script}", "ERROR");
            return;
        }

        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe");
        if (!File.Exists(powershell))
            powershell = "powershell.exe";

        RunLogged(window, root, powershell,
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
            "Publish",
            onSuccess: () =>
            {
                var exe = DistExe(root);
                if (File.Exists(exe))
                    window.AppendLog($"Published {exe}", "OK");
                else
                    window.AppendLog("publish.ps1 exit 0 but dist\\InstantFileSearch.exe is missing.", "WARN");
            });
    }

    public static void RunConfiguration(LaunchControlWindow window, string root, string configuration)
    {
        SaveLastConfiguration(configuration);
        var existing = FindProductPids(root);
        if (existing.Count > 0)
        {
            window.AppendLog(
                $"Already running PID {string.Join(", ", existing)}. Stop first, or use Restart.",
                "WARN");
            return;
        }

        try
        {
            var (file, args) = ResolveStart(root, configuration, refuseIfRunning: false);
            if (!File.Exists(file))
            {
                window.AppendLog($"Cannot launch; missing {file}", "ERROR");
                return;
            }

            var work = Directory.Exists(root) ? root : (Path.GetDirectoryName(file) ?? root);
            window.AppendLog($"Run {configuration}: {file} {args} (cwd {work})", "STEP");
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args ?? "",
                WorkingDirectory = work,
                UseShellExecute = false
            });
            if (started is null)
            {
                window.AppendLog($"Started {file}", "INFO");
                return;
            }

            try
            {
                Directory.CreateDirectory(LogDir);
                File.WriteAllText(PidFilePath, started.Id.ToString());
            }
            catch { /* ignore */ }

            window.AppendLog($"Launched PID {started.Id}: {file}", "OK");
        }
        catch (Exception ex)
        {
            window.AppendLog(ex.Message, "ERROR");
        }
    }

    public static void OpenCli(LaunchControlWindow window, string root)
    {
        if (!Directory.Exists(root) || !LooksLikeRoot(root))
        {
            window.AppendLog($"Repo root not found: {root}", "ERROR");
            return;
        }

        const string banner =
            "echo Instant File Search repo. & echo Useful: & echo   dotnet run --project src\\InstantFileSearch.Cli -- help & echo   dotnet run --project src\\InstantFileSearch.Cli -- scan . & echo   dotnet test tests\\InstantFileSearch.Tests\\InstantFileSearch.Tests.csproj & echo   .\\publish.ps1";

        try
        {
            var wt = FindWindowsTerminal();
            if (wt is not null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = wt,
                    Arguments = $"-d {Quote(root)} -- cmd /k {Quote(banner)}",
                    WorkingDirectory = root,
                    UseShellExecute = true
                });
                window.AppendLog($"Opened Windows Terminal (wt) at {root}");
                return;
            }
        }
        catch (Exception ex)
        {
            window.AppendLog($"Windows Terminal failed ({ex.Message}); falling back to cmd.exe.", "WARN");
        }

        var comspec = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(comspec))
            comspec = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        Process.Start(new ProcessStartInfo
        {
            FileName = comspec,
            Arguments = "/k " + banner,
            WorkingDirectory = root,
            UseShellExecute = true
        });
        window.AppendLog($"Opened cmd.exe at {root}");
    }

    public static void OpenSolution(LaunchControlWindow window, string root)
    {
        var slnx = Path.Combine(root, "InstantFileSearch.slnx");
        if (!File.Exists(slnx))
        {
            window.AppendLog($"Missing {slnx}", "ERROR");
            return;
        }

        ProcessUtil.OpenPath(slnx);
        window.AppendLog($"Opened solution {slnx}");
    }

    public static void OpenDist(LaunchControlWindow window, string root)
    {
        var dist = Path.Combine(root, "dist");
        if (!Directory.Exists(dist))
        {
            window.AppendLog("dist\\ does not exist yet. Use Publish (publish.ps1) first.", "WARN");
            return;
        }

        ProcessUtil.OpenPath(dist);
        window.AppendLog($"Opened {dist}");
    }

    public static void OpenFolder(LaunchControlWindow window, string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        Directory.CreateDirectory(path);
        ProcessUtil.OpenPath(path);
        window.AppendLog($"Opened {label}: {path}");
    }

    private static void RunLogged(
        LaunchControlWindow window,
        string workingDirectory,
        string fileName,
        string arguments,
        string title,
        Action? onSuccess = null)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            window.AppendLog($"Busy: another command is still running. Wait, then retry {title}.", "WARN");
            return;
        }

        window.AppendLog($"{title}: {fileName} {arguments} (cwd {workingDirectory})", "STEP");
        _ = Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = new Process { StartInfo = psi };
                var lines = 0;
                var truncated = false;
                void Handle(string? line)
                {
                    if (string.IsNullOrEmpty(line))
                        return;
                    AppendOpsFile(line);
                    if (Interlocked.Increment(ref lines) == MaxLoggedLines)
                    {
                        truncated = true;
                        window.AppendLog($"… truncated after {MaxLoggedLines} lines (ops.log still receives the rest).", "WARN");
                        return;
                    }

                    if (lines < MaxLoggedLines)
                        window.AppendLog(line, InferLevel(line));
                }

                proc.OutputDataReceived += (_, e) => Handle(e.Data);
                proc.ErrorDataReceived += (_, e) => Handle(e.Data);
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();
                var code = proc.ExitCode;
                if (truncated)
                    window.AppendLog($"{title} finished with extra lines in {OpsLogPath}.", "INFO");
                if (code == 0)
                {
                    window.AppendLog($"{title} exit 0", "OK");
                    onSuccess?.Invoke();
                }
                else
                    window.AppendLog($"{title} failed (exit {code}).", "ERROR");
            }
            catch (Exception ex)
            {
                window.AppendLog($"{title} failed: {ex.Message}", "ERROR");
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    private static void AppendOpsFile(string line)
    {
        try
        {
            EnsureLogDir();
            lock (OpsLock)
                File.AppendAllText(OpsLogPath, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        }
        catch { /* ignore */ }
    }

    private static string InferLevel(string line)
    {
        if (line.Contains("error ", StringComparison.OrdinalIgnoreCase)
            || line.Contains(": error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
            return "ERROR";
        if (line.Contains("warning ", StringComparison.OrdinalIgnoreCase)
            || line.Contains(": warning", StringComparison.OrdinalIgnoreCase))
            return "WARN";
        if (line.Contains("Passed!", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase))
            return "OK";
        return "INFO";
    }

    public static string? FindDotnet()
    {
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            var fromRoot = Path.Combine(root, "dotnet.exe");
            if (File.Exists(fromRoot))
                return fromRoot;
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "dotnet.exe")
                 })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        try
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                var candidate = Path.Combine(dir.Trim('"'), "dotnet.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch { }

        return null;
    }

    private static string? FindWindowsTerminal()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var candidate in new[]
                 {
                     Path.Combine(local, @"Microsoft\WindowsApps\wt.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Terminal", "wt.exe")
                 })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        try
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                var candidate = Path.Combine(dir.Trim('"'), "wt.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch { }

        return null;
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        if (value.IndexOfAny([' ', '\t', '"']) < 0)
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private sealed class LcSettings
    {
        public string? LastConfiguration { get; set; }
    }
}
