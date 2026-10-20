using Avalonia;
using Avalonia.Controls;
using AvaloniaEdit;
using ParserCore;

namespace ParserDesigner.CrossPlatform;

/// <summary>Shared local preset management and validated replacement for either regex editor.</summary>
internal sealed class RegexPresetsDialog : Window
{
    private readonly RegexPresetStore store;
    private List<RegexPreset> presets;
    private readonly ListBox list = new();
    private readonly TextBox name = Ui.Input(), notes = Ui.Input();
    private readonly TextEditor pattern = Ui.Editor();
    private readonly TextBlock status = Ui.Label("Select a preset, or create one.");
    private readonly string original;
    private readonly int start, length;

    internal RegexPresetsDialog(RegexPresetStore store, string original, int start, int length)
    {
        this.store = store;
        this.original = original;
        this.start = start;
        this.length = length;
        presets = store.Load();
        Ui.Setup(this, "Regex preset library", 1000, 740);
        var selection = Ui.Editor(length > 0 ? original.Substring(start, length) : "(No text selected)", readOnly: true);
        selection.Height = 70;
        Ui.Tip(pattern, "Decoded regex. Use noncapturing groups (?:...) unless new numbered tokens are intended.");
        Ui.Tip(notes, "Describe supported formats and limitations for this preset.");
        Content = new Border
        {
            Padding = new Thickness(16),
            Child = Ui.Rows("Auto,*,Auto,Auto",
                Ui.Label("Presets match the entire selected literal text. Save edits before using them. Nothing is sent online."),
                Ui.Columns("240,*", Ui.Rows("Auto,*",
                    Ui.Bar(Ui.Button("New", New), Ui.Button("Delete", () => Run(Delete))), list),
                    Ui.Rows("Auto,Auto,Auto,Auto,Auto,*,Auto,Auto",
                        Ui.Label("Selected text from the regex editor"), selection,
                        Ui.Label("Name"), name, Ui.Label("Regex"), pattern, Ui.Label("Description"), notes)),
                Ui.Stack(status, Ui.Label("Local library: " + store.Path)),
                Ui.Bar(Ui.Button("Save preset", () => Run(Save)),
                    Ui.AsyncButton("Check and replace selection", ApplyAsync),
                    Ui.Button("Close", () => Close())))
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not RegexPreset item)
                return;
            name.Text = item.Name;
            notes.Text = item.Description;
            Ui.Reset(pattern, item.Pattern);
            status.Text = "Saved preset selected. Editing requires Save preset.";
        };
        Refresh(presets.FirstOrDefault()?.Id);
    }
    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) { status.Text = "Unable to update library: " + ex.Message; }
    }
    private void Refresh(string? id)
    {
        list.ItemsSource = presets.ToArray();
        list.SelectedItem = presets.FirstOrDefault(p => p.Id == id);
    }
    private void New()
    {
        list.SelectedItem = null;
        name.Text = "";
        notes.Text = "";
        Ui.Reset(pattern, "");
        status.Text = "New preset. Enter a name and regex, then Save preset.";
        name.Focus();
    }
    private void Save()
    {
        string id = (list.SelectedItem as RegexPreset)?.Id ?? Guid.NewGuid().ToString("N");
        var item = new RegexPreset(id, name.Text?.Trim() ?? "", pattern.Text, notes.Text ?? "");
        var updated = presets.Where(p => p.Id != id).Append(item).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        store.Save(updated);
        presets = updated;
        Refresh(id);
        status.Text = "Saved locally. Available in both regex editors after restart.";
    }
    private void Delete()
    {
        if (list.SelectedItem is not RegexPreset item)
            return;
        var updated = presets.Where(p => p.Id != item.Id).ToList();
        store.Save(updated);
        presets = updated;
        New();
        Refresh(presets.FirstOrDefault()?.Id);
        status.Text = "Preset deleted from the local library.";
    }
    private async Task ApplyAsync()
    {
        try
        {
            if (list.SelectedItem is not RegexPreset item || item.Name != name.Text?.Trim() ||
                item.Pattern != pattern.Text || item.Description != notes.Text)
                throw new FormatException("Save the preset before applying it.");
            status.Text = "Checking selection…";
            string replacement = await Task.Run(() => RegexPresetEditing.Replacement(original, start, length, item.Pattern));
            Close(replacement);
        }
        catch (Exception ex) { status.Text = "Cannot replace selection: " + ex.Message; }
    }
}

internal static class RegexEditorActions
{
    internal static async Task PresetsAsync(Window owner, TextEditor editor)
    {
        string original = editor.Text;
        int start = editor.SelectionStart, length = editor.SelectionLength;
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcSightParserDesigner");
        var dialog = new RegexPresetsDialog(new RegexPresetStore(Path.Combine(folder, "regex-presets.json")), original, start, length);
        string? replacement = await dialog.ShowDialog<string?>(owner);
        if (replacement is null)
            return;
        if (editor.Text != original)
            throw new InvalidOperationException("The regex changed while the library was open. Select the text again.");
        Replace(editor, start, length, replacement);
    }
    internal static void Group(TextEditor editor)
    {
        int start = editor.SelectionStart, length = editor.SelectionLength;
        if (length == 0)
            throw new FormatException("Select text in the regex editor to create a capturing group.");
        Replace(editor, start, length, "(" + editor.SelectedText + ")");
    }
    internal static void Replace(TextEditor editor, int start, int length, string text)
    {
        using (editor.Document.RunUpdate())
            editor.Document.Replace(start, length, text);
        editor.Select(start, text.Length);
        editor.Focus();
    }
}
