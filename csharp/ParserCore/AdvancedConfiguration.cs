using System.Globalization;
using System.Text.RegularExpressions;

namespace ParserCore;

/// <summary>Validates severity rules and processor declarations; linked processors are never executed here.</summary>
public static class AdvancedConfiguration
{
    public static readonly string[] SeverityLevels = ["veryhigh", "high", "medium", "low"];
    public static readonly string[] ProcessorTypes = ["delimited", "json", "keyvalue", "map", "ntsubparser", "regex", "standardkeyvalue", "xml"];
    private static readonly string[] BooleanFields = ["flexagent", "clearfieldafterparsing", "overrideeventmappings",
        "casesensitive", "Casesensitive", "concatenatevalues", "allowoverwrite", "trimgettertokens", "trimsettertokens"];
    public static string SeverityKey(string level) => $"severity.map.{level}.if.deviceSeverity";
    private static bool Number(string s, out long value) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public static void ValidateSeverity(string rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
            return;
        foreach (string item in rule.Split(',').Select(s => s.Trim()))
        {
            if (item.Length == 0)
                throw new FormatException("Empty entry in severity mapping.");
            if (!item.Contains("..", StringComparison.Ordinal))
                continue;
            string[] range = item.Split("..", StringSplitOptions.None);
            if (range.Length != 2 || !Number(range[0], out long a) || !Number(range[1], out long b) || a > b)
                throw new FormatException("Invalid severity range: " + item);
        }
    }

    public static bool SeverityMatches(string rule, string value)
    {
        ValidateSeverity(rule);
        if (string.IsNullOrWhiteSpace(rule))
            return false;
        foreach (string item in rule.Split(',').Select(s => s.Trim()))
        {
            if (!item.Contains("..", StringComparison.Ordinal))
            {
                if (item == value)
                    return true;
                continue;
            }
            var range = item.Split("..", StringSplitOptions.None);
            if (Number(value, out long n) && n >= long.Parse(range[0], CultureInfo.InvariantCulture)
                && n <= long.Parse(range[1], CultureInfo.InvariantCulture))
                return true;
        }
        return false;
    }

    public static string PreviewSeverity(PropertiesDocument doc, string value)
    {
        string[] levels = SeverityLevels.Where(l => SeverityMatches(doc.Get(SeverityKey(l)), value)).ToArray();
        return levels.Length switch
        {
            0 => $"Severity: no rule for '{value}'.",
            1 => $"Severity: {levels[0]} (deviceSeverity={value}).",
            _ => "Severity is ambiguous: " + string.Join(", ", levels)
        };
    }

    public static bool IsProcessorKey(string key) => key == "extraprocessor.count" || key.StartsWith("extraprocessor[", StringComparison.Ordinal);
    public static int ValidateProcessors(PropertiesDocument doc)
    {
        var indices = new HashSet<int>();
        foreach (string key in doc.Values.Keys)
        {
            if (key == "extraprocessor.count")
                continue;
            var m = Regex.Match(key, @"\Aextraprocessor\[(0|[1-9][0-9]{0,3})\]\.(.+)\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!m.Success)
                throw new FormatException("Invalid extra processor property: " + key);
            indices.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        int count = indices.Count;
        if (indices.Any(i => i >= count))
            throw new FormatException("Processor indices must be consecutive: 0,1,2,... without gaps.");
        for (int i = 0; i < count; i++)
        {
            string p = $"extraprocessor[{i}].";
            if (!ProcessorTypes.Contains(doc.Get(p + "type")))
                throw new FormatException(p + "type is missing or unsupported.");
            if (string.IsNullOrWhiteSpace(doc.Get(p + "filename")))
                throw new FormatException(p + "filename is required.");
            foreach (string field in BooleanFields)
                if (doc.Values.TryGetValue(p + field, out string? v) && !bool.TryParse(v, out _))
                    throw new FormatException(p + field + " must be true or false.");
        }
        return count;
    }

    public static string Apply(string source, string severityToken, IReadOnlyDictionary<string, string> rules, string processors)
    {
        foreach (string rule in rules.Values)
            ValidateSeverity(rule);
        var extra = new PropertiesDocument(processors);
        int count = ValidateProcessors(extra);
        var doc = new PropertiesDocument(source);
        doc.Set("event.deviceSeverity", severityToken.Length == 0 ? null : severityToken);
        foreach (string level in SeverityLevels)
            doc.Set(SeverityKey(level), string.IsNullOrWhiteSpace(rules[level]) ? null : rules[level]);
        // Keep comments and custom options entered in the processor editor as well.
        extra.Set("extraprocessor.count", count.ToString(CultureInfo.InvariantCulture));
        doc.RemoveWhere(key => IsProcessorKey(key) && !extra.Values.ContainsKey(key));
        doc.MergeFrom(extra);
        return doc.Source;
    }

    public static string ProcessorTemplate(string type, int index)
    {
        if (!ProcessorTypes.Contains(type))
            throw new ArgumentException("Unknown processor type.");
        string p = $"extraprocessor[{index}].";
        string result = $"\r\n{p}type={type}\r\n{p}filename=custom/" + (type == "map" ? "mapping.csv" : "parser") + "\r\n";
        if (type != "map")
            result += $"{p}field=event.message\r\n{p}flexagent=true\r\n{p}clearfieldafterparsing=false\r\n";
        return result;
    }
}
