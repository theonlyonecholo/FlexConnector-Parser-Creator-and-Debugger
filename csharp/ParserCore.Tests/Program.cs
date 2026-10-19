using ParserCore;
using System.Text.RegularExpressions;

int passed = 0;
void Test(string name, Action action)
{
    try
    {
        action();
        passed++;
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); Environment.ExitCode = 1; }
}
void Check(bool condition, string description = "Assertion failed")
{
    if (!condition)
        throw new Exception(description);
}
void Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
Dictionary<string, string> Rules(string high = "") => new() { ["veryhigh"] = "", ["high"] = high, ["medium"] = "", ["low"] = "" };

Test("Properties: escape, separator, case and duplicates", () =>
{
    var d = new PropertiesDocument("# comment\\\r\nregex=(\\\\d+)\r\nx:first\nx second\nX=UPPER\nkey\\:part=value\n");
    Check(d.Get("regex") == @"(\d+)");
    Check(d.Get("x") == "second" && d.Get("X") == "UPPER");
    Check(d.Get("key:part") == "value");
});
Test("Properties: logical continuation and comment semantics", () =>
{
    var d = new PropertiesDocument("x=a\\\r\n  b\ny=\\\\\nz=ok\n\\\n#literal=value\n");
    Check(d.Get("x") == "ab" && d.Get("y") == "\\" && d.Get("z") == "ok");
    Check(d.Get("#literal") == "value");
});
Test("Properties: Unicode, whitespace, unknown escapes", () =>
{
    var d = new PropertiesDocument(@"x=\u010D\t\ value  " + "\n" + @"y=\q");
    Check(d.Get("x") == "č\t value  " && d.Get("y") == "q");
    Throws<FormatException>(() => new PropertiesDocument(@"bad=\u0xx0"));
    Throws<FormatException>(() => new PropertiesDocument(@"bad=\u00"));
    string value = " č 😀\n\\ :#!=";
    Check(PropertiesDocument.Decode(PropertiesDocument.Escape(value)) == value);
});
Test("Properties: set removes continued duplicate and preserves other raw blocks", () =>
{
    var d = new PropertiesDocument("# keep\nx=one\\\n two\nx=last\ncustom.option: untouched\n");
    d.Set("x", "updated");
    var reread = new PropertiesDocument(d.Source);
    Check(reread.Get("x") == "updated" && reread.Get("custom.option") == "untouched");
    Check(d.Source.Contains("custom.option: untouched\n") && d.Source.Contains("# keep\n"));
    Check(!d.Source.Contains("x=last"));
    d.Set("x", null);
    Check(!new PropertiesDocument(d.Source).Values.ContainsKey("x"));
});
Test("Properties: terminal continuation cannot swallow a new property", () =>
{
    var d = new PropertiesDocument("x=a\\");
    d.Set("regex", @"\d+");
    Check(new PropertiesDocument(d.Source).Get("regex") == @"\d+");
});
Test("Properties: ASCII export and Unicode round trip", () =>
{
    string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".properties");
    try
    {
        var d = new PropertiesDocument("# č\nvalue=\\č\nregex=(\\\\d+)\n");
        d.Set("other", "Žluťoučký 😀");
        d.Save(file);
        Check(File.ReadAllBytes(file).All(b => b < 128));
        var reloaded = PropertiesDocument.Load(file);
        Check(reloaded.Get("value") == "č" && reloaded.Get("other") == "Žluťoučký 😀");
        Check(reloaded.Get("regex") == @"(\d+)");
    }
    finally { if (File.Exists(file)) File.Delete(file); }
});
Test("Severity: lists, bounds and overlap", () =>
{
    Check(AdvancedConfiguration.SeverityMatches("400..599", "400"));
    Check(AdvancedConfiguration.SeverityMatches("400..599", "599"));
    Check(!AdvancedConfiguration.SeverityMatches("400..599", "600"));
    Check(AdvancedConfiguration.SeverityMatches("OPEN,CLOSE", "CLOSE"));
    Check(!AdvancedConfiguration.SeverityMatches("OPEN,CLOSE", "Open"));
    Check(AdvancedConfiguration.SeverityMatches("1,400..599", "500"));
    Check(!AdvancedConfiguration.SeverityMatches("", ""));
    Throws<FormatException>(() => AdvancedConfiguration.ValidateSeverity("2..1"));
    Throws<FormatException>(() => AdvancedConfiguration.ValidateSeverity("1,,2"));
    var d = new PropertiesDocument("severity.map.high.if.deviceSeverity=400..599\nseverity.map.low.if.deviceSeverity=500");
    Check(AdvancedConfiguration.PreviewSeverity(d, "500").Contains("ambiguous"));
});
Test("Processors: templates, unknown options, count normalization", () =>
{
    foreach (string kind in AdvancedConfiguration.ProcessorTypes)
        Check(AdvancedConfiguration.ValidateProcessors(new(AdvancedConfiguration.ProcessorTemplate(kind, 0))) == 1);
    string extras = AdvancedConfiguration.ProcessorTemplate("regex", 0) +
        AdvancedConfiguration.ProcessorTemplate("map", 1) + "extraprocessor.count=99\nextraprocessor[0].custom=keep\n";
    string merged = AdvancedConfiguration.Apply("regex=(.*)\ncustom=keep\n# comment\n", "Status", Rules("400..599"), extras);
    var d = new PropertiesDocument(merged);
    Check(d.Get("extraprocessor.count") == "2" && d.Get("extraprocessor[0].custom") == "keep");
    Check(d.Get("custom") == "keep" && merged.Contains("# comment"));
    Check(d.Get("event.deviceSeverity") == "Status");
});
Test("Processors: reject gap, invalid index, boolean and missing filename", () =>
{
    Throws<FormatException>(() => AdvancedConfiguration.ValidateProcessors(new(AdvancedConfiguration.ProcessorTemplate("map", 1))));
    Throws<FormatException>(() => AdvancedConfiguration.ValidateProcessors(new("extraprocessor[-1].type=map")));
    Throws<FormatException>(() => AdvancedConfiguration.ValidateProcessors(new("extraprocessor[00].type=map")));
    Throws<FormatException>(() => AdvancedConfiguration.ValidateProcessors(new("extraprocessor[0].type=map")));
    Throws<FormatException>(() => AdvancedConfiguration.ValidateProcessors(new(AdvancedConfiguration.ProcessorTemplate("map", 0) + "extraprocessor[0].allowoverwrite=maybe")));
});
Test("Advanced apply: removal, no duplicate mappings, custom data preserved", () =>
{
    string old = "regex=(.*)\nseverity.map.high.if.deviceSeverity=1,\\\n 2\nseverity.map.high.if.deviceSeverity=3\nevent.deviceSeverity.custom=keep\n" + AdvancedConfiguration.ProcessorTemplate("map", 0);
    var d = new PropertiesDocument(AdvancedConfiguration.Apply(old, "", Rules(), ""));
    Check(!d.Values.ContainsKey(AdvancedConfiguration.SeverityKey("high")));
    Check(d.Get("extraprocessor.count") == "0" && !d.Values.ContainsKey("extraprocessor[0].type"));
    Check(d.Get("event.deviceSeverity.custom") == "keep");
});
Test("Regex: all matches and exact highlight spans", () =>
{
    var r = RegexDebugger.Analyze(@"\d+", "abc12 x345");
    Check(r.Error is null && r.Matches.SequenceEqual(new[] { new TextSpan(3, 2), new TextSpan(7, 3) }));
});
Test("Regex: named groups, repeated captures and unmatched optional group", () =>
{
    var r = RegexDebugger.Analyze(@"(?<digits>\d)+(x)?", "123");
    Check(r.Rows.Count(row => row.Name == "digits") == 3);
    Check(r.Rows.Any(row => row.Group == 1 && !row.Success && row.Index == -1));
    Check(r.Rows.Where(row => row.Name == "digits").Select(row => row.Index).SequenceEqual([0, 1, 2]));
});
Test("Regex: zero-length results terminate and have positions", () =>
{
    var r = RegexDebugger.Analyze(@"(?=a)", "aa");
    Check(r.Matches.Count == 2 && r.Matches.All(m => m.Length == 0));
    Check(r.Matches[1].Index == 1);
});
Test("Regex: original UTF-16 positions after trim", () =>
{
    var r = RegexDebugger.Analyze(@"\d+", " \t😀12  ", trim: true);
    Check(r.Matches.Single() == new TextSpan(4, 2));
});
Test("Regex: full-event alternative backtracks and rejects partial match", () =>
{
    Check(RegexDebugger.Analyze("a|ab", "ab", fullEvent: true).Matches.Single().Length == 2);
    Check(RegexDebugger.Analyze("ab", "xab", fullEvent: true).Matches.Count == 0);
});
Test("Regex: option flags", () =>
{
    Check(RegexDebugger.Analyze("abc", "ABC", RegexOptions.IgnoreCase).Matches.Count == 1);
    Check(RegexDebugger.Analyze("^b", "a\nb", RegexOptions.Multiline).Matches.Single().Index == 2);
    Check(RegexDebugger.Analyze("a.b", "a\nb", RegexOptions.Singleline).Matches.Count == 1);
});
Test("Regex: invalid syntax returns diagnostic and no stale spans", () =>
{
    var r = RegexDebugger.Analyze("(", "text");
    Check(r.Error is not null && r.ErrorOffset is not null && r.Matches.Count == 0 && r.Rows.Count == 0);
});
Test("Regex: timeout on catastrophic backtracking", () =>
{
    var r = RegexDebugger.Analyze(@"(a+)+$", new string('a', 10000) + "!", timeoutMs: 10);
    Check(r.Error is not null && r.Status.Contains("timeout"));
    Check(r.Matches.Count == 0);
});
Test("Regex: cancellation and bounded match output", () =>
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    Throws<OperationCanceledException>(() => RegexDebugger.Analyze(".*", "x", cancellation: cts.Token));
    var r = RegexDebugger.Analyze("a", new string('a', 2000));
    Check(r.Matches.Count == RegexDebugger.MaxMatches && r.Status.Contains("truncated"));
    Check(RegexDebugger.Analyze("a", new string('a', RegexDebugger.MaxInputLength + 1)).Error is not null);
});
Test("Repository examples: parsing, severity, file round trip", () =>
{
    string examples = Path.Combine(AppContext.BaseDirectory, "examples");
    foreach (string file in Directory.GetFiles(examples, "*.properties"))
    {
        var d = PropertiesDocument.Load(file);
        string logFile = file.Contains("submessages") ? "submessages.log" : file.Contains("severity-processors") ? "severity-processors.log" : "demo.log";
        string log = File.ReadLines(Path.Combine(examples, logFile)).First();
        var r = RegexDebugger.Analyze(d.Get("regex"), log, fullEvent: true);
        Check(r.Error is null && r.Matches.Count == 1, file);
        if (logFile.StartsWith("severity"))
            Check(AdvancedConfiguration.PreviewSeverity(d, "404").Contains("high"));
        string temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".properties");
        try
        {
            d.Save(temp);
            var other = PropertiesDocument.Load(temp);
            Check(d.Values.Count == other.Values.Count && d.Values.All(kv => other.Get(kv.Key) == kv.Value));
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
});
string SubSource() => "regex=id=(\\\\w+) body=(.*)\ntoken.count=2\ntoken[0].name=ID\ntoken[1].name=Body\n" +
    "submessage.messageid.token=ID\nsubmessage.token=Body\nsubmessage.count=2\n" +
    "submessage[0].messageid=A\nsubmessage[0].pattern.count=2\nsubmessage[0].pattern[0].regex=user=(\\\\w+)\n" +
    "submessage[0].pattern[0].fields=event.sourceUserName\nsubmessage[0].pattern[1].regex=anonymous\n" +
    "submessage[1].pattern.count=1\nsubmessage[1].pattern[0].regex=(.*)\n";
Test("Batch: main mismatch does not attempt submessages", () =>
{
    var r = new ParserEvaluator(SubSource()).Evaluate("invalid");
    Check(r.State == ParseState.MainMismatch && !r.MainMatched && r.Attempts.Count == 0);
});
Test("Batch: ID route, first matching pattern and capture positions", () =>
{
    const string text = "id=A body=user=alice";
    var r = new ParserEvaluator(SubSource()).Evaluate(text);
    Check(r.Success && r.MainMatched && r.Route == "submessage[0].pattern[0]");
    Check(r.MessageId == "A" && r.Attempts.Count == 1);
    var field = r.Captures.Single(c => c.Name == "event.sourceUserName");
    Check(field.Index == text.IndexOf("alice", StringComparison.Ordinal) && text.Substring(field.Index, field.Length) == "alice");
});
Test("Batch: second pattern is tried in order", () =>
{
    var r = new ParserEvaluator(SubSource()).Evaluate("id=A body=anonymous");
    Check(r.Success && r.Route == "submessage[0].pattern[1]");
    Check(r.Attempts.Count == 2 && !r.Attempts[0].Matched && r.Attempts[1].Matched);
});
Test("Batch: unknown ID uses default", () =>
{
    var r = new ParserEvaluator(SubSource()).Evaluate("id=UNKNOWN body=hello");
    Check(r.Success && r.Route == "submessage[1].pattern[0] (default)");
});
Test("Batch: known ID mismatch never falls back to default", () =>
{
    var r = new ParserEvaluator(SubSource()).Evaluate("id=A body=wrong payload");
    Check(r.State == ParseState.SubmessageMismatch && r.MainMatched);
    Check(r.Attempts.Count == 2 && r.Attempts.All(a => a.Submessage == 0));
});
Test("Batch: missing route distinguished from pattern mismatch", () =>
{
    var doc = new PropertiesDocument(SubSource());
    doc.Set("submessage.count", "1");
    var r = new ParserEvaluator(doc.Source).Evaluate("id=B body=hello");
    Check(r.State == ParseState.NoSubmessage && r.MainMatched && r.Route == "—");
});
Test("Batch: trim offsets in original message including Unicode", () =>
{
    var doc = new PropertiesDocument(SubSource());
    doc.Set("trim.message", "true");
    doc.Set("trim.submessage", "true");
    const string text = " \tid=A body=  user=Žan  ";
    var r = new ParserEvaluator(doc.Source).Evaluate(text);
    Check(r.Success);
    var field = r.Captures.Single(c => c.Name == "event.sourceUserName");
    Check(text.Substring(field.Index, field.Length) == "Žan");
});
Test("Batch: invalid token references and pattern syntax", () =>
{
    var d = new PropertiesDocument(SubSource());
    d.Set("submessage.token", "MISSING");
    Throws<FormatException>(() => new ParserEvaluator(d.Source));
    d.Set("submessage.token", "Body");
    d.Set("submessage[0].pattern[0].regex", "(");
    Throws<FormatException>(() => new ParserEvaluator(d.Source));
});
Test("Batch: no submessages succeeds; summary avoids retaining captures", () =>
{
    var d = new PropertiesDocument(SubSource());
    d.Set("submessage.count", "0");
    var r = new ParserEvaluator(d.Source).Evaluate("id=A body=hello", details: false);
    Check(r.Success && r.Captures.Count == 0 && r.Attempts.Count == 0);
});
Test("Batch: timeout result and cancellation", () =>
{
    var d = new PropertiesDocument("regex=(a+)+$\ntoken.count=1\ntoken[0].name=A");
    var evaluator = new ParserEvaluator(d.Source, 10);
    Check(evaluator.Evaluate(new string('a', 10000) + "!").State == ParseState.Error);
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    Throws<OperationCanceledException>(() => evaluator.Evaluate("a", cancellation: cts.Token));
});
Test("Log file: blank lines, CRLF and original numbering", () =>
{
    string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log");
    try
    {
        File.WriteAllText(file, "first\r\n\r\nčeský log\n");
        var entries = LogFile.Read(file);
        Check(entries.Count == 3 && entries[1].LineNumber == 2 && entries[1].Text == "" && entries[2].Text == "český log");
    }
    finally { if (File.Exists(file)) File.Delete(file); }
});
Test("Tokens: capture groups only, names and count", () =>
{
    var draft = TokenGenerator.Draft("", @"(\d+)(?:-)(?<Kind>[A-Z]+)");
    Check(draft.Count == 2 && draft[0].Name == "Token1" && draft[1].Name == "Kind");
    var doc = new PropertiesDocument(TokenGenerator.Apply("# preserved\ncustom.option=keep", @"(\d+)(?:-)(?<Kind>[A-Z]+)", draft));
    Check(doc.Get("token.count") == "2" && doc.Get("token[1].type") == "String");
    Check(doc.Get("regex") == @"(\d+)(?:-)(?<Kind>[A-Z]+)" && doc.Get("custom.option") == "keep");
});
Test("Tokens: preserve metadata and remove obsolete declarations", () =>
{
    const string source = "token.count=2\ntoken[0].name=Date\ntoken[0].type=TimeStamp\ntoken[0].format=yyyy-MM-dd\ntoken[0].custom=keep\ntoken[1].name=Old\ntoken[1].type=String\nevent.name=Old\n";
    var draft = TokenGenerator.Draft(source, "(.*)");
    Check(draft.Single().Name == "Date" && draft.Single().Format == "yyyy-MM-dd");
    var doc = new PropertiesDocument(TokenGenerator.Apply(source, "(.*)", draft));
    Check(doc.Get("token[0].type") == "TimeStamp" && doc.Get("token[0].custom") == "keep");
    Check(!doc.Values.Keys.Any(k => k.StartsWith("token[1]", StringComparison.Ordinal)));
    Check(doc.Get("event.name") == "Old", "Existing mappings must not be silently rewritten");
});
Test("Tokens: collisions, zero captures, invalid regex and duplicate names", () =>
{
    var draft = TokenGenerator.Draft("token[1].name=Token1", "(a)(b)");
    Check(draft[0].Name == "Token1_2" && draft[1].Name == "Token1");
    Check(TokenGenerator.Draft("", "(?:abc)").Count == 0);
    Check(new PropertiesDocument(TokenGenerator.Apply("token[0].name=Old", "abc", [])).Get("token.count") == "0");
    Throws<RegexParseException>(() => TokenGenerator.Draft("", "("));
    Throws<FormatException>(() => TokenGenerator.Draft("", "(?<12>a)"));
    Throws<FormatException>(() => TokenGenerator.Apply("", "(a)(b)", [new(0, "Same", "String", ""), new(1, "Same", "String", "")]));
});
Test("Properties: insert and replace without leading whitespace or accumulating blank lines", () =>
{
    var doc = new PropertiesDocument("");
    doc.Set("regex", "(.*)");
    Check(doc.Source == "regex=(.*)\r\n");
    doc.Set("token.count", "1");
    doc.Set("regex", "(a)");
    string expected = "regex=(a)\r\ntoken.count=1\r\n";
    Check(doc.Source == expected);
    doc = new PropertiesDocument(doc.Source);
    doc.Set("regex", "(a)");
    doc.Set("token.count", "1");
    Check(doc.Source == expected, "Repeated edits must be idempotent");
});
Test("Properties: append preserves unterminated last line, comments and continuations", () =>
{
    foreach (string source in new[] { "x=a", "x=a\n", "x=a\\", "x=a\\\n", "# comment\\" })
    {
        var doc = new PropertiesDocument(source);
        doc.Set("regex", "(.*)");
        Check(new PropertiesDocument(doc.Source).Get("regex") == "(.*)");
        Check(!char.IsWhiteSpace(doc.Source[0]));
    }
    var preserved = new PropertiesDocument("# header\n\nx=one\nx=two\nother=keep\n");
    preserved.Set("x", "new");
    Check(preserved.Source.StartsWith("# header\n\nx=new", StringComparison.Ordinal));
    Check(!preserved.Source.Contains("x=two", StringComparison.Ordinal));
});
Test("Generated tokens and advanced config do not introduce leading blanks", () =>
{
    var tokens = TokenGenerator.Draft("", "(a)(b)");
    string source = TokenGenerator.Apply("", "(a)(b)", tokens);
    Check(source.StartsWith("regex=", StringComparison.Ordinal) && !source.Contains("\r\n\r\n", StringComparison.Ordinal));
    Check(TokenGenerator.Apply(source, "(a)(b)", tokens) == source);
    const string unfinished = "token.count=1\ntoken[0].name=Message";
    source = TokenGenerator.Apply(unfinished, "(a)(b)", TokenGenerator.Draft(unfinished, "(a)(b)"));
    Check(!source.Contains("\r\n\r\n", StringComparison.Ordinal), "Replacing the formerly last line must not duplicate its separator");
    source = AdvancedConfiguration.Apply("", "Severity", Rules("400..599"), "");
    Check(!char.IsWhiteSpace(source[0]) && !source.Contains("\r\n\r\n", StringComparison.Ordinal));
});
Test("Submessage editor: round trip preserves custom metadata and unrelated properties", () =>
{
    string source = SubSource() + "custom=keep\nsubmessage[0].custom=keep-sub\n" +
        "submessage[0].pattern[0].types=String\nsubmessage[0].pattern[0].formats=null\n" +
        "submessage[0].pattern[0].mappings=$1\nsubmessage[0].pattern[0].custom=keep-pattern\n";
    var model = SubmessageConfiguration.Load(source);
    string updated = model.Apply(source);
    var before = new PropertiesDocument(source);
    var after = new PropertiesDocument(updated);
    Check(before.Values.All(kv => after.Get(kv.Key) == kv.Value));
    Check(new ParserEvaluator(updated).Evaluate("id=A body=user=alice").Success);
});
Test("Submessage editor: add alternative fixes unmatched log and reports used indices", () =>
{
    var model = SubmessageConfiguration.Load(SubSource());
    model.Messages[0].Patterns.Add(new()
    {
        ["regex"] = "other=(.*)",
        ["fields"] = "event.message"
    });
    var parser = new ParserEvaluator(model.Apply(SubSource()));
    var r = parser.Evaluate("id=A body=other=hello");
    Check(r.Success && r.UsedSubmessage == 0 && r.UsedPattern == 2 && r.Attempts.Count == 3);
    Check(parser.Evaluate("id=A body=user=alice").UsedPattern == 0);
    Check(parser.Evaluate("id=A body=anonymous").UsedPattern == 1);
});
Test("Submessage editor: reordering changes first winner", () =>
{
    var model = SubmessageConfiguration.Load(SubSource());
    model.Messages[0].Patterns.Insert(0, SubmessageConfiguration.NewPattern());
    var first = new ParserEvaluator(model.Apply(SubSource())).Evaluate("id=A body=user=alice");
    Check(first.UsedPattern == 0 && first.Captures.Any(c => c.Name == "event.message"));
    var catchAll = model.Messages[0].Patterns[0];
    model.Messages[0].Patterns.RemoveAt(0);
    model.Messages[0].Patterns.Add(catchAll);
    var reordered = new ParserEvaluator(model.Apply(SubSource())).Evaluate("id=A body=user=alice");
    Check(reordered.Captures.Any(c => c.Name == "event.sourceUserName"));
});
Test("Submessage editor: removal reindexes default and removes stale patterns", () =>
{
    var model = SubmessageConfiguration.Load(SubSource());
    model.Messages.RemoveAt(0);
    string result = model.Apply(SubSource());
    var doc = new PropertiesDocument(result);
    Check(doc.Get("submessage.count") == "1" && !doc.Values.Keys.Any(k => k.StartsWith("submessage[1]", StringComparison.Ordinal)));
    Check(!doc.Values.ContainsKey("submessage[0].messageid"));
    Check(new ParserEvaluator(result).Evaluate("id=A body=anything").Route == "submessage[0].pattern[0] (default)");
});
Test("Submessage editor: multiple definitions with same ID and zero definitions", () =>
{
    var model = SubmessageConfiguration.Load(SubSource());
    var another = new SubmessageDefinition { MessageId = "A" };
    another.Patterns.Add(new()
    {
        ["regex"] = "third"
    });
    model.Messages.Add(another);
    var r = new ParserEvaluator(model.Apply(SubSource())).Evaluate("id=A body=third");
    Check(r.Success && r.UsedSubmessage == 2 && r.UsedPattern == 0);
    model.Messages.Clear();
    Check(new PropertiesDocument(model.Apply(SubSource())).Get("submessage.count") == "0");
});
Test("Submessage editor: invalid reference, empty pattern list and syntax rejected", () =>
{
    var model = SubmessageConfiguration.Load(SubSource());
    model.BodyToken = "Unknown";
    Throws<FormatException>(() => model.Apply(SubSource()));
    model.BodyToken = "Body";
    model.Messages[0].Patterns[0]["regex"] = "(";
    Throws<FormatException>(() => model.Apply(SubSource()));
    model.Messages[0].Patterns.Clear();
    Throws<FormatException>(() => model.Apply(SubSource()));
});
Test("Regex group map: nested groups, boundaries and noncapturing wrappers", () =>
{
    string pattern = @"prefix((ab)(?:cd))suffix";
    var map = RegexGroupMap.Build(pattern);
    Check(map.Count == 2);
    Check(RegexGroupMap.At(map, pattern.IndexOf("ab", StringComparison.Ordinal))?.Number == 2);
    Check(RegexGroupMap.At(map, pattern.IndexOf("cd", StringComparison.Ordinal))?.Number == 1);
    Check(RegexGroupMap.At(map, 6)?.Number == 1);
    Check(RegexGroupMap.At(map, 0) is null && RegexGroupMap.At(map, pattern.Length) is null);
});
Test("Regex group map: names use engine numbering, duplicates and explicit numbers", () =>
{
    string pattern = @"(?<Word>a)(b)(?<Word>c)(?<7>d)";
    var map = RegexGroupMap.Build(pattern);
    var regex = new Regex(pattern);
    Check(map.Count == 4);
    Check(map[0].Number == regex.GroupNumberFromName("Word") && map[2].Number == map[0].Number);
    Check(map[1].Number == 1 && map[3].Number == 7);
});
Test("Regex group map: escaped brackets, classes, subtraction, comments and options", () =>
{
    string pattern = @"(?x)(?<A>a) # (ignored)
[(](b)[a-z-[()]](?# ignored ()(?n:(c)(?<D>d))(?-n:(e))";
    var map = RegexGroupMap.Build(pattern);
    Check(map.Count == 4, "Only four capturing groups expected");
    Check(RegexGroupMap.At(map, pattern.IndexOf("(c)", StringComparison.Ordinal) + 1) is null);
    Check(RegexGroupMap.At(map, pattern.IndexOf("(e)", StringComparison.Ordinal) + 1)?.Number == 2);
    Check(RegexGroupMap.Build(@"\((a)\)[()]([]])").Count == 2);
    Check(RegexGroupMap.Build(@"[^^](a)[a-z-[^^]](b)").Count == 2);
    Check(RegexGroupMap.Build(@"[\-[(](a)").Count == 1);
});
Test("Regex group map: lookarounds, invalid and unsupported constructs", () =>
{
    Check(RegexGroupMap.Build(@"(?<=(a))(?=(b))(?:c)(?>d)").Count == 2);
    Check(RegexGroupMap.Build("(").Count == 0);
    Check(RegexGroupMap.Build(@"(a)?(?(1)b|c)").Count == 0);
    Check(RegexGroupMap.Build(@"(?<A>a)(?<-A>b)").Count == 0);
});
Test("Token formats: documented suggestions and properties round trip", () =>
{
    Check(TokenFormats.Types.Contains("IPv6Address") && TokenFormats.Types.Contains("Double"));
    Check(TokenFormats.Suggestions("Date")[0] == "MM/dd/yyyy");
    Check(TokenFormats.Suggestions("Time")[0] == "HH:mm:ss");
    foreach (string type in new[] { "Date", "Time", "TimeStamp", "RegexToken" })
        foreach (string format in TokenFormats.Suggestions(type))
        {
            var updated = TokenGenerator.Apply("", "(.*)", [new(0, "Value", type, format)]);
            Check(new PropertiesDocument(updated).Get("token[0].format") == format);
        }
    Check(TokenFormats.Suggestions("Integer").Count == 0 && TokenFormats.Suggestions("CustomType").Count == 0);
    string custom = "token.count=1\ntoken[0].name=Value\ntoken[0].type=CustomType\ntoken[0].format=custom";
    Check(TokenGenerator.Draft(custom, "(.*)")[0].Format == "custom");
});
Test("Event mapping: update, removal, duplicates and unrelated config preservation", () =>
{
    string source = "# Keep\nregex=(.*)\ntoken[0].name=Msg\ntoken[0].type=String\nevent.message=Old\nevent.message=Msg\nevent.name=Old\nsubmessage[0].pattern[0].mappings=$1\ncustom.option=yes\nevent.name.someOption=keep\n";
    Check(EventMapping.Load(source).Count == 2);
    string updated = EventMapping.Apply(source, [new("event.message", "Msg"), new("event.deviceProduct", EventMapping.Constant("Produkt č. 1"))]);
    var doc = new PropertiesDocument(updated);
    Check(doc.Get("event.message") == "Msg" && !doc.Values.ContainsKey("event.name"));
    Check(doc.Get("event.deviceProduct") == "__stringConstant(\"Produkt č. 1\")");
    Check(doc.Get("event.name.someOption") == "keep" && doc.Get("custom.option") == "yes" && doc.Get("submessage[0].pattern[0].mappings") == "$1");
    Check(updated.StartsWith("# Keep", StringComparison.Ordinal));
    Throws<FormatException>(() => EventMapping.Apply(source, [new("event.message", "Msg"), new("event.message", "Other")]));
    Throws<FormatException>(() => EventMapping.Apply(source, [new("token[0].name", "Msg")]));
    Throws<FormatException>(() => EventMapping.Apply(source, [new("event.message", "")]));
});
Test("Event mapping: diagnostics and custom expressions", () =>
{
    string source = "token[0].name=Port\ntoken[0].type=Integer\ntoken[1].name=Msg\ntoken[1].type=String";
    Check(EventMapping.Tokens(source)["Port"] == "Integer");
    var warnings = EventMapping.Warnings(source, [new("event.sourceAddress", "Port"), new("event.message", "Missing"), new("event.myCustomField", "__customOperation(Msg)")]);
    Check(warnings.Any(w => w.Contains("expects IPAddress", StringComparison.Ordinal)) && warnings.Any(w => w.Contains("Missing", StringComparison.Ordinal)));
    var valid = new EventAssignment[] { new("event.sourcePort", "Port"), new("event.name", EventMapping.Constant("Event")) };
    Check(EventMapping.Warnings(source, valid).Count == 0);
    string expression = "__customOperation(Msg,\"a,b|č\")";
    Check(new PropertiesDocument(EventMapping.Apply(source, [new("event.myCustomField", expression)])).Get("event.myCustomField") == expression);
    Check(EventMapping.Fields["event.deviceCustomNumber3"] == "Long" && EventMapping.Fields["event.destinationAddress"] == "IPAddress");
});
Test("Suggestion: typed key values, quoting and unseen events", () =>
{
    var proposal = ParserSuggester.Generate("user=alice src=10.0.0.1 dport=443 msg=\"hello world\"\nuser=bob src=10.0.0.2 dport=80 msg=\"bye world\"");
    var doc = new PropertiesDocument(proposal.Source);
    Check(doc.Get("token.count") == "4");
    Check(doc.Get("token[1].type") == "IPAddress" && doc.Get("token[2].type") == "Integer");
    Check(new ParserEvaluator(proposal.Source).Evaluate("user=carol src=10.0.0.3 dport=8080 msg=\"new text\"").Success);
    Check(!new ParserEvaluator(proposal.Source).Evaluate("unrelated line").Success);
    Check(!char.IsWhiteSpace(proposal.Source[0]));
});
Test("Suggestion: alternative submessages and coverage", () =>
{
    var proposal = ParserSuggester.Generate("LOGIN user=alice result=ok\nLOGIN user=bob result=failed\nCONNECT host=server dport=443\nCONNECT host=proxy dport=80");
    var parser = new ParserEvaluator(proposal.Source);
    Check(new PropertiesDocument(proposal.Source).Get("submessage[0].pattern.count") == "2");
    Check(parser.Evaluate("LOGIN user=carol result=ok").UsedPattern == 0);
    Check(parser.Evaluate("CONNECT host=other dport=53").UsedPattern == 1);
    Check(!parser.Evaluate("DELETE everything").Success);
});
Test("Suggestion: constant logs, escaping and rejected samples", () =>
{
    var proposal = ParserSuggester.Generate("[ready] server.up\n[ready] server.up");
    Check(new ParserEvaluator(proposal.Source).Evaluate("[ready] server.up").Success);
    Check(!new ParserEvaluator(proposal.Source).Evaluate("ready serverXup").Success);
    Throws<FormatException>(() => ParserSuggester.Generate("only one"));
    Throws<FormatException>(() => ParserSuggester.Generate(new string('x', 250001)));
    Throws<FormatException>(() => ParserSuggester.Generate("msg=\"unclosed\nmsg=\"also unclosed"));
});
Test("Elements: literals, quantifiers, anchors and trim offsets", () =>
{
    var result = RegexElementDebugger.Analyze(@"^id=(\d+)$", "  id=123\t", full: true, trim: true);
    Check(result.Elements.Single(e => e.Expression == "i").Captures.Single() == new TextSpan(2, 1));
    Check(result.Elements.Single(e => e.Expression == @"\d+").Captures.Single() == new TextSpan(5, 3));
    Check(result.Elements.Single(e => e.Expression == "$").Captures.Single() == new TextSpan(8, 0));
});
Test("Elements: alternatives, lookahead and repeated groups", () =>
{
    var result = RegexElementDebugger.Analyze(@"(?=ab)(?:(a)b)+|xyz", "abab", full: true);
    Check(result.Elements.Single(e => e.Expression == "a" && e.Start > 5).Captures.Count == 2);
    Check(result.Elements.Single(e => e.Expression == "x").Captures.Count == 0);
    Check(result.Elements.Single(e => e.Expression == "(?=ab)").Captures.Single().Length == 0);
});
Test("Elements: classes, escaped delimiters and lazy quantifiers", () =>
{
    var result = RegexElementDebugger.Analyze(@"\[[a-z]{2,4}?\]\s+\p{L}+", "[ab] čaj", full: true);
    Check(result.Elements.Single(e => e.Expression == "[a-z]{2,4}?").Captures.Single() == new TextSpan(1, 2));
    Check(result.Elements.Single(e => e.Expression == @"\p{L}+").Captures.Single() == new TextSpan(5, 3));
});
Test("Elements: unsupported constructs and failures are explicit", () =>
{
    Check(RegexElementDebugger.Analyze(@"(a)\1", "aa").Elements.Count == 0);
    Check(RegexElementDebugger.Analyze("(?x)a b", "ab").Elements.Count == 0);
    Check(RegexElementDebugger.Analyze("(", "a").Elements.Count == 0);
    var mismatch = RegexElementDebugger.Analyze("abc", "abd", full: true);
    Check(mismatch.IsPartial && mismatch.PartialSpan == new TextSpan(0, 2) && mismatch.Elements.Single(e => e.Expression == "c").Captures.Count == 0);
});
Test("Suggestion: severity conventions and cancellation", () =>
{
    var proposal = ParserSuggester.Generate("severity=ERROR user=alice\nseverity=INFO user=bob");
    var doc = new PropertiesDocument(proposal.Source);
    Check(doc.Get("severity.map.high.if.deviceSeverity") == "ERROR");
    Check(doc.Get("severity.map.low.if.deviceSeverity") == "INFO");
    Check(doc.Get("token[0].name") == "Severity");
    var numeric = ParserSuggester.Generate("severity=1\nseverity=2");
    Check(!new PropertiesDocument(numeric.Source).Values.Keys.Any(k => k.StartsWith("severity.map.")));
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    Throws<OperationCanceledException>(() => ParserSuggester.Generate("a\nb", cancelled.Token));
});
Test("Suggestion: positional variables and custom-field overflow", () =>
{
    var proposal = ParserSuggester.Generate("LOGIN alice 123\nLOGIN bob 456");
    Check(new ParserEvaluator(proposal.Source).Evaluate("LOGIN carol 789").Success);
    var overflow = ParserSuggester.Generate("a=1 b=2 c=3 d=4 e=5 f=6 g=7\na=2 b=3 c=4 d=5 e=6 f=7 g=8");
    Check(new PropertiesDocument(overflow.Source).Get("token.count") == "6");
    Check(overflow.Report.Contains("without capture"));
});
Test("Submessage debug: offsets include main and body trimming", () =>
{
    string source = "regex=id=(\\\\w+) body=(.*)\ntoken.count=2\ntoken[0].name=ID\ntoken[1].name=Body\ntrim.message=true\ntrim.submessage=true\nsubmessage.token=Body\nsubmessage.messageid.token=ID\nsubmessage.count=1\nsubmessage[0].messageid=LOGIN\nsubmessage[0].pattern.count=1\nsubmessage[0].pattern[0].regex=user=(\\\\w+)\n";
    const string input = "  id=LOGIN body=  user=alice  ";
    var debug = SubmessageDebugger.Analyze(source, input, 0, 0);
    Check(debug.Route.Success);
    Check(debug.Captures!.Rows.Single(r => r.Group == 1).Index == input.IndexOf("alice", StringComparison.Ordinal));
    Check(debug.Elements!.Elements.First(e => e.Expression == "u").Captures.Single().Index == input.IndexOf("user", StringComparison.Ordinal));
});
Test("Submessage debug: selected alternative is distinct from actual routing", () =>
{
    var doc = new PropertiesDocument(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples", "submessages.sdkrfilereader.properties")));
    doc.Set("submessage[0].pattern[1].regex", @"user=(\w+) result=(\w+)");
    var debug = SubmessageDebugger.Analyze(doc.Source, "id=LOGIN body=user=alice result=ok", 0, 1);
    Check(debug.Route.UsedPattern == 0 && debug.Captures!.Matches.Count == 1);
    Check(debug.Status.Contains("NOT used"));
    var failed = SubmessageDebugger.Analyze(doc.Source, "unrelated", 0, 1);
    Check(failed.Captures is null && failed.Elements is null);
});
Test("Submessage debug: body is not truncated and cancellation propagates", () =>
{
    const string source = "regex=(.*)\ntoken.count=1\ntoken[0].name=Body\nsubmessage.token=Body\nsubmessage.count=1\nsubmessage[0].pattern.count=1\nsubmessage[0].pattern[0].regex=(.*)END\n";
    string input = new string('x', 3000) + "END";
    var debug = SubmessageDebugger.Analyze(source, input, 0, 0);
    Check(debug.Captures!.Matches.Single().Length == 3003);
    Check(debug.Elements!.Elements.Single(e => e.Expression == "E").Captures.Single().Index == 3000);
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    Throws<OperationCanceledException>(() => SubmessageDebugger.Analyze(source, input, 0, 0, cancellation.Token));
});
Test("Layout: new tokens stay with tokens and preserve surrounding comments", () =>
{
    const string source = "# Header\nregex=(.*)\n# Tokens\ntoken.count=1\ntoken[0].name: Existing\ntoken[0].type=String\n\n# Event mappings\nevent.message=Existing\n# Custom tail\ncustom.option: untouched\n";
    var draft = TokenGenerator.Draft(source, "(.*)(.*)");
    string output = TokenGenerator.Apply(source, "(.*)(.*)", draft);
    Check(output.IndexOf("token[1].type", StringComparison.Ordinal) < output.IndexOf("# Event mappings", StringComparison.Ordinal));
    Check(output.Contains("token[0].name: Existing\n") && output.EndsWith("# Custom tail\ncustom.option: untouched\n", StringComparison.Ordinal));
    Check(!output.Contains('\r'));
    Check(TokenGenerator.Apply(output, "(.*)(.*)", TokenGenerator.Draft(output, "(.*)(.*)")) == output);
});
Test("Layout: nested fields and indices use natural numeric order", () =>
{
    var doc = new PropertiesDocument("token.count=11\ntoken[2].name=Two\ntoken[2].type=String\n# Ten\ntoken[10].name=Ten\ntoken[10].type=String\nevent.name=Name\n");
    doc.Set("token[2].format", "format");
    doc.Set("token[9].name", "Nine");
    doc.Set("token[9].type", "String");
    Check(doc.Source.IndexOf("token[2].format", StringComparison.Ordinal) < doc.Source.IndexOf("token[9].name", StringComparison.Ordinal));
    Check(doc.Source.IndexOf("token[9].type", StringComparison.Ordinal) < doc.Source.IndexOf("# Ten", StringComparison.Ordinal));
    var sub = new PropertiesDocument("submessage[0].pattern[2].regex=a\nsubmessage[0].pattern[10].regex=b\nevent.name=Name\n");
    sub.Set("submessage[0].pattern[2].fields", "event.message");
    sub.Set("submessage[0].pattern[9].regex", "c");
    Check(sub.Source.IndexOf("pattern[2].fields", StringComparison.Ordinal) < sub.Source.IndexOf("pattern[9]", StringComparison.Ordinal));
    Check(sub.Source.IndexOf("pattern[9]", StringComparison.Ordinal) < sub.Source.IndexOf("pattern[10]", StringComparison.Ordinal));
});
Test("Layout: missing categories fit between existing sections", () =>
{
    var doc = new PropertiesDocument("# Banner\nregex=(.*)\n# Event mapping\nevent.message=Body\n# Processors\nextraprocessor.count=0\ncustom.tail=keep\n");
    doc.Set("token.count", "1");
    doc.Set("token[0].name", "Body");
    doc.Set("submessage.count", "0");
    doc.Set("severity.map.high.if.deviceSeverity", "ERROR");
    Check(doc.Source.StartsWith("# Banner\nregex=(.*)\n"));
    Check(doc.Source.IndexOf("token[0]", StringComparison.Ordinal) < doc.Source.IndexOf("submessage.count", StringComparison.Ordinal));
    Check(doc.Source.IndexOf("submessage.count", StringComparison.Ordinal) < doc.Source.IndexOf("# Event mapping", StringComparison.Ordinal));
    Check(doc.Source.IndexOf("severity.map", StringComparison.Ordinal) < doc.Source.IndexOf("# Processors", StringComparison.Ordinal));
    Check(doc.Source.EndsWith("custom.tail=keep\n"));
});
Test("Layout: event mapping and severity additions remain in their categories", () =>
{
    string source = "regex=(.*)\ntoken.count=1\ntoken[0].name=Body\nevent.message=Body\n# Severity\nseverity.map.high.if.deviceSeverity=ERROR\n# Tail\ncustom=keep\n";
    string mapped = EventMapping.Apply(source, [new("event.message", "Body"), new("event.name", EventMapping.Constant("Test"))]);
    Check(mapped.IndexOf("event.name", StringComparison.Ordinal) < mapped.IndexOf("# Severity", StringComparison.Ordinal));
    var doc = new PropertiesDocument(mapped);
    doc.Set("severity.map.low.if.deviceSeverity", "INFO");
    Check(doc.Source.IndexOf("severity.map.low", StringComparison.Ordinal) < doc.Source.IndexOf("# Tail", StringComparison.Ordinal));
});
Test("Layout: submessage updates preserve position and comments across repeated apply", () =>
{
    string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples", "submessages.sdkrfilereader.properties"));
    source = source.Replace("submessage[0].pattern[0].regex=", "# Keep pattern note\nsubmessage[0].pattern[0].regex=", StringComparison.Ordinal)
        + "\n# End of parser\ncustom.tail=keep\n";
    var model = SubmessageConfiguration.Load(source);
    model.Messages[0].Patterns.Add(new()
    {
        ["regex"] = "(new)",
        ["fields"] = "event.message"
    });
    string updated = model.Apply(source);
    Check(updated.IndexOf("submessage[0].pattern[2]", StringComparison.Ordinal) < updated.IndexOf("submessage[1].messageid", StringComparison.Ordinal));
    Check(updated.Contains("# Keep pattern note\nsubmessage[0].pattern[0].regex="));
    Check(updated.EndsWith("# End of parser\ncustom.tail=keep\n"));
    Check(SubmessageConfiguration.Load(updated).Apply(updated) == updated);
});
Test("Layout: extra processor merge stays in place and does not duplicate comments", () =>
{
    const string source = "# Header\nregex=(.*)\n# Processors\nextraprocessor.count=1\nextraprocessor[0].type: regex\nextraprocessor[0].filename=old\n# Other category\ncustom.tail=keep\n";
    var edited = new PropertiesDocument(source);
    edited.RemoveWhere(k => !AdvancedConfiguration.IsProcessorKey(k));
    edited.Set("extraprocessor[0].filename", "new");
    string processors = edited.Source + "# New processor\nextraprocessor[1].type=map\nextraprocessor[1].filename=map.csv\n# Ending A\n# Ending B\n";
    string output = AdvancedConfiguration.Apply(source, "", Rules(), processors);
    Check(output.Contains("extraprocessor[0].type: regex\n"));
    Check(output.IndexOf("extraprocessor[1].filename", StringComparison.Ordinal) < output.IndexOf("# Other category", StringComparison.Ordinal));
    Check(output.IndexOf("# Ending A", StringComparison.Ordinal) < output.IndexOf("# Ending B", StringComparison.Ordinal));
    Check(output.Split("# Header").Length == 2 && output.Split("# New processor").Length == 2);
    Check(AdvancedConfiguration.Apply(output, "", Rules(), processors) == output);
    Check(new PropertiesDocument(output).Get("extraprocessor.count") == "2");
});
Test("Layout: unfamiliar keys stay untouched and a missing regex respects file banner", () =>
{
    var doc = new PropertiesDocument("# Banner\ntoken.count=0\ncustom.token[0]=private\n");
    doc.Set("regex", "literal");
    Check(doc.Source.StartsWith("# Banner\nregex=literal\ntoken.count=0\n"));
    doc.Set("private.foo", "new");
    Check(doc.Source.EndsWith("custom.token[0]=private\nprivate.foo=new\n"));
    var cr = new PropertiesDocument("token.count=0\revent.name=Name\r");
    cr.Set("token[0].name", "Token");
    Check(!cr.Source.Contains('\n'));
});
Test("Partial debug: nested groups expose completed atoms without inventing full captures", () =>
{
    var result = RegexElementDebugger.Analyze(@"^(abc\d+)$", "abX", full: true);
    Check(result.IsPartial && result.PartialSpan == new TextSpan(0, 2));
    Check(result.Elements.Single(e => e.Expression == "b").Captures.Single() == new TextSpan(1, 1));
    Check(result.Elements.Single(e => e.Expression == @"(abc\d+)").Captures.Count == 0);
});
Test("Partial debug: progress beats a greedy prefix and supports alternatives", () =>
{
    var result = RegexElementDebugger.Analyze(@".*abZ", "xxabY", full: true);
    Check(result.IsPartial && result.PartialSpan == new TextSpan(0, 4));
    Check(result.Elements.Single(e => e.Expression == "b").Captures.Single() == new TextSpan(3, 1));
    var alternate = RegexElementDebugger.Analyze(@"(?:abc|abd)Z", "abdX", full: true);
    Check(alternate.IsPartial && alternate.PartialSpan == new TextSpan(0, 3));
    Check(alternate.Elements.Single(e => e.Expression == "d").Captures.Single() == new TextSpan(2, 1));
});
Test("Partial debug: trailing input differs from an ordinary substring match", () =>
{
    var strict = RegexElementDebugger.Analyze("abc", "abcXYZ", full: true);
    Check(strict.IsPartial && strict.RegexBoundary == 3 && strict.PartialSpan == new TextSpan(0, 3));
    Check(!RegexElementDebugger.Analyze("abc", "abcXYZ").IsPartial);
    var search = RegexElementDebugger.Analyze("abcZ", "ax abcY");
    Check(search.IsPartial && search.PartialSpan == new TextSpan(3, 3));
    Check(!RegexElementDebugger.Analyze("abcZ", "xxabcY", full: true).IsPartial);
});
Test("Partial debug: quantifier bounds and assertions are not weakened", () =>
{
    Check(!RegexElementDebugger.Analyze(@"(ab){2}Z", "abX", full: true).IsPartial);
    Check(!RegexElementDebugger.Analyze(@"(?=SECRET)abc", "SECX", full: true).IsPartial);
    Check(!RegexElementDebugger.Analyze(@"\d{4}Z", "12X", full: true).IsPartial);
    Check(!RegexElementDebugger.Analyze("abc", "", full: true).IsPartial);
    Check(!RegexElementDebugger.Analyze(@"(a)\1Z", "aaX", full: true).IsPartial);
});
Test("Partial debug: trim and Unicode offsets are preserved", () =>
{
    var result = RegexElementDebugger.Analyze("😀ABZ", "  😀ABX  ", full: true, trim: true);
    Check(result.IsPartial && result.PartialSpan == new TextSpan(2, 4));
    Check(result.Elements.Single(e => e.Expression == "B").Captures.Single() == new TextSpan(5, 1));
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    Throws<OperationCanceledException>(() => RegexElementDebugger.Analyze("abc", "abX", cancellation: cancellation.Token));
});
Test("Partial submessage preview retains failed routing and full-log positions", () =>
{
    const string source = "regex=id=(\\\\w+) body=(.*)\ntoken.count=2\ntoken[0].name=ID\ntoken[1].name=Body\nsubmessage.token=Body\ntrim.submessage=true\nsubmessage.count=1\nsubmessage[0].pattern.count=1\nsubmessage[0].pattern[0].regex=abcZ\n";
    const string input = "id=TEST body=  abcX ";
    var preview = SubmessageDebugger.Analyze(source, input, 0, 0);
    Check(preview.Route.State == ParseState.SubmessageMismatch && preview.Captures!.Matches.Count == 0);
    Check(preview.Elements!.IsPartial && preview.Elements.PartialSpan == new TextSpan(input.IndexOf("abc", StringComparison.Ordinal), 3));
    Check(preview.Status.Contains("PARTIAL"));
});
Console.WriteLine($"{passed} tests passed; exit code {Environment.ExitCode}.");
