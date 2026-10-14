using ParserCore;

namespace ParserDesigner;

internal sealed partial class MainForm
{
    private void AddButton(Control parent, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 29 };
        button.Click += (_, _) => Guard(action);
        parent.Controls.Add(button);
    }
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "Parser Designer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private void UpdateTitle() => Text = $"ArcSight Parser Designer — C# — {path ?? "nový parser"}{(dirty || regexDirty ? " *" : "")}";
    private bool CanLeave()
    {
        if (!dirty && !regexDirty)
            return true;
        var choice = MessageBox.Show(this, "Máte neuložené změny. Uložit je před pokračováním?", "Neuložené změny",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.No)
            return true;
        if (choice != DialogResult.Yes)
            return false;
        bool saved = false;
        Guard(() => saved = SaveFile(false));
        return saved;
    }
    private void OpenFile()
    {
        if (!CanLeave())
            return;
        using var dialog = new OpenFileDialog { Filter = "FlexConnector (*.properties)|*.properties|Všechny soubory|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        var doc = PropertiesDocument.Load(dialog.FileName);
        if (!doc.Values.ContainsKey("regex"))
            throw new FormatException("Soubor neobsahuje hlavní vlastnost regex.");
        loading = true;
        source.Text = doc.Source;
        pattern.Text = doc.Get("regex");
        trim.Checked = doc.Get("trim.message").Equals("true", StringComparison.OrdinalIgnoreCase);
        loading = false;
        path = dialog.FileName;
        ((UndoTextBox)source).ResetHistory();
        ((UndoTextBox)pattern).ResetHistory();
        dirty = regexDirty = false;
        UpdateTitle();
        Schedule();
    }
    private bool SaveFile(bool saveAs)
    {
        if (regexDirty)
        {
            var choice = MessageBox.Show(this, "Zapsat aktuální pole Regex do konfigurace před uložením?",
                "Uložit regex", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (choice != DialogResult.OK)
                return false;
            WritePattern();
        }
        var doc = new PropertiesDocument(source.Text);
        if (!doc.Values.ContainsKey("regex"))
            throw new FormatException("Chybí vlastnost regex.");
        string? target = path;
        if (saveAs || target is null)
        {
            using var dialog = new SaveFileDialog { Filter = "FlexConnector (*.properties)|*.properties", DefaultExt = "properties", FileName = target ?? "parser.sdkrfilereader.properties" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return false;
            target = dialog.FileName;
        }
        doc.Save(target);
        path = target;
        dirty = false;
        UpdateTitle();
        return true;
    }
    private void ReadPattern()
    {
        var doc = new PropertiesDocument(source.Text);
        if (!doc.Values.ContainsKey("regex"))
            throw new FormatException("Chybí vlastnost regex.");
        if (regexDirty && MessageBox.Show(this, "Nahradit neuložený výraz hodnotou z konfigurace?", "Regex",
            MessageBoxButtons.OKCancel) != DialogResult.OK)
            return;
        loading = true;
        pattern.Text = doc.Get("regex");
        trim.Checked = doc.Get("trim.message").Equals("true", StringComparison.OrdinalIgnoreCase);
        loading = false;
        regexDirty = false;
        UpdateTitle();
        Schedule();
    }
    private void WritePattern()
    {
        var doc = new PropertiesDocument(source.Text);
        doc.Set("regex", pattern.Text);
        source.Text = doc.Source;
        regexDirty = false;
        UpdateTitle();
    }
    private void EditAdvanced()
    {
        using var dialog = new AdvancedDialog(source.Text);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            source.Text = dialog.ResultSource;
    }
    private void EditEventMapping()
    {
        using var dialog = new EventMappingDialog(source.Text);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            source.Text = dialog.ResultSource;
    }
    private void OpenBatch()
    {
        if (regexDirty)
        {
            if (MessageBox.Show(this, "Zapsat změněný Regex do konfigurace před testem logů?", "Test parseru", MessageBoxButtons.OKCancel) != DialogResult.OK)
                return;
            WritePattern();
        }
        var doc = new PropertiesDocument(source.Text);
        if (doc.Values.ContainsKey("multiline.starts.regex"))
            MessageBox.Show(this, "Tento parser má multiline konfiguraci. Test souboru nyní vyhodnocuje jednotlivé řádky; vícerádkové události nejprve spojte.", "Režim po řádcích");
        using var batch = new BatchForm(source.Text, path is null ? "Nový parser" : Path.GetFileName(path));
        batch.ShowDialog(this);
        if (batch.ConfigurationSource != source.Text)
            ApplyEditedSource(batch.ConfigurationSource);
    }
    private void ApplyEditedSource(string text)
    {
        var doc = new PropertiesDocument(text);
        loading = true;
        source.Text = text;
        pattern.Text = doc.Get("regex");
        trim.Checked = doc.Get("trim.message").Equals("true", StringComparison.OrdinalIgnoreCase);
        loading = false;
        dirty = true;
        regexDirty = false;
        UpdateTitle();
        Schedule();
    }
    private void EditSubmessages()
    {
        if (regexDirty)
        {
            if (MessageBox.Show(this, "Zapsat změněný Regex do konfigurace před úpravou submessages?", "Submessages", MessageBoxButtons.OKCancel) != DialogResult.OK)
                return;
            WritePattern();
        }
        using var dialog = new SubmessageDialog(source.Text, message.Text);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            ApplyEditedSource(dialog.ResultSource);
    }
    private void GenerateTokens()
    {
        using var dialog = new TokenDialog(source.Text, pattern.Text);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        source.Text = dialog.ResultSource;
        regexDirty = false;
        UpdateTitle();
    }
    private void LoadMessage()
    {
        using var dialog = new OpenFileDialog { Filter = "Log / text|*.log;*.txt|Všechny soubory|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        message.Text = File.ReadAllText(dialog.FileName);
    }
}
