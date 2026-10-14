using System.Runtime.InteropServices;
using ParserCore;

namespace ParserDesigner;

internal sealed partial class MainForm
{
    private void Step(int direction)
    {
        if (grid.Rows.Count == 0)
            return;
        int index = grid.SelectedRows.Count == 0 ? (direction > 0 ? -1 : grid.Rows.Count) : grid.SelectedRows[0].Index;
        index = Math.Clamp(index + direction, 0, grid.Rows.Count - 1);
        grid.CurrentCell = grid.Rows[index].Cells[0];
        grid.Rows[index].Selected = true;
        tabs.SelectedIndex = 1;
        HighlightSelected();
    }
    private void HighlightSelected()
    {
        activeRegexGroup = null;
        DebugRow? row = grid.SelectedRows.Count == 0 ? null : grid.SelectedRows[0].Tag as DebugRow;
        PaintHighlights(row);
        if (row is not null)
            status.Text = $"Match {row.Match}, group {row.Group} ({row.Name}), capture {row.Capture}, position {row.Index}, length {row.Length}.";
    }
    private void HighlightRegexGroup()
    {
        if (result is null)
            return;
        var group = RegexGroupMap.At(groupRegions, pattern.SelectionStart);
        activeRegexGroup = group?.Number;
        populating = true;
        grid.ClearSelection();
        populating = false;
        PaintHighlights(null);
        if (group is null)
        {
            status.Text = result.Error is not null ? result.Status : "The cursor is not inside a supported capturing group. Click between its parentheses.";
            return;
        }
        string tokenName = "";
        try
        {
            tokenName = new PropertiesDocument(source.Text).Get($"token[{group.Number - 1}].name");
        }
        catch (FormatException) { }
        var captures = result.Rows.Where(r => r.Group == group.Number && r.Success).ToArray();
        string label = $"Group {group.Number} ({group.Name})" + (tokenName.Length > 0 ? $" / token {tokenName}" : "");
        status.Text = captures.Length == 0 ? label + " — no capture in the current message." :
            label + $" — {captures.Length} captures highlighted in blue" +
            (captures.Any(r => r.Length == 0) ? "; empty captures at positions " + string.Join(", ", captures.Where(r => r.Length == 0).Take(10).Select(r => r.Index)) : "") + ".";
    }
    private void PaintHighlights(DebugRow? active)
    {
        if (coloring || message.IsDisposed)
            return;
        coloring = true;
        int start = message.SelectionStart, length = message.SelectionLength;
        // Formatting must not move the user's caret or scroll position while typing.
        var scroll = new NativePoint();
        SendMessage(message.Handle, 0x04DD, IntPtr.Zero, ref scroll); // EM_GETSCROLLPOS
        SendMessage(message.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero); // WM_SETREDRAW
        try
        {
            message.SelectAll();
            message.SelectionBackColor = message.BackColor;
            if (result is not null && analyzedText == message.Text)
            {
                foreach (TextSpan span in result.Matches)
                    if (span.Length > 0)
                    {
                        message.Select(span.Index, span.Length);
                        message.SelectionBackColor = Color.FromArgb(255, 235, 130);
                    }
                // Paint broad groups first, so nested, narrower captures remain visible.
                foreach (DebugRow row in result.Rows.Where(r => r.Group > 0 && r.Success && r.Length > 0).OrderByDescending(r => r.Length))
                {
                    message.Select(row.Index, row.Length);
                    message.SelectionBackColor = TokenColors.For(row.Group - 1);
                }
                if (active is { Success: true, Length: > 0 })
                {
                    message.Select(active.Index, active.Length);
                    message.SelectionBackColor = Color.LightSkyBlue;
                }
                if (activeRegexGroup is int group)
                    foreach (var row in result.Rows.Where(r => r.Group == group && r.Success && r.Length > 0))
                    {
                        message.Select(row.Index, row.Length);
                        message.SelectionBackColor = Color.LightSkyBlue;
                    }
            }
            message.Select(Math.Min(start, message.TextLength), Math.Min(length, Math.Max(0, message.TextLength - start)));
            if (length == 0)
                message.SelectionBackColor = message.BackColor;
        }
        finally
        {
            SendMessage(message.Handle, 0x04DE, IntPtr.Zero, ref scroll); // EM_SETSCROLLPOS
            SendMessage(message.Handle, 0x000B, (IntPtr)1, IntPtr.Zero);
            message.Invalidate();
            coloring = false;
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X; public int Y;
    }
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, ref NativePoint point);
}
