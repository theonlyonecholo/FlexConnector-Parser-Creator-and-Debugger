using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ParserCore;

public enum ParseState
{
    Matched, MainMismatch, NoSubmessage, SubmessageMismatch, Error
}
public sealed record ParserCapture(string Stage, string Name, int Index, int Length, string Value, int Group = 0);
public sealed record PatternAttempt(int Submessage, int Pattern, bool Matched);
public sealed record ParserResult(ParseState State, bool MainMatched, string MessageId, string Route,
    string Description, IReadOnlyList<ParserCapture> Captures, IReadOnlyList<PatternAttempt> Attempts,
    int? UsedSubmessage = null, int? UsedPattern = null)
{
    public bool Success => State == ParseState.Matched;
}

/// <summary>Immutable parser snapshot; regex and submessage preview, not an ArcSight operations engine.</summary>
public sealed class ParserEvaluator
{
    private sealed record SubPattern(int Index, Regex Regex, string[] Fields);
    private sealed record Submessage(int Index, string? Id, List<SubPattern> Patterns);
    private readonly PropertiesDocument document;
    private readonly Regex main;
    private readonly string[] tokens;
    private readonly List<Submessage> submessages = [];
    private readonly int bodyToken = -1, idToken = -1;
    public string Source
    {
        get;
    }

    /// <summary>Parses and compiles an immutable configuration snapshot, validating token references.</summary>
    public ParserEvaluator(string source, int timeoutMs = 250)
    {
        Source = source;
        document = new(source);
        if (!document.Values.ContainsKey("regex"))
            throw new FormatException("Chybí hlavní regex.");
        main = Compile(document.Get("regex"), "regex", timeoutMs);
        int count = Count(document, "token.count", 5000);
        tokens = Enumerable.Range(0, count).Select(i => document.Get($"token[{i}].name")).ToArray();
        if (tokens.Any(string.IsNullOrEmpty) || tokens.Distinct(StringComparer.Ordinal).Count() != count)
            throw new FormatException("Tokeny musí mít neprázdné a jedinečné názvy.");
        if (main.GetGroupNumbers().Length - 1 != count)
            throw new FormatException("token.count neodpovídá počtu skupin hlavního regexu.");
        int subCount = document.Values.ContainsKey("submessage.count") ? Count(document, "submessage.count", 1000) : 0;
        if (subCount == 0)
            return;
        bodyToken = Array.IndexOf(tokens, document.Get("submessage.token"));
        if (bodyToken < 0)
            throw new FormatException("submessage.token neodkazuje na deklarovaný token.");
        string idName = document.Get("submessage.messageid.token");
        idToken = Array.IndexOf(tokens, idName);
        if (idName.Length > 0 && idToken < 0)
            throw new FormatException("submessage.messageid.token neodkazuje na deklarovaný token.");
        int total = 0;
        for (int s = 0; s < subCount; s++)
        {
            string prefix = $"submessage[{s}].";
            string? id = document.Values.TryGetValue(prefix + "messageid", out var value) ? value : null;
            if (id is not null && idToken < 0)
                throw new FormatException("Submessage s ID vyžaduje submessage.messageid.token.");
            int patterns = Count(document, prefix + "pattern.count", 1000);
            if (patterns == 0 || (total += patterns) > 5000)
                throw new FormatException("Počet patternů musí být kladný, nejvýše 5000 celkem.");
            var items = new List<SubPattern>();
            for (int p = 0; p < patterns; p++)
            {
                string key = prefix + $"pattern[{p}].";
                if (!document.Values.ContainsKey(key + "regex"))
                    throw new FormatException("Chybí " + key + "regex");
                items.Add(new(p, Compile(document.Get(key + "regex"), key + "regex", timeoutMs), document.Get(key + "fields").Split(',')));
            }
            submessages.Add(new(s, id, items));
        }
    }

