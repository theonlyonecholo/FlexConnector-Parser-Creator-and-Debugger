using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using ParserCore;

namespace ParserDesigner.CrossPlatform;

/// <summary>Tests logs against an immutable parser snapshot. Only selected rows retain detailed captures.</summary>
internal sealed class BatchWindow : Window
{
    private sealed record LogRow(int Index, int Line, string Text, ParserResult? Result, string Mapping = "")
    {
        public override string ToString() => $"{Line} · {State(Result)} · {Result?.MessageId} · {Result?.Route} · {Mapping}\n{Text[..Math.Min(150, Text.Length)]}";
    }
    private readonly ListBox list = new(), captures = new();
    private readonly ComboBox filter = new() { ItemsSource = new[] { "All logs", "Matches", "Problems", "Untested", "Incomplete event processing", "No category values" }, SelectedIndex = 0, MinWidth = 130 };
    private readonly ComboBox encoding = new() { ItemsSource = new[] { "UTF-8", "Latin-1" }, SelectedIndex = 0 };
    private readonly TextBlock summary = Ui.Label("Open a log file. One physical line = one event, including empty lines.");
    private readonly TextEditor message = Ui.Editor(readOnly: true), description = Ui.Editor(readOnly: true);
    private readonly CaptureHighlighter painter;
    private readonly SemaphoreSlim batchWorker = new(1), detailWorker = new(1);
    private IReadOnlyList<LogEntry> logs = [];
    private ParserResult?[] results = [];
    private (bool Complete, bool Categorized, string Summary)?[] mappingResults = [];
    private readonly string? dependencyRoot;
    private ParserEvaluator evaluator;
    private CancellationTokenSource? runCancellation, detailCancellation;
    private int runVersion, detailVersion;
    private bool closed, refreshing;
    private BatchHighlighting? selectedHighlighting;
    public string ConfigurationSource
    {
        get; private set;
    }

