namespace ParserCore;

public sealed record SubmessageDebugResult(ParserResult Route, DebugResult? Captures,
    ElementDebugResult? Elements, string Status, string? BodyText = null, int BodyOffset = 0)
{
    public string? IdValue
    {
        get; init;
    }
    public string IdStatus { get; init; } = "";
}

/// <summary>Tests the actual route and independently previews a selected pattern against the full body token.</summary>
public static class SubmessageDebugger
{
    public static SubmessageDebugResult Analyze(string source, string input, int message, int pattern,
        CancellationToken cancellation = default)
    {
        var doc = new PropertiesDocument(source);
        // Extract the body independently: unfinished submessage drafts must not hide a valid main token.
        var mainDoc = new PropertiesDocument(source);
        mainDoc.Set("submessage.count", "0");
        var main = new ParserEvaluator(mainDoc.Source).Evaluate(input, true, cancellation);
        string idName = doc.Get("submessage.messageid.token");
        var idCapture = main.Captures.FirstOrDefault(c => c.Stage == "Token" && c.Name == idName);
        string? idValue = idCapture is null ? null : input.Substring(idCapture.Index, idCapture.Length);
        string idStatus = idName.Length == 0 ? "No ID token configured"
            : !EventMapping.Tokens(source).ContainsKey(idName) ? $"Token '{idName}' is not declared"
            : !main.MainMatched ? "ID unavailable: main regex did not match"
            : idValue is null ? $"Token '{idName}' was not captured"
            : $"{idName} · {idValue.Length} characters" + (idValue.Length == 0 ? " · empty capture" : "");
        SubmessageDebugResult WithId(SubmessageDebugResult value) => value with
        {
            IdValue = idValue,
            IdStatus = idStatus
        };
        ParserResult route;
        try
        {
            route = new ParserEvaluator(source).Evaluate(input, true, cancellation);
        }
        catch (FormatException ex)
        {
            route = new(ParseState.Error, main.MainMatched, "", "—", ex.Message, main.Captures, []);
        }
        string bodyName = doc.Get("submessage.token");
        var body = main.Captures.FirstOrDefault(c => c.Stage == "Token" && c.Name == bodyName);
        if (body is null)
        {
            string reason = bodyName.Length == 0 ? "Select a body token from the declared tokens."
                : !EventMapping.Tokens(source).ContainsKey(bodyName) ? $"Body token '{bodyName}' is not declared. Select a declared token."
                : !main.MainMatched ? "Body unavailable: " + main.Description + " Check Original log and the main regex."
                : $"Token '{bodyName}' was not captured by the main regex (an optional group may have been skipped).";
            if (!main.MainMatched && (doc.Get("regex").Contains(@"\t", StringComparison.Ordinal) || doc.Get("regex").Contains('\t'))
                && !input.Contains('\t') && input.Contains('\u2003'))
                reason += " The regex expects TAB (U+0009), but this input has EM SPACE (U+2003) and no tabs. Load the original log to preserve its separators.";
            return WithId(new(route, null, null, reason));
        }
        // Preserve the complete raw token, including whitespace and values longer than display snippets.
        string text = input.Substring(body.Index, body.Length);
        if (message < 0 || pattern < 0)
            return WithId(new(route, null, null, "Select a pattern to inspect its captures and regex elements.", text, body.Index));
        string key = $"submessage[{message}].pattern[{pattern}].regex";
        if (!doc.Values.ContainsKey(key))
            return WithId(new(route, null, null, "The selected pattern no longer exists.", text, body.Index));
        string regex = doc.Get(key);
        bool trim = doc.Get("trim.submessage").Equals("true", StringComparison.OrdinalIgnoreCase);
        cancellation.ThrowIfCancellationRequested();
        var captures = RegexDebugger.Analyze(regex, text, fullEvent: true, trim: trim, cancellation: cancellation);
        cancellation.ThrowIfCancellationRequested();
        var elements = RegexElementDebugger.Analyze(regex, text, full: true, trim: trim, cancellation: cancellation);
        cancellation.ThrowIfCancellationRequested();
        captures = captures with
        {
            Matches = captures.Matches.Select(s => s with { Index = s.Index + body.Index }).ToArray(),
            Rows = captures.Rows.Select(r => r.Success ? r with { Index = r.Index + body.Index } : r).ToArray()
        };
        elements = elements with
        {
            PartialSpan = elements.PartialSpan is { } partial ? partial with
            {
                Index = partial.Index + body.Index
            } : null,
            Status = elements.IsPartial ? "Body-relative details: " + elements.Status : elements.Status,
            Elements = elements.Elements.Select(e => e with
            {
                Captures = e.Captures.Select(s => s with { Index = s.Index + body.Index }).ToArray()
            }).ToArray()
        };
        bool used = route.UsedSubmessage == message && route.UsedPattern == pattern;
        string status = used ? "Selected pattern is the active route."
            : "Independent selected-pattern preview: this pattern was NOT used by routing (ID or pattern order may differ).";
        return WithId(new(route, captures, elements, status + " " + (elements.IsPartial ? elements.Summary : captures.Error ?? captures.Status), text, body.Index));
    }
}
