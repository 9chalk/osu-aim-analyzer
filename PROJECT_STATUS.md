# Project Status

## Current batch

TG2 — Preserving documents, resources and pure transformations: **IMPLEMENTED with documented support limits; NEEDS USER TEST**. TG3 is NOT STARTED. No item is ACCEPTED.
Branch: feature/toolkit-integration. Master baseline remains adc29a1.

## Delivered

TG1 immutable contracts and structured diagnosis remain intact. TG2 adds immutable preserving beatmap text/encoding, a referenced-resource inventory, an adapter to the existing analysis parser, and pure rate/stat/spacing preview transformations. No export, audio runtime, schema change or new practice tab was added.

Recent Play Summary, Training, Compare, Errors, Diagnosis and other readout pages use the full inner height; the play hero remains on Overview. Explicit fill sizing and immediate relayout support page switches/resizing.

## Verification

- Restore and Debug application build: succeeded.
- Release native suite: 71 passed, zero failed/skipped (43 existing, 28 new).
- Windows x64 self-contained single-file publish: succeeded in OsuAimAnalyzer/bin/Release/net8.0-windows/win-x64/publish-tg2/.
- Separate publish directory preserves the user's running prior build.
- Existing WFAC010 DPI warning remains.
- STA tests verify page/text bounds at three sizes; real-history/DPI visual checks remain NEEDS USER TEST.
- Reference folder is read-only and excluded from commits; master baseline preserved.

## Manual checks

1. Close the prior application, then launch publish-tg2/OsuAimAnalyzer.exe.
2. Select an analyzed replay in Dashboard Recent Play. Switch Summary, Training, Compare, Errors and Diagnosis; confirm text uses the inner height and remains readable when resized.
3. Return to Overview and confirm its play header returns. Check Top errors and Advanced with a real play.
4. Confirm familiar diagnosis, scoring and Quick Checker results remain unchanged.

TG2 is internal infrastructure, so no practice-generation navigation entry exists yet. TG3 adds the planner/preview UI.

## Remaining risks

Slider spacing uses control-point/repeat-parity exit proxies, not evaluated curves; achieved spacing is a head-distance metric. Only reductions are supported, and out-of-bounds layouts fail rather than clamp. Toolkit grouping/auto-fit is not implemented. Changed maps reject unsupported timed content, including videos/storyboards. Explicit difficulty edits require HP/CS/AR/OD keys and values in 0–10.

Resource inventory cannot certify a complete export: TG4 must resolve implicit samples, external .osb files, actual asset existence/containment and generated identities. TG5 must render rate-adjusted audio. Product choices for recipes, evidence thresholds, pitch/stat compensation and mod baking remain open. AIM-008 streak correction is separate.
