using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using ParserCore;

namespace ParserDesigner.CrossPlatform;

/// <summary>Edits ordered submessage definitions while retaining unknown properties in the core model.</summary>
internal sealed class SubmessagesDialog : Window
{
    private sealed record Node(int Message, int? Pattern, string Label)
    {
        public override string ToString() => Label;
    }
    private readonly string source;
    private readonly SubmessageConfiguration model;
    private readonly ListBox nodes = new();
    private readonly ComboBox body, idToken;
    private readonly TextBox messageId;
    private readonly CheckBox trim, fallback;
    private readonly Dictionary<string, TextEditor> fields = [];
    private readonly TextEditor sample, originalLog, result = Ui.Editor(readOnly: true);
    private readonly TextBlock bodyStatus = Ui.Label("SUBMESSAGE INPUT · press F5 to extract the configured token");
    private readonly CaptureHighlighter painter;
    private readonly SemaphoreSlim worker = new(1);
    private CancellationTokenSource? pending;
    private ParserResult? evaluated;
    private Node? selected;
    private bool loading, closed;
    private int revision;
    private readonly ListBox captures = new(), elements = new();
    private readonly TextBlock selectionTitle = Ui.Label("Select a pattern", 16), previewStatus = Ui.Label("Test the draft to inspect this pattern.");
    private readonly TextBlock routeStatus = Ui.Label("Actual route: not tested");
    private readonly CheckBox automatic = new() { Content = "Automatic preview", IsChecked = true };
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly TabControl previewTabs;
    private DebugResult? captureResult;
    private IReadOnlyList<RegexElement> elementResults = [];
    private ElementDebugResult? elementPreview;
    private bool selectingElement;
    private sealed record CaptureItem(DebugRow Row, string Field)
    {
        public override string ToString() => $"Group {Row.Group} · {Field} · " + (Row.Success ? $"[{Row.Index}..{Row.Index + Row.Length}) · {Row.Value[..Math.Min(100, Row.Value.Length)].Replace("\n", "\\n").Replace("\r", "\\r")}" : "no capture");
    }

