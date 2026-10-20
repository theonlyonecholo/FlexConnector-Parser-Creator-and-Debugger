namespace ParserCore;

public sealed record ParserInspection(string Family, bool Supported, IReadOnlyList<string> Diagnostics)
{
    public static ParserInspection Inspect(string source, string? fileName = null)
    {
        var doc = new PropertiesDocument(source);
        string name = fileName?.ToLowerInvariant() ?? "";
        var families = new List<string>();
        if (doc.Values.ContainsKey("key.delimiter") || doc.Values.ContainsKey("key.value.delimiter") || name.Contains("keyvalue"))
            families.Add("Key-value");
        if (name.Contains("json") || doc.Values.ContainsKey("trigger.node.location"))
            families.Add("JSON");
        if (name.Contains("xml") || name.Contains("xquery") || doc.Values.Keys.Any(k => k.StartsWith("namespace[")))
            families.Add("XML");
        if (name.Contains("database") || doc.Values.ContainsKey("query"))
            families.Add("Database");
        if (name.EndsWith(".sdkfilereader.properties") || doc.Values.ContainsKey("delimiter"))
            families.Add("Delimited");
        if (doc.Values.ContainsKey("regex"))
            families.Add("Regex");
        string family = families.Count == 0 ? "Unknown" : string.Join(" / ", families.Distinct());
        bool supported = families.Count == 1 && families[0] == "Regex";
        var diagnostics = new List<string>();
        if (!supported)
            diagnostics.Add($"Parser family: {family}. Event evaluation supports regex parsers only; configuration inspection remains available.");
        foreach (var group in doc.Declarations().GroupBy(p => p.Key).Where(g => g.Count() > 1))
            diagnostics.Add($"Duplicate key '{group.Key}': lines {string.Join(", ", group.Select(p => p.Line))}. Effective value from line {group.Last().Line}: {group.Last().Value}");
        return new(family, supported, diagnostics);
    }
}
