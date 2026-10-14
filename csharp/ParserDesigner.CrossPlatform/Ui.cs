using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace ParserDesigner.CrossPlatform;

/// <summary>Small layout helpers shared by native windows; no Windows-specific controls or APIs.</summary>
internal static class Ui
{
    public static readonly FontFamily Mono = new("Cascadia Mono,Consolas,DejaVu Sans Mono,Menlo,monospace");
    public static TextEditor Editor(string text = "", bool readOnly = false) => new()
    {
        Text = text,
        IsReadOnly = readOnly,
        WordWrap = true,
        ShowLineNumbers = false,
        FontFamily = Mono,
        FontSize = 14,
        MinHeight = 60,
        Background = Brushes.White,
        Foreground = Brushes.Black,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        Document = NewDocument(text)
    };

    private static TextDocument NewDocument(string text)
    {
        var document = new TextDocument(text);
        document.UndoStack.SizeLimit = 100;
        return document;
    }

    public static TextBox Input(string text = "", string hint = "")
    {
        var box = new TextBox { Text = text, MinWidth = 100, UndoLimit = 100, VerticalAlignment = VerticalAlignment.Center };
        Tip(box, hint);
        return box;
    }

    public static TextBlock Label(string text, double size = 13) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    public static Button Button(string text, Action action, string hint = "")
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6) };
        button.Click += (_, _) => action();
        Tip(button, hint);
        return button;
    }
    public static Button AsyncButton(string text, Func<Task> action, string hint = "") => Button(text, async () => await action(), hint);
    public static void Tip(Control control, string text)
    {
        if (text.Length == 0)
            return;
        ToolTip.SetTip(control, text);
        Avalonia.Automation.AutomationProperties.SetHelpText(control, text);
    }
    public static WrapPanel Bar(params Control[] controls)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6) };
        foreach (var control in controls)
            panel.Children.Add(control);
        return panel;
    }
    public static StackPanel Stack(params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var control in controls)
            panel.Children.Add(control);
        return panel;
    }
    public static Grid Rows(string definitions, params Control[] children)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions(definitions), RowSpacing = 6 };
        for (int i = 0; i < children.Length; i++)
        {
            Grid.SetRow(children[i], i);
            grid.Children.Add(children[i]);
        }
        return grid;
    }
    public static Grid Columns(string definitions, params Control[] children)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(definitions), ColumnSpacing = 12 };
        for (int i = 0; i < children.Length; i++)
        {
            Grid.SetColumn(children[i], i);
            grid.Children.Add(children[i]);
        }
        return grid;
    }
    public static Border Card(Control content) => new() { Child = content, Padding = new Thickness(12), Background = Brushes.White, CornerRadius = new CornerRadius(8) };
    public static void Setup(Window window, string title, double width = 1000, double height = 740)
    {
        window.Title = title;
        window.Width = width;
        window.Height = height;
        window.MinWidth = Math.Min(width, 760);
        window.MinHeight = Math.Min(height, 540);
        window.Background = Brush.Parse("#eef2f7");
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
    public static async Task<string?> Ask(Window owner, string title, string message, params string[] choices)
    {
        var dialog = new Window();
        Setup(dialog, title, 560, 260);
        var bar = Bar(choices.Select(choice => (Control)Button(choice, () => dialog.Close(choice))).ToArray());
        dialog.Content = new Border { Padding = new Thickness(20), Child = Rows("*,Auto", new ScrollViewer { Content = Label(message, 15) }, bar) };
        return await dialog.ShowDialog<string?>(owner);
    }
    public static async Task Error(Window owner, Exception error) => await Ask(owner, "Unable to complete action", error.Message, "Close");
    public static async Task Guard(Window owner, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Error(owner, ex); }
    }
    public static void Replace(TextEditor editor, string text)
    {
        // One undo unit; highlighting never alters the document.
        using (editor.Document.RunUpdate())
            editor.Document.Replace(0, editor.Document.TextLength, text);
    }
    public static void Reset(TextEditor editor, string text)
    {
        editor.Text = text;
        editor.Document.UndoStack.ClearAll();
    }
    public static Button Help(Window owner, string topic) => AsyncButton("Help · F1", () => new HelpDialog(topic).ShowDialog(owner), "Search offline help for this editor.");
    public static void HelpKey(Window window, string topic)
    {
        window.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.F1)
            {
                e.Handled = true;
                await new HelpDialog(topic).ShowDialog(window);
            }
        };
    }
}

internal sealed record Highlight(int Index, int Length, int Color, bool Selected = false);

/// <summary>Paints capture backgrounds without modifying text or undo history. Offsets are UTF-16.</summary>
internal sealed class CaptureHighlighter : DocumentColorizingTransformer
{
    private static readonly IBrush[] Palette = [Brush.Parse("#bbdefb"), Brush.Parse("#ffe0b2"), Brush.Parse("#e1bee7"), Brush.Parse("#c8e6c9"), Brush.Parse("#fff9c4"), Brush.Parse("#b2ebf2")];
    public IReadOnlyList<Highlight> Spans { get; set; } = [];
    protected override void ColorizeLine(DocumentLine line)
    {
        foreach (var span in Spans.OrderBy(s => s.Selected))
        {
            int start = Math.Max(line.Offset, span.Index), end = Math.Min(line.EndOffset, span.Index + span.Length);
            if (start < end)
                ChangeLinePart(start, end, element => element.TextRunProperties.SetBackgroundBrush(span.Selected ? Brush.Parse("#82b1ff") : Palette[Math.Abs(span.Color) % Palette.Length]));
        }
    }
    public static CaptureHighlighter Attach(TextEditor editor)
    {
        var painter = new CaptureHighlighter();
        editor.TextArea.TextView.LineTransformers.Add(painter);
        return painter;
    }
    public void Paint(TextEditor editor, IEnumerable<Highlight> spans)
    {
        Spans = spans.ToArray();
        editor.TextArea.TextView.Redraw();
    }
}
