using Avalonia;
using Avalonia.Controls;
using ParserCore;

namespace ParserDesigner.CrossPlatform;

internal sealed class MappingDialog : Window
{
    private sealed record MappingRow(TextBox Field, TextBox Expression, TextBlock Snippet, Control Container);
    private readonly List<MappingRow> rows = [];
    public MappingDialog(string source, string sample = "", string? pattern = null, System.Text.RegularExpressions.RegexOptions options = 0, bool full = false, bool trim = false, int timeoutMs = 250)
    {
        Ui.Setup(this, "Event mapping", 1100, 760);
        var table = new StackPanel { Spacing = 6 };
        var values = new Dictionary<string, string>();
        string previewStatus = "Loading sample preview…";
        string Preview(string expression)
        {
            if (expression.Length == 0)
                return "Select a token to preview its sample value";
            if (values.TryGetValue(expression, out string? captured))
                return captured.Length == 0 ? "(empty capture)" : captured;
            if (expression.StartsWith("__stringConstant(", StringComparison.Ordinal) && expression.EndsWith(')'))
            {
                try
                {
                    return "Constant: " + System.Text.Json.JsonSerializer.Deserialize<string>(expression[17..^1]);
                }
                catch (System.Text.Json.JsonException) { return "Invalid string constant"; }
            }
            return expression.StartsWith("__", StringComparison.Ordinal) ? "Expression: not evaluated locally" : previewStatus;
        }
        void UpdateSnippet(TextBlock target, string expression)
        {
            string text = Preview(expression);
            target.Text = text.Length > 120 ? text[..120] + "…" : text;
            Ui.Tip(target, text[..Math.Min(2000, text.Length)]);
        }
        table.Children.Add(Ui.Columns("*,2*,2*,80", Ui.Label("Event field"), Ui.Label("Token / expression"), Ui.Label("Message snippet"), Ui.Label("")));
        var diagnostics = Ui.Label("");
        var field = Ui.Input("", "Target event.fieldName. Choose from the catalog or enter a custom field.");
        var fields = new ComboBox { ItemsSource = EventMapping.Fields.Keys.Order().ToArray(), PlaceholderText = "Field catalog…", MinWidth = 240 };
        var type = Ui.Label("Select a target field to see its type.");
        fields.SelectionChanged += (_, _) =>
        {
            if (fields.SelectedItem is string value)
            {
                field.Text = value;
                type.Text = "Target type: " + EventMapping.Fields[value];
            }
        };
        var mode = new ComboBox { ItemsSource = new[] { "Token", "Constant", "Expression" }, SelectedIndex = 0 };
        var value = Ui.Input("", "Token name, literal constant or decoded ArcSight expression.");
        var draftSnippet = Ui.Label("");
        value.TextChanged += (_, _) => UpdateSnippet(draftSnippet, value.Text ?? "");
        var tokens = new ComboBox { ItemsSource = EventMapping.Tokens(source).Keys.ToArray(), PlaceholderText = "Declared tokens…", MinWidth = 180 };
        tokens.SelectionChanged += (_, _) => { if (tokens.SelectedItem is string token) { value.Text = token; mode.SelectedIndex = 0; } };
        void Diagnose() => diagnostics.Text = string.Join("\n", EventMapping.Warnings(source, rows.Select(r => new EventAssignment(r.Field.Text ?? "", r.Expression.Text ?? "")).ToArray()));
        void Add(string target, string expression)
        {
            var f = Ui.Input(target, "Target event field. Duplicate fields are rejected when applying.");
            var e = Ui.Input(expression, "Decoded token reference or ArcSight expression; not executed in the preview.");
            MappingRow? row = null;
            var snippet = Ui.Label("");
            UpdateSnippet(snippet, expression);
            var container = Ui.Columns("*,2*,2*,80", f, e, snippet, Ui.Button("Remove", () => { if (row is null) return; rows.Remove(row); table.Children.Remove(row.Container); Diagnose(); }));
            row = new(f, e, snippet, container);
            rows.Add(row);
            table.Children.Add(container);
            f.TextChanged += (_, _) => Diagnose();
            e.TextChanged += (_, _) => { Diagnose(); UpdateSnippet(snippet, e.Text ?? ""); };
        }
        foreach (var assignment in EventMapping.Load(source))
            Add(assignment.Field, assignment.Expression);
        var add = Ui.AsyncButton("Add / update", () => Ui.Guard(this, () =>
        {
            string target = field.Text ?? "", expression = mode.SelectedIndex == 1 ? EventMapping.Constant(value.Text ?? "") : value.Text ?? "";
            _ = EventMapping.Apply(source, [new(target, expression)]);
            var existing = rows.FirstOrDefault(r => r.Field.Text == target);
            if (existing is not null)
                existing.Expression.Text = expression;
            else
                Add(target, expression);
            field.Text = "";
            value.Text = "";
            Diagnose();
            return Task.CompletedTask;
        }));
        Content = new Border
        {
            Padding = new Thickness(18),
            Child = Ui.Rows("Auto,Auto,Auto,*,Auto,Auto",
            Ui.Label("Assign tokens, constants or expressions to event fields. Add the draft to the table, then apply.\nSnippets use the first match of the working regex against Message (last repeated capture). Hover for more text.\nValues are raw, before type/format conversion. String constants are shown; other ArcSight operations are not executed."),
            Ui.Stack(Ui.Bar(fields, tokens), Ui.Columns("*,140,2*", field, mode, value), draftSnippet), Ui.Bar(add, type),
            new ScrollViewer { Content = table }, new ScrollViewer { MaxHeight = 120, Content = diagnostics },
            Ui.Bar(Ui.AsyncButton("Apply to configuration", () => Ui.Guard(this, () =>
            {
                if (!string.IsNullOrEmpty(field.Text) || !string.IsNullOrEmpty(value.Text))
                    throw new FormatException("Add or clear the unfinished draft first.");
                Close(EventMapping.Apply(source, rows.Select(r => new EventAssignment(r.Field.Text ?? "", r.Expression.Text ?? "")).ToArray()));
                return Task.CompletedTask;
            })), Ui.Button("Cancel", () => Close()), Ui.Help(this, "mapping")))
        };
        Diagnose();
        bool closed = false;
        Closed += (_, _) => closed = true;
        Opened += async (_, _) =>
        {
            var doc = new PropertiesDocument(source);
            var result = await Task.Run(() => RegexDebugger.Analyze(pattern ?? doc.Get("regex"), sample, options, full, trim, timeoutMs));
            if (closed)
                return;
            foreach (var capture in result.Rows.Where(r => r.Match == 1 && r.Group > 0 && r.Success))
                values[doc.Get($"token[{capture.Group - 1}].name")] = capture.Value;
            previewStatus = result.Error ?? (result.Matches.Count == 0 ? "No sample match" : "No captured token with this name");
            foreach (var row in rows)
                UpdateSnippet(row.Snippet, row.Expression.Text ?? "");
            UpdateSnippet(draftSnippet, value.Text ?? "");
        };
        Ui.HelpKey(this, "mapping");
    }
}
