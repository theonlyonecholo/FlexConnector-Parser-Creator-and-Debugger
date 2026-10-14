namespace ParserDesigner;

/// <summary>Displays and searches bundled offline help topics without requiring the reference PDF at runtime.</summary>
internal sealed class HelpWindow : Form
{
    private readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "Search help titles and content…" };
    private readonly ListBox topics = new() { Dock = DockStyle.Fill, DisplayMember = "Title", IntegralHeight = false, HorizontalScrollbar = true };
    private readonly RichTextBox content = new() { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, Font = new Font("Segoe UI", 11) };
    public HelpWindow(string initial = "start")
    {
        Text = "Help — ArcSight Parser Designer";
        Size = new Size(1120, 800);
        MinimumSize = new Size(850, 600);
        StartPosition = FormStartPosition.CenterParent;
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 8 };
        split.Panel1.Controls.Add(topics);
        split.Panel1.Controls.Add(search);
        split.Panel2.Controls.Add(content);
        split.Panel1.Controls.Add(new Label { Text = "Search titles and content:", Dock = DockStyle.Top, Height = 28, Padding = new Padding(4) });
        Controls.Add(split);
        topics.SelectedIndexChanged += (_, _) =>
        {
            if (topics.SelectedItem is not HelpTopic topic)
                return;
            content.Text = topic.Title + "\n\n" + topic.Text;
            content.SelectAll();
            content.SelectionFont = content.Font;
            content.Select(0, topic.Title.Length);
            content.SelectionFont = new Font("Segoe UI", 16, FontStyle.Bold);
            content.Select(0, 0);
            content.ScrollToCaret();
        };
        search.TextChanged += (_, _) => Filter();
        Theme.Apply(this);
        Filter();
        topics.SelectedItem = HelpTopics.All.FirstOrDefault(t => t.Id == initial) ?? HelpTopics.All[0];
        Shown += (_, _) => split.SplitterDistance = 320;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }
    private void Filter()
    {
        var selected = topics.SelectedItem;
        topics.BeginUpdate();
        topics.Items.Clear();
        string query = search.Text.Trim();
        foreach (var topic in HelpTopics.All.Where(t => (t.Title + " " + t.Text).Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            topics.Items.Add(topic);
        topics.EndUpdate();
        if (selected is not null && topics.Items.Contains(selected))
            topics.SelectedItem = selected;
        else if (topics.Items.Count > 0)
            topics.SelectedIndex = 0;
        else
            content.Text = "No topics match your search. Try a shorter term, such as token, mappings or timeout.";
    }
}
