namespace ParserDesigner;

/// <summary>Maintains bounded text-difference history, excluding RichTextBox formatting changes.</summary>
internal sealed class EditHistory(TextBoxBase editor)
{
    private const int Capacity = 100;
    private sealed record Change(int Start, string Removed, string Inserted);
    private readonly List<Change> undo = [];
    private readonly List<Change> redo = [];
    private string previous = editor.Text;
    private bool restoring;
    private int transactionDepth;
    private bool changed;

    // A native replacement can raise separate deletion and insertion notifications.
    // Treat the complete window-message dispatch as one undoable transaction.
    public void Begin() => transactionDepth++;
    public void End()
    {
        if (--transactionDepth == 0 && changed)
        {
            changed = false;
            Changed();
        }
    }

    public void Reset()
    {
        undo.Clear();
        redo.Clear();
        previous = editor.Text;
        editor.ClearUndo();
    }
    public void Changed()
    {
        if (restoring)
            return;
        if (transactionDepth > 0)
        {
            changed = true;
            return;
        }
        string text = editor.Text;
        if (text == previous)
            return; // Coloring a RichTextBox is not an edit.
        undo.Add(CreateChange(previous, text));
        if (undo.Count > Capacity)
            undo.RemoveAt(0);
        redo.Clear();
        previous = text;
        editor.ClearUndo();
    }

    /// <summary>Finds the smallest contiguous replacement using shared prefix and suffix lengths.</summary>
    private static Change CreateChange(string before, string after)
    {
        int start = 0;
        int beforeEnd = before.Length;
        int afterEnd = after.Length;
        while (start < beforeEnd && start < afterEnd && before[start] == after[start])
            start++;
        while (beforeEnd > start && afterEnd > start && before[beforeEnd - 1] == after[afterEnd - 1])
        {
            beforeEnd--;
            afterEnd--;
        }
        return new Change(start, before[start..beforeEnd], after[start..afterEnd]);
    }
    public bool Command(Keys keys)
    {
        if (keys == (Keys.Control | Keys.Z))
        {
            Restore(false);
            return true;
        }
        if (keys == (Keys.Control | Keys.Y) || keys == (Keys.Control | Keys.Shift | Keys.Z))
        {
            Restore(true);
            return true;
        }
        return false;
    }
    private void Restore(bool forward)
    {
        var from = forward ? redo : undo;
        if (editor.ReadOnly || from.Count == 0)
            return;
        var change = from[^1];
        from.RemoveAt(from.Count - 1);
        restoring = true;
        try
        {
            editor.Select(change.Start, forward ? change.Removed.Length : change.Inserted.Length);
            editor.SelectedText = forward ? change.Inserted : change.Removed;
            editor.Select(change.Start + (forward ? change.Inserted.Length : change.Removed.Length), 0);
            previous = editor.Text;
            editor.ClearUndo();
            (forward ? undo : redo).Add(change);
            editor.ScrollToCaret();
        }
        finally { restoring = false; }
    }
    public void AddMenu()
    {
        var menu = new ContextMenuStrip();
        var back = menu.Items.Add("Zpět (Ctrl+Z)", null, (_, _) => Restore(false));
        var next = menu.Items.Add("Znovu (Ctrl+Y)", null, (_, _) => Restore(true));
        menu.Items.Add(new ToolStripSeparator());
        var cut = menu.Items.Add("Vyjmout", null, (_, _) => editor.Cut());
        menu.Items.Add("Kopírovat", null, (_, _) => editor.Copy());
        var paste = menu.Items.Add("Vložit", null, (_, _) => editor.Paste());
        menu.Items.Add("Vybrat vše", null, (_, _) => editor.SelectAll());
        menu.Opening += (_, _) =>
        {
            back.Enabled = !editor.ReadOnly && undo.Count > 0;
            next.Enabled = !editor.ReadOnly && redo.Count > 0;
            cut.Enabled = !editor.ReadOnly && editor.SelectionLength > 0;
            paste.Enabled = !editor.ReadOnly;
        };
        editor.ContextMenuStrip = menu;
        editor.Disposed += (_, _) => menu.Dispose();
    }
}

internal sealed class UndoTextBox : TextBox
{
    private readonly EditHistory history;
    public UndoTextBox()
    {
        history = new(this);
        history.AddMenu();
    }
    public void ResetHistory() => history.Reset();
    protected override void OnTextChanged(EventArgs e)
    {
        history?.Changed();
        base.OnTextChanged(e);
    }
    protected override void WndProc(ref Message m)
    {
        history?.Begin();
        try
        {
            base.WndProc(ref m);
        }
        finally { history?.End(); }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => history.Command(keyData) || base.ProcessCmdKey(ref msg, keyData);
}

internal sealed class UndoRichTextBox : RichTextBox
{
    private readonly EditHistory history;
    public UndoRichTextBox()
    {
        history = new(this);
        history.AddMenu();
    }
    public void ResetHistory() => history.Reset();
    protected override void OnTextChanged(EventArgs e)
    {
        history?.Changed();
        base.OnTextChanged(e);
    }
    protected override void WndProc(ref Message m)
    {
        history?.Begin();
        try
        {
            base.WndProc(ref m);
        }
        finally { history?.End(); }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => history.Command(keyData) || base.ProcessCmdKey(ref msg, keyData);
}
