using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ParserCore;

namespace ParserDesigner.CrossPlatform;

/// <summary>Creates exact event-class-ID maps and explains their two distinct deployment modes.</summary>
internal sealed class CategoryMapDialog : Window
{
    private CategoryMap map = new();
    private readonly ListBox list = new();
    private readonly TextBox id = Ui.Input(), search = Ui.Input();
    private readonly TextBox[] cells = CategoryMap.Fields.Select(_ => Ui.Input()).ToArray();
    private readonly ComboBox[] choices = CategoryMap.Fields.Select(_ => new ComboBox { PlaceholderText = "Dictionary…", MaxWidth = 300 }).ToArray();
    private readonly ComboBox mode = new() { ItemsSource = new[] { "Extra processor (parser configuration)", "Native categorizer (vendor / product)" }, SelectedIndex = 0 };
    private readonly TextBox vendor = Ui.Input(), product = Ui.Input();
    private readonly TextBlock status = Ui.Label("");
    private readonly CategoryCatalog catalog;
    private readonly string source, root;
    private string? openedPath;
    private string[]? selected;
    private bool DraftDirty => selected is null ? cells.Prepend(id).Any(c => !string.IsNullOrEmpty(c.Text)) : !selected.SequenceEqual(cells.Prepend(id).Select(c => c.Text ?? ""));

