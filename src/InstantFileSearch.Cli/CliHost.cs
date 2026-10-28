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
        if (args.Length == 0)
        {
            error.WriteLine("Usage: InstantFileSearch.Cli scan <folder>");
            return 1;
        }

        if (!LocalPathGuard.TryGetFullPath(args[0], out var path))
        {
            error.WriteLine(UncPath.LooksLikeUnc(args[0])
                ? "That is not a usable UNC folder. Use \\\\server\\share or \\\\10.x.x.x\\share."
                : "The folder path is invalid.");
            return 1;
        }

        var exclusions = ExclusionStore.Load(exclusionsPath, protector);
        var existingLoad = ScanCache.LoadAll(cachePath, protector);
        if (existingLoad.Status == ScanCacheLoadStatus.Failed)
        {
            error.WriteLine(existingLoad.Error ?? ScanCache.FormatLoadFailed(cachePath));
        }

        var existing = existingLoad.Scans;
        ScanResult result;
        try
        {
            result = new FileScanner().Scan(path, excludeDirectories: exclusions.Items);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
        var all = ScanCache.AppendHistory(existing, result);
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
        output.WriteLine($"{ScanCache.NewestPerPath(all).Count} location(s), {all.Count} snapshot(s) saved");

        return 0;
    }

    private static int Search(
        string[] args,
        TextWriter output,
        TextWriter error,
        string cachePath,
        IByteProtector protector)
    {
        if (!TryParseSearchArgs(args, out var query, out var ext, out var regex, out var usageError))
        {
            error.WriteLine(usageError);
            return 1;
        }

        var search = new SearchQuery
        {
            Text = query,
            ExtensionsText = ext,
            UseRegex = regex,
        };
        if (!search.HasCriteria)
        {
            error.WriteLine("Usage: InstantFileSearch.Cli search [--ext pdf,docx] [--regex] <query>");
            return 1;
        }

        var loaded = ScanCache.LoadAll(cachePath, protector);
        if (loaded.Status == ScanCacheLoadStatus.Failed)
        {
            error.WriteLine(loaded.Error ?? ScanCache.FormatLoadFailed(cachePath));
            return 1;
        }

        var scans = loaded.Scans;
        if (scans.Count == 0)
        {
            error.WriteLine("No saved scan. Run: InstantFileSearch.Cli scan <folder>");
            return 2;
        }

        if (search.HasInvalidRegex)
        {
            error.WriteLine(ResultsUi.InvalidRegexStatus);
            output.WriteLine("0 match(es)");
            return 0;
        }

        var matches = ScanHistorySearch.Search(scans, search).ToList();
        foreach (var hit in matches)
        {
            var kind = hit.IsFolder ? "Folder" : "File";
            var lastPresent = ResultsUi.FormatLastPresent(hit.LastPresentUtc);
            output.WriteLine($"{kind}\t{ByteFormatter.ToString(hit.Size)}\t{hit.FullPath}\t{lastPresent}");
        }

        output.WriteLine($"{matches.Count} match(es)");
        return 0;
    }

    private static bool TryParseSearchArgs(
        string[] args,
        out string query,
        out string ext,
        out bool regex,
        out string usageError)
    {
        query = "";
        ext = "";
        regex = false;
        usageError = "";
        var parts = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--ext" or "-e")
            {
                if (i + 1 >= args.Length)
                {
                    usageError = "Usage: InstantFileSearch.Cli search [--ext pdf,docx] [--regex] <query>";
                    return false;
                }

                ext = args[++i];
                continue;
            }

            if (arg.StartsWith("--ext=", StringComparison.Ordinal))
            {
                ext = arg["--ext=".Length..];
                continue;
            }

            if (arg is "--regex" or "-r")
            {
                regex = true;
                continue;
            }

            parts.Add(arg);
        }

        query = string.Join(" ", parts).Trim();
        return true;
    }

    private static int Status(
        TextWriter output,
        TextWriter error,
        string cachePath,
        string exclusionsPath,
        IByteProtector protector)
    {
        var loaded = ScanCache.LoadAll(cachePath, protector);
        if (loaded.Status == ScanCacheLoadStatus.Failed)
        {
            error.WriteLine(loaded.Error ?? ScanCache.FormatLoadFailed(cachePath));
            return 1;
        }

        var scans = loaded.Scans;
        if (scans.Count == 0)
        {
            error.WriteLine("No saved scan.");
            return 2;
        }

        var exclusions = ExclusionStore.Load(exclusionsPath, protector);
        output.WriteLine($"{scans.Count} snapshot(s)");
        foreach (var scan in ScanCache.ListEntries(scans))
        {
            output.WriteLine(scan.TreeLabel);
            output.WriteLine($"  {scan.SizeText}  {scan.FileCount:N0} files  {scan.DurationText}  {scan.CompletedLocalText}  {scan.LocationText}");
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
        output.WriteLine("  scan <folder>              Scan a local folder or UNC share (\\\\server\\share)");
        output.WriteLine("  search [--ext pdf,docx] [--regex] <query>");
        output.WriteLine("                             Exact name, * ?, + AND, path:. --ext filters last '.' segment");
        output.WriteLine("                             (comma/semicolon/space; leading dots optional). --regex: whole");
        output.WriteLine("                             query is one .NET regex (path: still a prefix filter; + is not AND).");
        output.WriteLine("                             Searches all saved snapshots; Last present is when the path was last seen.");
        output.WriteLine("  status                     Snapshots, last present times, exclusions");
        output.WriteLine("  exclude add|list|remove    Skip folders on future scans");
        output.WriteLine("Shares the GUI cache under %LocalAppData%\\InstantFileSearch");
    }
}
