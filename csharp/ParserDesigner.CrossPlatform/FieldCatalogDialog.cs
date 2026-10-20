using Avalonia;
using Avalonia.Controls;
using ParserCore;

namespace ParserDesigner.CrossPlatform;

/// <summary>Edits local field metadata overrides without modifying parser assignments or vendor defaults.</summary>
internal sealed class FieldCatalogDialog : Window
{
    private readonly EventFieldCatalog store;
    private List<EventFieldDefinition> overrides;
    private readonly ListBox list = new();
    private readonly TextBox field = Ui.Input(), type = Ui.Input(), length = Ui.Input(), source = Ui.Input();
    private readonly AvaloniaEdit.TextEditor restrictions = Ui.Editor();
    private readonly TextBlock status = Ui.Label("");
    internal FieldCatalogDialog(EventFieldCatalog store, string? selected = null)
    {
        this.store = store;
        overrides = store.LoadOverrides();
        Ui.Setup(this, "Field catalog · local definitions", 1100, 780);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not EventFieldDefinition item)
                return;
            field.Text = item.Field;
            field.IsReadOnly = true;
            type.Text = item.Type;
            length.Text = item.Length;
            source.Text = item.Source;
            Ui.Reset(restrictions, item.Restrictions);
            status.Text = overrides.Any(o => o.Field == item.Field) ? "Local definition / override" : "Documented built-in; saving creates a local override.";
        };
        Content = new Border
        {
            Padding = new Thickness(16),
            Child = Ui.Rows("Auto,*,Auto,Auto",
                Ui.Label("Manage field types and documented constraints. This catalog does not create fields in ArcSight.\nSave before switching fields or closing. Unsaved edits are discarded."),
                Ui.Columns("400,*", list, Ui.Rows("Auto,Auto,Auto,Auto,Auto,Auto,Auto,*,Auto,Auto",
                    Ui.Label("Field key (event.fieldName)"), field, Ui.Label("Data type"), type,
                    Ui.Label("Documented length (or n/a / Not specified)"), length, Ui.Label("Range / limitations / notes"), restrictions,
                    Ui.Label("Documentation source / version"), source)),
                Ui.Stack(status, Ui.Label("Local catalog: " + store.Path)),
                Ui.Bar(Ui.Button("New field", New), Ui.Button("Save definition", () => Run(Save)),
                    Ui.Button("Remove override / custom field", () => Run(Remove)), Ui.Button("Close", () => Close())))
        };
        Ui.Tip(type, "Editable type name, including future vendor types. Common types: String, Integer, Long, IPAddress, MacAddress, TimeStamp.");
        Refresh(selected);
    }
    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) { status.Text = "Unable to save catalog: " + ex.Message; }
    }
    private void Refresh(string? selected)
    {
        var merged = EventFieldCatalog.BuiltIn.ToDictionary(p => p.Key, p => p.Value);
        foreach (var item in overrides)
            merged[item.Field] = item;
        list.ItemsSource = merged.Values.OrderBy(v => v.Field, StringComparer.Ordinal).ToArray();
        list.SelectedItem = merged.GetValueOrDefault(selected ?? "");
    }
    private void New()
    {
        list.SelectedItem = null;
        field.IsReadOnly = false;
        field.Text = "event.";
        type.Text = "String";
        length.Text = "Not specified";
        source.Text = "User-defined; verify support in the target connector";
        Ui.Reset(restrictions, "");
        status.Text = "New local definition. Enter its field key, type and constraints.";
    }
    private void Save()
    {
        var item = new EventFieldDefinition(field.Text?.Trim() ?? "", type.Text?.Trim() ?? "",
            length.Text?.Trim() ?? "", restrictions.Text, source.Text ?? "");
        var updated = overrides.Where(o => o.Field != item.Field).Append(item).ToList();
        store.Save(updated);
        overrides = updated;
        Refresh(item.Field);
        status.Text = "Local definition saved. This does not change the connector's schema or existing mappings.";
    }
    private void Remove()
    {
        if (list.SelectedItem is not EventFieldDefinition item)
            return;
        var updated = overrides.Where(o => o.Field != item.Field).ToList();
        store.Save(updated);
        overrides = updated;
        New();
        Refresh(item.Field);
        status.Text = EventFieldCatalog.BuiltIn.ContainsKey(item.Field) ? "Documented built-in restored." : "Custom definition removed; existing parser mappings are retained.";
    }
}
