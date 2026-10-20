using Avalonia;
using Avalonia.Controls;
using ParserCore;

namespace ParserDesigner.CrossPlatform;

/// <summary>Selects a documented or locally defined field without changing parser assignments.</summary>
internal sealed class EventFieldPicker : Window
{
    private readonly TextBox search = Ui.Input("", "Search field names, types and restrictions.");
    private readonly ListBox list = new();
    private readonly TextBlock details = Ui.Label("");
    private readonly EventFieldCatalog store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArcSightParserDesigner", "event-fields.json"));
    private IReadOnlyList<EventFieldDefinition> definitions = [];

    internal EventFieldPicker()
    {
        Ui.Setup(this, "Select ArcSight field", 850, 650);
        search.PlaceholderText = "Search fields, types or restrictions…";
        void Refresh()
        {
            string query = search.Text?.Trim() ?? "";
            list.ItemsSource = definitions.Where(d =>
                $"{d.Field} {d.Type} {d.Restrictions}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            details.Text = "";
        }
        void Reload()
        {
            definitions = store.Load().Values.OrderBy(d => d.Field, StringComparer.Ordinal).ToArray();
            Refresh();
        }
        search.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Refresh(); };
        list.SelectionChanged += (_, _) =>
        {
            details.Text = list.SelectedItem is EventFieldDefinition d
                ? $"{d.Field} · {d.Type} · Documented length: {d.Length}\n{d.Restrictions}\nSource: {d.Source}" : "";
        };
        Content = new Border
        {
            Padding = new Thickness(16),
            Child = Ui.Rows("Auto,*,Auto,Auto", search, list,
                new ScrollViewer { MaxHeight = 150, Content = details },
                Ui.Bar(Ui.Button("Use field", () =>
                {
                    if (list.SelectedItem is EventFieldDefinition d)
                        Close(d.Field);
                }), Ui.AsyncButton("Manage fields…", () => Ui.Guard(this, async () =>
                {
                    await new FieldCatalogDialog(store, (list.SelectedItem as EventFieldDefinition)?.Field).ShowDialog(this);
                    Reload();
                })), Ui.Button("Cancel", () => Close())))
        };
        Reload();
    }
}
