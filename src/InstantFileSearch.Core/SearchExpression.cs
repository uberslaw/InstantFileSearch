namespace InstantFileSearch;

/// <summary>
/// Parsed search box (and CLI <c>search</c>) text. Not a regex / Everything engine.
/// </summary>
public sealed class SearchExpression
{
    public static SearchExpression Empty { get; } = new([], [], usesAnd: false);

    public IReadOnlyList<SearchTerm> Terms { get; }

    public IReadOnlyList<SearchTerm> PathFilters { get; }

    /// <summary>
    /// True when the query used <c>+</c> as AND. Bare terms then become contains
    /// (<c>*term*</c>). A single term with no <c>+</c> stays exact-name.
    /// </summary>
    public bool UsesAnd { get; }

    public bool HasText => Terms.Count > 0 || PathFilters.Count > 0;

    private SearchExpression(
        IReadOnlyList<SearchTerm> terms,
        IReadOnlyList<SearchTerm> pathFilters,
        bool usesAnd)
    {
        Terms = terms;
        PathFilters = pathFilters;
        UsesAnd = usesAnd;
    }

    public static SearchExpression Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        var terms = new List<string>();
        var paths = new List<string>();
        var usesAnd = false;
        var i = 0;
        var source = text;
        while (i < source.Length)
        {
            SkipWhiteSpace(source, ref i);
            if (i >= source.Length)
            {
                break;
            }

            if (source[i] == '+')
            {
                usesAnd = true;
                i++;
                continue;
            }

            if (TryConsumePathPrefix(source, ref i))
            {
                SkipWhiteSpace(source, ref i);
                var value = ReadToken(source, ref i);
                if (value.Length > 0)
                {
                    paths.Add(value);
                }

                continue;
            }

            var token = ReadToken(source, ref i);
            if (token.Length > 0)
            {
                terms.Add(token);
            }
        }

        // Spaces are not AND (that would break names with spaces). Only +.
        if (!usesAnd && terms.Count > 1)
        {
            terms = [string.Join(" ", terms)];
        }

        var compiledTerms = new SearchTerm[terms.Count];
        for (var t = 0; t < terms.Count; t++)
        {
            compiledTerms[t] = CompileTerm(terms[t], containsByDefault: usesAnd);
        }

        var compiledPaths = new SearchTerm[paths.Count];
        for (var p = 0; p < paths.Count; p++)
        {
            compiledPaths[p] = CompilePathFilter(paths[p]);
        }

        if (compiledTerms.Length == 0 && compiledPaths.Length == 0)
        {
            return Empty;
        }

        return new SearchExpression(compiledTerms, compiledPaths, usesAnd);
    }

    public static string NormalizeSlashes(string value) =>
        value.Replace('/', '\\');

    private static SearchTerm CompileTerm(string raw, bool containsByDefault)
    {
        var wildcard = FileNameSearch.HasWildcard(raw);
        if (!wildcard && containsByDefault)
        {
            return new SearchTerm("*" + raw + "*", Wildcard: true);
        }

        return new SearchTerm(raw, wildcard);
    }

    private static SearchTerm CompilePathFilter(string raw)
    {
        // No * / ? → contains on the full path (paths are long). Wildcards stay globs.
        return new SearchTerm(raw, FileNameSearch.HasWildcard(raw));
    }

    private static bool TryConsumePathPrefix(string source, ref int i)
    {
        const string prefix = "path:";
        if (i + prefix.Length > source.Length)
        {
            return false;
        }

        if (!source.AsSpan(i, prefix.Length).Equals(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        i += prefix.Length;
        return true;
    }

    private static string ReadToken(string source, ref int i)
    {
        if (i >= source.Length)
        {
            return "";
        }

        if (source[i] == '"')
        {
            i++;
            var start = i;
            while (i < source.Length && source[i] != '"')
            {
                i++;
            }

            var quoted = source[start..i];
            if (i < source.Length && source[i] == '"')
            {
                i++;
            }

            return quoted;
        }

        var from = i;
        while (i < source.Length && source[i] != '+' && !char.IsWhiteSpace(source[i]))
        {
            i++;
        }

        return source[from..i];
    }

    private static void SkipWhiteSpace(string source, ref int i)
    {
        while (i < source.Length && char.IsWhiteSpace(source[i]))
        {
            i++;
        }
    }
}

public readonly record struct SearchTerm(string Pattern, bool Wildcard);
