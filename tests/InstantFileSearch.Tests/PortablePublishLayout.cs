namespace InstantFileSearch.Tests;

/// <summary>
/// Ship contract for <c>scripts/PortablePublish.ps1</c>. Tests lock flags and copy
/// rules; the PowerShell helpers must match these values (no SDK on the target PC,
/// no runtime-pack DLL dump, no PDBs).
/// </summary>
public static class PortablePublishLayout
{
    public const string RuntimeIdentifier = "win-x64";
    public const string Configuration = "Release";
    public const bool SelfContained = true;
    public const bool PublishSingleFile = true;
    public const bool EnableCompressionInSingleFile = true;
    public const bool IncludeNativeLibrariesForSelfExtract = true;
    public const bool PublishTrimmed = false;
    public const bool PublishReadyToRun = false;
    public const string DebugType = "None";
    public const bool DebugSymbols = false;
    public const string AppFolderName = "InstantFileSearch";
    public const string ReadmeFileName = "README.txt";
    public const string WpfProjectRelativePath = "src/InstantFileSearch/InstantFileSearch.csproj";
    public const string CliProjectRelativePath = "src/InstantFileSearch.Cli/InstantFileSearch.Cli.csproj";

    public static IReadOnlyList<string> ExpectedExeNames { get; } =
    [
        "InstantFileSearch.exe",
        "InstantFileSearch.Cli.exe"
    ];

    /// <summary>
    /// WPF native binaries that sit beside the exe when
    /// IncludeNativeLibrariesForSelfExtract is missing. Copy them if they still
    /// leak; do not copy the rest of the runtime pack.
    /// </summary>
    public static IReadOnlyList<string> WpfNativeLeftoverFileNames { get; } =
    [
        "D3DCompiler_47_cor3.dll",
        "PenImc_cor3.dll",
        "PresentationNative_cor3.dll",
        "vcruntime140_cor3.dll",
        "wpfgfx_cor3.dll"
    ];

    public static IReadOnlyList<string> RuntimeBaggageFileNames { get; } =
    [
        "coreclr.dll",
        "clrjit.dll",
        "clretwrc.dll",
        "hostfxr.dll",
        "hostpolicy.dll",
        "mscordaccore.dll",
        "createdump.exe"
    ];

    public static IReadOnlyList<(string Name, string Value)> PublishMsBuildProperties { get; } =
    [
        ("PublishSingleFile", "true"),
        ("EnableCompressionInSingleFile", "true"),
        ("IncludeNativeLibrariesForSelfExtract", "true"),
        ("DebugType", "None"),
        ("DebugSymbols", "false")
    ];

    public static IReadOnlyList<string> DotnetPublishArguments(string projectPath, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        return
        [
            "publish",
            projectPath,
            "-c", Configuration,
            "-r", RuntimeIdentifier,
            "--self-contained", SelfContained ? "true" : "false",
            "-p:PublishSingleFile=true",
            "-p:EnableCompressionInSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:DebugType=None",
            "-p:DebugSymbols=false",
            "-o", outputDirectory
        ];
    }

    public static bool ShouldCopyPublishedFile(string? relativePath)
    {
        var name = FileName(relativePath);
        if (name.Length == 0)
            return false;
        if (LooksLikeSourceOrDocs(name) || IsRuntimeBaggage(name) || IsBundledSidecar(name))
            return false;
        if (IsUnderRuntimesFolder(relativePath))
            return false;
        if (Matches(ExpectedExeNames, name))
            return true;
        return Matches(WpfNativeLeftoverFileNames, name);
    }

    public static bool IsRuntimeBaggage(string? relativePath)
    {
        var name = FileName(relativePath);
        if (name.Length == 0)
            return false;
        if (Matches(RuntimeBaggageFileNames, name))
            return true;
        if (name.StartsWith("mscordaccore", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.StartsWith("Microsoft.DiaSymReader.Native", StringComparison.OrdinalIgnoreCase))
            return true;
        return IsUnderRuntimesFolder(relativePath);
    }

    public static bool ShouldWipeStalePortableFile(string? relativePath)
    {
        var name = FileName(relativePath);
        if (name.Length == 0)
            return false;
        if (ShouldCopyPublishedFile(name) || IsRuntimeBaggage(name))
            return true;
        if (name.Equals(ReadmeFileName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (IsBundledSidecar(name) || LooksLikeSourceOrDocs(name))
            return true;

        var ext = Path.GetExtension(name);
        return ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".pdb", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveAppFolder(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("Destination folder is required; do not default to bin/ or dist/.", nameof(destination));

        var full = Path.GetFullPath(destination);
        var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);
        if (leaf.Equals(AppFolderName, StringComparison.OrdinalIgnoreCase))
            return trimmed;
        return Path.Combine(trimmed, AppFolderName);
    }

    public static bool IsUnderDirectory(string? path, string? root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            return false;

        string fullPath;
        string fullRoot;
        try
        {
            fullPath = Path.GetFullPath(path);
            fullRoot = Path.GetFullPath(root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (fullPath.Equals(fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), comparison))
            return true;
        return fullPath.StartsWith(prefix, comparison);
    }

    public static bool IsWellKnownRepoDump(string repoRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(path))
            return false;
        foreach (var name in new[] { "bin", "obj", "dist", "build" })
        {
            var dump = Path.Combine(repoRoot, name);
            if (IsUnderDirectory(path, dump))
                return true;
        }

        return false;
    }

    public static string ReadmeText(IReadOnlyList<string>? extraNativeFiles = null)
    {
        var extras = extraNativeFiles is { Count: > 0 }
            ? string.Join(", ", extraNativeFiles)
            : "";
        var nativeNote = extras.Length > 0
            ? "WPF native libraries could not be fully bundled, so these files sit next to the GUI exe and are required: "
              + extras + "."
            : "Native WPF libraries are bundled inside InstantFileSearch.exe (IncludeNativeLibrariesForSelfExtract) and extract under %TEMP%\\.net on first run.";

        return
            """
            Instant File Search (portable)

            This folder is a self-contained Windows x64 copy of Instant File Search
            and InstantFileSearch.Cli. It is not a .NET SDK tree: no source, no obj/bin,
            no runtime-pack DLL dump.

            You do not need the .NET SDK or the .NET 8 Desktop Runtime installed on
            the PC that runs these executables. You do need Windows 10/11 x64.

            Run:
              InstantFileSearch.exe        GUI
              InstantFileSearch.Cli.exe    scan / search / status / exclude

            Settings and the last scan live in %LOCALAPPDATA%\InstantFileSearch
            (not in this folder). Copying this folder does not copy your scans.

            """
            + nativeNote
            + """


            Rebuild this pack from a machine with the .NET 8 SDK:
              scripts\PortablePublish.cmd
            """;
    }

    public static string PublishPropertySwitch(string name, string value) => $"-p:{name}={value}";

    private static bool IsBundledSidecar(string name) =>
        name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".exe.config", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSourceOrDocs(string name) =>
        name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderRuntimesFolder(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        return normalized.Equals("runtimes", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(IReadOnlyList<string> names, string name)
    {
        foreach (var candidate in names)
        {
            if (candidate.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string FileName(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return "";
        return Path.GetFileName(relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
