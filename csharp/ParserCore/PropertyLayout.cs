namespace ParserCore;

/// <summary>Conservative insertion order for known FlexConnector categories and indexed children.</summary>
internal static class PropertyLayout
{
    internal sealed record Position(int Category, string[] Path);
    private static readonly Dictionary<string, int> Fields = new(StringComparer.Ordinal)
    {
        ["regex"] = 0,
        ["messageid"] = 0,
        ["name"] = 10,
        ["fields"] = 10,
        ["token"] = 10,
        ["type"] = 20,
        ["types"] = 20,
        ["trim"] = 20,
        ["format"] = 30,
        ["formats"] = 30,
        ["count"] = 30,
        ["mappings"] = 40,
        ["extramappings"] = 50,
        ["filename"] = 30,
        ["field"] = 40,
        ["veryhigh"] = 0,
        ["high"] = 10,
        ["medium"] = 20,
        ["low"] = 30
    };
    internal static Position? Describe(string key)
    {
        string[] parts = key.Split(['.', '[', ']'], StringSplitOptions.RemoveEmptyEntries);
        if (key == "regex")
            return new(0, ["regex"]);
        if (key == "trim.message")
            return new(0, ["trim"]);
        if (key == "trim.submessage")
            return new(2, ["trim"]);
        if (parts.Length < 2)
            return null;
        int category = parts[0] switch
        {
            "token" => 1,
            "submessage" => 2,
            "event" => 3,
            "severity" => 4,
            "extraprocessor" => 5,
            _ => -1
        };
        return category < 0 ? null : new(category, parts[1..]);
    }
    internal static int Compare(Position left, Position right)
    {
        int category = left.Category.CompareTo(right.Category);
        if (category != 0)
            return category;
        for (int i = 0; i < Math.Min(left.Path.Length, right.Path.Length); i++)
        {
            string a = left.Path[i], b = right.Path[i];
            if (a == b)
                continue;
            bool an = a.All(char.IsAsciiDigit), bn = b.All(char.IsAsciiDigit);
            if (an != bn)
                return an ? 1 : -1; // Parent options/count precede indexed children.
            if (an)
            {
                string av = a.TrimStart('0'), bv = b.TrimStart('0');
                int length = av.Length.CompareTo(bv.Length);
                if (length != 0)
                    return length;
                int value = string.CompareOrdinal(av, bv);
                if (value != 0)
                    return value;
            }
            else
            {
                int rank = Fields.GetValueOrDefault(a, 100).CompareTo(Fields.GetValueOrDefault(b, 100));
                if (rank != 0)
                    return rank;
            }
            return string.CompareOrdinal(a, b);
        }
        return left.Path.Length.CompareTo(right.Path.Length);
    }
}
