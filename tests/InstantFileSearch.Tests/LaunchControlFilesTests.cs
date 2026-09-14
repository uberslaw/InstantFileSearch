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
        Assert.Contains("InstantFileSearch.exe", root.GetProperty("installExe").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Cmd_is_scanable_and_does_not_detach_with_start()
    {
        var cmd = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "InstantFileSearch-LaunchControl.cmd"));
        Assert.Contains("InstantFileSearch.LaunchControl.exe", cmd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotnet build", cmd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"%EXE%\" %*", cmd, StringComparison.Ordinal);
        Assert.DoesNotContain("start \"\" \"%EXE%\"", cmd, StringComparison.OrdinalIgnoreCase);
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
