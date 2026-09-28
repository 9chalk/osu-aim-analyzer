using System.Text;

namespace OsuAimAnalyzer;

public sealed class PracticePreviewControl : UserControl
{
    private readonly Button preview = ToolkitUi.Button("Build preview");
    private readonly Button cancel = ToolkitUi.Button("Cancel");
    private readonly Button export = ToolkitUi.Button("Export spacing/stat maps…");
    private readonly Button open = ToolkitUi.Button("Open package");
    private PracticeSeriesPreview? currentPreview;
    private PublishedPracticePackage? published;
    private readonly Label actionStatus = new() { Dock = DockStyle.Top, AutoSize = true, ForeColor = Theme.Warn, Padding = new Padding(0, 0, 0, 8) };
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
    public event Action<PracticeSeriesPreview>? ExportRequested;

    public PracticePreviewControl()
    {
        Dock = DockStyle.Fill; BackColor = Theme.Panel; Padding = new Padding(10);
        pitch.Items.AddRange(new object[] { "Preserve pitch (later)", "Change pitch (later)" });
        pitch.SelectedIndex = 0;
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        toolbar.Controls.AddRange(new Control[] { preview, cancel, pitch, export, open });
        Controls.Add(readout); Controls.Add(actionStatus); Controls.Add(toolbar);
        Resize += (_, _) => actionStatus.MaximumSize = new Size(Math.Max(1, ClientSize.Width - Padding.Horizontal), 0);
        preview.Click += (_, _) => PreviewRequested?.Invoke(pitch.SelectedIndex == 0 ? PracticePitchPolicy.PreservePitch : PracticePitchPolicy.ChangeWithRate);
        cancel.Click += (_, _) => CancelRequested?.Invoke();
        export.Click += (_, _) =>
        {
            if (currentPreview is null) { actionStatus.Text = "Build a preview first, then export an eligible spacing/stat map."; return; }
            if (!currentPreview.Variants.Any(v => v.Options.SourceClockRate == 1 && !v.Preview.RequiresAudioRendering))
            {
                actionStatus.Text = "Nothing exportable in this preview. " + UnavailableReason();
                return;
            }
            ExportRequested?.Invoke(currentPreview);
        };
        open.Click += (_, _) =>
        {
            if (published is null) { actionStatus.Text = "No package has been exported yet. Export spacing/stat maps first; Open package then imports the saved .osz with your registered osu! client."; return; }
            try
            {
                if (!File.Exists(published.PackagePath)) throw new FileNotFoundException("The package was moved or deleted.");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(published.PackagePath) { UseShellExecute = true });
                actionStatus.Text = "Asked Windows to open: " + published.PackagePath;
            }
            catch (Exception e) { actionStatus.Text = "Could not open package: " + e.Message + " Package path: " + published.PackagePath; }
        };
        pitch.SelectedIndexChanged += (_, _) =>
        {
            currentPreview = null;
            SetBusy(false);
            if (hasSelection) readout.Text = "Pitch policy changed. Build preview again to refresh the options. No audio is generated in this batch.";
        };
        Reset(null);
    }

    public void Reset(string? mapName)
    {
        hasSelection = mapName is not null;
        currentPreview = null;
        published = null;
        SetBusy(false);
        readout.Text = mapName is null ? "Select an analyzed play to preview practice variants."
            : $"Selected: {mapName}\r\n\r\nBuild a preview, then explicitly export eligible spacing/stat maps to a new .osz package. Original maps stay unchanged.\r\n\r\nTargets use the unmodded source map. Slowdown variants require later audio rendering and cannot be exported yet.";
    }

    public void SetBusy(bool busy)
    {
        if (busy) { currentPreview = null; published = null; }
        preview.Enabled = hasSelection && !busy;
        cancel.Enabled = busy;
        pitch.Enabled = !busy;
        // Keep these actions reachable so an unavailable operation explains itself instead of silently ignoring clicks.
        export.Enabled = open.Enabled = !busy;
        int eligible = currentPreview?.Variants.Count(v => v.Options.SourceClockRate == 1 && !v.Preview.RequiresAudioRendering) ?? 0;
        actionStatus.Text = busy ? "Working… Cancel remains available."
            : published is not null ? "Package saved. Open package imports it with your registered osu! client."
            : currentPreview is not null ? $"{eligible} exportable spacing/stat map(s). " + (eligible == 0 ? UnavailableReason() : "Export opens the selection and save dialogs. Slowdown exports need TG5.")
            : "Build a preview before exporting. Open package requires a successful export.";
        if (busy) readout.Text = "Reading the selected map, comparing diagnosis evidence and building previews…\r\nYou can cancel or select another play.";
    }

    public void ShowMessage(string message) { SetBusy(false); readout.Text = message; }
    private string UnavailableReason() => "Slowdown maps need audio rendering (TG5). " +
        (currentPreview?.Notes.FirstOrDefault(n => n.StartsWith("Reduced spacing:", StringComparison.Ordinal)) ?? "See the omitted-variant explanations below.");
    public void ShowPreview(PracticeSeriesPreview result, string text) { currentPreview = result; published = null; ShowMessage(text); }
    public void ShowPublished(PublishedPracticePackage package)
    {
        published = package;
        ShowMessage($"Exported {package.DifficultyEntries.Count} spacing/stat difficulty(s) to:\r\n{package.PackagePath}\r\n\r\nSlowdown variants were not included. This is the explicitly selected subset, not the full preview series.\r\n\r\nOpen package to import it with your registered osu! client. Import is optional; your original source files were not edited.");
    }
    public void ShowProgress(string message) => readout.Text = message;

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
            text.AppendLine(variant.Preview.RequiresAudioRendering ? "Export unavailable until audio rendering is implemented." : "Eligible for spacing/stat export (resources validated when exporting).");
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
