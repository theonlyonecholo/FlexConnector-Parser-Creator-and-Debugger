using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private static readonly HashSet<string> auditedWindows = [];
    private static readonly List<string> layoutProblems = [];
    private static async Task AuditCompactLayout(Window window, string name)
    {
        string tabs = string.Join(",", Controls<TabControl>(window).Distinct().Select(t => t.SelectedIndex));
        if (!auditedWindows.Add(window.GetType().Name + "/" + window.Title + "/" + tabs))
            return;
        double width = window.Width, height = window.Height;
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        await Task.Delay(200);
        foreach (var button in Controls<Button>(window).Distinct().Where(b => b.Classes.Contains("action")))
        {
            Check(button.ContentTemplate is not null, $"{window.GetType().Name}: icon for {button.Content}");
            Check(!System.Text.RegularExpressions.Regex.IsMatch(button.Content?.ToString() ?? "", @"[◀▶↑↓→]|^\+"),
                $"{window.GetType().Name}: no duplicate text icon in {button.Content}");
        }
        foreach (var control in window.GetVisualDescendants().OfType<Control>().Where(c =>
            c is TextEditor or TextBox or ComboBox || c is Button b && b.Classes.Contains("action")))
        {
            if (!control.IsVisible || control.GetVisualAncestors().OfType<Control>().Any(p => !p.IsVisible))
                continue;
            // Scrollable content may legitimately extend beyond the viewport.
            if (control.GetVisualAncestors().OfType<ScrollViewer>().Any())
                continue;
            var origin = control.TranslatePoint(default, window);
            if (origin is not { } p)
                continue;
            if (p.X < -1 || p.Y < -1 || p.X + control.Bounds.Width > window.Bounds.Width + 1 ||
                p.Y + control.Bounds.Height > window.Bounds.Height + 1 || control.Bounds.Width < 1 || control.Bounds.Height < 1)
                layoutProblems.Add($"{window.GetType().Name}: {control.GetType().Name} {(control as Button)?.Content} at {p}, {control.Bounds.Size}, window {window.Bounds.Size}");
        }
        await SaveScreenshot(window, name + "-minimum");
        window.Width = width;
        window.Height = height;
        await Task.Delay(100);
    }
    private static async Task ClickEditorAsync(TextEditor editor)
    {
        await Task.Delay(100);
        var view = editor.TextArea.TextView;
        var root = (Window)TopLevel.GetTopLevel(editor)!;
        var point = view.TranslatePoint(new Point(25, 10), root)!.Value;
        var pointer = new Avalonia.Input.Pointer(1, Avalonia.Input.PointerType.Mouse, true);
        view.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(view, pointer, root, point, 0,
            new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.LeftMouseButton,
                Avalonia.Input.PointerUpdateKind.LeftButtonPressed), Avalonia.Input.KeyModifiers.None));
        view.RaiseEvent(new Avalonia.Input.PointerReleasedEventArgs(view, pointer, root, point, 1,
            new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.None,
                Avalonia.Input.PointerUpdateKind.LeftButtonReleased), Avalonia.Input.KeyModifiers.None, Avalonia.Input.MouseButton.Left));
        await Task.Delay(100);
        Check(editor.IsKeyboardFocusWithin, "mouse event focuses the submessage input without crashing");
    }
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
        var button = Controls<Button>(owner).Distinct().Single(b => b.Content?.ToString() == label);
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
        await SaveScreenshot(window, name);
        await AuditCompactLayout(window, name);
    }
    private static async Task SaveScreenshot(Window window, string name)
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
        Click(main, "Next element");
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
        Click(main, "Next element");
        Check(Field<TextBlock>(main, "status").Text!.StartsWith("PARTIAL") && painter.Spans.Any(s => s.Selected && s.Length == 1), "partial element stepping remains explicitly labeled");
        main.Message.Text = originalMessage;
        Field<TabControl>(main, "tabs").SelectedIndex = 0;
        foreach (string expression in new[] { "(a[bc])", "(a[bc])Z" })
        {
            main.Pattern.Text = expression;
            main.Message.Text = "ab";
            await main.AnalyzeAsync();
            Field<TabControl>(main, "tabs").SelectedIndex = 1;
            foreach (int position in new[] { 1, 3, 4 })
            {
                main.Pattern.Select(position, 0);
                Check(main.Pattern.SelectionLength == 0 && main.Pattern.CaretOffset == position,
                    "main caret inside groups/classes is not converted into a replacement selection");
                await main.AnalyzeAsync();
                Check(main.Pattern.SelectionLength == 0 && main.Pattern.CaretOffset == position,
                    "main asynchronous preview preserves the insertion point");
            }
        }
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
        await Capture(help, "help");
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
        var catalogPicker = Field<ComboBox>(mapping, "fields");
        catalogPicker.SelectedItem = catalogPicker.Items.Cast<EventFieldDefinition>().Single(f => f.Field == "event.sourcePort");
        Check(Field<TextBlock>(mapping, "fieldDetails").Text!.Contains("65535") &&
            catalogPicker.SelectedItem.ToString()!.Contains("Integer"), "field catalog shows type and range");
        // Clear the draft field introduced by choosing a catalog entry.
        Controls<TextBox>(mapping).Single(t => t.Text == "event.sourcePort").Text = "";
        await Until(() => Controls<TextBlock>(mapping).Any(t => t.Text == "Resource missing"));
        Check(Controls<TextBlock>(mapping).Any(t => t.Text == "404"), "event mapping shows token snippets from the sample message");
        var tokenSearch = Controls<TextBox>(mapping).Single(t => t.PlaceholderText == "Search tokens…");
        var tokenPicker = Controls<ComboBox>(mapping).Single(t => t.PlaceholderText == "Declared tokens…");
        tokenSearch.Text = "missing";
        await Until(() => tokenPicker.ItemCount == 1);
        Check(tokenPicker.ItemCount == 1 && tokenPicker.Items[0]!.ToString()!.Contains("Message · Resource missing"), "token search matches captured text and shows a snippet");
        tokenSearch.Text = "status";
        await Until(() => tokenPicker.ItemCount == 1 && tokenPicker.Items[0]!.ToString()!.Contains("404"));
        Check(tokenPicker.ItemCount == 1 && tokenPicker.Items[0]!.ToString()!.Contains("404"), "token search matches names");
        tokenSearch.Text = "";
        var mappedExpression = Controls<TextBox>(mapping).Single(t => t.Text == "Message");
        mappedExpression.Text = EventMapping.Constant("constant preview");
        await Until(() => Controls<TextBlock>(mapping).Any(t => t.Text == "Constant: constant preview"));
        mappedExpression.Text = "Message";
        await Capture(mapping, "mapping");
        Click(mapping, "Apply to configuration");
        Check(new PropertiesDocument((await mappingTask)!).Get("event.message") == "Message", "mapping dialog preserves direct assignments");
        string categoryRoot = Path.Combine(Path.GetTempPath(), "category-ui-" + Guid.NewGuid());
        Directory.CreateDirectory(categoryRoot);
        var categoryStore = new CategoryCatalog(Path.Combine(categoryRoot, "dictionary.json"));
        var categories = new CategoryMapDialog(MainWindow.Example, categoryRoot, categoryStore);
        var categoriesTask = categories.ShowDialog<string?>(main);
        Field<TextBox>(categories, "id").Text = "404";
        Field<TextBox[]>(categories, "cells")[5].Text = "/Failure";
        Click(categories, "Add / update row");
        Check(Field<ListBox>(categories, "list").ItemCount == 1, "category row can be added");
        Field<TextBox>(categories, "search").Text = "Failure";
        Check(Field<ListBox>(categories, "list").ItemCount == 1, "category search includes values");
        Field<ComboBox>(categories, "mode").SelectedIndex = 1;
        Field<TextBox>(categories, "vendor").Text = "Test Vendor";
        Field<TextBox>(categories, "product").Text = "Product";
        await Capture(categories, "category-maps");
        Click(categories, "Save map and apply");
        await Until(() => categoriesTask.IsCompleted);
        await categoriesTask;
        string nativeCsv = Path.Combine(categoryRoot, "categorizer", "test_vendor", "product.csv");
        Check(CategoryMap.Parse(File.ReadAllText(nativeCsv)).Rows.Single()[6] == "/Failure", "native category export uses normalized vendor/product path");
        var dictionary = new CategoryCatalogDialog(categoryStore);
        var dictionaryTask = dictionary.ShowDialog(main);
        await Capture(dictionary, "category-dictionary");
        Click(dictionary, "Cancel");
        await dictionaryTask;
        var categoryParser = new PropertiesDocument(MainWindow.Example);
        categoryParser.RemoveWhere(k => k.StartsWith("severity."));
        categoryParser.Set("event.deviceEventClassId", "Status");
        categoryParser.Set("event.deviceVendor", "__stringConstant(Test Vendor)");
        categoryParser.Set("event.deviceProduct", "__stringConstant(Product)");
        var categoryBatch = new BatchWindow(categoryParser.Source, categoryRoot);
        var categoryBatchTask = categoryBatch.ShowDialog(main);
        await categoryBatch.LoadLogsAsync([new LogEntry(1, "status=404 message=Resource missing"), new LogEntry(2, "status=200 message=OK")]);
        await Until(() => Field<TextEditor>(categoryBatch, "description").Text.Contains("Event processing:"));
        Check(Field<TextBlock>(categoryBatch, "summary").Text!.Contains("1 with category values") &&
            Field<TextEditor>(categoryBatch, "description").Text.Contains("event.categoryOutcome: /Failure"), "batch evaluates native categorization for each log");
        Field<ComboBox>(categoryBatch, "filter").SelectedIndex = 5;
        Check(Field<ListBox>(categoryBatch, "list").ItemCount == 1, "uncategorized filter isolates unmatched category IDs");
        Field<ComboBox>(categoryBatch, "filter").SelectedIndex = 0;
        await Capture(categoryBatch, "category-batch");
        categoryBatch.Close();
        await categoryBatchTask;
        string catalogPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var catalogStore = new EventFieldCatalog(catalogPath);
            var editor = new FieldCatalogDialog(catalogStore, "event.sourcePort");
            var editorTask = editor.ShowDialog(main);
            Field<TextBox>(editor, "length").Text = "Custom constraint";
            Click(editor, "Save definition");
            Check(catalogStore.Load()["event.sourcePort"].Length == "Custom constraint", "catalog override is saved separately");
            Click(editor, "Remove override / custom field");
            Check(catalogStore.Load()["event.sourcePort"].Length == "n/a", "catalog built-in can be restored");
            Click(editor, "New field");
            Field<TextBox>(editor, "field").Text = "event.futureVendorField";
            Field<TextBox>(editor, "type").Text = "FutureType";
            Field<TextBox>(editor, "length").Text = "2048";
            Click(editor, "Save definition");
            await Capture(editor, "field-catalog");
            editor.Close();
            await editorTask;
            var customMapping = new MappingDialog(MainWindow.Example, catalogStore: new EventFieldCatalog(catalogPath));
            var customTask = customMapping.ShowDialog<string?>(main);
            var customPicker = Field<ComboBox>(customMapping, "fields");
            customPicker.SelectedItem = customPicker.Items.Cast<EventFieldDefinition>().Single(f => f.Field == "event.futureVendorField");
            Check(Field<TextBlock>(customMapping, "fieldDetails").Text!.Contains("FutureType") &&
                Field<TextBlock>(customMapping, "fieldDetails").Text!.Contains("Local definition"), "saved custom field appears with metadata after reopening mapping");
            await Capture(customMapping, "mapping-catalog");
            customMapping.Close();
            await customTask;
        }
        finally { if (File.Exists(catalogPath)) File.Delete(catalogPath); }
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
        await Capture(advanced, "advanced");
        Controls<TabControl>(advanced).Single().SelectedIndex = 1;
        await Capture(advanced, "processors");
        Click(advanced, "Apply to configuration");
        Check(new PropertiesDocument((await advancedTask)!).Get("severity.map.high.if.deviceSeverity") == "400..599", "severity editor round trips rules");

        string samplePath = Path.Combine(AppContext.BaseDirectory, "examples", "submessages.sdkrfilereader.properties");
        string source = PropertiesDocument.Load(samplePath).Source;
        var logs = LogFile.Read(Path.Combine(AppContext.BaseDirectory, "examples", "submessages.log"));
        var sub = new SubmessagesDialog(source, logs[0].Text, 0, 0);
        var subTask = sub.ShowDialog<string?>(main);
        await Until(() => Field<TextEditor>(sub, "sample").Text.Length > 0);
        Check(Field<ComboBox>(sub, "body").Items.Cast<string>().Contains("Body") &&
            Field<ComboBox>(sub, "idToken").Items.Cast<string>().Contains("MessageID"), "token dropdowns list declared main tokens");
        Check(Field<TextEditor>(sub, "result").Text.Contains("submessage[0].pattern[0]", StringComparison.Ordinal), "submessage test reports selected route");
        Check(Field<TextEditor>(sub, "idPreview").Text == "LOGIN", "submessage ID preview shows the extracted routing value");
        await ClickEditorAsync(Field<TextEditor>(sub, "sample"));
        await ClickEditorAsync(Field<TextEditor>(sub, "idPreview"));
        var fields = Field<Dictionary<string, TextEditor>>(sub, "fields");
        Check(Field<CheckBox>(sub, "automatic").IsChecked == true, "submessage live preview is enabled by default");
        fields["regex"].Text += "(?:)";
        await Until(() => Field<CaptureHighlighter>(sub, "painter").Spans.Count > 0);
        Check(Field<TextEditor>(sub, "result").Text.Contains("ACTUAL ROUTE:"), "typing refreshes submessage highlights without pressing Test");
        fields["regex"].Document.UndoStack.Undo();
        await Until(() => Field<CaptureHighlighter>(sub, "painter").Spans.Count > 0);
        var subNodes = Field<ListBox>(sub, "nodes");
        subNodes.SelectedIndex = 2;
        await Until(() => Field<TextEditor>(sub, "result").Text.Contains("SELECTED PATTERN:") &&
            Field<TextEditor>(sub, "result").Text.Contains("NOT used"));
        subNodes.SelectedIndex = 1;
        await Until(() => Field<CaptureHighlighter>(sub, "painter").Spans.Count > 0);
        Check(Field<TextEditor>(sub, "result").Text.Contains("active route"), "switching patterns refreshes the selected preview automatically");
        Field<CheckBox>(sub, "automatic").IsChecked = false;
        string beforeTypingCheck = fields["regex"].Text;
        foreach (string suffix in new[] { "", " REQUIRED" })
        {
            fields["regex"].Text = @"(user)=([a-z]+) result=(\w+)" + suffix;
            await sub.TestAsync();
            Field<TabControl>(sub, "previewTabs").SelectedIndex = 1;
            fields["regex"].Focus();
            foreach (int position in new[] { 2, 9, 11 })
            {
                fields["regex"].Select(position, 0);
                Check(fields["regex"].SelectionLength == 0 && fields["regex"].CaretOffset == position,
                    "submessage caret inside groups/classes does not select typed regex text");
                await sub.TestAsync();
                Check(fields["regex"].SelectionLength == 0 && fields["regex"].CaretOffset == position,
                    "submessage preview refresh preserves the insertion point");
            }
        }
        fields["regex"].Text = beforeTypingCheck;
        Field<TabControl>(sub, "previewTabs").SelectedIndex = 0;
        await sub.TestAsync();
        fields["regex"].CaretOffset = fields["regex"].Text.IndexOf('(') + 1;
        Check(Field<CaptureHighlighter>(sub, "painter").Spans.Any(s => s.Selected), "submessage group navigation highlights the body token");
        Click(sub, "Next element");
        int bodyStart = logs[0].Text.IndexOf("user=", StringComparison.Ordinal);
        Check(Field<TextEditor>(sub, "sample").Text == logs[0].Text[bodyStart..] && Field<TextEditor>(sub, "sample").IsReadOnly,
            "submessage input displays only the configured token and cannot diverge from the original log");
        Check(Field<TextEditor>(sub, "originalLog").Text == logs[0].Text, "full event remains available separately");
        Check(Field<CaptureHighlighter>(sub, "painter").Spans.Any(s => s.Selected && s.Index == 0 && s.Length == 1), "submessage literal stepping uses token-relative offsets");
        Check(Field<TextBlock>(sub, "routeStatus").Text!.Contains("pattern[0]"), "actual route remains visible during element navigation");
        await Capture(sub, "submessages");
        sub.Width = sub.MinWidth;
        sub.Height = sub.MinHeight;
        await ClickEditorAsync(Field<TextEditor>(sub, "sample"));
        await ClickEditorAsync(Field<TextEditor>(sub, "idPreview"));
        await Capture(sub, "submessages-compact");
        Check(Field<ListBox>(sub, "elements").Bounds.Height >= 60, "compact submessage window retains a usable results area");
        Check(fields["regex"].Bounds.Height >= 60 && Field<TextEditor>(sub, "sample").Bounds.Height >= 60, "submessage message and regex stay visible at minimum size");
        Field<TabControl>(sub, "previewTabs").SelectedIndex = 2;
        await Capture(sub, "submessages-fields");
        fields["regex"].Text += "(?:)";
        Check(Field<TextEditor>(sub, "sample").Text.Length > 0, "editing a submessage keeps the independently extracted body visible");
        Check(Field<CaptureHighlighter>(sub, "painter").Spans.Count == 0 && Field<ListBox>(sub, "elements").ItemCount == 0, "editing a submessage invalidates element results and highlights");
        fields["regex"].Document.UndoStack.Undo();
        await sub.TestAsync();
        Check(Field<CaptureHighlighter>(sub, "painter").Spans.Count > 0, "submessage undo and retest restore capture highlights");
        string validPattern = fields["regex"].Text;
        fields["regex"].Text += " REQUIRED";
        await sub.TestAsync();
        Check(Field<TextBlock>(sub, "previewStatus").Text!.Contains("PARTIAL") && Field<TextBlock>(sub, "routeStatus").Text!.Contains("SubmessageMismatch"), "partial submessage highlighting does not convert failed routing to success");
        Check(Field<CaptureHighlighter>(sub, "painter").Spans.Any(s => s.Index == 0 && s.Length > 1), "partial submessage prefix uses token-relative offsets");
        await Capture(sub, "submessages-partial");
        fields["regex"].Text = validPattern;
        Field<CheckBox>(sub, "automatic").IsChecked = true;
        fields["regex"].Text = "(";
        await Until(() => Field<TextBlock>(sub, "previewStatus").Text!.StartsWith("Unable to test draft"));
        Check(Field<CaptureHighlighter>(sub, "painter").Spans.Count == 0, "automatic submessage preview reports invalid regex without stale highlights");
        Check(Field<TextEditor>(sub, "sample").Text == logs[0].Text[bodyStart..], "invalid submessage regex does not block main token extraction");
        fields["regex"].Text = validPattern;
        await Until(() => Field<CaptureHighlighter>(sub, "painter").Spans.Count > 0);
        Field<CheckBox>(sub, "automatic").IsChecked = false;
        Field<ComboBox>(sub, "body").SelectedItem = "MessageID";
        await Until(() => Field<TextEditor>(sub, "sample").Text == "LOGIN");
        Check(Field<TextEditor>(sub, "sample").Text == "LOGIN", "changing submessage.token changes the selected pattern input");
        Field<ComboBox>(sub, "body").SelectedItem = "Body";
        Field<TextEditor>(sub, "originalLog").Text = "invalid original event";
        await sub.TestAsync();
        Check(Field<TextEditor>(sub, "sample").Text == "" && Field<CaptureHighlighter>(sub, "painter").Spans.Count == 0,
            "failed main extraction clears body text and highlights instead of using the full event");
        Field<TextEditor>(sub, "originalLog").Text = logs[0].Text;
        await sub.TestAsync();
        Click(sub, "Add pattern");
        fields["regex"].Text = validPattern;
        await sub.TestAsync();
        Check(Field<TextBlock>(sub, "previewStatus").Text!.Contains("NOT used") && Field<CaptureHighlighter>(sub, "painter").Spans.Count > 0, "unused submessage alternative can be debugged without claiming it won routing");
        fields["regex"].Text = "(unrecognized login payload)";
        Click(sub, "Apply to configuration");
        string updated = (await subTask)!;
        Check(new PropertiesDocument(updated).Get("submessage[0].pattern.count") == "3", "submessage editor adds and exports an alternative pattern");

        var urlDoc = new PropertiesDocument(source);
        urlDoc.Set("submessage[0].pattern[0].regex", @"(https)://([^/]+)(/.*)");
        var urlSub = new SubmessagesDialog(urlDoc.Source, "id=LOGIN body=https://example.com/path?q=1", 0, 0);
        var urlTask = urlSub.ShowDialog<string?>(main);
        await Until(() => Field<CaptureHighlighter>(urlSub, "painter").Spans.Count == 4);
        string urlLog = Field<TextEditor>(urlSub, "originalLog").Text;
        Field<TextEditor>(urlSub, "originalLog").Text = urlLog.Replace("LOGIN", new string('X', 180));
        await urlSub.TestAsync();
        await ClickEditorAsync(Field<TextEditor>(urlSub, "idPreview"));
        Check(Field<TextEditor>(urlSub, "idPreview").Text.Length == 180, "clicking a wrapped ID preview preserves its value");
        Field<TextEditor>(urlSub, "originalLog").Text = urlLog;
        await urlSub.TestAsync();
        var urlFields = Field<Dictionary<string, TextEditor>>(urlSub, "fields");
        urlFields["regex"].CaretOffset = 2;
        var urlEditor = Field<TextEditor>(urlSub, "sample");
        Check(!urlEditor.Options.EnableHyperlinks && !urlEditor.Options.EnableEmailHyperlinks &&
            !main.Message.Options.EnableHyperlinks, "URLs remain plain text in main and submessage previews");
        Check(Field<CaptureHighlighter>(urlSub, "painter").Spans.Any(s => s.Selected && s.Length == 5),
            "a capture inside a URL can be highlighted independently");
        await Capture(urlSub, "submessage-url");
        urlFields["regex"].Text += "(?:)";
        Field<TextBox>(urlSub, "search").Text = "destinationPort";
        Check(Field<ListBox>(urlSub, "nodes").ItemCount == 2, "submessage search finds mapping fields and retains parent context");
        Field<TextBox>(urlSub, "search").Text = "does-not-exist";
        Check(Field<ListBox>(urlSub, "nodes").ItemCount == 0, "submessage search supports no results");
        Field<TextBox>(urlSub, "search").Text = "LOGIN";
        Field<ListBox>(urlSub, "nodes").SelectedIndex = 1;
        Check(urlFields["regex"].Text.EndsWith("(?:)"), "search preserves unsaved pattern edits");
        Field<TextBox>(urlSub, "search").Text = "";
        urlFields["fields"].CaretOffset = 0;
        Field<TabControl>(urlSub, "previewTabs").SelectedIndex = 2;
        Click(urlSub, "Choose ArcSight field…");
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        await Until(() => lifetime.Windows.OfType<EventFieldPicker>().Any());
        var picker = lifetime.Windows.OfType<EventFieldPicker>().Single();
        Field<TextBox>(picker, "search").Text = "requestUrl";
        Field<ListBox>(picker, "list").SelectedIndex = 0;
        await Capture(picker, "submessage-field-picker");
        Click(picker, "Use field");
        await Until(() => urlFields["fields"].Text.StartsWith("event.requestUrl,"));
        Check(urlFields["fields"].Text.EndsWith(",event.deviceAction"), "field picker replaces only the field slot at the caret");
        urlFields["fields"].Document.UndoStack.Undo();
        Check(urlFields["fields"].Text.StartsWith("event.sourceUserName,"), "field choice is a single undo step");
        urlSub.Close();
        await urlTask;

        var batch = new BatchWindow(source);
        var batchTask = batch.ShowDialog(main);
        await batch.LoadLogsAsync(logs);
        Check(Field<ParserResult?[]>(batch, "results").Count(r => r?.Success == true) == 4, "log file baseline has four matches");
        var list = Field<ListBox>(batch, "list");
        list.SelectedIndex = 3;
        await Until(() => Field<TextEditor>(batch, "description").Text.Contains("Submessage mismatch", StringComparison.Ordinal));
        Check(Field<CaptureHighlighter>(batch, "painter").Spans.Any(s => s.Background == BatchHighlighting.Passed) &&
            Field<CaptureHighlighter>(batch, "painter").Spans.Any(s => s.Background == BatchHighlighting.Failed),
            "failed submessage retains green main extraction and marks the failed body red");
        await Capture(batch, "batch-failed");
        await batch.ApplyAsync(updated);
        Check(Field<ParserResult?[]>(batch, "results").Count(r => r?.Success == true) == 5, "editing parser retests the entire file");
        Check(list.SelectedIndex == 3, "batch retest retains the selected log");
        Field<ComboBox>(batch, "filter").SelectedIndex = 2;
        Check(list.ItemCount == 1, "problem filter shows only the remaining failed log");
        Field<ComboBox>(batch, "filter").SelectedIndex = 0;
        list.SelectedIndex = 0;
        await Until(() => Field<TextEditor>(batch, "description").Text.Contains("Line 1", StringComparison.Ordinal));
        Check(Field<CaptureHighlighter>(batch, "painter").Spans.All(s => s.Background == BatchHighlighting.Passed),
            "successful log uses green status highlighting");
        Field<ListBox>(batch, "captures").SelectedIndex = 1;
        Check(Field<CaptureHighlighter>(batch, "painter").Spans.Any(s => s.Selected),
            "capture selection overlays status highlighting");
        Field<ListBox>(batch, "captures").SelectedIndex = -1;
        Check(!Field<TextBlock>(batch, "summary").Text!.StartsWith("Testing", StringComparison.Ordinal), "queued progress cannot overwrite completed batch totals");
        await Capture(batch, "batch");
        Click(batch, "Edit parser…");
        var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        await Until(() => desktop.Windows.Any(w => w.Title == "Edit parser and retest"));
        var parserEditor = desktop.Windows.Single(w => w.Title == "Edit parser and retest");
        await Capture(parserEditor, "batch-parser-editor");
        Click(parserEditor, "Cancel");
        var noRoute = new PropertiesDocument(source);
        noRoute.Set("submessage.messageid.token", "Body");
        noRoute.Set("submessage[2].messageid", "STATUS");
        await batch.ApplyAsync(noRoute.Source);
        await Until(() => Field<TextEditor>(batch, "description").Text.Contains("No pattern regex was tested", StringComparison.Ordinal));
        Check(Field<TextEditor>(batch, "description").Text.Contains("Routing token: Body", StringComparison.Ordinal),
            "batch detail explains exact-ID routing failures before regex matching");
        var routeSpan = Field<CaptureHighlighter>(batch, "painter").Spans.Last();
        Check(routeSpan.Background == BatchHighlighting.Failed && logs[0].Text.Substring(routeSpan.Index, routeSpan.Length).StartsWith("user="),
            "routing failure colors the configured message ID token");
        await Capture(batch, "batch-routing");
        var partialDoc = new PropertiesDocument("");
        partialDoc.Set("regex", @"abc=(\d+) END");
        partialDoc.Set("token.count", "1");
        partialDoc.Set("token[0].name", "Number");
        partialDoc.Set("token[0].type", "String");
        string partialSource = partialDoc.Source;
        foreach (string text in new[] { "abc=123 WRONG", "abc=123", "" })
        {
            var result = new ParserEvaluator(partialSource).Evaluate(text);
            var colors = BatchHighlighting.Analyze(partialSource, text, result, default);
            Check(!result.Success && !colors.Spans.Any(s => s.Background == BatchHighlighting.Passed),
                "partial or empty main input is never reported as green");
            if (text.Length > 0)
                Check(colors.Spans.Any(s => s.Background == BatchHighlighting.Partial), "partial prefix is orange");
            if (text == "abc=123")
                Check(colors.Explanation.Contains("required text may be missing"), "missing suffix is explained without inventing a failing character");
            if (text.Length == 0)
                Check(colors.Explanation.Contains("input is empty"), "empty failed input has a textual diagnostic");
        }
        await batch.ApplyAsync(partialSource);
        await batch.LoadLogsAsync([new LogEntry(1, "abc=123 WRONG")]);
        await Until(() => Field<TextEditor>(batch, "description").Text.Contains("estimated matching prefix"));
        await Capture(batch, "batch-partial");
        batch.Close();
        await batchTask;

        string libraryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var library = new RegexPresetsDialog(new RegexPresetStore(libraryPath), "host=192.0.2.1", 5, 9);
            var libraryTask = library.ShowDialog<string?>(main);
            var presetList = Field<ListBox>(library, "list");
            presetList.SelectedItem = presetList.Items.Cast<RegexPreset>().Single(p => p.Id == "ipv4");
            await Capture(library, "regex-presets");
            Click(library, "Check and replace selection");
            string replacement = (await libraryTask)!;
            Check(replacement.Contains("25[0-5]"), "preset dialog validates and returns the matching IPv4 expression");
            var edit = Ui.Editor("host=192.0.2.1");
            RegexEditorActions.Replace(edit, 5, 9, replacement);
            Check(edit.Text == "host=" + replacement, "preset replacement changes only the original selection");
            edit.Document.UndoStack.Undo();
            Check(edit.Text == "host=192.0.2.1", "preset replacement is one undo step");

            library = new RegexPresetsDialog(new RegexPresetStore(libraryPath), "not an IP", 0, 9);
            libraryTask = library.ShowDialog<string?>(main);
            presetList = Field<ListBox>(library, "list");
            presetList.SelectedItem = presetList.Items.Cast<RegexPreset>().Single(p => p.Id == "ipv4");
            Click(library, "Check and replace selection");
            await Until(() => Field<TextBlock>(library, "status").Text!.StartsWith("Cannot replace"));
            Check(library.IsVisible, "mismatching preset keeps the dialog open without replacement");
            Click(library, "New");
            Field<TextBox>(library, "name").Text = "My phrase";
            Field<TextEditor>(library, "pattern").Text = "not an IP";
            Click(library, "Save preset");
            Check(new RegexPresetStore(libraryPath).Load().Any(p => p.Name == "My phrase"), "preset save persists through a fresh library instance");
            Field<TextEditor>(library, "pattern").Text = "not .*";
            Click(library, "Save preset");
            Check(new RegexPresetStore(libraryPath).Load().Single(p => p.Name == "My phrase").Pattern == "not .*", "preset edits persist");
            Click(library, "Delete");
            Check(!new RegexPresetStore(libraryPath).Load().Any(p => p.Name == "My phrase"), "preset deletion persists");
            library.Close();
            await libraryTask;
        }
        finally { if (File.Exists(libraryPath)) File.Delete(libraryPath); }
        string beforeGroup = main.Pattern.Text;
        main.Pattern.Select(0, 6);
        Click(main, "Group selection");
        Check(main.Pattern.Text == "(" + beforeGroup[..6] + ")" + beforeGroup[6..], "main regex group button wraps only the selected text");
        main.Pattern.Document.UndoStack.Undo();
        Check(main.Pattern.Text == beforeGroup, "group insertion is one undo step");
        string pipelineRoot = Path.Combine(AppContext.BaseDirectory, "examples");
        string pipelineSource = PropertiesDocument.Load(Path.Combine(pipelineRoot, "event-pipeline.sdkrfilereader.properties")).Source;
        var pipeline = new EventPreviewDialog(pipelineSource, "user=alice action=allow port=80", pipelineRoot);
        var pipelineTask = pipeline.ShowDialog(main);
        await Until(() => Field<TextBlock>(pipeline, "status").Text!.Contains("SUPPORTED STAGES EVALUATED"));
        Check(Field<TextEditor>(pipeline, "output").Text.Contains("HTTP traffic") &&
            Field<TextEditor>(pipeline, "output").Text.Contains("overwrite disabled"), "event preview shows resolved fields and overwrite history");
        await Capture(pipeline, "event-preview");
        string expectedJson = File.ReadAllText(Path.Combine(pipelineRoot, "event-pipeline.expected.json"));
        Field<TextEditor>(pipeline, "fixture").Text = expectedJson;
        await pipeline.TestAsync();
        Check(Field<TextBlock>(pipeline, "status").Text!.StartsWith("2 PASS"), "expected-field tests run in the UI");
        Field<TextEditor>(pipeline, "fixture").Text = expectedJson.Replace("HTTP traffic", "wrong");
        await pipeline.TestAsync();
        Check(Field<TextBlock>(pipeline, "status").Text!.Contains("1 FAIL"), "UI reports mismatched expected fields");
        Field<TextBox>(pipeline, "root").Text = "";
        await pipeline.TestAsync();
        Check(Field<TextBlock>(pipeline, "status").Text!.Contains("2 INCOMPLETE"), "missing dependencies cannot produce a passing fixture");
        pipeline.Close();
        await pipelineTask;
        // The session is untouched by modal draft tests, so closing cannot prompt for unsaved work.
        Check(!main.IsDirty, "modal previews do not silently modify the main parser");
        var formatDialog = new FormatDialog("TimeStamp", "yyyy-MM-dd HH:mm:ss");
        var formatTask = formatDialog.ShowDialog<string?>(main);
        await Capture(formatDialog, "format");
        formatDialog.Close();
        await formatTask;
        var promptTask = Ui.Ask(main, "Layout check", "Check confirmation actions.", "Save", "Discard", "Cancel");
        var prompt = ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows.Single(w => w.Title == "Layout check");
        await Capture(prompt, "confirmation");
        Click(prompt, "Cancel");
        await promptTask;
        Check(layoutProblems.Count == 0, "compact-window layout: " + string.Join("\n", layoutProblems));
    }
}
