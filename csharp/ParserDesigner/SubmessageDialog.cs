using ParserCore;

namespace ParserDesigner;

/// <summary>Edits ordered submessage definitions and previews the selected log using the draft configuration.</summary>
internal sealed class SubmessageDialog : Form
{
    private readonly string original;
    private readonly SubmessageConfiguration model;
    private readonly TreeView tree = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly UndoTextBox bodyToken = new() { Width = 145 };
    private readonly UndoTextBox idToken = new() { Width = 145 };
    private readonly CheckBox trim = new() { Text = "Trim submessage", AutoSize = true };
    private readonly UndoTextBox id = new() { Dock = DockStyle.Fill };
    private readonly CheckBox fallback = new() { Text = "Default pro neznámé ID", AutoSize = true };
    private readonly Dictionary<string, TextBox> edits = [];
    private readonly Label selection = new() { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4), Font = new Font("Segoe UI Semibold", 11) };
    private readonly RichTextBox sample = new() { ReadOnly = true, Dock = DockStyle.Fill, DetectUrls = false };
    private readonly TextBox result = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private (int Message, int Pattern)? current;
    private bool switching;
    private int highlightVersion;
    private readonly SemaphoreSlim highlightWorker = new(1, 1);
    public string ResultSource
    {
        get; private set;
    }

    public SubmessageDialog(string source, string sampleLog = "", int selectedSubmessage = -1, int selectedPattern = -1)
    {
        original = ResultSource = source;
        model = SubmessageConfiguration.Load(source);
        Text = "Submessages — definice a pořadí patternů";
        Size = new Size(1150, 830);
        MinimumSize = new Size(950, 720);
        StartPosition = FormStartPosition.CenterParent;
        bodyToken.Text = model.BodyToken;
        idToken.Text = model.IdToken;
        trim.Checked = model.Trim;
        var routing = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(8), AutoScroll = true, WrapContents = false };
        routing.Controls.AddRange([new Label { Text = "Token podzprávy:", AutoSize = true }, bodyToken,
            new Label { Text = "Token message ID:", AutoSize = true }, idToken, trim]);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
        Button Add(Control parent, string label, Action action)
        {
            var b = new Button { Text = label, AutoSize = true };
            b.Click += (_, _) => Guard(action);
            parent.Controls.Add(b);
            return b;
        }
        Add(bottom, "Použít v konfiguraci", () => { ResultSource = BuildSource(); DialogResult = DialogResult.OK; });
        var cancel = new Button { Text = "Zrušit", AutoSize = true, DialogResult = DialogResult.Cancel };
        bottom.Controls.Add(cancel);
        CancelButton = cancel;
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 8 };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 112, Padding = new Padding(4) };
        Add(tools, "+ Submessage", () =>
        {
            Commit();
            var message = new SubmessageDefinition { MessageId = "NEW_" + model.Messages.Count };
            message.Patterns.Add(SubmessageConfiguration.NewPattern());
            model.Messages.Add(message);
            Rebuild(model.Messages.Count - 1, 0);
        });
        Add(tools, "+ Pattern", () =>
        {
            Commit();
            if (current is not { } c)
                return;
            model.Messages[c.Message].Patterns.Add(SubmessageConfiguration.NewPattern());
            Rebuild(c.Message, model.Messages[c.Message].Patterns.Count - 1);
        });
        Add(tools, "Odstranit", () =>
        {
            Commit();
            if (current is not { } c)
                return;
            if (c.Pattern < 0)
                model.Messages.RemoveAt(c.Message);
            else
                model.Messages[c.Message].Patterns.RemoveAt(c.Pattern);
            Rebuild(Math.Min(c.Message, model.Messages.Count - 1), -1);
        });
        Add(tools, "↑ Dříve", () => MoveDefinition(-1));
        Add(tools, "↓ Později", () => MoveDefinition(1));
        split.Panel1.Controls.Add(tree);
        split.Panel1.Controls.Add(tools);
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 0, Padding = new Padding(8), AutoScroll = true };
        fields.ColumnStyles.Add(new(SizeType.Absolute, 190));
        fields.ColumnStyles.Add(new(SizeType.Percent, 100));
        void Field(string label, Control edit, int height = 34)
        {
            int row = fields.RowCount++;
            fields.RowStyles.Add(new(SizeType.Absolute, height));
            fields.Controls.Add(new Label { Text = label, AutoSize = true }, 0, row);
            fields.Controls.Add(edit, 1, row);
        }
        Field("Message ID", id);
        Field("", fallback);
        foreach (string key in new[] { "regex", "fields", "types", "formats", "mappings", "mappings.delimiter", "extramappings", "extramappings.delimiter" })
        {
            var edit = new UndoTextBox
            {
                Dock = DockStyle.Fill,
                Multiline = key == "regex",
                ScrollBars = key == "regex" ? ScrollBars.Vertical : ScrollBars.None,
                Font = new Font("Consolas", 10)
            };
            edits[key] = edit;
            Field(key, edit, key == "regex" ? 70 : 34);
            edit.TextChanged += (_, _) => ClearGroupHighlight();
        }
        sample.Text = sampleLog;
        Field("Vybraný celý log", sample, 60);
        var test = new Button { Text = "Testovat log s těmito změnami", AutoSize = true };
        test.Click += async (_, _) =>
        {
            try
            {
                var parser = new ParserEvaluator(BuildSource());
                string input = sample.Text;
                test.Enabled = false;
                var r = await Task.Run(() => parser.Evaluate(input));
                if (!IsDisposed)
                    result.Text = r.Route + "\r\n" + r.Description;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException) { if (!IsDisposed) result.Text = ex.Message; }
            finally { if (!IsDisposed) test.Enabled = true; }
        };
        Field("", test, 38);
        Field("Výsledek", result, 66);
        edits["regex"].MouseUp += async (_, _) => await HighlightGroupAsync();
        edits["regex"].KeyUp += async (_, _) => await HighlightGroupAsync();
        bodyToken.TextChanged += (_, _) => ClearGroupHighlight();
        idToken.TextChanged += (_, _) => ClearGroupHighlight();
        id.TextChanged += (_, _) => ClearGroupHighlight();
        fallback.CheckedChanged += (_, _) => ClearGroupHighlight();
        trim.CheckedChanged += (_, _) => ClearGroupHighlight();
        split.Panel2.Controls.Add(fields);
        split.Panel2.Controls.Add(selection);
        Controls.Add(split);
        Controls.Add(routing);
        Controls.Add(bottom);
        tree.AfterSelect += (_, _) =>
        {
            if (switching)
                return;
            Commit();
            current = tree.SelectedNode?.Tag as ValueTuple<int, int>?;
            LoadCurrent();
        };
        fallback.CheckedChanged += (_, _) => id.Enabled = !fallback.Checked;
        Theme.Apply(this);
        HelpUi.Describe(bodyToken, "submessages", "Název tokenu hlavního regexu obsahujícího tělo podzprávy (submessage.token), například Body.");
        HelpUi.Describe(idToken, "submessages", "Název tokenu, jehož hodnota vybírá submessage (submessage.messageid.token), například MessageID.");
        HelpUi.Describe(id, "submessages", "Přesná hodnota ID, pro kterou je tato definice určena, například LOGIN.");
        HelpUi.Describe(fallback, "submessages", "Default nemá vlastnost messageid. Použije se jen pro neznámé ID, nikoli pro neodpovídající pattern známého ID.");
        HelpUi.Describe(trim, "submessages", "Odstraní okrajové mezery/tabulátory těla před testem (trim.submessage).");
        HelpUi.Describe(tree, "submessages", "Definice a jejich patterny. Vyberte uzel pro úpravu; první odpovídající pattern v pořadí vyhrává.");
        HelpUi.Describe(sample, "submessages", "Celý log převzatý z hlavního okna nebo testu souboru. Kliknutí do regex skupiny zvýrazní její pole.");
        HelpUi.Describe(result, "submessages", "Výsledek testu návrhu: použitá cesta, důvod neshody nebo pozice vybraného pole.");
        foreach (var edit in edits)
            HelpUi.Describe(edit.Value, "submessages", edit.Key switch
            {
                "regex" => "Dekódovaný regex pro celé tělo podzprávy. Skupiny se číslují znovu od $1.",
                "fields" => "Cílová event pole v pořadí skupin, oddělená čárkou; např. event.sourceUserName,event.deviceAction.",
                "types" => "Typy skupin ve stejném pořadí jako fields, např. String,IPAddress,Integer. Náhled převod neprovádí.",
                "formats" => "Formáty skupin ve stejném pořadí. null označuje položku bez formátu.",
                "mappings" => "Výrazy pro fields, standardně oddělené |; např. __getProtocolName($1)|$2. Provádí je konektor.",
                "mappings.delimiter" => "Vlastní oddělovač položek mappings místo výchozího |.",
                "extramappings" => "Další přiřazení event.pole=výraz, standardně oddělená |, například konstanta event.name.",
                _ => "Vlastní oddělovač dalších přiřazení extramappings místo výchozího |."
            });
        Shown += (_, _) => split.SplitterDistance = 300;
        Rebuild(selectedSubmessage >= 0 ? selectedSubmessage : 0, selectedPattern);
        bodyToken.ResetHistory();
        idToken.ResetHistory();
    }
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { MessageBox.Show(this, ex.Message, "Submessages", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private void Commit()
    {
        if (current is not { } c || c.Message < 0 || c.Message >= model.Messages.Count)
            return;
        var m = model.Messages[c.Message];
        m.MessageId = fallback.Checked ? null : id.Text;
        if (c.Pattern >= 0 && c.Pattern < m.Patterns.Count)
            foreach (var edit in edits)
            {
                if (edit.Key != "regex" && edit.Value.Text.Length == 0)
                    m.Patterns[c.Pattern].Remove(edit.Key);
                else
                    m.Patterns[c.Pattern][edit.Key] = edit.Value.Text;
            }
        var parent = tree.Nodes.Count > c.Message ? tree.Nodes[c.Message] : null;
        if (parent is not null)
            parent.Text = $"submessage[{c.Message}]  ·  {m.MessageId ?? "DEFAULT"}";
    }
    private void LoadCurrent()
    {
        ClearGroupHighlight();
        bool valid = current is { } c && c.Message >= 0 && c.Message < model.Messages.Count;
        id.Enabled = fallback.Enabled = valid;
        foreach (var edit in edits.Values)
        {
            edit.Text = "";
            edit.Enabled = false;
            ((UndoTextBox)edit).ResetHistory();
        }
        if (!valid)
        {
            selection.Text = "Přidejte submessage";
            id.Clear();
            id.ResetHistory();
            return;
        }
        var value = current!.Value;
        var m = model.Messages[value.Message];
        id.Text = m.MessageId ?? "";
        fallback.Checked = m.MessageId is null;
        id.Enabled = !fallback.Checked;
        id.ResetHistory();
        selection.Text = $"submessage[{value.Message}]" + (value.Pattern >= 0 ? $".pattern[{value.Pattern}] — klikněte do skupiny regexu" : "");
        if (value.Pattern < 0)
            return;
        foreach (var edit in edits)
        {
            edit.Value.Text = m.Patterns[value.Pattern].GetValueOrDefault(edit.Key, "");
            edit.Value.Enabled = true;
            ((UndoTextBox)edit.Value).ResetHistory();
        }
    }
    private void Rebuild(int s, int p)
    {
        switching = true;
        current = null;
        tree.Nodes.Clear();
        for (int i = 0; i < model.Messages.Count; i++)
        {
            var m = model.Messages[i];
            var parent = new TreeNode($"submessage[{i}]  ·  {m.MessageId ?? "DEFAULT"}") { Tag = (i, -1) };
            for (int j = 0; j < m.Patterns.Count; j++)
                parent.Nodes.Add(new TreeNode($"pattern[{j}]") { Tag = (i, j) });
            tree.Nodes.Add(parent);
        }
        if (s >= 0 && s < tree.Nodes.Count)
        {
            var parent = tree.Nodes[s];
            parent.Expand();
            tree.SelectedNode = p >= 0 && p < parent.Nodes.Count ? parent.Nodes[p] : parent;
            current = ((int, int))tree.SelectedNode.Tag!;
        }
        switching = false;
        LoadCurrent();
    }
    private void MoveDefinition(int delta)
    {
        Commit();
        if (current is not { } c)
            return;
        if (c.Pattern >= 0)
        {
            var patterns = model.Messages[c.Message].Patterns;
            int next = c.Pattern + delta;
            if (next < 0 || next >= patterns.Count)
                return;
            (patterns[c.Pattern], patterns[next]) = (patterns[next], patterns[c.Pattern]);
            Rebuild(c.Message, next);
        }
        else
        {
            int next = c.Message + delta;
            if (next < 0 || next >= model.Messages.Count)
                return;
            (model.Messages[c.Message], model.Messages[next]) = (model.Messages[next], model.Messages[c.Message]);
            Rebuild(next, -1);
        }
    }
    private string BuildSource()
    {
        Commit();
        model.BodyToken = bodyToken.Text;
        model.IdToken = idToken.Text;
        model.Trim = trim.Checked;
        return model.Apply(original);
    }
    private void ClearGroupHighlight()
    {
        highlightVersion++;
        int start = sample.SelectionStart, length = sample.SelectionLength;
        sample.SelectAll();
        sample.SelectionBackColor = sample.BackColor;
        sample.Select(start, length);
    }
    private async Task HighlightGroupAsync()
    {
        ClearGroupHighlight();
        if (current is not { Pattern: >= 0 } selected)
            return;
        var group = RegexGroupMap.At(RegexGroupMap.Build(edits["regex"].Text), edits["regex"].SelectionStart);
        if (group is null)
        {
            result.Text = "Kurzor není v podporované zachytávací skupině.";
            return;
        }
        int version = highlightVersion;
        try
        {
            await highlightWorker.WaitAsync();
            try
            {
                if (IsDisposed || version != highlightVersion)
                    return;
                var parser = new ParserEvaluator(BuildSource());
                string input = sample.Text;
                var evaluated = await Task.Run(() => parser.Evaluate(input));
                if (IsDisposed || version != highlightVersion)
                    return;
                if (evaluated.UsedSubmessage != selected.Message || evaluated.UsedPattern != selected.Pattern)
                {
                    result.Text = $"{evaluated.Route}\r\nVybraný pattern nebyl použit pro tento log. {evaluated.Description}";
                    return;
                }
                var captures = evaluated.Captures.Where(c => c.Stage == "Submessage" && c.Group == group.Number).ToArray();
                int start = sample.SelectionStart, length = sample.SelectionLength;
                foreach (var capture in captures.Where(c => c.Length > 0))
                {
                    sample.Select(capture.Index, capture.Length);
                    sample.SelectionBackColor = Color.LightSkyBlue;
                }
                sample.Select(start, length);
                result.Text = $"{evaluated.Route} — skupina {group.Number} ({group.Name})\r\n" +
                    (captures.Length == 0 ? "Skupina v tomto logu nic nezachytila." : string.Join("; ", captures.Select(c => $"{c.Name}: pozice {c.Index}, délka {c.Length}")));
            }
            finally { highlightWorker.Release(); }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            if (!IsDisposed && version == highlightVersion)
                result.Text = ex.Message;
        }
    }
}
