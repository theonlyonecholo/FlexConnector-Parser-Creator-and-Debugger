namespace ParserCore;

public sealed record EventWrite(string Stage, string Field, string? Before, string? After, string Status);
public sealed record EventPreview(string Family, string ParseState, string Route, bool Complete,
    IReadOnlyDictionary<string, string?> Fields, IReadOnlyList<EventWrite> Trace, IReadOnlyList<string> Diagnostics,
    IReadOnlyList<MapDependency> Dependencies);

/// <summary>Deterministic local preview of supported event-processing stages, with explicit incompleteness.</summary>
public static class EventPipeline
{
    public static bool IsAssignment(string key) => EventMapping.IsDirectField(key) ||
        key.StartsWith("additionaldata.", StringComparison.Ordinal) && key != "additionaldata.enabled";

    public static EventPreview Evaluate(string source, string input, string? dependencyRoot = null,
        string? fileName = null, CancellationToken cancellation = default)
    {
        var inspection = ParserInspection.Inspect(source, fileName);
        var doc = new PropertiesDocument(source);
        var diagnostics = inspection.Diagnostics.ToList();
        var dependencies = MapFileProcessor.Inspect(doc, dependencyRoot);
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        var trace = new List<EventWrite>();
        bool complete = inspection.Diagnostics.Count == 0;
        string state = "Not evaluated", route = "—";
        EventPreview Result() => new(inspection.Family, state, route, complete, fields, trace, diagnostics, dependencies);
        void Issue(string message)
        {
            complete = false;
            diagnostics.Add(message);
        }
        if (!inspection.Supported)
        {
            complete = false;
            return Result();
        }
        ParserResult parsed;
        try
        {
            parsed = new ParserEvaluator(source).Evaluate(input, true, cancellation);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            Issue(ex.Message);
            state = "Configuration error";
            return Result();
        }
        state = parsed.State.ToString();
        route = parsed.Route;
        if (!parsed.Success)
        {
            Issue(parsed.Description);
            return Result();
        }
        var tokens = new Dictionary<string, string?>(StringComparer.Ordinal);
        string timezone = doc.Get("event.deviceTimeZone");
        if (timezone.Length > 0 && timezone is not "__stringConstant(UTC)" and not "__stringConstant(\"UTC\")")
            Issue("Configured deviceTimeZone is not applied to timestamp parsing by the local UTC preview.");
        int count = Count(doc, "token.count", 5000);
        for (int i = 0; i < count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            string prefix = $"token[{i}].", name = doc.Get(prefix + "name");
            var capture = parsed.Captures.FirstOrDefault(c => c.Stage == "Token" && c.Name == name);
            string? raw = capture is null ? null : input.Substring(capture.Index, capture.Length);
            try
            {
                tokens[name] = EventExpressions.Convert(raw, doc.Get(prefix + "type"), doc.Get(prefix + "format"));
                trace.Add(new("Token conversion", name, raw, tokens[name], "Converted"));
                if (doc.Get(prefix + "type") == "TimeStamp")
                    diagnostics.Add(name + ": timestamps without offsets are interpreted in UTC by this local preview.");
            }
            catch (Exception ex) when (ex is FormatException or NotSupportedException)
            {
                Issue(name + ": " + ex.Message);
            }
        }
        Dictionary<string, string?> Context() => tokens.Concat(fields).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
        void Assign(string field, string expression, string stage, string type = "", string format = "")
        {
            cancellation.ThrowIfCancellationRequested();
            string? before = fields.GetValueOrDefault(field);
            try
            {
                if (!IsAssignment(field))
                    throw new NotSupportedException("Unsupported target field: " + field);
                string? value = EventExpressions.Evaluate(expression, Context());
                if (type.Length > 0 && type != "null")
                    value = EventExpressions.Convert(value, type, format == "null" ? "" : format);
                if (value is not null && EventFieldCatalog.BuiltIn.TryGetValue(field, out var definition))
                {
                    if (definition.Type is "Integer" or "Long" or "IPAddress")
                        value = EventExpressions.Convert(value, definition.Type, "");
                    if (definition.Type == "TimeStamp")
                        value = EventExpressions.Convert(value, "Long", "");
                }
                fields[field] = value;
                trace.Add(new(stage, field, before, value, "Assigned"));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException)
            {
                fields.Remove(field);
                trace.Add(new(stage, field, before, null, "Unresolved: " + ex.Message));
                Issue(stage + " / " + field + ": " + ex.Message);
            }
        }
        void Mappings(string prefix, string stage)
        {
            foreach (var item in doc.Values.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal) && IsAssignment(p.Key[prefix.Length..])))
                Assign(item.Key[prefix.Length..], item.Value, stage);
        }
        void Conditions(string prefix)
        {
            for (int i = 0; i < Count(doc, prefix + "conditionalmap.count", 1000); i++)
            {
                cancellation.ThrowIfCancellationRequested();
                string key = prefix + $"conditionalmap[{i}].";
                string reference = doc.Get(key + "token");
                bool byToken = reference.Length > 0;
                if (!byToken)
                    reference = doc.Get(key + "field");
                if (byToken && doc.Get(key + "field").Length > 0)
                {
                    Issue(key + "Configure either token or field, not both.");
                    continue;
                }
                var values = byToken ? tokens : fields;
                if (!values.TryGetValue(reference, out var value))
                {
                    Issue(key + "Unresolved condition reference: " + reference);
                    continue;
                }
                int branches = Count(doc, key + "mappings.count", 1000);
                for (int j = 0; j < branches; j++)
                {
                    string branch = key + $"mappings[{j}].";
                    string delimiter = doc.Get(branch + "delimiter");
                    if (delimiter.Length == 0)
                        delimiter = ",";
                    if (!doc.Values.ContainsKey(branch + "values") ||
                        value is not null && doc.Get(branch + "values").Split(delimiter, StringSplitOptions.None).Contains(value))
                    {
                        trace.Add(new(branch, reference, value, value, "Selected conditional branch"));
                        Mappings(branch, branch);
                        break;
                    }
                }
            }
        }
        try
        {
            Mappings("", "Main mapping");
            Conditions("");
            if (parsed.UsedSubmessage is int message && parsed.UsedPattern is int pattern)
            {
                string prefix = $"submessage[{message}].pattern[{pattern}].";
                var bodyCaptures = parsed.Captures.Where(c => c.Stage == "Submessage" && c.Group > 0).ToArray();
                string[] names = doc.Get(prefix + "fields").Split(',');
                string[] types = doc.Get(prefix + "types").Split(',');
                string[] formats = doc.Get(prefix + "formats").Split(',');
                string delimiter = doc.Get(prefix + "mappings.delimiter");
                if (delimiter.Length == 0)
                    delimiter = "|";
                if (delimiter.Length != 1)
                    throw new NotSupportedException("Only single-character submessage mapping delimiters are supported.");
                var mappings = doc.Values.ContainsKey(prefix + "mappings")
                    ? EventExpressions.Split(doc.Get(prefix + "mappings"), delimiter[0]) : [];
                var bodyRegex = new System.Text.RegularExpressions.Regex(doc.Get(prefix + "regex"),
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
                foreach (int number in bodyRegex.GetGroupNumbers().Where(n => n > 0))
                    tokens["$" + number] = null;
                foreach (var c in bodyCaptures)
                    tokens["$" + c.Group] = input.Substring(c.Index, c.Length);
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].Trim().Length == 0)
                        continue;
                    string expression = mappings.Count == 0 ? "$" + (i + 1) :
                        i < mappings.Count ? mappings[i] : throw new FormatException("Submessage mapping/field count mismatch.");
                    Assign(names[i].Trim(), expression, prefix, i < types.Length ? types[i].Trim() : "", i < formats.Length ? formats[i].Trim() : "");
                }
                if (mappings.Count != 0 && mappings.Count != names.Length)
                    Issue(prefix + "Mapping count does not match fields.");
                string extra = doc.Get(prefix + "extramappings");
                string extraDelimiter = doc.Get(prefix + "extramappings.delimiter");
                if (extraDelimiter.Length == 0)
                    extraDelimiter = "|";
                if (extra.Length > 0)
                {
                    if (extraDelimiter.Length != 1)
                        throw new NotSupportedException("Only single-character extra mapping delimiters are supported.");
                    foreach (var assignment in EventExpressions.Split(extra, extraDelimiter[0]))
                    {
                        int equal = assignment.IndexOf('=');
                        if (equal < 1)
                            throw new FormatException("Malformed extra mapping.");
                        Assign(assignment[..equal].Trim(), assignment[(equal + 1)..], prefix + "extramappings");
                    }
                }
                Conditions(prefix);
            }
            if (doc.Values.Keys.Any(k => k.StartsWith("severity.map.")))
                Issue("Severity rules are configured; normalized severity is not evaluated in the event pipeline.");
            diagnostics.Add("Dependency paths are relative to the selected root. Map files without charencoding use strict UTF-8 in this preview.");
            foreach (var dependency in dependencies)
            {
                cancellation.ThrowIfCancellationRequested();
                if (dependency.Status != "Ready")
                {
                    Issue(dependency.Stage + ": " + dependency.Status);
                    break;
                }
                if (dependency.Type != "map")
                {
                    Issue(dependency.Stage + ": unsupported processor type " + dependency.Type);
                    break;
                }
                try
                {
                    MapFileProcessor.Apply(doc, dependency, fields, trace, cancellation);
                }
                catch (Exception ex) when (ex is IOException or FormatException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
                {
                    Issue(dependency.Stage + ": " + ex.Message);
                    break;
                }
            }
            foreach (string key in new[] { "multiline.starts.regex", "multiline.ends.regex", "line.filter.regex", "ignore.regex" })
                if (doc.Values.ContainsKey(key))
                    Issue("Not evaluated: " + key);
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException)
        {
            Issue(ex.Message);
        }
        return Result();
    }
    internal static int Count(PropertiesDocument doc, string key, int max)
    {
        if (!doc.Values.ContainsKey(key))
            return 0;
        if (!int.TryParse(doc.Get(key), out int count) || count < 0 || count > max)
            throw new FormatException($"{key} must be between 0 and {max}.");
        return count;
    }
}
