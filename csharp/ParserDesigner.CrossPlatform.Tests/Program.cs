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
        await Capture(main, "main");
        main.Width = main.MinWidth;
        main.Height = main.MinHeight;
        await Capture(main, "main-compact");
        main.Width = 1380;
        main.Height = 980;

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
        Click(tokens, "Apply to configuration");
        string generated = (await tokensTask)!;
        Check(!char.IsWhiteSpace(generated[0]) && new PropertiesDocument(generated).Get("token[0].type") == "TimeStamp", "token export preserves configuration without leading whitespace");

        var mapping = new MappingDialog(MainWindow.Example);
        var mappingTask = mapping.ShowDialog<string?>(main);
        await Capture(mapping, "mapping");
        Click(mapping, "Apply to configuration");
        Check(new PropertiesDocument((await mappingTask)!).Get("event.message") == "Message", "mapping dialog preserves direct assignments");
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
        Click(sub, "+ Pattern");
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
