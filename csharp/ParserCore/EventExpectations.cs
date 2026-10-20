using System.Text.Json;

namespace ParserCore;

public sealed record ExpectedEventCase(string Name, string Log, Dictionary<string, string?> Fields, string[]? AbsentFields = null, string? Route = null);
public sealed record ExpectedEventSuite(int Version, List<ExpectedEventCase> Cases);
public sealed record EventCaseResult(string Name, string Status, IReadOnlyList<string> Differences);

public static class EventExpectations
{
    public static ExpectedEventSuite Load(string json)
    {
        if (json.Length > 4 * 1024 * 1024)
            throw new FormatException("Fixture limit is 4 MB.");
        var suite = JsonSerializer.Deserialize<ExpectedEventSuite>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (suite is null || suite.Version != 1 || suite.Cases is null || suite.Cases.Count is 0 or > 500 ||
            suite.Cases.Any(c => c is null || string.IsNullOrWhiteSpace(c.Name) || c.Log is null || c.Log.Length > 250000 ||
                c.Fields is null || c.Fields.Count == 0 && (c.AbsentFields?.Length ?? 0) == 0 && c.Route is null))
            throw new FormatException("Use Version 1 and 1–500 named cases, each with a Log and at least one field, absent-field or route assertion.");
        return suite;
    }
    public static IReadOnlyList<EventCaseResult> Run(string source, string json, string? root = null, CancellationToken cancellation = default)
    {
        var output = new List<EventCaseResult>();
        foreach (var test in Load(json).Cases)
        {
            cancellation.ThrowIfCancellationRequested();
            var actual = EventPipeline.Evaluate(source, test.Log, root, cancellation: cancellation);
            var differences = new List<string>();
            foreach (var expected in test.Fields)
                if (!actual.Fields.TryGetValue(expected.Key, out var value))
                    differences.Add(expected.Key + ": missing; expected " + JsonSerializer.Serialize(expected.Value));
                else if (value != expected.Value)
                    differences.Add(expected.Key + ": expected " + JsonSerializer.Serialize(expected.Value) + ", actual " + JsonSerializer.Serialize(value));
            foreach (var absent in test.AbsentFields ?? [])
                if (actual.Fields.ContainsKey(absent))
                    differences.Add(absent + ": expected absent, but field exists.");
            if (test.Route is not null && test.Route != actual.Route)
                differences.Add("Route: expected " + test.Route + ", actual " + actual.Route);
            if (!actual.Complete)
                differences.AddRange(actual.Diagnostics);
            output.Add(new(test.Name, !actual.Complete ? "INCOMPLETE" : differences.Count == 0 ? "PASS" : "FAIL", differences));
        }
        return output;
    }
}