    public SubmessagesDialog(string source, string log, int selectedMessage = -1, int selectedPattern = -1)
    {
        this.source = source;
        model = SubmessageConfiguration.Load(source);
        Ui.Setup(this, "Submessages · pattern workbench", 1280, 940);
        MinWidth = 1000;
        MinHeight = 760;
        string[] declaredTokens = EventMapping.Tokens(source).Keys.ToArray();
        ComboBox TokenPicker(string value, string hint)
        {
            // Retain an unknown configured value so opening the editor never silently replaces it.
            var picker = new ComboBox
            {
                ItemsSource = new[] { "" }.Concat(declaredTokens).Append(value).Distinct(StringComparer.Ordinal).ToArray(),
                SelectedItem = value,
                PlaceholderText = "Select a declared token…",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
            Ui.Tip(picker, hint);
            return picker;
        }
        body = TokenPicker(model.BodyToken, "Declared main token containing the body: submessage.token. An existing unknown value is retained until you select a valid token.");
        idToken = TokenPicker(model.IdToken, "Declared main token used for routing: submessage.messageid.token. Select the empty entry for no ID token.");
        trim = new()
        {
            Content = "Trim submessage",
            IsChecked = model.Trim
        };
        Ui.Tip(trim, "Trim spaces/tabs around the body before matching.");
        messageId = Ui.Input("", "Exact, case-sensitive value of the Message ID token, such as LOGIN. This is not a display name. For regex-only selection use a default definition with multiple patterns.");
        fallback = new()
        {
            Content = "Default for unknown IDs"
        };
        Ui.Tip(fallback, "Omit this definition's messageid property. For an unknown ID (or no ID token), try its patterns in order until the first match. A known ID with mismatching patterns does not fall back. An empty messageid property is not the same as omitting it.");
        Ui.Tip(automatic, "Refresh routing and highlights after a typing pause, just like the main window. Turn off to test manually with F5.");
        originalLog = Ui.Editor(log);
        sample = Ui.Editor(readOnly: true);
        painter = CaptureHighlighter.Attach(sample);
        Ui.Tip(sample, "Raw value of submessage.token extracted by the main regex. Highlights use token-relative positions and refresh automatically while Automatic preview is enabled.");
        Ui.Tip(originalLog, "Full event used by the main parser to extract the body and message ID tokens. Editing refreshes the extracted input and preview.");
        var mappings = new StackPanel { Spacing = 8 };
        var advanced = new StackPanel { Spacing = 8 };
        var hints = new Dictionary<string, string>
        {
            ["regex"] = "Decoded regex for the entire body. Highlights refresh after a typing pause. Click a group to locate its captured text.",
            ["fields"] = "Comma-separated event fields in group order. $1 is the first submessage group.",
            ["types"] = "Comma-separated ArcSight types in field order. Not converted locally.",
            ["formats"] = "Formats in field order. null means no format. Not evaluated locally.",
            ["mappings"] = "ArcSight expressions in field order, separated by | by default. Not executed locally.",
            ["mappings.delimiter"] = "Custom mappings separator instead of |.",
            ["extramappings"] = "Additional event.field=expression assignments, separated by | by default.",
            ["extramappings.delimiter"] = "Custom extramappings separator instead of |."
        };
        foreach (var (key, hint) in hints)
        {
            var input = Ui.Editor();
            input.Height = key == "regex" ? 90 : 60;
            Ui.Tip(input, hint);
            fields[key] = input;
            if (key != "regex")
            {
                var page = key is "fields" or "types" or "formats" ? mappings : advanced;
                page.Children.Add(Ui.Stack(Ui.Label(key), input));
            }
            input.TextChanged += (_, _) => Changed();
        }
        fields["regex"].TextArea.Caret.PositionChanged += (_, _) => HighlightGroup();
        var tools = Ui.Bar(
            Ui.Button("+ Submessage", () =>
            {
                Store();
                var message = new SubmessageDefinition { MessageId = "NEW_" + model.Messages.Count };
                message.Patterns.Add(SubmessageConfiguration.NewPattern());
                model.Messages.Add(message);
                Refresh(model.Messages.Count - 1, 0);
            }, "Add a new message ID with its first pattern."),
            Ui.Button("+ Pattern", () =>
            {
                if (selected is null)
                    return;
                Store();
                var patterns = model.Messages[selected.Message].Patterns;
                patterns.Add(SubmessageConfiguration.NewPattern());
                Refresh(selected.Message, patterns.Count - 1);
            }, "Add an alternative body regex to the selected definition."),
            Ui.Button("Remove", Remove, "Remove the selected pattern or entire definition."),
            Ui.Button("↑ Earlier", () => Move(-1), "The first matching pattern wins."),
            Ui.Button("↓ Later", () => Move(1), "Move the selected definition or pattern later in the order."));
        previewTabs = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = Ui.Label("Captures"), Content = captures },
                new TabItem { Header = Ui.Label("Regex elements"), Content = elements },
                new TabItem { Header = Ui.Label("Fields / types"), Content = new ScrollViewer { Content = mappings } },
                new TabItem { Header = Ui.Label("Advanced mappings"), Content = new ScrollViewer { Content = advanced } },
                new TabItem { Header = Ui.Label("Route / diagnostics"), Content = result },
                new TabItem { Header = Ui.Label("Original log"), Content = originalLog }
            }
        };
        var controls = Ui.Bar(Ui.AsyncButton("Test log with these changes", RunPreviewAsync, "F5: test routing and the selected pattern independently."),
            Ui.Button("◀ Element", () => MoveElement(-1)), Ui.Button("Element ▶", () => MoveElement(1)), automatic);
        var right = Ui.Rows("Auto,Auto,85,Auto,90,Auto,Auto,*",
            selectionTitle, bodyStatus, sample,
            Ui.Label("SELECTED REGEX · click a group or use Regex elements; long expressions wrap"), fields["regex"],
            controls, Ui.Stack(routeStatus, previewStatus), previewTabs);
        var navigation = Ui.Rows("Auto,Auto,Auto,Auto,*", Ui.Label("Definitions & pattern order", 16),
            tools, Ui.Stack(Ui.Label("Message ID"), messageId, fallback),
            Ui.Label("Select a pattern below to edit it. The first matching pattern wins.\nDefault = no messageid property; tries its patterns for unknown IDs.\nApp limits: 1,000 definitions; 1,000 patterns per definition; 5,000 patterns total."), nodes);
        Content = new Border
        {
            Padding = new Thickness(16),
            Child = Ui.Rows("Auto,*,Auto",
            Ui.Columns("*,*,Auto", Ui.Stack(Ui.Label("Body token"), body), Ui.Stack(Ui.Label("Message ID token (optional)"), idToken), trim),
            Ui.Columns("250,*", Ui.Card(navigation), Ui.Card(right)),
            Ui.Bar(Ui.AsyncButton("Apply to configuration", () => Ui.Guard(this, () => { Close(BuildSource()); return Task.CompletedTask; })),
                Ui.Button("Cancel", () => Close()), Ui.Help(this, "submessages")))
        };
        nodes.SelectionChanged += (_, _) => SelectNode();
        // TextBox.TextChanged is deferred; observe the property synchronously so loading
        // another node cannot cancel a test started immediately after that load.
        messageId.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Changed(); };
        body.SelectionChanged += (_, _) => InputChanged();
        idToken.SelectionChanged += (_, _) => { Changed(); if (!loading && !closed) debounce.Start(); };
        trim.IsCheckedChanged += (_, _) => Changed();
        fallback.IsCheckedChanged += (_, _) => { messageId.IsEnabled = fallback.IsChecked != true; Changed(); };
        originalLog.TextChanged += (_, _) => InputChanged();
        captures.SelectionChanged += (_, _) => { if (captures.SelectedItem is CaptureItem item) HighlightCapture(item.Row); };
        elements.SelectionChanged += (_, _) => SelectElement();
        automatic.IsCheckedChanged += (_, _) => { debounce.Stop(); if (automatic.IsChecked == true) debounce.Start(); };
        debounce.Tick += async (_, _) => { debounce.Stop(); await RunPreviewAsync(); };
        KeyDown += async (_, e) => { if (e.Key == Key.F5) { e.Handled = true; await RunPreviewAsync(); } };
        Closed += (_, _) => { closed = true; debounce.Stop(); pending?.Cancel(); };
        Ui.HelpKey(this, "submessages");
        Refresh(selectedMessage >= 0 ? selectedMessage : 0, selectedPattern >= 0 ? selectedPattern : 0);
        Opened += async (_, _) => await RunPreviewAsync();
    }
    private void Changed()
    {
        if (loading)
            return;
        revision++;
        debounce.Stop();
        pending?.Cancel();
        evaluated = null;
        captureResult = null;
        elementResults = [];
        elementPreview = null;
        captures.ItemsSource = null;
        elements.ItemsSource = null;
        painter.Paint(sample, []);
        // Pattern edits invalidate highlights, not the independently extracted main token.
        sample.Select(0, 0);
        previewStatus.Text = automatic.IsChecked == true ? "Draft changed. Updating routing and highlights…" : "Draft changed. Press F5 to refresh routing and highlights.";
        result.Text = "Draft changed. Test again to refresh the route and field highlights.";
        routeStatus.Text = "Actual route: preview out of date";
        if (!closed && automatic.IsChecked == true)
            debounce.Start();
    }
    private void InputChanged()
    {
        if (loading)
            return;
        Changed();
        sample.Text = "";
        bodyStatus.Text = "SUBMESSAGE INPUT · extracting selected token…";
        if (!closed)
            debounce.Start();
    }
    private void Store()
    {
        model.BodyToken = body.SelectedItem as string ?? "";
        model.IdToken = idToken.SelectedItem as string ?? "";
        model.Trim = trim.IsChecked == true;
        if (selected is null)
            return;
        var definition = model.Messages[selected.Message];
        definition.MessageId = fallback.IsChecked == true ? null : messageId.Text ?? "";
        if (selected.Pattern is not int p)
            return;
        foreach (var (key, input) in fields)
        {
            if (key != "regex" && input.Text.Length == 0)
                definition.Patterns[p].Remove(key);
            else
                definition.Patterns[p][key] = input.Text;
        }
    }
    private void SelectNode()
    {
        if (loading)
            return;
        Store();
        selected = nodes.SelectedItem as Node;
        LoadNode();
        Changed();
    }
    private void LoadNode()
    {
        loading = true;
        try
        {
            var definition = selected is null ? null : model.Messages[selected.Message];
            selectionTitle.Text = selected is null ? "Add a submessage to get started" : $"submessage[{selected.Message}] · {definition?.MessageId ?? "DEFAULT"}" + (selected.Pattern is int selectedPattern ? $" · pattern[{selectedPattern}]" : " · select a pattern below");
            messageId.Text = definition?.MessageId ?? "";
            fallback.IsChecked = definition?.MessageId is null;
            messageId.IsEnabled = definition is not null && fallback.IsChecked != true;
            fallback.IsEnabled = definition is not null;
            foreach (var (key, input) in fields)
            {
                input.IsEnabled = selected?.Pattern is not null;
                Ui.Reset(input, selected?.Pattern is int p ? definition!.Patterns[p].GetValueOrDefault(key, "") : "");
            }
        }
        finally { loading = false; }
    }
    private void Refresh(int message, int? pattern)
    {
        loading = true;
        var items = new List<Node>();
        for (int i = 0; i < model.Messages.Count; i++)
        {
            items.Add(new(i, null, $"submessage[{i}] · {model.Messages[i].MessageId ?? "DEFAULT"}"));
            for (int p = 0; p < model.Messages[i].Patterns.Count; p++)
                items.Add(new(i, p, $"    pattern[{p}]"));
        }
        nodes.ItemsSource = items;
        selected = items.FirstOrDefault(n => n.Message == message && n.Pattern == pattern) ?? items.FirstOrDefault();
        nodes.SelectedItem = selected;
        loading = false;
        LoadNode();
        Changed();
    }
    private void Remove()
    {
        if (selected is null)
            return;
        Store();
        int index = selected.Message;
        if (selected.Pattern is int p)
            model.Messages[index].Patterns.RemoveAt(p);
        else
            model.Messages.RemoveAt(index);
        selected = null;
        Refresh(Math.Min(index, model.Messages.Count - 1), null);
    }
    private void Move(int direction)
    {
        if (selected is null)
            return;
        Store();
        int m = selected.Message;
        if (selected.Pattern is int p)
        {
            var list = model.Messages[m].Patterns;
            int target = p + direction;
            if (target < 0 || target >= list.Count)
                return;
            (list[p], list[target]) = (list[target], list[p]);
            Refresh(m, target);
        }
        else
        {
            int target = m + direction;
            if (target < 0 || target >= model.Messages.Count)
                return;
            (model.Messages[m], model.Messages[target]) = (model.Messages[target], model.Messages[m]);
            Refresh(target, null);
        }
    }
    internal string BuildSource(bool validate = true)
    {
        Store();
        return model.Apply(source, validate);
    }
    internal async Task TestAsync()
    {
        debounce.Stop();
        string configuration = BuildSource(validate: false), text = originalLog.Text;
        int message = selected?.Message ?? -1, pattern = selected?.Pattern ?? -1;
        string[] names = fields["fields"].Text.Split(',');
        pending?.Cancel();
        pending?.Dispose();
        pending = new();
        var cancellation = pending.Token;
        int current = ++revision;
        result.Text = "Testing draft…";
        await worker.WaitAsync(cancellation);
        SubmessageDebugResult debug;
        try
        {
            debug = await Task.Run(() => SubmessageDebugger.Analyze(configuration, text, message, pattern, cancellation), cancellation);
        }
        finally { worker.Release(); }
        if (closed || current != revision || cancellation.IsCancellationRequested)
            return;
        evaluated = debug.Route;
        sample.Text = debug.BodyText ?? "";
        bodyStatus.Text = debug.BodyText is null
            ? "SUBMESSAGE INPUT · " + debug.Status
            : $"SUBMESSAGE INPUT · {body.SelectedItem} · {debug.BodyText.Length} characters · positions relative to token";
        // Core/batch results retain original-log positions. This editor displays only the body token.
        int offset = debug.BodyOffset;
        if (debug.Captures is { } localCaptures)
            debug = debug with
            {
                Captures = localCaptures with
                {
                    Matches = localCaptures.Matches.Select(s => s with { Index = s.Index - offset }).ToArray(),
                    Rows = localCaptures.Rows.Select(r => r.Success ? r with { Index = r.Index - offset } : r).ToArray()
                }
            };
        if (debug.Elements is { } localElements)
            debug = debug with
            {
                Elements = localElements with
                {
                    PartialSpan = localElements.PartialSpan is { } span ? span with
                    {
                        Index = span.Index - offset
                    } : null,
                    Elements = localElements.Elements.Select(e => e with
                    {
                        Captures = e.Captures.Select(s => s with { Index = s.Index - offset }).ToArray()
                    }).ToArray()
                }
            };
        routeStatus.Text = "Actual route: " + debug.Route.Route + " · " + debug.Route.State;
        captureResult = debug.Captures;
        elementResults = debug.Elements?.Elements ?? [];
        elementPreview = debug.Elements;
        result.Text = "ACTUAL ROUTE: " + debug.Route.Route + "\n" + debug.Route.Description + "\n"
            + string.Join("\n", debug.Route.Attempts.Select(a => $"submessage[{a.Submessage}].pattern[{a.Pattern}]: {(a.Matched ? "MATCH" : "mismatch")}"))
            + "\n\nSELECTED PATTERN: " + debug.Status + "\n\nELEMENT PREVIEW: " + (debug.Elements?.Status ?? "Select a pattern with a captured body.");
        previewStatus.Text = debug.Route.State == ParseState.Error ? "Unable to test draft: " + debug.Route.Description
            : debug.Elements?.IsPartial == true ? debug.Elements.Summary : debug.Status;
        captures.ItemsSource = captureResult?.Rows.Select(r => new CaptureItem(r,
            r.Group == 0 ? "Entire body match" : r.Group <= names.Length ? names[r.Group - 1].Trim() : "$" + r.Group)).ToArray();
        elements.ItemsSource = elementResults.Select(e => $"[{e.Start}..{e.Start + e.Length}) {e.Expression} → "
            + (e.Captures.Count == 0 ? "no final capture" : string.Join(", ", e.Captures.Take(10).Select(c => $"text[{c.Index}..{c.Index + c.Length})")))).ToArray();
        Paint();
        // Editing can leave the caret inside a group without another caret event.
        // Restore its visual association after the asynchronous preview completes.
        if (fields["regex"].IsKeyboardFocusWithin)
            HighlightGroup();
    }
    private void HighlightGroup()
    {
        if (loading || selectingElement || captureResult is null)
            return;
        int caret = fields["regex"].CaretOffset;
        if (previewTabs.SelectedIndex == 1 || elementPreview?.IsPartial == true)
        {
            var element = elementResults.Where(e => e.Start <= caret && caret < e.Start + e.Length).OrderBy(e => e.Length).FirstOrDefault();
            if (element is not null)
                elements.SelectedIndex = elementResults.ToList().IndexOf(element);
            return;
        }
        var group = RegexGroupMap.At(RegexGroupMap.Build(fields["regex"].Text), caret);
        Paint(group?.Number);
        if (group is null)
        {
            sample.Select(0, 0);
            return;
        }
        var row = captureResult.Rows.FirstOrDefault(r => r.Group == group.Number && r.Success);
        if (row is not null)
            Locate(row.Index, row.Length);
        else
            sample.Select(0, 0);
        previewStatus.Text = row is null ? $"Group {group.Number}: no capture." : $"Group {group.Number}: position {row.Index}, length {row.Length}.";
    }
    private async Task RunPreviewAsync()
    {
        try
        {
            await TestAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (closed)
                return;
            previewStatus.Text = "Unable to test draft: " + ex.Message;
            bodyStatus.Text = "SUBMESSAGE INPUT · " + ex.Message;
            result.Text = ex.Message;
        }
    }
    private void Paint(int? group = null, DebugRow? selectedCapture = null)
    {
        if (elementPreview is { IsPartial: true, PartialSpan: { } span })
        {
            painter.Paint(sample, [new Highlight(span.Index, span.Length, 1)]);
            return;
        }
        painter.Paint(sample, captureResult?.Rows.Where(r => r.Success).Select(r =>
            new Highlight(r.Index, r.Length, r.Group == 0 ? 4 : r.Group - 1, r == selectedCapture || r.Group == group)) ?? []);
    }
    private void HighlightCapture(DebugRow row)
    {
        Paint(selectedCapture: row);
        if (row.Success)
            Locate(row.Index, row.Length);
        else
            sample.Select(0, 0);
        previewStatus.Text = row.Success ? $"Group {row.Group}: position {row.Index}, length {row.Length}. Raw value; no ArcSight conversion." : $"Group {row.Group}: no capture.";
    }
    private void Locate(int index, int length)
    {
        sample.Select(index, length);
        sample.ScrollToLine(sample.Document.GetLineByOffset(index).LineNumber);
    }
    private void MoveElement(int direction)
    {
        previewTabs.SelectedIndex = 1;
        if (elementResults.Count == 0)
        {
            previewStatus.Text = "Press F5 to create element preview. See Route / diagnostics for errors or unsupported syntax.";
            return;
        }
        elements.SelectedIndex = Math.Clamp(elements.SelectedIndex < 0 ? 0 : elements.SelectedIndex + direction, 0, elementResults.Count - 1);
        elements.ScrollIntoView(elements.SelectedItem!);
    }
    private void SelectElement()
    {
        if (elements.SelectedIndex < 0 || elements.SelectedIndex >= elementResults.Count)
            return;
        var element = elementResults[elements.SelectedIndex];
        selectingElement = true;
        try
        {
            fields["regex"].Select(element.Start, element.Length);
            fields["regex"].ScrollToLine(fields["regex"].Document.GetLineByOffset(element.Start).LineNumber);
        }
        finally { selectingElement = false; }
        painter.Paint(sample, element.Captures.Select(c => new Highlight(c.Index, c.Length, 0, true)));
        if (element.Captures.FirstOrDefault() is { } span)
            Locate(span.Index, span.Length);
        else
            sample.Select(0, 0);
        previewStatus.Text = (elementPreview?.IsPartial == true ? "PARTIAL · full match failed · " : "") + $"Element {elements.SelectedIndex + 1}/{elementResults.Count}: {element.Expression} — "
            + (element.Captures.Count == 0 ? "no final capture" : $"{element.Captures.Count} span(s); first position {element.Captures[0].Index}, length {element.Captures[0].Length}. Zero length means an assertion or empty match.");
    }
}
