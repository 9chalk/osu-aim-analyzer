using System.Security.Cryptography;

namespace OsuAimAnalyzer;

public sealed record PracticeVariant(string Name, PracticeTransformOptions Options,
    BeatmapTransformResult Preview, string Reason);

public sealed class PracticeSeriesPreview
{
    public PracticeSourceIdentity Source { get; }
    public IReadOnlyList<PracticeVariant> Variants { get; }
    public IReadOnlyList<string> Notes { get; }
    internal PracticeSeriesPreview(PracticeSourceIdentity source, List<PracticeVariant> variants, List<string> notes)
    {
        Source = source;
        Variants = Array.AsReadOnly(variants.ToArray());
        Notes = Array.AsReadOnly(notes.ToArray());
    }
}

/// <summary>Bounded, deterministic preview policy v1. Associations guide proposals, not outcome promises.</summary>
public static class PracticeSeriesPlanner
{
    public static PracticeSeriesPreview Plan(PracticeSourceIdentity source, BeatmapDocument document,
        long evidencePlayId, IReadOnlyList<RecommendationEvidence> evidence, PracticePitchPolicy pitch,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (source.SelectedPlayId != evidencePlayId) throw new ArgumentException("Diagnosis belongs to a different selected play.");
        if (!Convert.ToHexString(MD5.HashData(document.ToBytes())).Equals(source.BeatmapHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source content no longer matches the selected replay's beatmap hash.");
        var map = BeatmapParser.ParseDocument(document);
        if (map.Mode != 0 || map.HitObjects.Count == 0) throw new NotSupportedException("Preview requires a nonempty osu!standard map.");
        var difficulty = new SerializedDifficulty(map.HP, map.CS, map.AR, map.OD);
        var variants = new List<PracticeVariant>();
        var notes = new List<string>
        {
            "Preview only: no files or audio are generated. Suggestions are associations, not proof of improvement.",
            "Rates and stats are relative to the unmodded source. Played mods are context only; none are baked in.",
            "Serialized AR/OD stay unchanged except the explicitly labeled AR recipe. Timing changes do not apply DT/HT preempt or hit-window scaling.",
            "HP, CS and OD remain unchanged. No OD-specific evidence is currently available."
        };
        var usableEvidence = source.PlayedMods == 0 ? evidence : Array.Empty<RecommendationEvidence>();
        if (source.PlayedMods != 0) notes.Add("Modded-play evidence is not used to select unmodded target values; conservative defaults are used instead.");

        RecommendationEvidence? Pick(string unit, out bool blocked)
        {
            var candidates = usableEvidence.Where(e => e.UnitKind == unit && Reliable(e))
                .OrderByDescending(e => e.ConfidencePercent).ThenByDescending(e => e.ErrorSamples)
                .ThenBy(e => e.ControlledUpperQuartile).ThenBy(e => e.Metric, StringComparer.Ordinal).ToArray();
            blocked = candidates.Any(e => e.Direction == "below" && e.ObservedMedian < e.ControlledLowerQuartile);
            if (blocked) { notes.Add($"{unit}: evidence points toward lower-demand errors or conflicts; reduction recipes omitted."); return null; }
            return candidates.FirstOrDefault(e => e.Direction == "above" && e.ObservedMedian > e.ControlledUpperQuartile);
        }
        var bpm = Pick("bpm", out bool blockRate);
        var spacing = Pick("spacing", out bool blockSpacing);
        var ar = Pick("ar", out bool blockAr);
        double strongRate = bpm is null ? .90 : Math.Round(Math.Clamp(bpm.ControlledUpperQuartile / bpm.ObservedMedian, .80, .95), 3);
        double mildRate = Math.Round((1 + strongRate) / 2, 3);
        double spacingRatio = spacing is null ? .90 : Math.Round(Math.Clamp(spacing.ControlledUpperQuartile / spacing.ObservedMedian, .80, .95), 3);
        string Reason(RecommendationEvidence? e, string fallback)
            => e is null ? "Conservative suggestion; insufficient qualifying evidence. " + fallback
                : $"Association-based: {e.Metric}, observed {e.ObservedMedian:0.##}, controlled band {e.ControlledLowerQuartile:0.##}–{e.ControlledUpperQuartile:0.##}; " +
                  $"{e.ConfidencePercent:0}% association confidence, lift {e.Lift:0.00}, {e.ErrorSamples} error / {e.ComparisonSamples} comparison samples. Targets are bounded, not guaranteed comfort levels.";
        string rateReason = Reason(bpm, "Try a small source-tempo reduction; geometry and serialized stats stay fixed.");
        string spacingReason = Reason(spacing, "Try 10% lower jump spacing while keeping BPM and CS fixed.");
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        string original = Convert.ToHexString(SHA256.HashData(document.ToBytes()));
        void Add(string name, double rate, double space, SerializedDifficulty stats, string reason)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var options = new PracticeTransformOptions(rate, space, difficulty, stats, pitch);
                var transformed = BeatmapTransforms.Apply(document, options, token);
                string fingerprint = Convert.ToHexString(SHA256.HashData(transformed.Document.ToBytes()));
                if (fingerprint == original || !fingerprints.Add(fingerprint)) { notes.Add($"{name}: omitted because it produces unchanged or duplicate content."); return; }
                // A translated slider can make the achieved head-spacing differ from the requested proxy spacing.
                if (space < 1 && transformed.AchievedHeadSpacingRatio >= .995)
                { notes.Add($"{name}: omitted because it does not meaningfully reduce measured head spacing."); return; }
                variants.Add(new(name, options, transformed, reason));
            }
            catch (Exception e) when (e is NotSupportedException or FormatException or ArgumentException)
            { notes.Add($"{name}: unavailable — {e.Message}"); }
        }
        if (!blockRate)
        {
            Add("Mild slowdown", mildRate, 1, difficulty, rateReason);
            Add("Stronger slowdown", strongRate, 1, difficulty, rateReason);
        }
        if (!blockSpacing) Add("Reduced spacing", 1, spacingRatio, difficulty, spacingReason);
        if (!blockRate && !blockSpacing) Add("Slowdown + spacing", strongRate, spacingRatio, difficulty,
            rateReason + " " + spacingReason + " Combined changes cannot isolate which variable helps.");
        if (!blockRate && !blockAr && ar is not null && map.AR - ar.ControlledUpperQuartile >= .25)
        {
            var eased = new SerializedDifficulty(map.HP, map.CS, Math.Round(Math.Max(0, map.AR - Math.Clamp(map.AR - ar.ControlledUpperQuartile, .25, 1)), 2), map.OD);
            Add("Slowdown + eased AR", mildRate, 1, eased, Reason(ar, "") + " OD is unchanged; lower AR also increases visible density.");
        }
        else notes.Add("Slowdown + eased AR: omitted without compatible qualifying AR evidence. No unsupported OD reduction is proposed.");
        notes.Add($"{variants.Count} of up to 5 distinct variants available; omissions are explained above.");
        notes.Add("Spacing is measured between consecutive heads. Groups are translated to fit, with spacing eased toward the source when necessary. Slider shapes stay intact; existing off-screen anchors cannot extend their original envelope. Exit positions remain control-point/repeat proxies, not exact rendered curves.");
        return new(source, variants, notes);
    }

    private static bool Reliable(RecommendationEvidence e)
        => new[] { e.ObservedMedian, e.ControlledMedian, e.ControlledLowerQuartile, e.ControlledUpperQuartile, e.ConfidencePercent, e.Lift }.All(double.IsFinite)
            && e.ObservedMedian > 0 && e.ControlledLowerQuartile > 0 && e.ControlledLowerQuartile <= e.ControlledMedian
            && e.ControlledMedian <= e.ControlledUpperQuartile && e.ErrorSamples >= 10 && e.ComparisonSamples >= 20
            && e.ConfidencePercent >= 55 && e.ConfidencePercent <= 100 && e.Lift >= 1.1;
}
