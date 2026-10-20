using System.Globalization;
using System.Text;

namespace ParserCore;

/// <summary>Small allowlisted interpreter. No scripts, reflection, network access or arbitrary code execution.</summary>
public static class EventExpressions
{
    public static IReadOnlyList<string> Split(string text, char delimiter = ',')
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        bool quoted = false, escaped = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }
            if (c == '\\')
            {
                escaped = true;
                continue;
            }
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (quoted)
                continue;
            if (c == '(')
                depth++;
            if (c == ')')
                depth--;
            if (depth < 0)
                throw new FormatException("Unbalanced expression.");
            if (c == delimiter && depth == 0)
            {
                parts.Add(text[start..i].Trim());
                start = i + 1;
            }
        }
        if (quoted || depth != 0)
            throw new FormatException("Unbalanced expression.");
        parts.Add(text[start..].Trim());
        return parts;
    }
    private static string Literal(string value)
    {
        if (!value.StartsWith('"'))
            return value;
        if (!value.EndsWith('"') || value.Length < 2)
            throw new FormatException("Unterminated string.");
        var output = new StringBuilder();
        for (int i = 1; i < value.Length - 1; i++)
        {
            char c = value[i];
            if (c == '\\' && i + 1 < value.Length - 1)
            {
                char next = value[++i];
                output.Append(next switch
                {
                    'n' => "\n",
                    'r' => "\r",
                    't' => "\t",
                    '"' => "\"",
                    '\\' => "\\",
                    _ => "\\" + next
                });
            }
            else
                output.Append(c);
        }
        return output.ToString();
    }
    public static string? Evaluate(string expression, IReadOnlyDictionary<string, string?> values, int depth = 0)
    {
        if (depth > 32 || expression.Length > 16000)
            throw new FormatException("Expression limit exceeded.");
        string text = expression.Trim();
        if (text.StartsWith('"'))
            return Literal(text);
        if (!text.StartsWith("__", StringComparison.Ordinal))
        {
            if (values.TryGetValue(text, out var value))
                return value;
            if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number))
                return number.ToString(CultureInfo.InvariantCulture);
            throw new NotSupportedException("Unknown token/reference: " + text);
        }
        int open = text.IndexOf('(');
        if (open < 2 || !text.EndsWith(')'))
            throw new FormatException("Malformed operation: " + text);
        string operation = text[..open];
        var args = Split(text[(open + 1)..^1]);
        void Arity(int n)
        {
            if (args.Count != n)
                throw new FormatException($"{operation} expects {n} arguments.");
        }
        if (operation == "__stringConstant")
        {
            Arity(1);
            return Literal(args[0]);
        }
        if (operation is "__integerConstant" or "__longConstant")
        {
            Arity(1);
            return Convert(Literal(args[0]), operation == "__integerConstant" ? "Integer" : "Long", "");
        }
        var supported = new[] { "__concatenate", "__oneOf", "__oneOfInteger", "__oneOfLong", "__safeToInteger", "__safeToLong", "__toLowerCase", "__toUpperCase", "__stringTrim", "__parseMultipleTimeStamp" };
        if (!supported.Contains(operation))
            throw new NotSupportedException("Unsupported operation: " + operation);
        var evaluated = args.Select(a => Evaluate(a, values, depth + 1)).ToArray();
        string? first = evaluated.FirstOrDefault();
        switch (operation)
        {
            case "__concatenate":
                if (evaluated.Sum(v => v?.Length ?? 0) > 250000)
                    throw new FormatException("Output length limit exceeded.");
                return string.Concat(evaluated);
            case "__oneOf":
                return evaluated.FirstOrDefault(v => !string.IsNullOrEmpty(v));
            case "__oneOfInteger":
                return SafeNumber(evaluated.FirstOrDefault(v => !string.IsNullOrEmpty(v)), true);
            case "__oneOfLong":
                return SafeNumber(evaluated.FirstOrDefault(v => !string.IsNullOrEmpty(v)), false);
            case "__safeToInteger":
                Arity(1);
                return SafeNumber(first, true);
            case "__safeToLong":
                Arity(1);
                return SafeNumber(first, false);
            case "__toLowerCase":
                Arity(1);
                return first?.ToLowerInvariant();
            case "__toUpperCase":
                Arity(1);
                return first?.ToUpperInvariant();
            case "__stringTrim":
                Arity(1);
                return first?.Trim();
            case "__parseMultipleTimeStamp":
                if (evaluated.Length < 2)
                    throw new FormatException("At least one timestamp format is required.");
                foreach (var format in evaluated.Skip(1))
                    try
                    {
                        return Convert(first, "TimeStamp", format ?? "");
                    }
                    catch (FormatException) { }
                throw new FormatException("No supported timestamp format matched.");
            default:
                throw new NotSupportedException(operation);
        }
    }
    private static string? SafeNumber(string? value, bool integer)
    {
        if (value is null || !long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number) ||
            integer && (number < int.MinValue || number > int.MaxValue))
            return null;
        return number.ToString(CultureInfo.InvariantCulture);
    }
    public static string? Convert(string? value, string type, string format)
    {
        if (value is null)
            return null;
        switch (type)
        {
            case "":
            case "String":
                return value;
            case "Integer":
            case "Long":
                return SafeNumber(value, type == "Integer") ?? throw new FormatException($"Invalid {type}: {value}");
            case "IPAddress":
                if (!System.Net.IPAddress.TryParse(value, out var ip))
                    throw new FormatException("Invalid IP address.");
                return ip.ToString();
            case "TimeStamp":
                var formats = new Dictionary<string, string>
                {
                    ["yyyy-MM-dd HH:mm:ss"] = "yyyy-MM-dd HH:mm:ss",
                    ["yyyy-MM-dd HH:mm:ss.SSS"] = "yyyy-MM-dd HH:mm:ss.fff",
                    ["MM/dd/yyyy HH:mm:ss.SSS"] = "MM/dd/yyyy HH:mm:ss.fff",
                    ["EEE MMM dd HH:mm:ss yyyy"] = "ddd MMM dd HH:mm:ss yyyy",
                    ["yyyy-MM-dd'T'HH:mm:ssXXX"] = "yyyy-MM-dd'T'HH:mm:sszzz",
                    ["yyyy-MM-dd'T'HH:mm:ss.SSSXXX"] = "yyyy-MM-dd'T'HH:mm:ss.fffzzz"
                };
                if (!formats.TryGetValue(format, out var translated))
                    throw new NotSupportedException("Unsupported timestamp format: " + format);
                if (format.EndsWith("XXX", StringComparison.Ordinal) && value.EndsWith('Z'))
                    value = value[..^1] + "+00:00";
                if (!DateTimeOffset.TryParseExact(value, translated, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
                    throw new FormatException("Invalid timestamp: " + value);
                return time.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            default:
                throw new NotSupportedException("Unsupported token type: " + type);
        }
    }
}