    public BatchWindow(string source, string? dependencyRoot = null)
    {
        ConfigurationSource = source;
        this.dependencyRoot = dependencyRoot;
        evaluator = new(source);
        Ui.Setup(this, "Log workbench · Cross-platform", 1300, 860);
        painter = CaptureHighlighter.Attach(message);
        Ui.Tip(encoding, "Encoding used the next time a log file is opened. BOMs are recognized.");
        Ui.Tip(filter, "Filter by parser result; arrows navigate only visible logs.");
        Ui.Tip(list, "Original line number, result, message ID and selected route. Select a log for capture details.");
        var tools = Ui.Bar(Ui.AsyncButton("Open logs…", () => Ui.Guard(this, OpenAsync)), encoding,
            Ui.AsyncButton("Test all", () => Ui.Guard(this, RunAsync)), Ui.Button("Stop", () => runCancellation?.Cancel()),
            Ui.Button("Previous log", () => Move(-1)), Ui.Button("Next log", () => Move(1)), filter,
            Ui.AsyncButton("Edit submessage…", () => Ui.Guard(this, EditSubmessageAsync)),
            Ui.AsyncButton("Edit parser…", () => Ui.Guard(this, EditParserAsync)), Ui.Help(this, "batch"));
        tools.Children.Insert(tools.Children.Count - 1, Ui.AsyncButton("Event preview / tests…", () => Ui.Guard(this,
            () => new EventPreviewDialog(ConfigurationSource, (list.SelectedItem as LogRow)?.Text ?? "", dependencyRoot).ShowDialog(this))));
        tools.Children.Insert(tools.Children.Count - 1, Ui.AsyncButton("Category maps…", () => Ui.Guard(this, async () =>
        {
            if (dependencyRoot is null)
                throw new FormatException("Save the parser in the main window and reopen the log workbench first.");
            string? updated = await new CategoryMapDialog(ConfigurationSource, dependencyRoot).ShowDialog<string?>(this);
            if (updated is not null)
                await ApplyAsync(updated);
        })));
        Content = new Border
        {
            Padding = new Thickness(18),
            Child = Ui.Rows("Auto,Auto,*,Auto", tools, summary,
            Ui.Columns("*,*", list, Ui.Rows("Auto,2*,*,*", Ui.Label("LOG DETAIL · Green: passed · Red: failed stage / route · Orange: partial / uncertain\nSelect a capture to highlight it in blue."), message, captures, description)),
            Ui.Label("Edits return to the main configuration when this window closes. Use Save there to write to disk."))
        };
        filter.SelectionChanged += (_, _) => Refresh();
        list.SelectionChanged += async (_, _) => { if (!refreshing) await Ui.Guard(this, DetailAsync); };
        captures.SelectionChanged += (_, _) => Paint(captures.SelectedItem is CaptureItem item ? item.Capture : null);
        Closed += (_, _) => { closed = true; runVersion++; detailVersion++; runCancellation?.Cancel(); detailCancellation?.Cancel(); };
        Ui.HelpKey(this, "batch");
    }
    internal static string State(ParserResult? result) => result?.State switch
    {
        ParseState.Matched => "Match",
        ParseState.MainMismatch => "Main regex mismatch",
        ParseState.NoSubmessage => "No matching message ID / default",
        ParseState.SubmessageMismatch => "Submessage mismatch",
        ParseState.Error => "Error / timeout",
        _ => "Untested"
    };
    private async Task OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Open log file",
            FileTypeFilter = [new("Log files") { Patterns = ["*.log", "*.txt"] }, FilePickerFileTypes.All]
        });
        if (files.Count == 0)
            return;
        string file = files[0].TryGetLocalPath() ?? throw new IOException("Select a local file.");
        bool latin1 = encoding.SelectedIndex == 1;
        var loaded = await Task.Run(() => LogFile.Read(file, latin1));
        if (closed)
            return;
        Title = "Log workbench · " + files[0].Name;
        await LoadLogsAsync(loaded);
    }
    internal async Task LoadLogsAsync(IReadOnlyList<LogEntry> loaded)
    {
        runCancellation?.Cancel();
        detailCancellation?.Cancel();
        detailVersion++;
        logs = loaded;
        results = new ParserResult?[logs.Count];
        await RunAsync();
    }
    internal async Task RunAsync()
    {
        runCancellation?.Cancel();
        runCancellation?.Dispose();
        runCancellation = new();
        var cancellation = runCancellation.Token;
        int version = ++runVersion;
        detailCancellation?.Cancel();
        detailVersion++;
        var snapshot = evaluator;
        var entries = logs;
        var output = new ParserResult?[entries.Count];
        var mapped = new (bool Complete, bool Categorized, string Summary)?[entries.Count];
        string source = ConfigurationSource;
        mappingResults = mapped;
        results = output;
        Refresh();
        bool finished = false;
        try
        {
            await batchWorker.WaitAsync(cancellation);
            try
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        output[i] = snapshot.Evaluate(entries[i].Text, false, cancellation);
                        var preview = EventPipeline.Evaluate(source, entries[i].Text, dependencyRoot, cancellation: cancellation);
                        bool categorized = CategoryMap.Fields.Any(f => !string.IsNullOrEmpty(preview.Fields.GetValueOrDefault("event." + f)));
                        mapped[i] = (preview.Complete, categorized, preview.Complete
                            ? categorized ? "Event processed · categorized" : "Event processed · no categories"
                            : "Event processing incomplete");
                        if (i % 100 == 0)
                        {
                            int completed = i + 1;
                            Dispatcher.UIThread.Post(() => { if (!closed && !finished && version == runVersion) summary.Text = $"Testing {completed:N0} / {entries.Count:N0} logs…"; });
                        }
                    }
                }, cancellation);
            }
            finally { batchWorker.Release(); }
        }
        catch (OperationCanceledException) { }
        finally
        {
            finished = true;
            if (!closed && version == runVersion)
            {
                int tested = output.Count(r => r is not null), matched = output.Count(r => r?.Success == true);
                summary.Text = $"{entries.Count:N0} logs · {matched:N0} regex matches · {tested - matched:N0} regex problems · {entries.Count - tested:N0} untested\n" +
                    $"{mapped.Count(r => r?.Complete == true):N0} complete event previews · {mapped.Count(r => r?.Categorized == true):N0} with category values";
                Refresh();
            }
        }
    }
    private void Refresh()
    {
        if (closed)
            return;
        int previous = (list.SelectedItem as LogRow)?.Index ?? -1;
        var visible = Enumerable.Range(0, logs.Count).Where(i => filter.SelectedIndex switch
        {
            1 => results[i]?.Success == true,
            2 => results[i] is { Success: false },
            3 => results[i] is null,
            4 => mappingResults.ElementAtOrDefault(i)?.Complete == false,
            5 => mappingResults.ElementAtOrDefault(i)?.Categorized == false,
            _ => true
        }).Select(i => new LogRow(i, logs[i].LineNumber, logs[i].Text, results[i], mappingResults.ElementAtOrDefault(i)?.Summary ?? "")).ToArray();
        refreshing = true;
        list.ItemsSource = visible;
        list.SelectedItem = visible.FirstOrDefault(r => r.Index == previous) ?? visible.FirstOrDefault();
        refreshing = false;
        _ = Ui.Guard(this, DetailAsync);
    }
    private void Move(int delta)
    {
        if (list.ItemCount > 0)
            list.SelectedIndex = Math.Clamp(list.SelectedIndex + delta, 0, list.ItemCount - 1);
    }
    private async Task DetailAsync()
    {
        detailCancellation?.Cancel();
        detailCancellation?.Dispose();
        detailCancellation = new();
        int version = ++detailVersion;
        var cancellation = detailCancellation.Token;
        selectedHighlighting = null;
        captures.ItemsSource = null;
        painter.Paint(message, []);
        if (list.SelectedItem is not LogRow row)
        {
            message.Text = "";
            description.Text = "No log selected.";
            return;
        }
        message.Text = row.Text;
        description.Text = "Evaluating selected log…";
        var snapshot = evaluator;
        string source = ConfigurationSource;
        try
        {
            await detailWorker.WaitAsync(cancellation);
            ParserResult detail;
            BatchHighlighting highlighting;
            EventPreview eventPreview;
            try
            {
                detail = await Task.Run(() => snapshot.Evaluate(row.Text, true, cancellation), cancellation);
                highlighting = await Task.Run(() => BatchHighlighting.Analyze(source, row.Text, detail, cancellation), cancellation);
                eventPreview = await Task.Run(() => EventPipeline.Evaluate(source, row.Text, dependencyRoot, cancellation: cancellation), cancellation);
            }
            finally { detailWorker.Release(); }
            if (closed || version != detailVersion || cancellation.IsCancellationRequested)
                return;
            selectedHighlighting = highlighting;
            captures.ItemsSource = detail.Captures.Select(c => new CaptureItem(c)).ToArray();
            description.Text = $"Line {row.Line} · {State(detail)}\n{highlighting.Explanation}\n{detail.Description}\nMessage ID: {detail.MessageId}\nRoute: {detail.Route}\n" +
                string.Join("\n", detail.Attempts.Select(a => $"submessage[{a.Submessage}].pattern[{a.Pattern}]: {(a.Matched ? "MATCH" : "mismatch")}")) +
                $"\n\nEvent processing: {(eventPreview.Complete ? "complete local preview" : "INCOMPLETE")}\n" +
                string.Join("\n", eventPreview.Trace.Where(t => t.Stage.StartsWith("extraprocessor") || t.Stage == "Categorizer")
                    .Select(t => $"{t.Stage} · {t.Field} · {t.Status} · {t.After}")) +
                "\n" + string.Join("\n", CategoryMap.Fields.Select(f => $"event.{f}: {eventPreview.Fields.GetValueOrDefault("event." + f) ?? "(unset)"}")) +
                "\n" + string.Join("\n", eventPreview.Diagnostics) +
                "\nHighlight colors describe regex routing only. Category values and processing failures are shown above.";
            Paint();
        }
        catch (OperationCanceledException) { }
    }
    private void Paint(ParserCapture? selected = null)
    {
        IEnumerable<Highlight> spans = selectedHighlighting?.Spans ?? [];
        if (selected is not null)
            spans = spans.Append(new(selected.Index, selected.Length, 0, Selected: true));
        painter.Paint(message, spans);
        if (selected is not null)
        {
            message.Select(selected.Index, selected.Length);
            message.ScrollToLine(message.Document.GetLineByOffset(selected.Index).LineNumber);
        }
    }
    private async Task EditSubmessageAsync()
    {
        var row = list.SelectedItem as LogRow;
        string text = row?.Text ?? "";
        var snapshot = evaluator;
        var detail = await Task.Run(() => snapshot.Evaluate(text));
        if (closed)
            return;
        string? updated = await new SubmessagesDialog(ConfigurationSource, text, detail.UsedSubmessage ?? detail.Attempts.FirstOrDefault()?.Submessage ?? -1,
            detail.UsedPattern ?? detail.Attempts.FirstOrDefault()?.Pattern ?? -1).ShowDialog<string?>(this);
        if (updated is not null)
            await ApplyAsync(updated);
    }
    private async Task EditParserAsync()
    {
        var editor = Ui.Editor(ConfigurationSource);
        var dialog = new Window();
        Ui.Setup(dialog, "Edit parser and retest", 1000, 760);
        dialog.Content = new Border
        {
            Padding = new Thickness(18),
            Child = Ui.Rows("Auto,*,Auto",
            Ui.Label("Full .properties configuration. Apply validates references and regexes, then retests the file."), editor,
            Ui.Bar(Ui.AsyncButton("Apply and retest", () => Ui.Guard(dialog, () => { _ = new ParserEvaluator(editor.Text); dialog.Close(editor.Text); return Task.CompletedTask; })),
                Ui.Button("Cancel", () => dialog.Close()), Ui.Help(dialog, "properties")))
        };
        string? updated = await dialog.ShowDialog<string?>(this);
        if (updated is not null)
            await ApplyAsync(updated);
    }
    internal async Task ApplyAsync(string source)
    {
        var validated = new ParserEvaluator(source);
        evaluator = validated;
        ConfigurationSource = source;
        await RunAsync();
    }
    private sealed record CaptureItem(ParserCapture Capture)
    {
        public override string ToString() => $"{Capture.Stage} · {Capture.Name} · @{Capture.Index} +{Capture.Length} · {Capture.Value}";
    }
}
