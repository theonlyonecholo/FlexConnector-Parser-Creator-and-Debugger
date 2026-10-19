using System.Text;
using System.Text.RegularExpressions;

namespace ParserCore;

public sealed record ParserProposal(string Source, string Report, IReadOnlyList<string> Samples);

/// <summary>Offline, bounded structural inference. Suggestions are drafts, never vendor-certified parsers.</summary>
public static class ParserSuggester
{
    private static readonly Regex Words = new("(?:[^\\s\"']+|\"[^\"\"]*\"|'[^']*')+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Dictionary<string, string> Mappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["src"] = "sourceAddress",
        ["srcip"] = "sourceAddress",
        ["sourceip"] = "sourceAddress",
        ["dst"] = "destinationAddress",
        ["dstip"] = "destinationAddress",
        ["destinationip"] = "destinationAddress",
        ["sport"] = "sourcePort",
        ["srcport"] = "sourcePort",
        ["dport"] = "destinationPort",
        ["dstport"] = "destinationPort",
        ["user"] = "sourceUserName",
        ["username"] = "sourceUserName",
        ["action"] = "deviceAction",
        ["result"] = "deviceAction",
        ["severity"] = "deviceSeverity",
        ["level"] = "deviceSeverity",
        ["host"] = "deviceHostName",
        ["hostname"] = "deviceHostName",
        ["msg"] = "message",
        ["message"] = "message",
        ["id"] = "deviceEventClassId",
        ["eventid"] = "deviceEventClassId",
        ["proto"] = "transportProtocol"
    };
    private sealed record Part(string Name, string Field, string Type, string Format);
    private sealed record Shape(string Regex, IReadOnlyList<Part> Parts);
    private static (string Key, string Value) Split(string text)
    {
        int equals = text.IndexOf('=');
        return equals > 0 ? (text[..equals], text[(equals + 1)..]) : ("", text);
    }
    private static string Signature(string[] words) => string.Join("\u001f", words.Select((w, index) =>
    {
        var (key, value) = Split(w);
        return key.Length > 0 ? key + "=" + (value.StartsWith('"') ? "quoted" : value.StartsWith('\'') ? "single" : "value")
            : index == 0 && Regex.IsMatch(w, "^[A-Za-z_]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ? w : "#";
    }));
    public static ParserProposal Generate(string input, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        if (input.Length > 250_000)
            throw new FormatException("Paste at most 250,000 characters.");
        string[] lines = input.Replace("\r\n", "\n").Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (lines.Length is < 2 or > 200)
            throw new FormatException("Supply 2–200 nonempty sample lines, one event per line.");
        if (lines.Any(l => l.Length > 8000))
            throw new FormatException("Each sample must have at most 8,000 characters.");
        var rows = lines.Select(l => Words.Matches(l).Select(m => m.Value).ToArray()).ToArray();
        if (rows.Any(r => r.Length > 100))
            throw new FormatException("At most 100 whitespace-separated fields per sample are supported.");
        var clusters = rows.GroupBy(Signature).ToArray();
        if (clusters.Length > 30)
            throw new FormatException("More than 30 structures detected. Supply a more focused set of similar events.");
        var notes = new List<string>();
        var shapes = clusters.Select(c => Build(c.ToArray(), notes)).ToArray();
        var doc = new PropertiesDocument("# Generated locally from sample logs. Review mappings and test unseen events before deployment.\n");
        doc.Set("event.name", EventMapping.Constant("Generated event"));
        if (shapes.Length == 1)
        {
            var shape = shapes[0];
            doc = new(TokenGenerator.Apply(doc.Source, shape.Regex, shape.Parts.Select((p, i) => new TokenDefinition(i, p.Name, p.Type, p.Format)).ToArray()));
            foreach (var p in shape.Parts)
                doc.Set(p.Field, p.Name);
        }
        else
        {
            doc = new(TokenGenerator.Apply(doc.Source, "(.*)", [new(0, "RawMessage", "String", "")]));
            doc.Set("event.message", "RawMessage");
            doc.Set("submessage.token", "RawMessage");
            doc.Set("submessage.count", "1");
            doc.Set("submessage[0].pattern.count", shapes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (int i = 0; i < shapes.Length; i++)
            {
                string prefix = $"submessage[0].pattern[{i}].";
                doc.Set(prefix + "regex", shapes[i].Regex);
                doc.Set(prefix + "fields", string.Join(",", shapes[i].Parts.Select(p => p.Field)));
                doc.Set(prefix + "types", string.Join(",", shapes[i].Parts.Select(p => p.Type)));
                doc.Set(prefix + "formats", string.Join(",", shapes[i].Parts.Select(p => p.Format.Length > 0 ? p.Format : "null")));
            }
            notes.Add("Different structures use ordered alternative patterns in one default submessage. No reliable message-ID routing was assumed.");
        }
        var evaluator = new ParserEvaluator(doc.Source);
        if (shapes.Any(s => s.Parts.Any(p => p.Field == "event.deviceSeverity")))
        {
            var severity = new Dictionary<string, List<string>>();
            foreach (var word in rows.SelectMany(r => r))
            {
                var (key, value) = Split(word);
                if (!Mappings.TryGetValue(key, out var field) || field != "deviceSeverity")
                    continue;
                string level = value.Trim('"', '\'').ToUpperInvariant() switch
                {
                    "CRITICAL" or "FATAL" or "EMERGENCY" => "veryhigh",
                    "ERROR" or "HIGH" => "high",
                    "WARN" or "WARNING" or "MEDIUM" => "medium",
                    "INFO" or "INFORMATION" or "DEBUG" or "LOW" => "low",
                    _ => ""
                };
                if (level.Length == 0)
                    continue;
                if (!severity.TryGetValue(level, out var values))
                    severity[level] = values = [];
                values.Add(value.Trim('"', '\''));
            }
            foreach (var (level, values) in severity)
                doc.Set(AdvancedConfiguration.SeverityKey(level), string.Join(",", values.Distinct()));
            if (severity.Count > 0)
                notes.Add("Textual severity labels were mapped by convention. Confirm the meaning with the device documentation; unknown and numeric levels are not inferred.");
        }
        var results = lines.Select(l =>
        {
            cancellation.ThrowIfCancellationRequested();
            if (timer.ElapsedMilliseconds > 10_000)
                throw new FormatException("Proposal validation exceeded 10 seconds. Use fewer samples.");
            return evaluator.Evaluate(l, cancellation: cancellation);
        }).ToArray();
        if (results.Any(r => !r.Success))
            throw new FormatException("The inferred parser did not match every sample. Split the samples into simpler sets.");
        var report = new StringBuilder($"{lines.Length}/{lines.Length} samples matched; {shapes.Length} structure(s).\n");
        for (int i = 0; i < results.Length; i++)
            report.AppendLine($"Line {i + 1}: {results[i].Route} — {results[i].State}");
        report.AppendLine("Heuristic draft: mappings are suggestions; sample coverage does not prove correctness for future events.");
        report.AppendLine("Unknown fields use deviceCustomString1–6. Timestamp-like values remain strings unless explicitly configured. Severity scales and extra processors require your device documentation and are not invented.");
        foreach (string note in notes.Distinct())
            report.AppendLine(note);
        return new(doc.Source, report.ToString(), lines);
    }
    private static Shape Build(string[][] samples, List<string> notes)
    {
        var pattern = new StringBuilder(@"\s*");
        var parts = new List<Part>();
        var used = new HashSet<string>();
        int custom = 0;
        for (int i = 0; i < samples[0].Length; i++)
        {
            if (i > 0)
                pattern.Append(@"\s+");
            var (key, _) = Split(samples[0][i]);
            string[] values = samples.Select(s => Split(s[i]).Value).ToArray();
            bool constant = key.Length == 0 && values.Distinct().Count() == 1;
            if (constant)
            {
                pattern.Append(Regex.Escape(values[0]));
                continue;
            }
            if (key.Length > 0)
                pattern.Append(Regex.Escape(key)).Append('=');
            bool quoted = values.All(v => v.Length >= 2 && v[0] == '"' && v[^1] == '"');
            bool single = values.All(v => v.Length >= 2 && v[0] == '\'' && v[^1] == '\'');
            string[] raw = quoted || single ? values.Select(v => v[1..^1]).ToArray() : values;
            string field = Mappings.TryGetValue(key, out string? known) ? "event." + known : "";
            if (field.Length == 0 || used.Contains(field))
            {
                field = ++custom <= 6 ? "event.deviceCustomString" + custom : "";
            }
            string type = field.Length > 0 && EventMapping.Fields.TryGetValue(field, out var expected) ? expected : "String";
            // Avoid claiming numeric/IP conversion when samples do not support it.
            bool numeric = raw.All(v => int.TryParse(v, out _));
            bool ip = raw.All(v => System.Net.IPAddress.TryParse(v, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if ((type == "IPAddress" && !ip) || (type == "Integer" && !numeric))
            {
                field = ++custom <= 6 ? "event.deviceCustomString" + custom : "";
                type = "String";
            }
            string atom = quoted ? "[^\"]*" : single ? "[^']*" : numeric ? @"-?\d+" : @"\S+";
            if (quoted)
                pattern.Append('"');
            if (single)
                pattern.Append('\'');
            if (field.Length > 0)
            {
                string stem = new string(key.Where(char.IsAsciiLetterOrDigit).ToArray());
                if (stem.Length == 0 || !char.IsAsciiLetter(stem[0]))
                    stem = "Field";
                string name = char.ToUpperInvariant(stem[0]) + stem[1..];
                string baseName = name;
                int suffix = 2;
                while (parts.Any(p => p.Name == name))
                    name = baseName + suffix++;
                parts.Add(new(name, field, type, ""));
                used.Add(field);
                pattern.Append('(').Append(atom).Append(')');
            }
            else
            {
                pattern.Append(atom);
                notes.Add("Some unmapped fields exceed the six custom-string slots and are matched without capture. Assign them manually if needed.");
            }
            if (quoted)
                pattern.Append('"');
            if (single)
                pattern.Append('\'');
        }
        pattern.Append(@"\s*");
        // Even constant-only lines should expose a token and a valid submessage field.
        if (parts.Count == 0)
            return new("(" + pattern + ")", [new("Message", "event.message", "String", "")]);
        return new(pattern.ToString(), parts);
    }
}
