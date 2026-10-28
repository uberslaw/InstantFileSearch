using System.Text.RegularExpressions;

namespace InstantFileSearch;

/// <summary>
/// Parsed search box (and CLI <c>search</c>) text. Default language is exact /
/// wildcard / <c>+</c> AND / <c>path:</c> — not regex. Regex mode (opt-in) treats
/// the whole remaining box as one .NET pattern; <c>path:</c> is still extracted.
/// </summary>
public sealed class SearchExpression
{
    public static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(250);

    public static SearchExpression Empty { get; } = new([], [], usesAnd: false);

    public IReadOnlyList<SearchTerm> Terms { get; }

    public IReadOnlyList<SearchTerm> PathFilters { get; }

    /// <summary>
    /// True when the query used <c>+</c> as AND. Bare terms then become contains
    /// (<c>*term*</c>). A single term with no <c>+</c> stays exact-name.
    /// Disabled in regex mode: <c>+</c> is a regex quantifier, not AND.
    /// </summary>
    public bool UsesAnd { get; }

    public bool IsRegex { get; }

    public bool IsRegexInvalid { get; }

    public Regex? CompiledRegex { get; }

    public bool HasText =>
        Terms.Count > 0
        || PathFilters.Count > 0
        || (IsRegex && (CompiledRegex is not null || IsRegexInvalid));

    private SearchExpression(
        IReadOnlyList<SearchTerm> terms,
        IReadOnlyList<SearchTerm> pathFilters,
        bool usesAnd,
        bool isRegex = false,
        bool isRegexInvalid = false,
        Regex? compiledRegex = null)
    {
        Terms = terms;
        PathFilters = pathFilters;
        UsesAnd = usesAnd;
        IsRegex = isRegex;
        IsRegexInvalid = isRegexInvalid;
        CompiledRegex = compiledRegex;
    }

    public static SearchExpression Parse(string? text) => Parse(text, regex: false);

    public static SearchExpression Parse(string? text, bool regex)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        if (regex)
        {
            return ParseRegex(text);
        }

        return ParseDefault(text);
    }

    private static SearchExpression ParseDefault(string text)
    {
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

    /// <summary>
    /// Regex mode: <c>path:</c> tokens stay prefix filters (quoted values allowed).
    /// Everything else, including <c>+</c> and quotes, is one .NET regex joined
    /// with single spaces. Invalid patterns set <see cref="IsRegexInvalid"/>.
    /// </summary>
    private static SearchExpression ParseRegex(string text)
    {
        var paths = new List<string>();
        var chunks = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            SkipWhiteSpace(text, ref i);
            if (i >= text.Length)
            {
                break;
            }

            if (TryConsumePathPrefix(text, ref i))
            {
                SkipWhiteSpace(text, ref i);
                var value = ReadToken(text, ref i);
                if (value.Length > 0)
                {
                    paths.Add(value);
                }

                continue;
            }

            chunks.Add(ReadRegexChunk(text, ref i));
        }

        var compiledPaths = new SearchTerm[paths.Count];
        for (var p = 0; p < paths.Count; p++)
        {
            compiledPaths[p] = CompilePathFilter(paths[p]);
        }

        var pattern = string.Join(" ", chunks);
        if (pattern.Length == 0)
        {
            return compiledPaths.Length == 0
                ? Empty
                : new SearchExpression([], compiledPaths, usesAnd: false, isRegex: true);
        }

        try
        {
            var compiled = new Regex(
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexMatchTimeout);
            return new SearchExpression(
                [],
                compiledPaths,
                usesAnd: false,
                isRegex: true,
                compiledRegex: compiled);
        }
        catch (ArgumentException)
        {
            return new SearchExpression(
                [],
                compiledPaths,
                usesAnd: false,
                isRegex: true,
                isRegexInvalid: true);
        }
    }

    private static string ReadRegexChunk(string source, ref int i)
    {
        var from = i;
        while (i < source.Length && !char.IsWhiteSpace(source[i]))
        {
            i++;
        }

        return source[from..i];
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
