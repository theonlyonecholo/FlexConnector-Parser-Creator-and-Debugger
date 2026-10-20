using Avalonia;
using Avalonia.Controls;
using ParserCore;

namespace ParserDesigner.CrossPlatform;

/// <summary>Maintains the local category vocabulary independently of parser files.</summary>
internal sealed class CategoryCatalogDialog : Window
{
    public CategoryCatalogDialog(CategoryCatalog store)
    {
        Ui.Setup(this, "Category dictionary", 900, 680);
        var values = store.Load();
        var list = new ListBox();
        var field = new ComboBox { ItemsSource = CategoryMap.Fields, SelectedIndex = 0 };
        var path = Ui.Input("", "Category path, starting with /.");
        var description = Ui.Input("", "Your explanation of this category.");
        var search = Ui.Input("", "Search category paths and descriptions.");
        search.PlaceholderText = "Search dictionary…";
        CategoryValue? selected = null;
        void Refresh() => list.ItemsSource = values.Where(v => (v.Field + v.Value + v.Description)
            .Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).Select(v => new Item(v)).ToArray();
        search.TextChanged += (_, _) => Refresh();
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not Item item)
                return;
            selected = item.Value;
            field.SelectedItem = selected.Field;
            path.Text = selected.Value;
            description.Text = selected.Description;
        };
        Content = new Border
        {
            Padding = new Thickness(16),
            Child = Ui.Rows("Auto,Auto,*,Auto,Auto",
                Ui.Label("Local vocabulary shared by all parsers. Starter values are examples, not the full ArcSight taxonomy.\nConfirm custom paths against your deployed ArcSight categorizer/AUP. Dictionary edits do not modify existing CSV rows."),
                search, list,
                Ui.Stack(field, path, description),
                Ui.Bar(Ui.Button("New", () => { selected = null; path.Text = ""; description.Text = ""; }),
                    Ui.AsyncButton("Add / update", () => Ui.Guard(this, () =>
                    {
                        var updated = values.Where(v => v != selected).Append(new CategoryValue((string)field.SelectedItem!, path.Text ?? "", description.Text ?? "")).ToList();
                        CategoryCatalog.Validate(updated);
                        values = updated;
                        selected = null;
                        path.Text = "";
                        description.Text = "";
                        Refresh();
                        return Task.CompletedTask;
                    })),
                    Ui.Button("Remove", () => { if (selected is not null) values.Remove(selected); selected = null; Refresh(); }),
                    Ui.AsyncButton("Save dictionary", () => Ui.Guard(this, () =>
                    {
                        if (selected is null && !string.IsNullOrEmpty(path.Text) ||
                            selected is not null && (selected.Field != (string)field.SelectedItem! || selected.Value != path.Text || selected.Description != description.Text))
                            throw new FormatException("Add or clear the unfinished entry first.");
                        store.Save(values);
                        Close();
                        return Task.CompletedTask;
                    })), Ui.Button("Cancel", () => Close())))
        };
        Refresh();
    }
    private sealed record Item(CategoryValue Value)
    {
        public override string ToString() => $"{Value.Field} · {Value.Value}\n{Value.Description}";
    }
}
