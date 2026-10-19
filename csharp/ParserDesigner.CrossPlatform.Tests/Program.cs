using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaEdit;
using ParserCore;
using ParserDesigner.CrossPlatform;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => AppBuilder.Configure<TestApp>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
}

internal sealed class TestApp : DesignerApp
{
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var main = (MainWindow)lifetime.MainWindow!;
        main.Opened += async (_, _) =>
        {
            try
            {
                await Checks.Run(main);
                Console.WriteLine("PASS native cross-platform UI regression checks on " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
                lifetime.Shutdown(0);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); lifetime.Shutdown(1); }
        };
    }
}

internal static class Checks
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    private static IEnumerable<T> Controls<T>(Control owner) => owner.GetLogicalDescendants().OfType<T>();
    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
    private static void Click(Control owner, string label)
    {
        var button = Controls<Button>(owner).Single(b => b.Content?.ToString() == label);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 200; i++)
        {
            if (condition())
                return;
            await Task.Delay(25);
        }
        throw new TimeoutException("UI condition did not become ready.");
    }
    private static async Task Capture(Window window, string name)
    {
        await Task.Delay(150);
        string output = Environment.GetEnvironmentVariable("PARSER_SCREENSHOTS") ?? Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(output);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
    }
    public static async Task Run(MainWindow main)
    {
        await main.AnalyzeAsync();
        var painter = Field<CaptureHighlighter>(main, "painter");
        Check(painter.Spans.Count == 3, "entire match and tokens have independent highlight spans");
        main.Pattern.CaretOffset = main.Pattern.Text.IndexOf("\\d", StringComparison.Ordinal);
        Check(painter.Spans.Any(s => s.Selected && s.Index == 7 && s.Length == 3), "regex caret selects the corresponding token text");
        Click(main, "Element ▶");
        Check(painter.Spans.Any(s => s.Selected && s.Index == 0 && s.Length == 1), "regex element stepping highlights a literal outside capture groups");
        Field<TabControl>(main, "tabs").SelectedIndex = 0;
        await Capture(main, "main");
        main.Width = main.MinWidth;
        main.Height = main.MinHeight;
        await Capture(main, "main-compact");
        string originalPattern = main.Pattern.Text;
        main.Pattern.Text = string.Concat(Enumerable.Repeat("([a-z]+) ", 80));
        await Task.Delay(200);
        Check(main.Pattern.TextArea.TextView.VisualLines.First().Height > 40, "long regex wraps into visible lines at compact width");
        Check(main.Pattern.TextArea.Bounds.Width < main.Bounds.Width, "editor viewport stays inside the compact window");
        await Capture(main, "long-regex");
        main.Pattern.Text = originalPattern;
        main.Width = 1380;
        main.Height = 980;

        string originalMessage = main.Message.Text;
        main.Pattern.Text = "abcZ";
        main.Message.Text = "abcX";
        await main.AnalyzeAsync();
        Check(Field<TextBlock>(main, "status").Text!.StartsWith("PARTIAL") && painter.Spans.Any(s => s.Index == 0 && s.Length == 3), "main debugger highlights a partial prefix while reporting full-match failure");
        await Capture(main, "partial-debug");
        Click(main, "Element ▶");
        Check(Field<TextBlock>(main, "status").Text!.StartsWith("PARTIAL") && painter.Spans.Any(s => s.Selected && s.Length == 1), "partial element stepping remains explicitly labeled");
        main.Message.Text = originalMessage;
        Field<TabControl>(main, "tabs").SelectedIndex = 0;

        main.Pattern.Text = "404";
        await main.AnalyzeAsync();
        Check(painter.Spans.Any(s => s.Index == 7 && s.Length == 3), "regex without groups still highlights matching text");
        main.Pattern.Text = "(";
        await main.AnalyzeAsync();
        Check(painter.Spans.Count == 0, "invalid regex clears stale highlights");
        main.Pattern.Text = @"status=(\d+) message=(.*)";
        await main.AnalyzeAsync();

        var history = Ui.Editor("initial 😀");
        for (int i = 0; i < 105; i++)
        {
            history.Document.Insert(history.Document.TextLength, "x");
        }
        for (int i = 0; i < 100; i++)
            history.Document.UndoStack.Undo();
        Check(history.Text == "initial 😀xxxxx", "editor retains exactly 100 undo operations");
        for (int i = 0; i < 100; i++)
            history.Document.UndoStack.Redo();
        Check(history.Text.EndsWith(new string('x', 105), StringComparison.Ordinal), "editor redo restores text");
        Ui.Replace(history, "generated");
        history.Document.UndoStack.Undo();
        Check(history.Text.StartsWith("initial", StringComparison.Ordinal), "generated edits are one undo unit");

        var help = new HelpDialog("submessages");
        var helpTask = help.ShowDialog(main);
        var search = Controls<TextBox>(help).Single();
        search.Text = "overrideeventmappings";
        await Until(() => Controls<ListBox>(help).Single().ItemCount == 1);
        Check(Controls<ListBox>(help).Single().ItemCount == 1, "offline help searches topic content");
        search.Text = "not-found-123";
        await Until(() => Controls<ListBox>(help).Single().ItemCount == 0);
        Check(Controls<TextEditor>(help).Single().Text.StartsWith("No topics", StringComparison.Ordinal), "empty help search explains the result");
        help.Close();
        await helpTask;

        var tokens = new TokensDialog(MainWindow.Example, @"status=(\d+) message=(.*)", "status=404 message=Resource missing", 0, false, false, 250);
        var tokensTask = tokens.ShowDialog<string?>(main);
        await Until(() => Controls<TextBlock>(tokens).Any(t => t.Text == "404"));
        Check(Controls<TextBlock>(tokens).Any(t => t.Text == "Resource missing"), "token snippets show actual captured text");
        var type = Controls<ComboBox>(tokens).First();
        type.SelectedItem = "TimeStamp";
        Check(Controls<TextBox>(tokens).Any(t => t.Text == "yyyy-MM-dd HH:mm:ss"), "token type suggests a date/time format");
        await Capture(tokens, "tokens");
        var nameBox = Controls<TextBox>(tokens).First();
        var nameOrigin = nameBox.TranslatePoint(new Point(0, nameBox.Bounds.Height / 2), tokens)!.Value;
        var typeOrigin = type.TranslatePoint(new Point(0, type.Bounds.Height / 2), tokens)!.Value;
        Check(Math.Abs(nameOrigin.Y - typeOrigin.Y) < 2, "token name and type share the same vertical center");
        tokens.Width = tokens.MinWidth;
        await Capture(tokens, "tokens-compact");
        Check(Controls<TextBox>(tokens).All(t => t.Bounds.Width > 40), "token inputs remain usable at minimum window width");
        Click(tokens, "Apply to configuration");
        string generated = (await tokensTask)!;
        Check(!char.IsWhiteSpace(generated[0]) && new PropertiesDocument(generated).Get("token[0].type") == "TimeStamp", "token export preserves configuration without leading whitespace");

        var mapping = new MappingDialog(MainWindow.Example, "status=404 message=Resource missing");
        var mappingTask = mapping.ShowDialog<string?>(main);
        await Until(() => Controls<TextBlock>(mapping).Any(t => t.Text == "Resource missing"));
        Check(Controls<TextBlock>(mapping).Any(t => t.Text == "404"), "event mapping shows token snippets from the sample message");
        var mappedExpression = Controls<TextBox>(mapping).Single(t => t.Text == "Message");
        mappedExpression.Text = EventMapping.Constant("constant preview");
        await Until(() => Controls<TextBlock>(mapping).Any(t => t.Text == "Constant: constant preview"));
        mappedExpression.Text = "Message";
        await Capture(mapping, "mapping");
        Click(mapping, "Apply to configuration");
        Check(new PropertiesDocument((await mappingTask)!).Get("event.message") == "Message", "mapping dialog preserves direct assignments");
        var suggestion = new SuggestParserDialog("LOGIN user=alice result=ok\nLOGIN user=bob result=failed\nCONNECT host=server dport=443");
        var suggestionTask = suggestion.ShowDialog<string?>(main);
        Click(suggestion, "Generate and validate");
        await Until(() => Controls<Button>(suggestion).Single(b => b.Content?.ToString() == "Use proposed parser").IsEnabled);
        Field<TextEditor>(suggestion, "samples").Text += "\nLOGIN user=carol result=ok";
        Check(!Controls<Button>(suggestion).Single(b => b.Content?.ToString() == "Use proposed parser").IsEnabled, "changed samples invalidate a generated parser proposal");
        Click(suggestion, "Generate and validate");
        await Until(() => Controls<Button>(suggestion).Single(b => b.Content?.ToString() == "Use proposed parser").IsEnabled);
        await Capture(suggestion, "suggestion");
        Click(suggestion, "Use proposed parser");
        string proposed = (await suggestionTask)!;
        Check(new ParserEvaluator(proposed).Evaluate("CONNECT host=other dport=80").Success, "offline suggestion exports a working parser with submessages");
        var advanced = new AdvancedDialog(MainWindow.Example);
        var advancedTask = advanced.ShowDialog<string?>(main);
        Click(advanced, "Apply to configuration");
        Check(new PropertiesDocument((await advancedTask)!).Get("severity.map.high.if.deviceSeverity") == "400..599", "severity editor round trips rules");

        string samplePath = Path.Combine(AppContext.BaseDirectory, "examples", "submessages.sdkrfilereader.properties");
        string source = PropertiesDocument.Load(samplePath).Source;
        var logs = LogFile.Read(Path.Combine(AppContext.BaseDirectory, "examples", "submessages.log"));
        var sub = new SubmessagesDialog(source, logs[0].Text, 0, 0);
        var subTask = sub.ShowDialog<string?>(main);
        await sub.TestAsync();
        Check(Field<TextEditor>(sub, "result").Text.Contains("submessage[0].pattern[0]", StringComparison.Ordinal), "submessage test reports selected route");
        var fields = Field<Dictionary<string, TextEditor>>(sub, "fields");
        fields["regex"].CaretOffset = fields["regex"].Text.IndexOf('(') + 1;
        Check(Field<CaptureHighlighter>(sub, "painter").Spans.Any(s => s.Selected), "submessage group navigation highlights the original log");
        Click(sub, "Element ▶");
        int bodyStart = logs[0].Text.IndexOf("user=", StringComparison.Ordinal);
        Check(Field<CaptureHighlighter>(sub, "painter").Spans.Any(s => s.Selected && s.Index == bodyStart && s.Length == 1), "submessage literal stepping uses original full-log offsets");
        Check(Field<TextBlock>(sub, "routeStatus").Text!.Contains("pattern[0]"), "actual route remains visible during element navigation");
        await Capture(sub, "submessages");
        sub.Width = sub.MinWidth;
        sub.Height = sub.MinHeight;
        await Capture(sub, "submessages-compact");
        Check(fields["regex"].Bounds.Height >= 60 && Field<TextEditor>(sub, "sample").Bounds.Height >= 60, "submessage message and regex stay visible at minimum size");
        Field<TabControl>(sub, "previewTabs").SelectedIndex = 2;
        await Capture(sub, "submessages-fields");
        fields["regex"].Text += "(?:)";
        Check(Field<CaptureHighlighter>(sub, "painter").Spans.Count == 0 && Field<ListBox>(sub, "elements").ItemCount == 0, "editing a submessage invalidates element results and highlights");
        fields["regex"].Document.UndoStack.Undo();
        await sub.TestAsync();
        Check(Field<CaptureHighlighter>(sub, "painter").Spans.Count > 0, "submessage undo and retest restore capture highlights");
        string validPattern = fields["regex"].Text;
        fields["regex"].Text += " REQUIRED";
        await sub.TestAsync();
        Check(Field<TextBlock>(sub, "previewStatus").Text!.Contains("PARTIAL") && Field<TextBlock>(sub, "routeStatus").Text!.Contains("SubmessageMismatch"), "partial submessage highlighting does not convert failed routing to success");
        Check(Field<CaptureHighlighter>(sub, "painter").Spans.Any(s => s.Index == bodyStart && s.Length > 1), "partial submessage prefix uses full-log offsets");
        await Capture(sub, "submessages-partial");
        fields["regex"].Text = validPattern;
        Field<CheckBox>(sub, "automatic").IsChecked = true;
        fields["regex"].Text = "(";
        await Until(() => Field<TextBlock>(sub, "previewStatus").Text!.StartsWith("Unable to test draft"));
        Check(Field<CaptureHighlighter>(sub, "painter").Spans.Count == 0, "automatic submessage preview reports invalid regex without stale highlights");
        fields["regex"].Text = validPattern;
        await Until(() => Field<CaptureHighlighter>(sub, "painter").Spans.Count > 0);
        Field<CheckBox>(sub, "automatic").IsChecked = false;
        Click(sub, "+ Pattern");
        fields["regex"].Text = validPattern;
        await sub.TestAsync();
        Check(Field<TextBlock>(sub, "previewStatus").Text!.Contains("NOT used") && Field<CaptureHighlighter>(sub, "painter").Spans.Count > 0, "unused submessage alternative can be debugged without claiming it won routing");
        fields["regex"].Text = "(unrecognized login payload)";
        Click(sub, "Apply to configuration");
        string updated = (await subTask)!;
        Check(new PropertiesDocument(updated).Get("submessage[0].pattern.count") == "3", "submessage editor adds and exports an alternative pattern");

        var batch = new BatchWindow(source);
        var batchTask = batch.ShowDialog(main);
        await batch.LoadLogsAsync(logs);
        Check(Field<ParserResult?[]>(batch, "results").Count(r => r?.Success == true) == 4, "log file baseline has four matches");
        var list = Field<ListBox>(batch, "list");
        list.SelectedIndex = 3;
        await Until(() => Field<TextEditor>(batch, "description").Text.Contains("Submessage mismatch", StringComparison.Ordinal));
        await batch.ApplyAsync(updated);
        Check(Field<ParserResult?[]>(batch, "results").Count(r => r?.Success == true) == 5, "editing parser retests the entire file");
        Check(list.SelectedIndex == 3, "batch retest retains the selected log");
        Field<ComboBox>(batch, "filter").SelectedIndex = 2;
        Check(list.ItemCount == 1, "problem filter shows only the remaining failed log");
        Field<ComboBox>(batch, "filter").SelectedIndex = 0;
        list.SelectedIndex = 0;
        await Until(() => Field<TextEditor>(batch, "description").Text.Contains("Line 1", StringComparison.Ordinal));
        Check(!Field<TextBlock>(batch, "summary").Text!.StartsWith("Testing", StringComparison.Ordinal), "queued progress cannot overwrite completed batch totals");
        await Capture(batch, "batch");
        batch.Close();
        await batchTask;

        // The session is untouched by modal draft tests, so closing cannot prompt for unsaved work.
        Check(!main.IsDirty, "modal previews do not silently modify the main parser");
    }
}
