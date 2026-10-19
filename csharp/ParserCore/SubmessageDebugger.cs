namespace ParserCore;

public sealed record SubmessageDebugResult(ParserResult Route, DebugResult? Captures,
    ElementDebugResult? Elements, string Status, string? BodyText = null, int BodyOffset = 0);

/// <summary>Tests the actual route and independently previews a selected pattern against the full body token.</summary>
public static class SubmessageDebugger
{
    public static SubmessageDebugResult Analyze(string source, string input, int message, int pattern,
        CancellationToken cancellation = default)
    {
        var route = new ParserEvaluator(source).Evaluate(input, true, cancellation);
        var doc = new PropertiesDocument(source);
        var body = route.Captures.FirstOrDefault(c => c.Stage == "Token" && c.Name == doc.Get("submessage.token"));
        if (body is null)
            return new(route, null, null, "The main regex did not capture the configured body token. Fix the original log or main regex first.");
        // Preserve the complete raw token, including whitespace and values longer than display snippets.
        string text = input.Substring(body.Index, body.Length);
        if (message < 0 || pattern < 0)
            return new(route, null, null, "Select a pattern to inspect its captures and regex elements.", text, body.Index);
        string key = $"submessage[{message}].pattern[{pattern}].regex";
        if (!doc.Values.ContainsKey(key))
            return new(route, null, null, "The selected pattern no longer exists.", text, body.Index);
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
        return new(route, captures, elements, status + " " + (elements.IsPartial ? elements.Summary : captures.Error ?? captures.Status), text, body.Index);
    }
}
