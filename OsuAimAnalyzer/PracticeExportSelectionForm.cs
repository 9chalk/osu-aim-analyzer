namespace OsuAimAnalyzer;

public sealed class PracticeExportSelectionForm : Form
{
    private readonly CheckedListBox choices = new() { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None };
    private readonly PracticeVariant[] eligible;
    public IReadOnlyList<PracticeVariant> SelectedVariants => choices.CheckedIndices.Cast<int>().Select(i => eligible[i]).ToArray();

    public PracticeExportSelectionForm(PracticeSeriesPreview series)
    {
        eligible = series.Variants.Where(v => v.Options.SourceClockRate == 1 && !v.Preview.RequiresAudioRendering).ToArray();
        Text = "Export practice maps"; ClientSize = new Size(540, 340); MinimumSize = new Size(450, 300);
        StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Panel; ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10f); Padding = new Padding(14);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = $"Choose the spacing/stat difficulties to package.\r\n{series.Variants.Count - eligible.Length} slowdown variant(s) cannot be exported until audio rendering is implemented. This exports only your selected subset." }, 0, 0);
        foreach (var variant in eligible) choices.Items.Add(variant.Name, true);
        layout.Controls.Add(choices, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = ToolkitUi.Button("Choose destination…");
        var cancel = ToolkitUi.Button("Cancel"); cancel.DialogResult = DialogResult.Cancel;
        save.Enabled = eligible.Length > 0;
        save.Click += (_, _) => { if (choices.CheckedItems.Count > 0) { DialogResult = DialogResult.OK; Close(); } };
        choices.ItemCheck += (_, e) => save.Enabled = choices.CheckedItems.Count + (e.NewValue == CheckState.Checked ? 1 : -1) > 0;
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 2); Controls.Add(layout);
        AcceptButton = save; CancelButton = cancel;
    }
}
