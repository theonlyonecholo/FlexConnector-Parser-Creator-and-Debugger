using Avalonia;
using Avalonia.Controls;
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
    private readonly TextBox body, idToken, messageId;
    private readonly CheckBox trim, fallback;
    private readonly Dictionary<string, TextEditor> fields = [];
    private readonly TextEditor sample, result = Ui.Editor(readOnly: true);
    private readonly CaptureHighlighter painter;
    private readonly SemaphoreSlim worker = new(1);
    private CancellationTokenSource? pending;
    private ParserResult? evaluated;
    private Node? selected;
    private bool loading, closed;
    private int revision;

    public SubmessagesDialog(string source, string log, int selectedMessage = -1, int selectedPattern = -1)
    {
        this.source = source;
        model = SubmessageConfiguration.Load(source);
        Ui.Setup(this, "Submessages · definitions and pattern order", 1220, 880);
        body = Ui.Input(model.BodyToken, "Main token containing the body: submessage.token.");
        idToken = Ui.Input(model.IdToken, "Main token whose value selects the definition: submessage.messageid.token.");
        trim = new()
        {
            Content = "Trim submessage",
            IsChecked = model.Trim
        };
        Ui.Tip(trim, "Trim spaces/tabs around the body before matching.");
        messageId = Ui.Input("", "Exact message ID handled by this definition, such as LOGIN.");
        fallback = new()
        {
            Content = "Default for unknown IDs"
        };
        Ui.Tip(fallback, "Only unknown IDs use this definition. A known ID with mismatching patterns does not fall back.");
        sample = Ui.Editor(log);
        painter = CaptureHighlighter.Attach(sample);
        Ui.Tip(sample, "Full original log, including the main header. Test the draft before applying it.");
        var editor = new StackPanel { Spacing = 8 };
        editor.Children.Add(Ui.Columns("130,*", Ui.Label("Message ID"), messageId));
        editor.Children.Add(fallback);
        var hints = new Dictionary<string, string>
        {
            ["regex"] = "Decoded regex for the entire body. Click a group after testing to highlight its field.",
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
            editor.Children.Add(Ui.Columns("155,*", Ui.Label(key), input));
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
        var right = Ui.Rows("*,Auto,100,Auto,110", new ScrollViewer { Content = editor }, Ui.Label("Selected full log"), sample,
            Ui.Bar(Ui.AsyncButton("Test log with these changes", () => Ui.Guard(this, TestAsync))), result);
        Content = new Border
        {
            Padding = new Thickness(16),
            Child = Ui.Rows("Auto,*,Auto",
            Ui.Columns("Auto,*,Auto,*,Auto", Ui.Label("Body token"), body, Ui.Label("Message ID token"), idToken, trim),
            Ui.Columns("280,*", Ui.Rows("Auto,*", tools, nodes), right),
            Ui.Bar(Ui.AsyncButton("Apply to configuration", () => Ui.Guard(this, () => { Close(BuildSource()); return Task.CompletedTask; })),
                Ui.Button("Cancel", () => Close()), Ui.Help(this, "submessages")))
        };
        nodes.SelectionChanged += (_, _) => SelectNode();
        // TextBox.TextChanged is deferred; observe the property synchronously so loading
        // another node cannot cancel a test started immediately after that load.
        foreach (var box in new[] { body, idToken, messageId })
            box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Changed(); };
        trim.IsCheckedChanged += (_, _) => Changed();
        fallback.IsCheckedChanged += (_, _) => { messageId.IsEnabled = fallback.IsChecked != true; Changed(); };
        sample.TextChanged += (_, _) => Changed();
        Closed += (_, _) => { closed = true; pending?.Cancel(); };
        Ui.HelpKey(this, "submessages");
        Refresh(selectedMessage >= 0 ? selectedMessage : 0, selectedPattern >= 0 ? selectedPattern : null);
    }
    private void Changed()
    {
        if (loading)
            return;
        revision++;
        pending?.Cancel();
        evaluated = null;
        painter.Paint(sample, []);
        result.Text = "Draft changed. Test again to refresh the route and field highlights.";
    }
    private void Store()
    {
        model.BodyToken = body.Text ?? "";
        model.IdToken = idToken.Text ?? "";
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
    internal string BuildSource()
    {
        Store();
        return model.Apply(source);
    }
    internal async Task TestAsync()
    {
        string configuration = BuildSource(), text = sample.Text;
        pending?.Cancel();
        pending?.Dispose();
        pending = new();
        var cancellation = pending.Token;
        int current = ++revision;
        result.Text = "Testing draft…";
        await worker.WaitAsync(cancellation);
        ParserResult detail;
        try
        {
            detail = await Task.Run(() => new ParserEvaluator(configuration).Evaluate(text, true, cancellation), cancellation);
        }
        finally { worker.Release(); }
        if (closed || current != revision || cancellation.IsCancellationRequested)
            return;
        evaluated = detail;
        result.Text = detail.Route + "\n" + detail.Description + "\n" + string.Join("\n", detail.Attempts.Select(a => $"submessage[{a.Submessage}].pattern[{a.Pattern}]: {(a.Matched ? "MATCH" : "mismatch")}"));
        painter.Paint(sample, detail.Captures.Where(c => c.Name != "Entire match").Select((c, i) => new Highlight(c.Index, c.Length, i)));
    }
    private void HighlightGroup()
    {
        if (evaluated is null || selected?.Pattern is not int p)
            return;
        if (evaluated.UsedSubmessage != selected.Message || evaluated.UsedPattern != p)
        {
            result.Text = "The selected pattern was not used. " + evaluated.Route + "\n" + evaluated.Description;
            return;
        }
        var group = RegexGroupMap.At(RegexGroupMap.Build(fields["regex"].Text), fields["regex"].CaretOffset);
        painter.Paint(sample, evaluated.Captures.Where(c => c.Name != "Entire match").Select((c, i) => new Highlight(c.Index, c.Length, i, c.Stage == "Submessage" && c.Group == group?.Number)));
        if (group is null)
            return;
        var capture = evaluated.Captures.FirstOrDefault(c => c.Stage == "Submessage" && c.Group == group.Number);
        result.Text = evaluated.Route + "\n" + (capture is null ? $"Group {group.Number}: no capture." : $"{capture.Name}: position {capture.Index}, length {capture.Length}");
    }
}