    public CategoryMapDialog(string source, string root, CategoryCatalog? catalog = null)
    {
        this.source = source;
        this.root = Path.GetFullPath(root);
        this.catalog = catalog ?? new CategoryCatalog(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ArcSightParserDesigner", "categories.json"));
        Ui.Setup(this, "Category maps", 1100, 800);
        var form = new StackPanel { Spacing = 6 };
        form.Children.Add(Ui.Label("Event class ID (event.deviceEventClassId)"));
        form.Children.Add(id);
        for (int i = 0; i < cells.Length; i++)
        {
            int index = i;
            form.Children.Add(Ui.Label(CategoryMap.Fields[i]));
            form.Children.Add(Ui.Columns("*,*", cells[i], choices[i]));
            choices[i].SelectionChanged += (_, _) => { if (choices[index].SelectedItem is CategoryValueItem v) cells[index].Text = v.Value.Value; };
        }
        search.PlaceholderText = "Search IDs and categories…";
        search.TextChanged += (_, _) => Refresh();
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not RowItem item)
                return;
            if (DraftDirty)
            {
                status.Text = "Add / update or clear your draft before selecting another row.";
                return;
            }
            selected = item.Row;
            id.Text = selected[0];
            for (int i = 0; i < cells.Length; i++)
                cells[i].Text = selected[i + 1];
        };
        void Clear()
        {
            selected = null;
            id.Text = "";
            foreach (var cell in cells)
                cell.Text = "";
        }
        void Describe() => status.Text = mode.SelectedIndex == 0
            ? "Save inside the parser directory. Adds a UTF-8, case-insensitive map processor; category setters overwrite existing values. Deploy CSV under user/agent/aup/fcp/ using the configured relative path."
            : "Local preview reads categorizer/<vendor>/<product>.csv beside the parser. Vendor/product are lowercased; punctuation becomes _. Already categorized events are skipped. Deploy under user/agent/aup/acp/categorizer/current/ (check your connector version).";
        mode.SelectionChanged += (_, _) => Describe();
        Content = new Border
        {
            Padding = new Thickness(16),
            Child = Ui.Rows("Auto,Auto,*,Auto,Auto",
                Ui.Bar(Ui.AsyncButton("Open CSV…", () => Ui.Guard(this, OpenAsync)),
                    Ui.AsyncButton("Manage dictionary…", () => Ui.Guard(this, async () => { await new CategoryCatalogDialog(this.catalog).ShowDialog(this); LoadDictionary(); })), mode),
                Ui.Columns("*,*", Ui.Stack(Ui.Label("Device vendor (native mode)"), vendor), Ui.Stack(Ui.Label("Device product (native mode)"), product)),
                Ui.Columns("*,*", Ui.Rows("Auto,*", search, list), new ScrollViewer { Content = form }),
                new ScrollViewer { MaxHeight = 85, Content = status },
                Ui.Bar(Ui.Button("New / clear draft", Clear), Ui.AsyncButton("Add / update row", () => Ui.Guard(this, () =>
                {
                    var row = new[] { id.Text ?? "" }.Concat(cells.Select(c => c.Text ?? "")).ToArray();
                    var proposed = new CategoryMap();
                    proposed.Rows.AddRange(map.Rows.Where(r => r != selected));
                    proposed.Rows.Add(row);
                    proposed.Validate();
                    map = proposed;
                    Clear();
                    Refresh();
                    return Task.CompletedTask;
                })),
                Ui.Button("Remove row", () => { if (selected is not null) map.Rows.Remove(selected); Clear(); Refresh(); }),
                Ui.AsyncButton("Save map and apply", () => Ui.Guard(this, SaveAsync)), Ui.Button("Cancel", () => Close())))
        };
        LoadDictionary();
        Describe();
    }
    private void LoadDictionary()
    {
        var values = catalog.Load();
        for (int i = 0; i < choices.Length; i++)
            choices[i].ItemsSource = values.Where(v => v.Field == CategoryMap.Fields[i]).Select(v => new CategoryValueItem(v)).ToArray();
    }
    private void Refresh() => list.ItemsSource = map.Rows.Where(r => string.Join(" ", r).Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).Select(r => new RowItem(r)).ToArray();
    private async Task OpenAsync()
    {
        if (DraftDirty)
            throw new FormatException("Add or clear the unfinished draft first.");
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Open category CSV",
            FileTypeFilter = [new("CSV") { Patterns = ["*.csv"] }]
        });
        if (files.Count == 0)
            return;
        string file = files[0].TryGetLocalPath() ?? throw new IOException("Select a local file.");
        if (new FileInfo(file).Length > 4 * 1024 * 1024)
            throw new FormatException("Map exceeds 4 MB.");
        map = CategoryMap.Parse(File.ReadAllText(file, new System.Text.UTF8Encoding(false, true)));
        openedPath = file;
        selected = null;
        id.Text = "";
        foreach (var cell in cells)
            cell.Text = "";
        Refresh();
        status.Text = "Loaded " + file;
    }
    private async Task SaveAsync()
    {
        if (DraftDirty)
            throw new FormatException("Add / update or clear the unfinished draft first.");
        if (map.Rows.Count == 0)
            throw new FormatException("Add at least one category row.");
        string csv = map.ToCsv(), target, updated = source;
        if (mode.SelectedIndex == 1)
        {
            target = Path.Combine(root, "categorizer", CategoryMap.NativeName(vendor.Text ?? ""), CategoryMap.NativeName(product.Text ?? "") + ".csv");
            if (File.Exists(target) && await Ui.Ask(this, "Replace category map", target, "Replace", "Cancel") != "Replace")
                return;
        }
        else
        {
            var folder = await StorageProvider.TryGetFolderFromPathAsync(root);
            var file = await StorageProvider.SaveFilePickerAsync(new()
            {
                Title = "Save CSV inside the parser directory",
                SuggestedStartLocation = folder,
                SuggestedFileName = Path.GetFileName(openedPath) ?? "categories.csv",
                DefaultExtension = "csv"
            });
            if (file is null)
                return;
            target = file.TryGetLocalPath() ?? throw new IOException("Select a local file.");
            string relative = Path.GetRelativePath(root, target);
            if (relative.Replace('\\', '/').StartsWith("categorizer/", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("The categorizer directory is reserved for native maps. Save processor maps elsewhere inside the parser directory.");
            updated = CategoryMap.Attach(source, relative);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, csv, new System.Text.UTF8Encoding(false));
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Close(updated);
    }
    private sealed record RowItem(string[] Row)
    {
        public override string ToString() => Row[0] + "\n" + string.Join(" · ", Row.Skip(1).Where(s => s.Length > 0));
    }
    private sealed record CategoryValueItem(CategoryValue Value)
    {
        public override string ToString() => Value.Value + " · " + Value.Description;
    }
}