    private static int Count(PropertiesDocument doc, string key, int max)
    {
        if (!int.TryParse(doc.Get(key), out int n) || n < 0 || n > max)
            throw new FormatException($"{key} musí být 0 až {max}.");
        return n;
    }
    private static Regex Compile(string pattern, string key, int timeout)
    {
        if (pattern.Length > RegexDebugger.MaxPatternLength)
            throw new FormatException(key + ": příliš dlouhý regex.");
        try
        {
            return new(@"\A(?:" + pattern + @")\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(Math.Clamp(timeout, 10, 2000)));
        }
        catch (ArgumentException ex) { throw new FormatException(key + ": " + ex.Message, ex); }
    }
    private bool Flag(string key) => document.Get(key).Equals("true", StringComparison.OrdinalIgnoreCase);
    private static (string Text, int Offset) TrimInput(string text, bool trim)
    {
        int start = 0, end = text.Length;
        if (trim)
        {
            while (start < end && text[start] is ' ' or '\t')
                start++;
            while (end > start && text[end - 1] is ' ' or '\t')
                end--;
        }
        return (text[start..end], start);
    }

    /// <summary>Matches a whole event and routes its body to the first successful submessage pattern.</summary>
    /// <param name="input">The original event text, before configured trimming.</param>
    /// <param name="details">Collect captures and attempts; disable for memory-efficient batch summaries.</param>
    /// <param name="cancellation">Cancellation requested between regex operations.</param>
    /// <remarks>Unknown IDs may use the default route. A known ID with no matching pattern never falls back.</remarks>
    public ParserResult Evaluate(string input, bool details = true, CancellationToken cancellation = default)
    {
        var captures = new List<ParserCapture>();
        var attempts = new List<PatternAttempt>();
        bool mainMatched = false;
        string id = "", route = "—";
        int? usedSubmessage = null, usedPattern = null;
        ParserResult Result(ParseState state, string description) => new(state, mainMatched, id, route, description, captures, attempts, usedSubmessage, usedPattern);
        var timer = Stopwatch.StartNew();
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (input.Length > RegexDebugger.MaxInputLength)
                return Result(ParseState.Error, "Log překračuje limit délky náhledu.");
            var text = TrimInput(input, Flag("trim.message"));
            Match match = main.Match(text.Text);
            if (!match.Success)
                return Result(ParseState.MainMismatch, "Hlavní regex se neshoduje s celou událostí.");
            mainMatched = true;
            if (details)
            {
                captures.Add(new("Hlavní regex", "Celá shoda", text.Offset, match.Length, ""));
                for (int i = 0; i < tokens.Length; i++)
                {
                    Group g = match.Groups[i + 1];
                    if (g.Success)
                        captures.Add(new("Token", tokens[i], text.Offset + g.Index, g.Length,
                        text.Text.Substring(g.Index, Math.Min(g.Length, 2000)) + (g.Length > 2000 ? "…" : "")));
                }
            }
            if (submessages.Count == 0)
                return Result(ParseState.Matched, "Hlavní regex odpovídá; parser nemá submessages.");
            Group body = match.Groups[bodyToken + 1];
            if (!body.Success)
                return Result(ParseState.Error, "Token submessage nebyl zachycen.");
            if (idToken >= 0)
            {
                if (!match.Groups[idToken + 1].Success)
                    return Result(ParseState.Error, "Token message ID nebyl zachycen.");
                id = match.Groups[idToken + 1].Value;
            }
            var candidates = submessages.Where(s => s.Id is not null && s.Id == id).ToArray();
            bool fallback = candidates.Length == 0;
            if (fallback)
                candidates = submessages.Where(s => s.Id is null).ToArray();
            if (candidates.Length == 0)
                return Result(ParseState.NoSubmessage, "Pro message ID není submessage ani default.");
            route = string.Join(", ", candidates.Select(s => $"submessage[{s.Index}]")) + (fallback ? " (default)" : "");
            var subtext = TrimInput(body.Value, Flag("trim.submessage"));
            int baseOffset = text.Offset + body.Index + subtext.Offset;
            foreach (var sub in candidates)
                foreach (var pattern in sub.Patterns)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (timer.ElapsedMilliseconds > 2000)
                        return Result(ParseState.Error, "Překročen celkový limit testu události (2 s).");
                    Match sm = pattern.Regex.Match(subtext.Text);
                    if (details)
                        attempts.Add(new(sub.Index, pattern.Index, sm.Success));
                    if (!sm.Success)
                        continue;
                    usedSubmessage = sub.Index;
                    usedPattern = pattern.Index;
                    route = $"submessage[{sub.Index}].pattern[{pattern.Index}]" + (fallback ? " (default)" : "");
                    if (details)
                    {
                        captures.Add(new("Submessage", "Celá shoda", baseOffset, sm.Length, ""));
                        foreach (int n in pattern.Regex.GetGroupNumbers().Where(n => n > 0))
                        {
                            Group g = sm.Groups[n];
                            if (g.Success && captures.Count < 5000)
                                captures.Add(new("Submessage", n <= pattern.Fields.Length && pattern.Fields[n - 1].Trim().Length > 0 ? pattern.Fields[n - 1].Trim() : "$" + n,
                                    baseOffset + g.Index, g.Length, subtext.Text.Substring(g.Index, Math.Min(g.Length, 2000)) + (g.Length > 2000 ? "…" : ""), n));
                        }
                    }
                    return Result(ParseState.Matched, "Shoda hlavního regexu i submessage. První odpovídající pattern byl vybrán.");
                }
            return Result(ParseState.SubmessageMismatch, "Submessage existuje, ale žádný její pattern se neshoduje. Default se pro známé ID nepoužije.");
        }
        catch (RegexMatchTimeoutException) { return Result(ParseState.Error, "Regex překročil časový limit."); }
    }
}
