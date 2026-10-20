using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace ParserDesigner.CrossPlatform;

/// <summary>Original vector action icons. Labels remain visible and retain accessible names.</summary>
internal static class Icons
{
    private static readonly IReadOnlyDictionary<string, string> Paths = new Dictionary<string, string>
    {
        ["open"] = "M3,7 L3,4 L10,4 L12,7 L21,7 L21,19 L3,19 Z M3,10 L21,10",
        ["save"] = "M4,3 L18,3 L21,6 L21,21 L3,21 L3,3 Z M7,3 L7,9 L17,9 L17,3 M7,21 L7,14 L17,14 L17,21",
        ["file"] = "M5,3 L14,3 L19,8 L19,21 L5,21 Z M14,3 L14,8 L19,8 M8,12 L16,12 M8,16 L16,16",
        ["play"] = "M7,4 L21,12 L7,20 Z",
        ["tokens"] = "M3,5 L9,5 L9,11 L3,11 Z M15,5 L21,5 L21,11 L15,11 Z M9,15 L15,15 L15,21 L9,21 Z M6,11 L6,18 L9,18 M18,11 L18,18 L15,18",
        ["branch"] = "M5,3 L5,17 Q5,21 9,21 L19,21 M5,8 L19,8 M15,4 L19,8 L15,12 M15,17 L19,21 L15,25",
        ["mapping"] = "M3,5 L9,5 L9,11 L3,11 Z M15,13 L21,13 L21,19 L15,19 Z M9,8 L18,8 L18,13",
        ["settings"] = "M3,6 L21,6 M3,12 L21,12 M3,18 L21,18 M7,3 L7,9 M16,9 L16,15 M10,15 L10,21",
        ["search"] = "M16,10 A6,6 0 1 1 4,10 A6,6 0 1 1 16,10 M15,15 L21,21",
        ["help"] = "M12,2 A10,10 0 1 1 12,22 A10,10 0 1 1 12,2 M9,8 Q9,5 12,5 Q16,5 16,9 Q16,11 12,13 L12,15 M12,18 L12,19",
        ["add"] = "M12,3 L12,21 M3,12 L21,12",
        ["remove"] = "M4,6 L20,6 M9,3 L15,3 M6,6 L7,21 L17,21 L18,6 M10,10 L10,17 M14,10 L14,17",
        ["check"] = "M3,12 L9,18 L21,5",
        ["close"] = "M5,5 L19,19 M19,5 L5,19",
        ["stop"] = "M5,5 L19,5 L19,19 L5,19 Z",
        ["library"] = "M3,3 L8,3 L8,21 L3,21 Z M11,3 L16,3 L16,21 L11,21 Z M18,4 L22,20",
        ["group"] = "M8,3 Q2,12 8,21 M16,3 Q22,12 16,21",
        ["spark"] = "M12,2 L15,9 L22,12 L15,15 L12,22 L9,15 L2,12 L9,9 Z",
        ["undo"] = "M8,4 L3,9 L8,14 M3,9 L14,9 Q21,9 21,16 L21,20",
        ["arrow"] = "M3,12 L21,12 M15,6 L21,12 L15,18",
        ["previous"] = "M21,12 L3,12 M9,6 L3,12 L9,18",
        ["up"] = "M12,21 L12,3 M6,9 L12,3 L18,9",
        ["down"] = "M12,3 L12,21 M6,15 L12,21 L18,15",
        ["edit"] = "M4,16 L16,4 L20,8 L8,20 L3,21 Z M13,7 L17,11"
    };

    private static string Key(string label)
    {
        if (label.StartsWith("Previous", StringComparison.Ordinal))
            return "previous";
        if (label.StartsWith("Next", StringComparison.Ordinal))
            return "arrow";
        if (label == "Earlier")
            return "up";
        if (label == "Later")
            return "down";
        if (label.StartsWith("Write", StringComparison.Ordinal))
            return "save";
        if (label.StartsWith("Edit", StringComparison.Ordinal) || label.StartsWith("Build", StringComparison.Ordinal))
            return "edit";
        if (label.StartsWith("New", StringComparison.Ordinal))
            return "add";
        if (label.StartsWith("Clear", StringComparison.Ordinal) || label == "Discard")
            return "remove";
        if (label.StartsWith("Check", StringComparison.Ordinal))
            return "check";
        if (label == "Replace")
            return "arrow";
        if (label.Contains(" · ", StringComparison.Ordinal))
            return "add"; // Format-symbol insertion.
        if (label.StartsWith("Open", StringComparison.Ordinal) || label.StartsWith("Choose folder", StringComparison.Ordinal))
            return "open";
        if (label.StartsWith("Save", StringComparison.Ordinal))
            return "save";
        if (label.StartsWith("Load", StringComparison.Ordinal))
            return "file";
        if (label.StartsWith("Test", StringComparison.Ordinal) || label.StartsWith("Run", StringComparison.Ordinal) || label.StartsWith("Evaluate", StringComparison.Ordinal))
            return "play";
        if (label.StartsWith("Tokens", StringComparison.Ordinal))
            return "tokens";
        if (label.StartsWith("Submessages", StringComparison.Ordinal))
            return "branch";
        if (label.StartsWith("Event mapping", StringComparison.Ordinal) || label.StartsWith("Choose ArcSight", StringComparison.Ordinal))
            return "mapping";
        if (label.StartsWith("Event preview", StringComparison.Ordinal))
            return "search";
        if (label.StartsWith("Severity", StringComparison.Ordinal) || label.StartsWith("Manage", StringComparison.Ordinal))
            return "settings";
        if (label.StartsWith("Help", StringComparison.Ordinal))
            return "help";
        if (label.StartsWith("+", StringComparison.Ordinal) || label.StartsWith("Add", StringComparison.Ordinal))
            return "add";
        if (label.StartsWith("Remove", StringComparison.Ordinal) || label.StartsWith("Delete", StringComparison.Ordinal))
            return "remove";
        if (label.StartsWith("Apply", StringComparison.Ordinal) || label.StartsWith("Use ", StringComparison.Ordinal))
            return "check";
        if (label is "Cancel" or "Close")
            return "close";
        if (label is "Stop")
            return "stop";
        if (label.StartsWith("Regex presets", StringComparison.Ordinal))
            return "library";
        if (label.StartsWith("Group", StringComparison.Ordinal))
            return "group";
        if (label.StartsWith("Suggest", StringComparison.Ordinal) || label.StartsWith("Generate", StringComparison.Ordinal))
            return "spark";
        if (label.StartsWith("Restore", StringComparison.Ordinal))
            return "undo";
        if (label.Contains(" → ", StringComparison.Ordinal))
            return "arrow";
        return "arrow";
    }

    public static void Decorate(Button button, string label)
    {
        string key = Key(label);
        Avalonia.Automation.AutomationProperties.SetName(button, label);
        button.ContentTemplate = new FuncDataTemplate<string>((text, _) =>
        {
            var caption = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(caption, 1);
            return new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 7,
                Children =
                {
                    new Path
                    {
                        Data = Geometry.Parse(Paths[key]), Width = 16, Height = 16,
                        Stretch = Stretch.Uniform, Stroke = Brush.Parse("#315d84"),
                        StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round,
                        StrokeJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center,
                        IsHitTestVisible = false
                    },
                    caption
                }
            };
        });
    }
}
