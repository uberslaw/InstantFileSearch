using System.Text.Json;

namespace InstantFileSearch.Tests;

public class LaunchControlFilesTests
{
    [Fact]
    public void Sidecar_is_generic_json_without_services_or_venv()
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "launch-control.json"));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("instantfilesearch", root.GetProperty("productId").GetString());
        Assert.Equal("Instant File Search", root.GetProperty("productName").GetString());
        Assert.False(root.GetProperty("showVenvUi").GetBoolean());
        Assert.False(root.GetProperty("showBrowser").GetBoolean());
        Assert.Equal(0, root.GetProperty("serviceNames").GetArrayLength());
        var installExe = root.GetProperty("installExe").GetString();
        Assert.Contains("InstantFileSearch.exe", installExe, StringComparison.Ordinal);
        Assert.StartsWith("../src/InstantFileSearch/bin/Release/", installExe, StringComparison.Ordinal);
    }

    [Fact]
    public void Cmd_is_scanable_and_does_not_detach_with_start()
    {
        var cmd = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "InstantFileSearch-LaunchControl.cmd"));
        Assert.Contains("InstantFileSearch.LaunchControl.exe", cmd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotnet build", cmd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"%EXE%\" %*", cmd, StringComparison.Ordinal);
        Assert.DoesNotContain("start \"\" \"%EXE%\"", cmd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MLC_ROOT", cmd, StringComparison.Ordinal);
    }

    [Fact]
    public void Scripts_lc_compat_is_absent_so_unexpanded_themePath_cannot_shadow_bin()
    {
        Assert.False(File.Exists(Path.Combine(RepoRoot(), "scripts", "lc-compat.json")));
        var template = File.ReadAllText(Path.Combine(RepoRoot(), "launch-control", "lc-compat.json.in"));
        Assert.Contains("__THEME_PATH__", template, StringComparison.Ordinal);
        Assert.DoesNotContain("%LOCALAPPDATA%", template, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(template.Replace("__THEME_PATH__", "C:/theme.json", StringComparison.Ordinal));
        Assert.Equal("instantfilesearch", doc.RootElement.GetProperty("productId").GetString());
        Assert.Equal("InstantFileSearch", doc.RootElement.GetProperty("appDataFolder").GetString());
    }

    [Fact]
    public void Lc_csproj_resolves_standard_only_when_the_clone_exists()
    {
        var csproj = File.ReadAllText(Path.Combine(
            RepoRoot(), "launch-control", "InstantFileSearch.LaunchControl.csproj"));
        Assert.Contains("<EnableWindowsTargeting>true</EnableWindowsTargeting>", csproj, StringComparison.Ordinal);
        Assert.Contains("$(MLC_ROOT)", csproj, StringComparison.Ordinal);
        Assert.Contains("USERPROFILE)\\Projects\\master-launch-control", csproj, StringComparison.Ordinal);
        Assert.Contains(
            "Exists('C:\\Users\\christopher.owen\\Projects\\master-launch-control\\src\\LaunchControl.Standard\\LaunchControl.Standard.csproj')",
            csproj,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<LcStandard Condition=\"'$(LcStandard)' == ''\">C:\\Users\\christopher.owen\\Projects\\master-launch-control",
            csproj,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OpenCli_quotes_windows_argv_not_c_escapes()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "launch-control", "IfsLaunchOps.cs"));
        Assert.Contains("value.Replace(\"\\\"\", \"\\\"\\\"\")", src, StringComparison.Ordinal);
        Assert.DoesNotContain("value.Replace(\"\\\"\", \"\\\\\\\"\")", src, StringComparison.Ordinal);
        Assert.Contains("LooksLikeRoot(root)", src, StringComparison.Ordinal);
        Assert.Contains("WorkingDirectory = root", src, StringComparison.Ordinal);
    }

    [Fact]
    public void FindRoot_walks_from_bin_and_cwd()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "launch-control", "IfsLaunchOps.cs"));
        Assert.Contains("AppContext.BaseDirectory", src, StringComparison.Ordinal);
        Assert.Contains("Directory.GetCurrentDirectory()", src, StringComparison.Ordinal);
        Assert.Contains("PidFileMatchesProcess", src, StringComparison.Ordinal);
        Assert.Contains("dotnet", src, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wpf_csproj_version_is_readable()
    {
        var csproj = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "InstantFileSearch", "InstantFileSearch.csproj"));
        Assert.Contains("<Version>1.0.0</Version>", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_code_behind_has_explicit_system_io_for_wpftmp()
    {
        var src = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "InstantFileSearch", "MainWindow.xaml.cs"));
        Assert.True(
            src.Contains("using System.IO;", StringComparison.Ordinal)
            || src.Contains("System.IO.File", StringComparison.Ordinal),
            "WPF markup compile (wpftmp) often lacks ImplicitUsings; File/Path in code-behind need using System.IO or System.IO.File.");
    }

    [Fact]
    public void RepoRoot_from_test_host_is_within_eight_parents()
    {
        var root = RepoRoot();
        Assert.True(File.Exists(Path.Combine(root, "InstantFileSearch.slnx")));
        Assert.True(File.Exists(Path.Combine(root, "src", "InstantFileSearch", "InstantFileSearch.csproj")));
        Assert.True(File.Exists(Path.Combine(root, "scripts", "InstantFileSearch-LaunchControl.cmd")));
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "InstantFileSearch.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new DirectoryNotFoundException("Could not find InstantFileSearch.slnx above " + AppContext.BaseDirectory);
    }
}
