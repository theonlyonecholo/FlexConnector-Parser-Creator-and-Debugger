using System.Text.RegularExpressions;

namespace ParserCore;

/// <summary>A zero-based token declaration with decoded, unescaped property values.</summary>
public sealed record TokenDefinition(int Index, string Name, string Type, string Format);
/// <summary>Creates and applies token declarations while preserving metadata by token index.</summary>
public static class TokenGenerator
{
    /// <summary>Builds a proposal from capturing groups; rejects non-contiguous group numbers.</summary>
    public static IReadOnlyList<TokenDefinition> Draft(string source, string pattern)
    {
        if (pattern.Length > RegexDebugger.MaxPatternLength)
            throw new FormatException("The regex is too long.");
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        int[] groups = regex.GetGroupNumbers().Where(n => n > 0).ToArray();
        if (!groups.SequenceEqual(Enumerable.Range(1, groups.Length)))
            throw new FormatException("Explicitly numbered groups must be consecutive: 1,2,... without gaps.");
        if (groups.Length > 5000)
            throw new FormatException("The generator limit is 5,000 tokens.");
        var doc = new PropertiesDocument(source);
        var used = new HashSet<string>(StringComparer.Ordinal);
        // Reserve existing names first, so newly generated names cannot collide with them.
        foreach (int group in groups)
        {
            string oldName = doc.Get($"token[{group - 1}].name");
            if (oldName.Length > 0)
                used.Add(oldName);
        }
        var result = new List<TokenDefinition>();
        foreach (int group in groups)
        {
            string prefix = $"token[{group - 1}].";
            string name = doc.Get(prefix + "name");
            if (name.Length == 0)
            {
                string baseName = regex.GroupNameFromNumber(group);
                if (int.TryParse(baseName, out _))
                    baseName = "Token" + group;
                name = baseName;
                int suffix = 2;
                while (!used.Add(name))
                    name = baseName + "_" + suffix++;
            }
            result.Add(new(group - 1, name, doc.Get(prefix + "type") is { Length: > 0 } type ? type : "String", doc.Get(prefix + "format")));
        }
        return result;
    }

    /// <summary>Validates the proposal and returns updated properties without writing a file.</summary>
    public static string Apply(string source, string pattern, IReadOnlyList<TokenDefinition> tokens)
    {
        if (Draft(source, pattern).Count != tokens.Count)
            throw new FormatException("The token count does not match the regex.");
        if (!tokens.Select(t => t.Index).SequenceEqual(Enumerable.Range(0, tokens.Count)))
            throw new FormatException("Token indices must start at zero and be consecutive.");
        if (tokens.Any(t => string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.Type)))
            throw new FormatException("Every token requires a name and type.");
        if (tokens.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != tokens.Count)
            throw new FormatException("Token names must be unique.");
        var doc = new PropertiesDocument(source);
        // Remove declarations beyond the new capture count; keep custom metadata on retained indices.
        doc.RemoveWhere(key =>
        {
            if (!key.StartsWith("token[", StringComparison.Ordinal))
                return false;
            int end = key.IndexOf(']');
            return end > 6 && int.TryParse(key.AsSpan(6, end - 6), out int index) && index >= tokens.Count;
        });
        doc.Set("regex", pattern);
        doc.Set("token.count", tokens.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var token in tokens)
        {
            string prefix = $"token[{token.Index}].";
            doc.Set(prefix + "name", token.Name);
            doc.Set(prefix + "type", token.Type);
            doc.Set(prefix + "format", token.Format.Length == 0 ? null : token.Format);
        }
        return doc.Source;
    }
}
