using System.Drawing.Drawing2D;
using System.Reflection;

namespace OsuAimAnalyzer;

public static class ToolkitUi
{
    public static Button Button(string text)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Panel2, ForeColor = Theme.Text, Padding = new Padding(11, 2, 11, 2), Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = Theme.Border; b.FlatAppearance.MouseOverBackColor = Theme.Popup; b.FlatAppearance.MouseDownBackColor = Color.FromArgb(55, 60, 74);
        b.Resize += (_, _) => Round(b, 8);
        return b;
    }

    public static Control Wrap(Control child, string title)
    {
        // Use a real two-row layout instead of overlaying a docked title on top of a
        // padded fill control.  The old layout could shave a few pixels off the top
        // of DataGridViews at some DPI/scaling combinations, clipping header text.
        var p = new Panel
        {
            Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(10)
        };
        p.Resize += (_, _) => Round(p, 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Panel, Margin = Padding.Empty, Padding = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = title, Dock = DockStyle.Fill, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 10f),
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(2, 0, 0, 2)
        };

        child.Dock = DockStyle.Fill;
        child.Margin = Padding.Empty;
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(child, 0, 1);
        p.Controls.Add(layout);
        return p;
    }

    public static void StyleDataGrid(DataGridView g)
    {
        g.Dock = DockStyle.Fill;
        g.BackgroundColor = Theme.Panel;
        g.BorderStyle = BorderStyle.None;
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.ReadOnly = true;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = false;
        g.EnableHeadersVisualStyles = false;
        g.RowHeadersVisible = false;
        g.GridColor = Theme.Grid;
        try
        {
            typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(g, true);
        }
        catch { }

        // Give both the header and the first data row generous vertical room.  This
        // avoids the one-or-two-pixel clipping that can occur under Windows display
        // scaling while still leaving enough room for wrapped header labels.
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = 72;
        g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Panel2;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
        g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8.5f);
        g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        g.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 8, 4, 8);
        g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;

        g.DefaultCellStyle.BackColor = Theme.Panel;
        g.DefaultCellStyle.ForeColor = Theme.Text;
        g.DefaultCellStyle.SelectionBackColor = Theme.Popup;
        g.DefaultCellStyle.SelectionForeColor = Theme.Text;
        g.DefaultCellStyle.Padding = new Padding(2, 3, 2, 3);
        g.RowTemplate.Height = 34;
    }

    public static TabControl Tabs()
    {
        var tabs = new SmoothTabControl { Dock = DockStyle.Fill, Padding = new Point(18, 6), DrawMode = TabDrawMode.OwnerDrawFixed, ItemSize = new Size(150, 34), SizeMode = TabSizeMode.Fixed };
        tabs.DrawItem += (_, e) =>
        {
            bool active = e.Index == tabs.SelectedIndex;
            var rect = e.Bounds;
            using var bg = new SolidBrush(active ? Theme.Panel2 : Theme.Background);
            e.Graphics.FillRectangle(bg, rect);
            if (active) using (var pen = new Pen(Theme.Accent, 3)) e.Graphics.DrawLine(pen, rect.Left + 10, rect.Bottom - 2, rect.Right - 10, rect.Bottom - 2);
            string text = tabs.TabPages[e.Index].Text;
            using var tabFont = new Font("Segoe UI Semibold", 9f);
            TextRenderer.DrawText(e.Graphics, text, tabFont, rect, active ? Theme.Text : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        return tabs;
    }

    public static bool SetSplitterDistanceSafe(SplitContainer split, int desired)
    {
        // WinForms validates SplitterDistance against the *current* client size. During
        // construction/embedding that size can briefly be far smaller than the final
        // layout, so never assign until there is a legal interval.
        int total = split.Orientation == Orientation.Vertical
            ? split.ClientSize.Width
            : split.ClientSize.Height;
        if (total <= 0) return false;

        int min = Math.Max(0, split.Panel1MinSize);
        int max = total - Math.Max(0, split.Panel2MinSize) - Math.Max(1, split.SplitterWidth);
        if (max < min) return false;

        int safe = Math.Clamp(desired, min, max);
        try
        {
            if (split.SplitterDistance != safe) split.SplitterDistance = safe;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            // A parent layout can change size between measuring and assignment. The next
            // SizeChanged/Layout pass will retry instead of crashing the whole app.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static void RoundControl(Control c, int radius) => Round(c, radius);

    private static void Round(Control c, int radius)
    {
        if (c.Width <= 1 || c.Height <= 1) return;
        var path = new GraphicsPath(); int d = radius * 2;
        path.AddArc(0, 0, d, d, 180, 90); path.AddArc(c.Width-d-1, 0, d, d, 270, 90);
        path.AddArc(c.Width-d-1, c.Height-d-1, d, d, 0, 90); path.AddArc(0, c.Height-d-1, d, d, 90, 90); path.CloseFigure();
        c.Region?.Dispose(); c.Region = new Region(path);
    }
}


public sealed class SmoothTabControl : TabControl
{
    public SmoothTabControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = true;
    }
}
