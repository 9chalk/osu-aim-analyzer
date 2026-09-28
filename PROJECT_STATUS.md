# Project Status

## Current batch

TG4 — Staged spacing/stat package export: **IMPLEMENTED; NEEDS USER TEST**. TG5 is NOT STARTED. No item is ACCEPTED.
Branch: feature/toolkit-integration. Master baseline remains adc29a1.
The user confirmed TG3's preview looked alright and requested continued development.

## Delivered

Practice → Build preview → Export spacing/stat maps… lets the user explicitly select eligible rate-1 variants and choose a new .osz destination. The current planner normally offers Reduced spacing alone for export. Slowdown variants remain unavailable until TG5 and are counted/explained in the dialog; this is not presented as a complete series.

One shared exporter supports multiple spacing/stat variants, source/options revalidation, preserving metadata changes, nested resources, basic unchanged storyboards/animations, conservative local hitsound-bank inclusion, provenance JSON, ZIP/hash verification and non-overwriting same-directory publication. Open package is a separate user-triggered import action. There is no audio renderer, new schema, scoring change or source-map write.

## Verification

- Restore and Debug application build: succeeded.
- Release suite: 101 passed, zero failed/skipped (87 existing plus 14 new).
- Windows x64 self-contained single-file publish: succeeded in OsuAimAnalyzer/bin/Release/net8.0-windows/win-x64/publish-tg4/.
- Existing WFAC010 high-DPI warning remains.
- Tests cover selected multi-difficulty archive contents, fresh IDs/version names, title/background bytes, nested .osb/animation resources, hitsound banks, source immutability, collisions, missing/changed files, cancellation cleanup, unsafe paths/junctions and MainForm publish/open state.
- Tests use synthetic asset bytes: actual osu! import, separate-mapset behavior and media playback remain manual checks.
- Reference hashes unchanged; no reference files tracked. Master baseline preserved.

## Manual checks

1. Close the prior app and launch publish-tg4/OsuAimAnalyzer.exe.
2. Select a known play, open Practice and Build preview. Choose Export spacing/stat maps… and confirm the dialog explicitly excludes slowdown variants.
3. Save the selected Reduced spacing map to a new local .osz path outside Songs. Original map files should remain unchanged.
4. Optionally Open package in osu!. Check it imports as a separate local mapset, retains the original visible song title/background/audio, and contains only the selected practice difficulty with reduced spacing. Check the original map still works.
5. Try cancellation and choosing an existing filename. No finished package should be overwritten; failed/canceled work before publication should leave no staging file.

## Limits and next batch

TG5 is required for playable slowdown exports. Current source-relative stat/mod rules remain unchanged. Slider spacing still uses exit proxies, not exact curve evaluation. Basic storyboards are preserved only at unchanged rate; variables and unsupported resource/event types fail explicitly. Linked/reparse paths are rejected, including linked output folders; choose a normal local directory.

Documents are capped at 16 MiB, assets at 256 MiB each and 1 GiB total, and resource entries at 20,000. Assets are copied, not decoded. Rechecks detect normal editing races but do not constitute a hostile concurrent-filesystem security boundary. Cancellation after the atomic publication point retains the completed user-selected artifact. Import behavior remains NEEDS USER TEST, not ACCEPTED.

## TG4 feedback follow-up

IMPLEMENTED; NEEDS USER TEST: Practice now shows export eligibility and the Reduced spacing omission reason beside the buttons. Unavailable Export/Open actions explain why instead of ignoring clicks. The reported screenshot had only slowdown variants; out-of-bounds spacing was correctly rejected. No unsafe transform bypass or audio generation was added. Native suite now contains 102 passing tests, including actual action-click coverage. Test build: publish-tg4-fix/OsuAimAnalyzer.exe.
