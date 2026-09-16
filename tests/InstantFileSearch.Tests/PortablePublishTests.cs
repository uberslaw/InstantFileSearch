namespace InstantFileSearch.Tests;

public class PortablePublishTests
{
    [Fact]
    public void Publish_flags_are_self_contained_single_file_win_x64_without_trim()
    {
        Assert.Equal("win-x64", PortablePublishLayout.RuntimeIdentifier);
        Assert.Equal("Release", PortablePublishLayout.Configuration);
        Assert.True(PortablePublishLayout.SelfContained);
        Assert.True(PortablePublishLayout.PublishSingleFile);
        Assert.True(PortablePublishLayout.EnableCompressionInSingleFile);
        Assert.True(PortablePublishLayout.IncludeNativeLibrariesForSelfExtract);
        Assert.False(PortablePublishLayout.PublishTrimmed);
        Assert.False(PortablePublishLayout.PublishReadyToRun);
        Assert.Equal("None", PortablePublishLayout.DebugType);
        Assert.False(PortablePublishLayout.DebugSymbols);

        var args = PortablePublishLayout.DotnetPublishArguments("app.csproj", "out");
        Assert.Equal(
        [
            "publish", "app.csproj",
            "-c", "Release",
            "-r", "win-x64",
            "--self-contained", "true",
            "-p:PublishSingleFile=true",
            "-p:EnableCompressionInSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:DebugType=None",
            "-p:DebugSymbols=false",
            "-o", "out"
        ], args);
        Assert.DoesNotContain(args, a => a.Contains("PublishTrimmed", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(args, a => a.Contains("PublishReadyToRun", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Copy_rules_keep_exes_and_wpf_natives_not_pdb_or_runtime_pack()
    {
        Assert.True(PortablePublishLayout.ShouldCopyPublishedFile("InstantFileSearch.exe"));
        Assert.True(PortablePublishLayout.ShouldCopyPublishedFile("InstantFileSearch.Cli.exe"));
        Assert.True(PortablePublishLayout.ShouldCopyPublishedFile("wpfgfx_cor3.dll"));
        Assert.True(PortablePublishLayout.ShouldCopyPublishedFile(@"publish\PresentationNative_cor3.dll"));

        Assert.False(PortablePublishLayout.ShouldCopyPublishedFile("InstantFileSearch.pdb"));
        Assert.False(PortablePublishLayout.ShouldCopyPublishedFile("InstantFileSearch.xml"));
        Assert.False(PortablePublishLayout.ShouldCopyPublishedFile("InstantFileSearch.deps.json"));
        Assert.False(PortablePublishLayout.ShouldCopyPublishedFile("InstantFileSearch.runtimeconfig.json"));
        Assert.False(PortablePublishLayout.ShouldCopyPublishedFile("InstantFileSearch.dll"));
        Assert.False(PortablePublishLayout.ShouldCopyPublishedFile("coreclr.dll"));
        Assert.False(PortablePublishLayout.ShouldCopyPublishedFile("hostfxr.dll"));
        Assert.False(PortablePublishLayout.ShouldCopyPublishedFile("createdump.exe"));
        Assert.False(PortablePublishLayout.ShouldCopyPublishedFile(@"runtimes\win-x64\native\coreclr.dll"));
        Assert.False(PortablePublishLayout.ShouldCopyPublishedFile("MainWindow.xaml"));
        Assert.False(PortablePublishLayout.ShouldCopyPublishedFile("InstantFileSearch.Tests.dll"));
    }

    [Fact]
    public void Runtime_baggage_is_the_sea_of_host_dlls_not_wpf_cor3()
    {
        Assert.True(PortablePublishLayout.IsRuntimeBaggage("coreclr.dll"));
        Assert.True(PortablePublishLayout.IsRuntimeBaggage("hostpolicy.dll"));
        Assert.True(PortablePublishLayout.IsRuntimeBaggage(@"runtimes\win-x64\native\foo.dll"));
        Assert.False(PortablePublishLayout.IsRuntimeBaggage("wpfgfx_cor3.dll"));
        Assert.False(PortablePublishLayout.IsRuntimeBaggage("InstantFileSearch.exe"));
    }

    [Fact]
    public void App_folder_nests_unless_the_pick_is_already_named_InstantFileSearch()
    {
        var parent = Path.Combine(Path.GetTempPath(), "ifs-portable-tests", "Apps");
        var named = Path.Combine(parent, "InstantFileSearch");
        Assert.Equal(named, PortablePublishLayout.ResolveAppFolder(parent));
        Assert.Equal(named, PortablePublishLayout.ResolveAppFolder(named));
        Assert.Equal(named, PortablePublishLayout.ResolveAppFolder(named + Path.DirectorySeparatorChar));
        var ex = Assert.Throws<ArgumentException>(() => PortablePublishLayout.ResolveAppFolder("  "));
        Assert.Contains("bin/", ex.Message, StringComparison.Ordinal);
        Assert.Contains("dist/", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Well_known_repo_dumps_are_bin_obj_dist_build_not_an_explicit_apps_folder()
    {
        var repo = Path.Combine(Path.GetTempPath(), "ifs-portable-tests", "repo");
        Assert.True(PortablePublishLayout.IsWellKnownRepoDump(repo, Path.Combine(repo, "dist")));
        Assert.True(PortablePublishLayout.IsWellKnownRepoDump(repo, Path.Combine(repo, "bin", "Release")));
        Assert.True(PortablePublishLayout.IsWellKnownRepoDump(repo, Path.Combine(repo, "obj")));
        Assert.False(PortablePublishLayout.IsWellKnownRepoDump(repo, Path.Combine(repo, "portable-out")));
        Assert.True(PortablePublishLayout.IsUnderDirectory(Path.Combine(repo, "scripts"), repo));
        Assert.False(PortablePublishLayout.IsUnderDirectory(repo + "-other", repo));
    }

    [Fact]
    public void Stale_wipe_replaces_old_dlls_not_user_notes()
    {
        Assert.True(PortablePublishLayout.ShouldWipeStalePortableFile("InstantFileSearch.exe"));
        Assert.True(PortablePublishLayout.ShouldWipeStalePortableFile("coreclr.dll"));
        Assert.True(PortablePublishLayout.ShouldWipeStalePortableFile("InstantFileSearch.deps.json"));
        Assert.True(PortablePublishLayout.ShouldWipeStalePortableFile("README.txt"));
        Assert.True(PortablePublishLayout.ShouldWipeStalePortableFile("leftover.pdb"));
        Assert.False(PortablePublishLayout.ShouldWipeStalePortableFile("notes.txt"));
        Assert.False(PortablePublishLayout.ShouldWipeStalePortableFile("license.md"));
    }

    [Fact]
    public void Readme_is_honest_about_self_contained_and_no_sdk()
    {
        var text = PortablePublishLayout.ReadmeText();
        Assert.Contains("self-contained", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not need the .NET SDK", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Desktop Runtime", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InstantFileSearch.exe", text, StringComparison.Ordinal);
        Assert.Contains("InstantFileSearch.Cli.exe", text, StringComparison.Ordinal);
        Assert.Contains("Windows", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("install the runtime", text, StringComparison.OrdinalIgnoreCase);

        var withNatives = PortablePublishLayout.ReadmeText(["wpfgfx_cor3.dll"]);
        Assert.Contains("wpfgfx_cor3.dll", withNatives, StringComparison.Ordinal);
        Assert.Contains("could not be fully bundled", withNatives, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projects_are_win_x64_publish_candidates_not_multi_rid()
    {
        var root = RepoRoot();
        var wpf = File.ReadAllText(Path.Combine(root, PortablePublishLayout.WpfProjectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var cli = File.ReadAllText(Path.Combine(root, PortablePublishLayout.CliProjectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("<TargetFramework>net8.0-windows</TargetFramework>", wpf, StringComparison.Ordinal);
        Assert.Contains("<UseWPF>true</UseWPF>", wpf, StringComparison.Ordinal);
        Assert.Contains("<TargetFramework>net8.0</TargetFramework>", cli, StringComparison.Ordinal);
        Assert.DoesNotContain("<RuntimeIdentifiers>", wpf, StringComparison.Ordinal);
        Assert.DoesNotContain("<RuntimeIdentifiers>", cli, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishTrimmed", wpf, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishTrimmed", cli, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerShell_helpers_match_the_csharp_contract()
    {
        var helpers = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "PortablePublish.Helpers.ps1"));
        Assert.Contains("win-x64", helpers, StringComparison.Ordinal);
        Assert.Contains("--self-contained", helpers, StringComparison.Ordinal);
        Assert.Contains("InstantFileSearch.exe", helpers, StringComparison.Ordinal);
        Assert.Contains("InstantFileSearch.Cli.exe", helpers, StringComparison.Ordinal);
        Assert.Contains("wpfgfx_cor3.dll", helpers, StringComparison.Ordinal);
        Assert.Contains("coreclr.dll", helpers, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishTrimmed", helpers, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishReadyToRun", helpers, StringComparison.Ordinal);
        Assert.DoesNotContain("IncludeAllContentForSelfExtract", helpers, StringComparison.Ordinal);
        foreach (var (name, value) in PortablePublishLayout.PublishMsBuildProperties)
        {
            Assert.Contains(
                PortablePublishLayout.PublishPropertySwitch(name, value),
                helpers,
                StringComparison.OrdinalIgnoreCase);
        }

        foreach (var exe in PortablePublishLayout.ExpectedExeNames)
            Assert.Contains(exe, helpers, StringComparison.Ordinal);
        foreach (var native in PortablePublishLayout.WpfNativeLeftoverFileNames)
            Assert.Contains(native, helpers, StringComparison.Ordinal);
    }

    [Fact]
    public void Scripts_prompt_for_a_folder_and_do_not_default_to_dist()
    {
        var root = RepoRoot();
        var ps1 = File.ReadAllText(Path.Combine(root, "scripts", "PortablePublish.ps1"));
        var cmd = File.ReadAllText(Path.Combine(root, "scripts", "PortablePublish.cmd"));
        var helpers = File.ReadAllText(Path.Combine(root, "scripts", "PortablePublish.Helpers.ps1"));

        Assert.Contains("PortablePublish.Helpers.ps1", ps1, StringComparison.Ordinal);
        Assert.Contains("src\\InstantFileSearch\\InstantFileSearch.csproj", ps1, StringComparison.Ordinal);
        Assert.Contains("src\\InstantFileSearch.Cli\\InstantFileSearch.Cli.csproj", ps1, StringComparison.Ordinal);
        Assert.Contains("BrowseForFolder", ps1, StringComparison.Ordinal);
        Assert.Contains("FolderBrowserDialog", ps1, StringComparison.Ordinal);
        Assert.Contains("Read-Host", ps1, StringComparison.Ordinal);
        Assert.Contains("NonInteractive", ps1, StringComparison.Ordinal);
        Assert.DoesNotContain("Join-Path $root \"dist\"", ps1, StringComparison.Ordinal);
        Assert.DoesNotContain("Join-Path $root 'dist'", ps1, StringComparison.Ordinal);
        Assert.Contains("do not default", helpers, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GetTempPath", ps1, StringComparison.Ordinal);

        Assert.Contains("PortablePublish.ps1", cmd, StringComparison.Ordinal);
        Assert.Contains("-STA", cmd, StringComparison.Ordinal);
        Assert.Contains("ExecutionPolicy Bypass", cmd, StringComparison.Ordinal);
        Assert.DoesNotContain("start \"\"", cmd, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Existing_publish_ps1_still_strips_pdbs_and_keeps_wpf_single_file()
    {
        var publish = File.ReadAllText(Path.Combine(RepoRoot(), "publish.ps1"));
        Assert.Contains("PublishSingleFile=true", publish, StringComparison.Ordinal);
        Assert.Contains("IncludeNativeLibrariesForSelfExtract=true", publish, StringComparison.Ordinal);
        Assert.Contains("DebugSymbols=false", publish, StringComparison.Ordinal);
        Assert.Contains("*.pdb", publish, StringComparison.Ordinal);
        Assert.Contains("InstantFileSearch.Cli", publish, StringComparison.Ordinal);
    }

    [Fact]
    public void Launch_control_does_not_gain_an_interactive_portable_pack_button()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "launch-control", "App.xaml.cs"));
        Assert.Contains("new(\"Publish\", w => IfsLaunchOps.Publish(w, root), \"Ship\")", app, StringComparison.Ordinal);
        Assert.DoesNotContain("PortablePublish", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Portable pack", app, StringComparison.OrdinalIgnoreCase);
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
