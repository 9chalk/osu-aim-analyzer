namespace OsuAimAnalyzer;

public static class Theme
{
    public static readonly Color Background = Color.FromArgb(20, 22, 28);
    public static readonly Color Panel = Color.FromArgb(27, 30, 38);
    public static readonly Color Panel2 = Color.FromArgb(34, 38, 48);
    public static readonly Color Popup = Color.FromArgb(39, 43, 54);
    public static readonly Color Border = Color.FromArgb(62, 68, 84);
    public static readonly Color Grid = Color.FromArgb(44, 49, 60);
    public static readonly Color Text = Color.FromArgb(235, 238, 245);
    public static readonly Color Muted = Color.FromArgb(151, 159, 178);
    public static readonly Color Accent = Color.FromArgb(255, 102, 170);
    public static readonly Color Accent2 = Color.FromArgb(103, 178, 255);
    public static readonly Color Good = Color.FromArgb(91, 201, 143);
    public static readonly Color Warn = Color.FromArgb(242, 193, 78);
    public static readonly Color Bad = Color.FromArgb(239, 102, 111);

    public static void Style(Control c)
    {
        c.BackColor = Background; c.ForeColor = Text;
        foreach (Control child in c.Controls) Style(child);
    }
}
