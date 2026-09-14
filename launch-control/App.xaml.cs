using System.Windows;
using LaunchControl.Standard.Host;

namespace InstantFileSearch.LaunchControl;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var root = IfsLaunchOps.FindRoot();
        IfsLaunchOps.EnsureLogDir();
        IfsLaunchOps.LoadSettings();
        var version = IfsLaunchOps.ReadProductVersion(root);
        var opsLog = IfsLaunchOps.OpsLogPath;
        var crashLog = IfsLaunchOps.CrashLogPath;
        var pidFile = IfsLaunchOps.PidFilePath;
        IfsLaunchOps.TouchLog(opsLog);

        LaunchControlApp.Run(this, new LaunchControlProfile
        {
            ProductId = "instantfilesearch",
            ProductName = "Instant File Search",
            AppDataFolder = "InstantFileSearch",
            ServiceNames = [],
            ShowVenvUi = false,
            ShowBrowserButton = false,
            ShowRestartButton = true,
            ShowStartStopButtons = true,
            ShowRefreshButton = true,
            ShowFollowLogsButton = true,
            InstallRoot = root,
            LogPaths = [opsLog],
            CrashLogPath = crashLog,
            StartButtonText = "Start",
            StopButtonText = "Stop",
            FooterText = "Closing this window does not stop Instant File Search.",
            DefaultColors = new Dictionary<string, string>
            {
                ["ChromeColor"] = "#1B365D",
                ["PrimaryActionColor"] = "#2A9D8F"
            },
            MetaText = () =>
                $"Instant File Search v{version}   last {IfsLaunchOps.LastConfiguration}   {root}",
            StartupNotes =
            [
                "Closing this window does not stop Instant File Search.",
                "Start / Restart use the last Release or Debug config (default Release).",
                "Stop only kills InstantFileSearch.exe (and a dotnet run) belonging to this repo — not other apps.",
                "Rebuild / Publish / tests stream into this pane. Follow logs tails ops.log.",
                "Theme… is LaunchControl.Standard (same Theme stack as other product LCs)."
            ],
            ProcessFallback = new ProcessFallbackSpec
            {
                WorkingDirectory = () => root,
                DetachedGui = true,
                PidFile = pidFile,
                FindRunningPids = () => IfsLaunchOps.FindProductPids(root),
                LaunchNotes = () =>
                    [("Launching Instant File Search for this repo (built exe if present, else dotnet run).", "INFO")],
                StartInfo = () => IfsLaunchOps.ResolveStart(root, IfsLaunchOps.LastConfiguration, refuseIfRunning: true)
            },
            ExtraActions =
            [
                new("Rebuild Release", w => IfsLaunchOps.Rebuild(w, root, "Release"), "Build"),
                new("Rebuild Debug", w => IfsLaunchOps.Rebuild(w, root, "Debug"), "Build"),
                new("Run Release", w => IfsLaunchOps.RunConfiguration(w, root, "Release"), "Run"),
                new("Run Debug", w => IfsLaunchOps.RunConfiguration(w, root, "Debug"), "Run"),
                new("Open CLI", w => IfsLaunchOps.OpenCli(w, root), "Run"),
                new("Publish", w => IfsLaunchOps.Publish(w, root), "Ship"),
                new("Run tests", w => IfsLaunchOps.RunTests(w, root), "Ship"),
                new("Open solution", w => IfsLaunchOps.OpenSolution(w, root), "Folders"),
                new("Open project folder", w => IfsLaunchOps.OpenFolder(w, root, "project folder"), "Folders"),
                new("Open dist folder", w => IfsLaunchOps.OpenDist(w, root), "Folders"),
                new("Open logs folder", w => IfsLaunchOps.OpenFolder(w, IfsLaunchOps.LogDir, "logs folder"), "Folders"),
            ]
        });
    }
}
