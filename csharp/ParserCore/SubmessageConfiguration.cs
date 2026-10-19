using System.Text.RegularExpressions;

namespace ParserCore;

public sealed class SubmessageDefinition
{
    public string? MessageId
    {
        get; set;
    }
    public Dictionary<string, string> Options { get; } = new(StringComparer.Ordinal);
    public List<Dictionary<string, string>> Patterns { get; } = [];
}

/// <summary>Editable submessage model that preserves extension options and reindexes definitions when applied.</summary>
public sealed class SubmessageConfiguration
{
    public string BodyToken { get; set; } = "";
    public string IdToken { get; set; } = "";
    public bool Trim
    {
        get; set;
    }
    public List<SubmessageDefinition> Messages { get; } = [];

    public static SubmessageConfiguration Load(string source)
    {
        var doc = new PropertiesDocument(source);
        var model = new SubmessageConfiguration
        {
            BodyToken = doc.Get("submessage.token"),
            IdToken = doc.Get("submessage.messageid.token"),
            Trim = doc.Get("trim.submessage").Equals("true", StringComparison.OrdinalIgnoreCase)
        };
        int Count(string key, int max)
        {
            if (!doc.Values.ContainsKey(key))
                return 0;
            if (!int.TryParse(doc.Get(key), out int n) || n < 0 || n > max)
                throw new FormatException("Invalid " + key);
            return n;
        }
        int count = Count("submessage.count", 1000), total = 0;
        for (int i = 0; i < count; i++)
        {
            string prefix = $"submessage[{i}].";
            var message = new SubmessageDefinition { MessageId = doc.Values.GetValueOrDefault(prefix + "messageid") };
            foreach (var kv in doc.Values.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)))
            {
                string key = kv.Key[prefix.Length..];
                if (key != "messageid" && key != "pattern.count" && !key.StartsWith("pattern[", StringComparison.Ordinal))
                    message.Options[key] = kv.Value;
            }
            int patterns = Count(prefix + "pattern.count", 1000);
            total += patterns;
            if (total > 5000)
                throw new FormatException("The limit is 5,000 patterns in total.");
            for (int p = 0; p < patterns; p++)
            {
                string pp = prefix + $"pattern[{p}].";
                message.Patterns.Add(doc.Values.Where(kv => kv.Key.StartsWith(pp, StringComparison.Ordinal))
                    .ToDictionary(kv => kv.Key[pp.Length..], kv => kv.Value, StringComparer.Ordinal));
            }
            model.Messages.Add(message);
        }
        return model;
    }

    public string Apply(string source)
    {
        if (Messages.Count > 1000 || Messages.Sum(m => m.Patterns.Count) > 5000)
            throw new FormatException("The submessage/pattern count limit was exceeded.");
        var doc = new PropertiesDocument(source);
        var retained = new HashSet<string>(StringComparer.Ordinal);
        void SetIndexed(string key, string? value)
        {
            if (value is not null)
                retained.Add(key);
            doc.Set(key, value);
        }
        doc.Set("submessage.count", Messages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        doc.Set("submessage.token", BodyToken.Length == 0 ? null : BodyToken);
        doc.Set("submessage.messageid.token", IdToken.Length == 0 ? null : IdToken);
        doc.Set("trim.submessage", Trim ? "true" : "false");
        for (int i = 0; i < Messages.Count; i++)
        {
            var message = Messages[i];
            if (message.Patterns.Count == 0)
                throw new FormatException($"submessage[{i}] must have at least one pattern.");
            string prefix = $"submessage[{i}].";
            SetIndexed(prefix + "messageid", message.MessageId);
            foreach (var kv in message.Options)
                SetIndexed(prefix + kv.Key, kv.Value);
            SetIndexed(prefix + "pattern.count", message.Patterns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (int p = 0; p < message.Patterns.Count; p++)
                foreach (var kv in message.Patterns[p])
                    SetIndexed(prefix + $"pattern[{p}]." + kv.Key, kv.Value);
        }
        doc.RemoveWhere(k => k.StartsWith("submessage[", StringComparison.Ordinal) && !retained.Contains(k));
        // Validate references and every expression before committing to the editor.
        _ = new ParserEvaluator(doc.Source);
        return doc.Source;
    }

    public static Dictionary<string, string> NewPattern() => new(StringComparer.Ordinal) { ["regex"] = "(.*)", ["fields"] = "event.message" };
}
