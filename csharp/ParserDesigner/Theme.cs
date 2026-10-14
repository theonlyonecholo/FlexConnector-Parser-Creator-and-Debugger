namespace ParserDesigner;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(241, 245, 249);
    public static readonly Color Ink = Color.FromArgb(30, 41, 59);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static void Apply(Control root)
    {
        root.BackColor = Background;
        root.ForeColor = Ink;
        if (root is Form)
            root.Font = new Font("Segoe UI", 10);
        foreach (Control c in root.Controls)
            Apply(c);
        switch (root)
        {
            case TextBox text:
                text.BackColor = Color.White;
                text.BorderStyle = BorderStyle.FixedSingle;
                break;
            case RichTextBox rich:
                rich.BackColor = Color.White;
                rich.BorderStyle = BorderStyle.FixedSingle;
                break;
            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(219, 234, 254);
                button.BackColor = Color.White;
                button.Padding = new Padding(5, 2, 5, 2);
                button.Cursor = Cursors.Hand;
                break;
            case DataGridView grid:
                grid.BackgroundColor = Color.White;
                grid.BorderStyle = BorderStyle.None;
                grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                grid.GridColor = Color.FromArgb(226, 232, 240);
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(226, 232, 240);
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
                grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9);
                grid.ColumnHeadersHeight = 36;
                grid.DefaultCellStyle.BackColor = Color.White;
                grid.DefaultCellStyle.ForeColor = Ink;
                grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
                grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 64, 175);
                grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
                grid.RowTemplate.Height = 30;
                break;
        }
        if (root is Form form)
            HelpUi.Install(form);
    }
    public static Label Header(string title, string subtitle) => new()
    {
        Text = title + "\n" + subtitle,
        Dock = DockStyle.Top,
        Height = 76,
        BackColor = Color.FromArgb(15, 23, 42),
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 13),
        Padding = new Padding(20, 12, 10, 8)
    };
}
