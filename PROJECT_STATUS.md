# Project Status

## Current batch

TG3 — Evidence-backed series planner and Recent Play preview: **IMPLEMENTED; NEEDS USER TEST**. TG4 is NOT STARTED. No item is ACCEPTED.
Branch: feature/toolkit-integration. Master baseline remains adc29a1.

The user confirmed the TG2 Recent Play layout works and approved TG3's conservative preview recipe defaults.

## Delivered

Dashboard → Recent Play → Practice → Build preview now shows up to five distinct in-memory recipes, changes, evidence/fallback reasons, achieved head spacing, uncertainty and future audio requirements. Sparse evidence normally yields four recipes; eased AR requires compatible evidence. OD/CS/HP remain fixed because OD-specific evidence is not currently available.

PracticeSeriesPlanner reuses TG1 contracts/structured diagnosis and TG2 document/transforms. PracticePreviewSession and PracticePreviewSource provide cancellation, stale-result rejection and bounded read-only source hash checks before/after computation. Existing MainForm selection, BeatmapResolver and database remain authoritative. No map/audio generation, score change, new schema or reference runtime was introduced.

## Verification

- Restore and Debug application build: succeeded.
- Release suite: 87 passed, zero failed/skipped (71 existing plus 16 new).
- Windows x64 self-contained single-file publish: succeeded in OsuAimAnalyzer/bin/Release/net8.0-windows/win-x64/publish-tg3/.
- Existing WFAC010 high-DPI warning remains; no new test warnings.
- STA tests cover three layout sizes and the MainForm background preview/retry/changed-source/selection-reset flow using synthetic files and in-memory SQLite.
- Real-history/DPI visual testing of TG3 remains pending. No item is ACCEPTED.
- Reference remains read-only and excluded from commits; master baseline preserved.

## Manual checks

1. Close the prior app and launch publish-tg3/OsuAimAnalyzer.exe.
2. Select a known play, open Practice, and click Build preview. Read changes, reasons and omitted-variant notes; no files should be generated.
3. Try sparse-history and modded plays. Suggestions should clearly identify conservative defaults and NM/source-relative targets.
4. Try cancel/retry and select another play during work; old results should not replace the current selection.
5. Resize and compare existing Diagnosis/score views with familiar results. Pitch selection affects preview intent only; rebuild after changing it.

## Remaining limits and next batch

Policy thresholds are conservative engineering defaults, not causal evidence. Reliable contrary/conflicting evidence blocks reductions; modded-play evidence is not mapped to NM targets. Slider exits remain approximations, out-of-bounds spacing fails explicitly, and storyboard/video rate recipes are unavailable. Preview source size is limited to 16 MiB. Displayed results are snapshots; subsequent file edits require rebuilding.

TG4 must implement staged export, complete resources, physical containment checks and new identities/provenance. TG5 must render rate-adjusted audio. No export button exists in TG3. AIM-008 remains separate; the planner does not use lifetime streak evidence.
