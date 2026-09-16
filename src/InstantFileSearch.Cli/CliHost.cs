using InstantFileSearch;

namespace InstantFileSearch.Cli;

public static class CliHost
{
    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        string? cachePath = null,
        string? exclusionsPath = null,
        IByteProtector? protector = null)
    {
        cachePath ??= ScanCache.DefaultFilePath;
        exclusionsPath ??= ExclusionStore.DefaultFilePath;
        protector ??= ByteProtector.CreateDefault();

        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteHelp(output);
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "scan" => Scan(args.Skip(1).ToArray(), output, error, cachePath, exclusionsPath, protector),
                "search" => Search(args.Skip(1).ToArray(), output, error, cachePath, protector),
                "status" => Status(output, error, cachePath, exclusionsPath, protector),
                "exclude" => Exclude(args.Skip(1).ToArray(), output, error, exclusionsPath, protector),
                _ => Unknown(args[0], error),
            };
        }
        catch (Exception ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Scan(
        string[] args,
        TextWriter output,
        TextWriter error,
        string cachePath,
        string exclusionsPath,
        IByteProtector protector)
    {
        if (args.Length == 0 || !LocalPathGuard.TryResolveExistingDirectory(args[0], out var path))
        {
            error.WriteLine("Usage: InstantFileSearch.Cli scan <folder>");
            return 1;
        }

        var exclusions = ExclusionStore.Load(exclusionsPath, protector);
        ScanCache.TryLoadAll(cachePath, out var existing, protector);
        var result = new FileScanner().Scan(path, excludeDirectories: exclusions.Items);
        var all = ScanCache.Upsert(existing, result);
        ScanCache.SaveAll(all, cachePath, protector);
        try
        {
            ScanLog.Append(result);
        }
        catch
        {
            // Keep the CLI usable if the log file cannot be written.
        }

        output.WriteLine(result.Root.Name);
        output.WriteLine($"{ByteFormatter.ToString(result.Root.Size)}  {result.Root.FileCount:N0} files  {result.Root.FolderCount:N0} folders  {ScanLocation.FormatDuration(result.Duration)}");
        output.WriteLine("Last scan " + result.CompletedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        output.WriteLine($"{all.Count} location(s) saved");

        return 0;
    }

    private static int Search(
        string[] args,
        TextWriter output,
        TextWriter error,
        string cachePath,
        IByteProtector protector)
    {
        var query = string.Join(" ", args).Trim();
        if (query.Length == 0)
        {
            error.WriteLine("Usage: InstantFileSearch.Cli search <query>");
            return 1;
        }

        if (!ScanCache.TryLoadAll(cachePath, out var scans, protector) || scans.Count == 0)
        {
            error.WriteLine("No saved scan. Run: InstantFileSearch.Cli scan <folder>");
            return 2;
        }

        var matches = FileNameSearch.FilterHits(
            FolderFilesNode.FoldersForSearch(scans.Select(scan => scan.Root), selected: null, selectedFolderScope: false),
            scans.SelectMany(scan => scan.AllFiles),
            new SearchQuery { Text = query }).ToList();
        foreach (var hit in matches)
        {
            var kind = hit.IsFolder ? "Folder" : "File";
            output.WriteLine($"{kind}\t{ByteFormatter.ToString(hit.Size)}\t{hit.FullPath}");
        }

        output.WriteLine($"{matches.Count} match(es)");
        return 0;
    }

    private static int Status(
        TextWriter output,
        TextWriter error,
        string cachePath,
        string exclusionsPath,
        IByteProtector protector)
    {
        if (!ScanCache.TryLoadAll(cachePath, out var scans, protector) || scans.Count == 0)
        {
            error.WriteLine("No saved scan.");
            return 2;
        }

        var exclusions = ExclusionStore.Load(exclusionsPath, protector);
        output.WriteLine($"{scans.Count} location(s)");
        foreach (var scan in scans)
        {
            output.WriteLine(scan.Root.Name);
            output.WriteLine($"  {ByteFormatter.ToString(scan.Root.Size)}  {scan.Root.FileCount:N0} files  {ScanLocation.FormatDuration(scan.Duration)}  {scan.CompletedUtc.ToLocalTime():yyyy-MM-dd HH:mm}");
        }
        output.WriteLine($"Excluded folders: {exclusions.Count}");
        foreach (var path in exclusions.Items)
        {
            output.WriteLine("  " + path);
        }

        return 0;
    }

    private static int Exclude(
        string[] args,
        TextWriter output,
        TextWriter error,
        string exclusionsPath,
        IByteProtector protector)
    {
        if (args.Length == 0)
        {
            error.WriteLine("Usage: InstantFileSearch.Cli exclude add|list|remove <folder>");
            return 1;
        }

        var set = ExclusionStore.Load(exclusionsPath, protector);
        switch (args[0].ToLowerInvariant())
        {
            case "list":
                foreach (var path in set.Items)
                {
                    output.WriteLine(path);
                }

                output.WriteLine($"{set.Count} excluded");
                return 0;
            case "add" when args.Length >= 2:
            {
                var addRaw = string.Join(" ", args.Skip(1));
                if (!LocalPathGuard.TryGetFullPath(addRaw, out var addPath))
                {
                    error.WriteLine("Invalid folder path.");
                    return 1;
                }

                if (!set.Add(addPath))
                {
                    output.WriteLine("Already excluded: " + addPath);
                    return 0;
                }

                ExclusionStore.Save(set, exclusionsPath, protector);
                output.WriteLine("Excluded " + addPath);
                return 0;
            }
            case "remove" when args.Length >= 2:
            {
                var removeRaw = string.Join(" ", args.Skip(1));
                if (!LocalPathGuard.TryGetFullPath(removeRaw, out var removePath))
                {
                    error.WriteLine("Invalid folder path.");
                    return 1;
                }

                if (!set.Remove(removePath))
                {
                    error.WriteLine("Not in the exclusion list: " + removePath);
                    return 1;
                }

                ExclusionStore.Save(set, exclusionsPath, protector);
                output.WriteLine("Removed " + removePath);
                return 0;
            }
            default:
                error.WriteLine("Usage: InstantFileSearch.Cli exclude add|list|remove <folder>");
                return 1;
        }
    }

    private static int Unknown(string command, TextWriter error)
    {
        error.WriteLine("Unknown command: " + command);
        WriteHelp(error);
        return 1;
    }

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "help" or "/?";

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("Instant File Search CLI");
        output.WriteLine("  scan <folder>              Scan and save an encrypted cache");
        output.WriteLine("  search <query>             Exact name (files and folders), or * ? wildcards");
        output.WriteLine("  status                     Last scan time, size, exclusions");
        output.WriteLine("  exclude add|list|remove    Skip folders on future scans");
        output.WriteLine("Shares the GUI cache under %LocalAppData%\\InstantFileSearch");
    }
}
