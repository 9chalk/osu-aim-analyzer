using System.Text;

namespace OsuAimAnalyzer;

public sealed class PracticePreviewControl : UserControl
{
    private readonly Button preview = ToolkitUi.Button("Build preview");
    private readonly Button cancel = ToolkitUi.Button("Cancel");
    private readonly ComboBox pitch = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 165 };
    private readonly TextBox readout = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true,
        ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
        BackColor = Theme.Panel, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10f)
    };
    private bool hasSelection;
    public event Action<PracticePitchPolicy>? PreviewRequested;
    public event Action? CancelRequested;

    public PracticePreviewControl()
    {
        Dock = DockStyle.Fill; BackColor = Theme.Panel; Padding = new Padding(10);
        pitch.Items.AddRange(new object[] { "Preserve pitch (later)", "Change pitch (later)" });
        pitch.SelectedIndex = 0;
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        toolbar.Controls.AddRange(new Control[] { preview, cancel, pitch });
        Controls.Add(readout); Controls.Add(toolbar);
        preview.Click += (_, _) => PreviewRequested?.Invoke(pitch.SelectedIndex == 0 ? PracticePitchPolicy.PreservePitch : PracticePitchPolicy.ChangeWithRate);
        cancel.Click += (_, _) => CancelRequested?.Invoke();
        pitch.SelectedIndexChanged += (_, _) =>
        {
            if (hasSelection) readout.Text = "Pitch policy changed. Build preview again to refresh the options. No audio is generated in this batch.";
        };
        Reset(null);
    }

    public void Reset(string? mapName)
    {
        hasSelection = mapName is not null;
        SetBusy(false);
        readout.Text = mapName is null ? "Select an analyzed play to preview practice variants."
            : $"Selected: {mapName}\r\n\r\nBuild an in-memory preview of up to five practice variants. Your original map and scores stay unchanged.\r\n\r\nTargets use the unmodded source map. Pitch is an explicit option for later audio export; no audio or map files will be created now.";
    }

    public void SetBusy(bool busy)
    {
        preview.Enabled = hasSelection && !busy;
        cancel.Enabled = busy;
        pitch.Enabled = !busy;
        if (busy) readout.Text = "Reading the selected map, comparing diagnosis evidence and building previews…\r\nYou can cancel or select another play.";
    }

    public void ShowMessage(string message) { SetBusy(false); readout.Text = message; }

    public static string FormatPreview(PracticeSeriesPreview result)
    {
        var text = new StringBuilder();
        text.AppendLine($"PRACTICE PREVIEW · {result.Variants.Count} variants");
        text.AppendLine($"Source: {Path.GetFileName(result.Source.BeatmapPath)}");
        text.AppendLine($"Play {result.Source.SelectedPlayId} · played mods: {ModUtils.ToShortString(result.Source.PlayedMods)} · targets: NM source");
        text.AppendLine($"Source MD5: {result.Source.BeatmapHash}");
        text.AppendLine();
        foreach (var variant in result.Variants)
        {
            var o = variant.Options;
            var map = BeatmapParser.ParseDocument(variant.Preview.Document);
            var bpms = map.TimingPoints.Where(t => t.Uninherited && t.BeatLength > 0).Select(t => 60000 / t.BeatLength).ToArray();
            text.AppendLine(variant.Name.ToUpperInvariant());
            text.AppendLine($"Source rate {o.SourceClockRate:0.###}× ({100 * (o.SourceClockRate - 1):+0.#;-0.#;0}% BPM); preview BPM {(bpms.Length > 0 ? $"{bpms.Min():0.#}–{bpms.Max():0.#}" : "unavailable")}");
            text.AppendLine($"Requested spacing {o.SpacingMultiplier:0.###}×; achieved head spacing {variant.Preview.AchievedHeadSpacingRatio:0.###}×");
            text.AppendLine($"HP {o.SourceDifficulty.Hp:0.##} → {o.GeneratedDifficulty.Hp:0.##} · CS {o.SourceDifficulty.Cs:0.##} → {o.GeneratedDifficulty.Cs:0.##}");
            text.AppendLine($"AR {o.SourceDifficulty.Ar:0.##} → {o.GeneratedDifficulty.Ar:0.##} · OD {o.SourceDifficulty.Od:0.##} → {o.GeneratedDifficulty.Od:0.##} (serialized; NM effective values)");
            text.AppendLine($"Audio: {(variant.Preview.RequiresAudioRendering ? "rendering required in a later batch" : "unchanged")} · pitch option: {o.PitchPolicy}");
            text.AppendLine(variant.Reason);
            text.AppendLine();
        }
        text.AppendLine("LIMITS AND OMITTED VARIANTS");
        foreach (string note in result.Notes) text.AppendLine("• " + note);
        return text.ToString();
    }
}
