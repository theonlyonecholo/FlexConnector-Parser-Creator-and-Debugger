using Avalonia;
using Avalonia.Controls;

namespace ParserDesigner.CrossPlatform;

internal sealed class HelpDialog : Window
{
    public HelpDialog(string topic = "start")
    {
        Ui.Setup(this, "Parser Designer · Offline help", 1100, 780);
        var search = Ui.Input("", "Search topic titles and their contents.");
        search.PlaceholderText = "Search help…";
        var topics = new ListBox();
        var body = Ui.Editor(readOnly: true);
        topics.SelectionChanged += (_, _) =>
        {
            if (topics.SelectedItem is TopicItem item)
                body.Text = item.Topic.Title + "\n\n" + item.Topic.Text;
        };
        void Filter()
        {
            var items = HelpTopics.All.Where(t => (t.Title + " " + t.Text).Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).Select(t => new TopicItem(t)).ToArray();
            topics.ItemsSource = items;
            topics.SelectedItem = items.FirstOrDefault(t => t.Topic.Id == topic) ?? items.FirstOrDefault();
            if (items.Length == 0)
                body.Text = "No topics match your search.";
        }
        search.TextChanged += (_, _) => Filter();
        Content = new Border { Padding = new Thickness(18), Child = Ui.Rows("Auto,*", search, Ui.Columns("280,*", topics, body)) };
        Filter();
    }
    private sealed record TopicItem(HelpTopic Topic)
    {
        public override string ToString() => Topic.Title;
    }
}
