using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using ParserCore;
using System.Text.RegularExpressions;

namespace ParserDesigner.CrossPlatform;

/// <summary>Owns the native editing session. Background previews use immutable snapshots and revision guards.</summary>
internal sealed class MainWindow : Window
{
    internal const string Example = "# FlexConnector Regex Configuration File\nregex=status=(\\\\d+) message=(.*)\ntoken.count=2\ntoken[0].name=Status\ntoken[0].type=String\ntoken[1].name=Message\ntoken[1].type=String\nevent.deviceSeverity=Status\nevent.message=Message\nseverity.map.high.if.deviceSeverity=400..599\n";
    internal readonly TextEditor Source = Ui.Editor(Example);
    internal readonly TextEditor Pattern = Ui.Editor(@"status=(\d+) message=(.*)");
    internal readonly TextEditor Message = Ui.Editor("status=404 message=Resource missing");
    private readonly TextEditor report = Ui.Editor(readOnly: true);
    private readonly ListBox captures = new();
    private readonly TextBlock status = Ui.Label("Ready");
    private readonly CheckBox automatic = new() { Content = "Automatic", IsChecked = true };
    private readonly CheckBox ignoreCase = new() { Content = "Ignore case" };
    private readonly CheckBox multiline = new() { Content = "Multiline" };
    private readonly CheckBox singleline = new() { Content = "Singleline" };
    private readonly CheckBox entire = new() { Content = "Entire message" };
    private readonly CheckBox trim = new() { Content = "Trim spaces/tabs" };
    private readonly NumericUpDown timeout = new() { Minimum = 10, Maximum = 2000, Value = 250, Increment = 50, Width = 150 };
    private readonly CaptureHighlighter painter;
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly SemaphoreSlim worker = new(1);
    private CancellationTokenSource? previewCancellation;
    private int revision;
    private bool closing, allowClose, checkingClose, loading;
    private string savedSource = Example, savedPattern = @"status=(\d+) message=(.*)";
    private string? path;
    private DebugResult? result;
    private IReadOnlyList<RegexGroupRegion> groups = [];
    private readonly TabControl tabs;
    private readonly ListBox elements = new();
    private IReadOnlyList<RegexElement> elementResults = [];
    private ElementDebugResult? elementPreview;
    private bool selectingElement;
    internal bool IsDirty => Source.Text != savedSource || Pattern.Text != savedPattern;

