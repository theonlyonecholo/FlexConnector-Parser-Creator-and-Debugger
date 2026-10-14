using Avalonia;
using Avalonia.Controls;
using ParserCore;

namespace ParserDesigner.CrossPlatform;

internal sealed class MappingDialog : Window
{
    private sealed record MappingRow(TextBox Field, TextBox Expression, Control Container);
    private readonly List<MappingRow> rows = [];
    public MappingDialog(string source)
    {
        Ui.Setup(this, "Event mapping", 1100, 760);
        var table = new StackPanel { Spacing = 6 };
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
        var tokens = new ComboBox { ItemsSource = EventMapping.Tokens(source).Keys.ToArray(), PlaceholderText = "Declared tokens…", MinWidth = 180 };
        tokens.SelectionChanged += (_, _) => { if (tokens.SelectedItem is string token) { value.Text = token; mode.SelectedIndex = 0; } };
        void Diagnose() => diagnostics.Text = string.Join("\n", EventMapping.Warnings(source, rows.Select(r => new EventAssignment(r.Field.Text ?? "", r.Expression.Text ?? "")).ToArray()));
        void Add(string target, string expression)
        {
            var f = Ui.Input(target, "Target event field. Duplicate fields are rejected when applying.");
            var e = Ui.Input(expression, "Decoded token reference or ArcSight expression; not executed in the preview.");
            MappingRow? row = null;
            var container = Ui.Columns("*,2*,Auto", f, e, Ui.Button("Remove", () => { if (row is null) return; rows.Remove(row); table.Children.Remove(row.Container); Diagnose(); }));
            row = new(f, e, container);
            rows.Add(row);
            table.Children.Add(container);
            f.TextChanged += (_, _) => Diagnose();
            e.TextChanged += (_, _) => Diagnose();
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
            Ui.Label("Assign tokens, constants or expressions to event fields. Add the draft to the table, then apply.\nType diagnostics cover direct token references. ArcSight operations are stored, not executed."),
            Ui.Stack(Ui.Bar(fields, tokens), Ui.Columns("*,140,2*", field, mode, value)), Ui.Bar(add, type),
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
        Ui.HelpKey(this, "mapping");
    }
}
