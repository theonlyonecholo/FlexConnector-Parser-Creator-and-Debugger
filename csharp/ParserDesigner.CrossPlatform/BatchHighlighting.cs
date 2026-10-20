using ParserCore;

namespace ParserDesigner.CrossPlatform;

/// <summary>Explains parser stages without treating a speculative regex prefix as a successful parse.</summary>
internal sealed record BatchHighlighting(IReadOnlyList<Highlight> Spans, string Explanation)
{
    internal const string Passed = "#c8e6c9", Failed = "#ffcdd2", Partial = "#ffe0b2";

    internal static BatchHighlighting Analyze(string source, string input, ParserResult result, CancellationToken cancellation)
    {
        var spans = new List<Highlight>();
        var doc = new PropertiesDocument(source);
        void Mark(int index, int length, string color)
        {
            if (length > 0)
                spans.Add(new(index, length, 0, Background: color));
        }
        ParserCapture? Token(string name) => result.Captures.FirstOrDefault(c => c.Stage == "Token" && c.Name == name);
        if (result.MainMatched)
        {
            foreach (var capture in result.Captures.Where(c => c.Stage == "Main regex" && c.Name == "Entire match"))
                Mark(capture.Index, capture.Length, Passed);
        }
        if (result.Success)
            return new(spans, "Green: regex stages passed. Token conversions and event transformations are not validated.");
        if (result.State == ParseState.NoSubmessage)
        {
            var id = Token(doc.Get("submessage.messageid.token"));
            if (id is not null)
                Mark(id.Index, id.Length, Failed);
            return new(spans, "Red: routing token has no matching message ID or default. Main extraction passed; no body regex was tested." +
                (id is { Length: 0 } ? " The routing token is empty, so there is no character to color." : ""));
        }
        if (result.State == ParseState.Error)
        {
            Mark(0, input.Length, Partial);
            return new(spans, "Orange: evaluation error or timeout; an exact failing character cannot be determined. See the diagnostic below.");
        }

        int offset = 0;
        string text = input, pattern = doc.Get("regex");
        bool trim = doc.Get("trim.message").Equals("true", StringComparison.OrdinalIgnoreCase);
        string scope = "Main regex";
        if (result.State == ParseState.SubmessageMismatch)
        {
            var body = Token(doc.Get("submessage.token"));
            var attempt = result.Attempts.FirstOrDefault();
            if (body is null || attempt is null)
                return new(spans, "Submessage failed; no reliable body span or attempted pattern is available.");
            offset = body.Index;
            text = input.Substring(offset, body.Length);
            pattern = doc.Get($"submessage[{attempt.Submessage}].pattern[{attempt.Pattern}].regex");
            trim = doc.Get("trim.submessage").Equals("true", StringComparison.OrdinalIgnoreCase);
            scope = $"submessage[{attempt.Submessage}].pattern[{attempt.Pattern}] (first attempted pattern)";
        }
        Mark(offset, text.Length, Failed);
        if (text.Length == 0)
            return new(spans, $"{scope}: the input is empty, so there is no character to color. The regex stage failed.");
        var debug = RegexElementDebugger.Analyze(pattern, text, full: true, trim: trim, cancellation: cancellation);
        if (debug.IsPartial && debug.PartialSpan is { } prefix)
        {
            Mark(offset + prefix.Index, prefix.Length, Partial);
            return new(spans, $"{scope}: orange is an estimated matching prefix, not a successful parse. Red is the remaining failed-stage text, not a proven error location. " +
                (prefix.Index + prefix.Length == text.Length ? "The prefix reaches the end of the input; required text may be missing after it. " : "") + debug.Summary);
        }
        return new(spans, $"{scope}: red marks the failed-stage input; no reliable partial prefix was found. It does not identify an exact failing character. " +
            (text.Length == 0 ? "The input is empty, so there is no character to color. " : "") + debug.Status);
    }
}