    public MainWindow()
    {
        Ui.Setup(this, "ArcSight Parser Designer · Cross-platform 2.7.1", 1380, 980);
        MinWidth = 900;
        MinHeight = 800;
        painter = CaptureHighlighter.Attach(Message);
        var header = new Border
        {
            Background = Brush.Parse("#142238"),
            Padding = new Thickness(20, 14),
            Child = Ui.Stack(new TextBlock { Text = "PARSER STUDIO", FontSize = 23, Foreground = Brushes.White },
                new TextBlock { Text = "ArcSight FlexConnector · Cross-platform workbench", Foreground = Brush.Parse("#bcd0e8") })
        };
        Button Command(string name, Func<Task> action, string hint) => Ui.AsyncButton(name, () => Ui.Guard(this, action), hint);
        var toolbar = Ui.Bar(
            Command("Open…", OpenAsync, "Open a .properties parser; offers to save unsaved changes."),
            Command("Save", () => SaveAsync(false), "Save configuration and optionally synchronize the working regex. Ctrl+S."),
            Command("Save as…", () => SaveAsync(true), "Save the parser to another file."),
            Command("Load message…", LoadMessageAsync, "Load one text file into Message."),
            Command("Suggest parser…", SuggestAsync, "Build an offline parser draft from multiple sample logs and validate its coverage."),
            Command("Tokens…", EditTokensAsync, "Create named tokens and formats from capturing groups."),
            Command("Submessages…", EditSubmessagesAsync, "Edit message IDs, ordered body patterns and mappings."),
            Command("Event mapping…", EditMappingAsync, "Assign tokens, constants or expressions to event fields."),
            Command("Severity / processors…", EditAdvancedAsync, "Normalize severity and configure downstream parsers."),
            Command("Test log file…", OpenBatchAsync, "Test a parser against all lines, inspect routes and tune it."), Ui.Help(this, "start"));
        toolbar.Children.Insert(toolbar.Children.Count - 1,
            Command("Event preview / tests…", () => new EventPreviewDialog(Source.Text, Message.Text,
                path is null ? null : Path.GetDirectoryName(path), path).ShowDialog(this),
                "Inspect configuration compatibility, final event fields, map dependencies and expected-field tests."));
        var options = Ui.Bar(automatic, ignoreCase, multiline, singleline, entire, trim, Ui.Columns("Auto,Auto", Ui.Label("Timeout (ms)"), timeout));
        foreach (var check in new[] { automatic, ignoreCase, multiline, singleline, entire, trim })
        {
            check.Margin = new Thickness(0, 0, 16, 0);
            check.IsCheckedChanged += (_, _) => Invalidate();
        }
        timeout.ValueChanged += (_, _) => Invalidate();
        Ui.Tip(automatic, "Refresh after a typing pause. Turn off to run with F5 only.");
        Ui.Tip(ignoreCase, "Preview flag i; ignores letter case. Not saved to the parser.");
        Ui.Tip(multiline, "Preview flag m changes ^ and $ boundaries; it does not assemble multiline log events.");
        Ui.Tip(singleline, "Preview flag s makes the dot match newlines.");
        Ui.Tip(entire, "Require a whole-input match. File testing always uses whole-event matching.");
        Ui.Tip(trim, "Trim spaces/tabs at both ends for preview; keep original capture positions.");
        Ui.Tip(timeout, "Time limit for each regex operation, from 10 to 2000 milliseconds.");
        Ui.Tip(Pattern, "Decoded regex, e.g. \\d. Click inside a group to highlight its captures. Long expressions wrap.");
        Ui.Tip(Source, "Java properties syntax. Backslashes are doubled here. Changes remain in memory until Save.");
        Ui.Tip(Message, "Paste a sample log. Captured tokens use distinct colors; selected captures are blue.");
        Ui.Tip(captures, "Match / group / repeated capture / zero-based UTF-16 position / length / value. Click to highlight.");
        var actions = Ui.Bar(Command("Test / Debug · F5", AnalyzeAsync, "Inspect regex elements and captures from the final match; this is not an internal backtracking trace."),
            Command("Regex presets…", () => RegexEditorActions.PresetsAsync(this, Pattern), "Manage saved regex presets and replace matching selected text."),
            Command("Group (…)", () => { RegexEditorActions.Group(Pattern); return Task.CompletedTask; }, "Wrap selected regex text in a capturing group. This changes group numbering; review tokens."),
            Ui.Button("◀ Element", () => MoveElement(-1)), Ui.Button("Element ▶", () => MoveElement(1)),
            Ui.Button("Stop", Stop, "Cancel the preview; a running regex is bounded by its timeout."),
            Command("Regex → Configuration", () => { WritePattern(); return Task.CompletedTask; }, "Escape and write the regex property without regenerating tokens."),
            Command("Configuration → Regex", ReadPatternAsync, "Decode the regex property, replacing the working expression."));
        tabs = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = Ui.Label("Configuration .properties", 14), Content = Source },
                new TabItem { Header = Ui.Label("Regex elements", 14), Content = elements },
                new TabItem { Header = Ui.Label("Regex captures", 14), Content = Ui.Rows("Auto,*", Ui.Bar(Ui.Button("◀ Capture", () => MoveCapture(-1)), Ui.Button("Capture ▶", () => MoveCapture(1))), captures) },
                new TabItem { Header = Ui.Label("Diagnostics", 14), Content = report }
            }
        };
        var editors = Ui.Rows("Auto,90,Auto,66,Auto,Auto,Auto,*",
            Ui.Label("MESSAGE · token colors follow capture groups"), Message,
            Ui.Label("REGEX · click inside a capturing group to locate its text"), Pattern,
            options, actions, status, tabs);
        Content = Ui.Rows("Auto,Auto,*", header, new Border { Padding = new Thickness(16, 0), Child = toolbar },
            new Border { Padding = new Thickness(16, 0, 16, 16), Child = Ui.Card(editors) });
        Source.TextChanged += (_, _) => { UpdateTitle(); Invalidate(); };
        Pattern.TextChanged += (_, _) => { UpdateTitle(); Invalidate(); };
        Message.TextChanged += (_, _) => Invalidate();
        Pattern.TextArea.Caret.PositionChanged += (_, _) => HighlightGroup();
        elements.SelectionChanged += (_, _) => SelectElement();
        captures.SelectionChanged += (_, _) => Paint(captures.SelectedItem is CaptureItem item ? item.Row : null);
        debounce.Tick += async (_, _) => { debounce.Stop(); await AnalyzeAsync(); };
        Opened += async (_, _) => await AnalyzeAsync();
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                await AnalyzeAsync();
            }
            if (e.Key == Key.S && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                e.Handled = true;
                await Ui.Guard(this, () => SaveAsync(false));
            }
        };
        Ui.HelpKey(this, "start");
        Closing += async (_, e) =>
        {
            if (allowClose || !IsDirty)
                return;
            e.Cancel = true;
            if (checkingClose)
                return;
            checkingClose = true;
            try
            {
                if (await ConfirmDiscardAsync())
                {
                    allowClose = true;
                    Close();
                }
            }
            finally { checkingClose = false; }
        };
        Closed += (_, _) => { closing = true; debounce.Stop(); previewCancellation?.Cancel(); };
    }

    private void UpdateTitle() => Title = $"ArcSight Parser Designer · {Path.GetFileName(path) ?? "New parser"}{(IsDirty ? " *" : "")} · 2.7.1";
    internal RegexOptions Options => (ignoreCase.IsChecked == true ? RegexOptions.IgnoreCase : RegexOptions.None)
        | (multiline.IsChecked == true ? RegexOptions.Multiline : RegexOptions.None)
        | (singleline.IsChecked == true ? RegexOptions.Singleline : RegexOptions.None);
    private void Invalidate()
    {
        revision++;
        previewCancellation?.Cancel();
        debounce.Stop();
        result = null;
        painter.Paint(Message, []);
        captures.ItemsSource = null;
        elementResults = [];
        elementPreview = null;
        elements.ItemsSource = null;
        if (loading || closing)
            return;
        status.Text = "Preview out of date. Press F5 to evaluate.";
        if (automatic.IsChecked == true)
            debounce.Start();
    }
    private void Stop()
    {
        revision++;
        debounce.Stop();
        previewCancellation?.Cancel();
        status.Text = "Evaluation stopped.";
    }
    internal async Task AnalyzeAsync()
    {
        debounce.Stop();
        previewCancellation?.Cancel();
        previewCancellation?.Dispose();
        previewCancellation = new();
        var cancellation = previewCancellation.Token;
        int current = ++revision;
        string pattern = Pattern.Text, text = Message.Text, source = Source.Text;
        var options = Options;
        bool full = entire.IsChecked == true, trimValue = trim.IsChecked == true;
        int timeoutMs = (int)(timeout.Value ?? 250);
        status.Text = "Evaluating regex…";
        try
        {
            await worker.WaitAsync(cancellation);
            DebugResult analyzed;
            ElementDebugResult elementDebug;
            try
            {
                analyzed = await Task.Run(() => RegexDebugger.Analyze(pattern, text, options, full, trimValue, timeoutMs, cancellation), cancellation);
                elementDebug = await Task.Run(() => RegexElementDebugger.Analyze(pattern, text, options, full, trimValue, timeoutMs, cancellation), cancellation);
            }
            finally { worker.Release(); }
            if (closing || current != revision || cancellation.IsCancellationRequested)
                return;
            result = analyzed;
            elementResults = elementDebug.Elements;
            elementPreview = elementDebug;
            selectingElement = true;
            try
            {
                elements.ItemsSource = elementResults.Select(e => $"[{e.Start}..{e.Start + e.Length})  {e.Expression}  → " + (e.Captures.Count == 0 ? "no capture" : string.Join(", ", e.Captures.Take(12).Select(c => $"text[{c.Index}..{c.Index + c.Length})")))).ToArray();
            }
            finally { selectingElement = false; }
            groups = RegexGroupMap.Build(pattern);
            PropertiesDocument? doc = null;
            string diagnostics = "Engine: .NET, not Java. Debug shows captures, not backtracking. Positions are UTF-16 indices.\nPreview switches are not saved to the parser.\n";
            try
            {
                doc = new(source);
            }
            catch (FormatException ex) { diagnostics += ex.Message + "\n"; }
            captures.ItemsSource = analyzed.Rows.Select(row => new CaptureItem(row, row.Group > 0 ? doc?.Get($"token[{row.Group - 1}].name") ?? "" : "Entire match")).ToArray();
            status.Text = analyzed.Status + $" ({analyzed.Milliseconds:F1} ms)";
            if (elementDebug.IsPartial)
                status.Text = elementDebug.Summary;
            if (analyzed.Error is not null)
                diagnostics += analyzed.Error + $"\nRegex error position: {analyzed.ErrorOffset}\n";
            if (doc is not null)
            {
                diagnostics += string.Join("\n", ParserInspection.Inspect(source, path).Diagnostics) + "\n";
                if (doc.Get("regex") != pattern)
                    diagnostics += "The working regex differs from the configuration. Synchronize before file testing.\n";
                int count = analyzed.Rows.Where(r => r.Match == 1 && r.Group > 0).Select(r => r.Group).Distinct().Count();
                if (analyzed.Matches.Count > 0 && doc.Get("token.count") != count.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    diagnostics += "token.count differs from the number of capture groups.\n";
                var severity = analyzed.Rows.LastOrDefault(r => r.Match == 1 && r.Group > 0 && r.Success && doc.Get($"token[{r.Group - 1}].name") == doc.Get("event.deviceSeverity"));
                if (severity is not null)
                    try
                    {
                        diagnostics += AdvancedConfiguration.PreviewSeverity(doc, severity.Value) + "\n";
                    }
                    catch (FormatException ex) { diagnostics += ex.Message + "\n"; }
            }
            report.Text = diagnostics;
            report.Text += "\nElement debugger: " + elementDebug.Status;
            Paint();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closing && current == revision) { status.Text = ex.Message; result = null; elementPreview = null; Paint(); } }
    }
    private void Paint(DebugRow? selected = null, int? group = null)
    {
        if (elementPreview is { IsPartial: true, PartialSpan: { } span })
        {
            painter.Paint(Message, [new Highlight(span.Index, span.Length, 1)]);
            return;
        }
        painter.Paint(Message, result?.Rows.Where(r => r.Success).Select(r => new Highlight(r.Index, r.Length, r.Group == 0 ? 4 : r.Group - 1, r == selected || r.Group == group)) ?? []);
        if (selected is { Success: true, Index: >= 0 })
        {
            Message.Select(selected.Index, selected.Length);
            Message.ScrollToLine(Message.Document.GetLineByOffset(selected.Index).LineNumber);
        }
    }
    private void HighlightGroup()
    {
        if (selectingElement)
            return;
        if ((tabs.SelectedIndex == 1 || elementPreview?.IsPartial == true) && elementResults.Count > 0)
        {
            int caret = Pattern.CaretOffset;
            var selectedElement = elementResults.Where(e => e.Start <= caret && caret < e.Start + e.Length).OrderBy(e => e.Length).FirstOrDefault();
            if (selectedElement is not null)
            {
                selectingElement = true;
                try
                {
                    elements.SelectedIndex = elementResults.ToList().IndexOf(selectedElement);
                }
                finally { selectingElement = false; }
                SelectElement(selectRegex: false);
            }
            return;
        }
        if (result is null)
            return;
        int position = Pattern.CaretOffset;
        var group = RegexGroupMap.At(groups, position);
        Paint(group: group?.Number);
        if (group is null)
            return;
        var rows = result.Rows.Where(r => r.Group == group.Number && r.Success).ToArray();
        status.Text = rows.Length == 0 ? $"Group {group.Number}: no capture." : $"Group {group.Number}: {rows.Length} captures; " + string.Join(", ", rows.Take(12).Select(r => $"position {r.Index}, length {r.Length}"));
    }
    private void MoveCapture(int delta)
    {
        int count = captures.ItemCount;
        if (count > 0)
            captures.SelectedIndex = Math.Clamp(captures.SelectedIndex + delta, 0, count - 1);
    }
    internal void WritePattern()
    {
        var doc = new PropertiesDocument(Source.Text);
        doc.Set("regex", Pattern.Text);
        Ui.Replace(Source, doc.Source);
    }
    private async Task ReadPatternAsync()
    {
        var doc = new PropertiesDocument(Source.Text);
        if (!doc.Values.ContainsKey("regex"))
            throw new FormatException("The regex property is missing.");
        if (Pattern.Text != doc.Get("regex") && await Ui.Ask(this, "Replace regex", "Replace the working expression with the configuration value?", "Replace", "Cancel") != "Replace")
            return;
        Ui.Replace(Pattern, doc.Get("regex"));
        trim.IsChecked = doc.Get("trim.message") == "true";
    }
    private async Task<bool> SynchronizeAsync()
    {
        if (new PropertiesDocument(Source.Text).Get("regex") == Pattern.Text)
            return true;
        var choice = await Ui.Ask(this, "Synchronize regex", "The working regex differs from the configuration. Which value should this action use?", "Write working regex", "Use configuration", "Cancel");
        if (choice == "Write working regex")
            WritePattern();
        return choice is "Write working regex" or "Use configuration";
    }
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!IsDirty)
            return true;
        var answer = await Ui.Ask(this, "Unsaved changes", "Save your parser changes before continuing?", "Save", "Discard", "Cancel");
        if (answer == "Discard")
            return true;
        if (answer != "Save")
            return false;
        bool saved = false;
        await Ui.Guard(this, async () => saved = await SaveAsync(false));
        return saved;
    }
    private static FilePickerFileType PropertiesType => new("FlexConnector parser") { Patterns = ["*.properties"] };
    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync())
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Open parser",
            FileTypeFilter = [PropertiesType, FilePickerFileTypes.All]
        });
        if (files.Count == 0)
            return;
        string file = files[0].TryGetLocalPath() ?? throw new IOException("Select a local file.");
        if (new FileInfo(file).Length > 8 * 1024 * 1024)
            throw new IOException("Parser files are limited to 8 MB.");
        var doc = PropertiesDocument.Load(file);
        if (!ParserInspection.Inspect(doc.Source, file).Supported)
        {
            await new EventPreviewDialog(doc.Source, Message.Text, Path.GetDirectoryName(file), file).ShowDialog(this);
            return;
        }
        loading = true;
        try
        {
            Ui.Reset(Source, doc.Source);
            Ui.Reset(Pattern, doc.Get("regex"));
            trim.IsChecked = doc.Get("trim.message") == "true";
            path = file;
            savedSource = Source.Text;
            savedPattern = Pattern.Text;
        }
        finally { loading = false; }
        UpdateTitle();
        await AnalyzeAsync();
    }
    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (!await SynchronizeAsync())
            return false;
        string? target = path;
        if (saveAs || target is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new()
            {
                Title = "Save parser",
                SuggestedFileName = Path.GetFileName(path) ?? "parser.sdkrfilereader.properties",
                DefaultExtension = "properties",
                FileTypeChoices = [PropertiesType]
            });
            if (file is null)
                return false;
            target = file.TryGetLocalPath() ?? throw new IOException("Select a local file.");
        }
        new PropertiesDocument(Source.Text).Save(target);
        path = target;
        savedSource = Source.Text;
        savedPattern = Pattern.Text;
        UpdateTitle();
        status.Text = "Saved " + path;
        return true;
    }
    private async Task LoadMessageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Load message",
            FileTypeFilter = [FilePickerFileTypes.All]
        });
        if (files.Count == 0)
            return;
        await using var stream = await files[0].OpenReadAsync();
        if (stream.Length > 1024 * 1024)
            throw new IOException("Message file is too large (maximum 1 MB). Use log file testing.");
        using var reader = new StreamReader(stream);
        Ui.Replace(Message, await reader.ReadToEndAsync());
    }
    private async Task EditTokensAsync()
    {
        string? updated = await new TokensDialog(Source.Text, Pattern.Text, Message.Text, Options, entire.IsChecked == true, trim.IsChecked == true, (int)(timeout.Value ?? 250)).ShowDialog<string?>(this);
        if (updated is not null)
            Ui.Replace(Source, updated);
    }
    private async Task EditSubmessagesAsync()
    {
        if (!await SynchronizeAsync())
            return;
        string? updated = await new SubmessagesDialog(Source.Text, Message.Text).ShowDialog<string?>(this);
        if (updated is not null)
            Ui.Replace(Source, updated);
    }
    private async Task EditMappingAsync()
    {
        string? updated = await new MappingDialog(Source.Text, Message.Text, Pattern.Text, Options, entire.IsChecked == true, trim.IsChecked == true, (int)(timeout.Value ?? 250)).ShowDialog<string?>(this);
        if (updated is not null)
            Ui.Replace(Source, updated);
    }
    private async Task SuggestAsync()
    {
        string? proposal = await new SuggestParserDialog(Message.Text).ShowDialog<string?>(this);
        if (proposal is null)
            return;
        if (await Ui.Ask(this, "Use proposed parser", "Replace the working configuration and regex with the generated draft? Your file is unchanged until Save.", "Replace", "Cancel") != "Replace")
            return;
        Ui.Replace(Source, proposal);
        Ui.Replace(Pattern, new PropertiesDocument(proposal).Get("regex"));
    }
    private void MoveElement(int direction)
    {
        tabs.SelectedIndex = 1;
        if (elementResults.Count == 0)
        {
            status.Text = "No element preview. Run F5 and check Diagnostics for unsupported syntax or errors.";
            return;
        }
        elements.SelectedIndex = Math.Clamp(elements.SelectedIndex < 0 ? 0 : elements.SelectedIndex + direction, 0, elementResults.Count - 1);
        elements.ScrollIntoView(elements.SelectedItem!);
    }
    private void SelectElement(bool selectRegex = true)
    {
        if (selectingElement || elements.SelectedIndex < 0 || elements.SelectedIndex >= elementResults.Count)
            return;
        var element = elementResults[elements.SelectedIndex];
        selectingElement = true;
        try
        {
            // Caret-driven highlighting must never turn typed text into a replacement selection.
            if (selectRegex)
                Pattern.Select(element.Start, element.Length);
        }
        finally { selectingElement = false; }
        painter.Paint(Message, element.Captures.Select(s => new Highlight(s.Index, s.Length, 0, true)));
        if (element.Captures.FirstOrDefault() is { } first)
        {
            Message.Select(first.Index, first.Length);
            Message.ScrollToLine(Message.Document.GetLineByOffset(first.Index).LineNumber);
        }
        else
            Message.Select(0, 0);
        status.Text = (elementPreview?.IsPartial == true ? "PARTIAL · full match failed · " : "") + $"Element {elements.SelectedIndex + 1}/{elementResults.Count}: {element.Expression} — " + (element.Captures.Count == 0 ? "no capture in this preview" : $"{element.Captures.Count} capture(s); first position {element.Captures[0].Index}, length {element.Captures[0].Length}. Zero length means an assertion or empty match.");
    }
    private async Task EditAdvancedAsync()
    {
        string? updated = await new AdvancedDialog(Source.Text).ShowDialog<string?>(this);
        if (updated is not null)
            Ui.Replace(Source, updated);
    }
    private async Task OpenBatchAsync()
    {
        if (!await SynchronizeAsync())
            return;
        var batch = new BatchWindow(Source.Text, path is null ? null : Path.GetDirectoryName(path));
        await batch.ShowDialog(this);
        if (batch.ConfigurationSource != Source.Text)
        {
            Ui.Replace(Source, batch.ConfigurationSource);
            Ui.Replace(Pattern, new PropertiesDocument(batch.ConfigurationSource).Get("regex"));
        }
    }
    private sealed record CaptureItem(DebugRow Row, string Token)
    {
        public override string ToString() => $"Match {Row.Match} · group {Row.Group} ({Token}) · capture {Row.Capture} · @{Row.Index} +{Row.Length} · {(Row.Success ? Row.Value.Length == 0 ? "(empty capture)" : Row.Value[..Math.Min(500, Row.Value.Length)] : "— no capture")}";
    }
}
