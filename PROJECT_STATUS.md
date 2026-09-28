# Project Status

## Public beta preparation — v0.1.0-beta.1

IMPLEMENTED; NEEDS USER TEST. Added a screenshot-ready public feature guide, MIT license, beta test checklist, issue templates, release notes, and dependency license notices. Assembly/package version is 0.1.0-beta.1; scoring and database behavior are unchanged. TG6 remains deferred and nothing is marked ACCEPTED.

Restore, Debug build, all 133 Release tests, and self-contained Windows x64 publish passed. Existing WFAC010 warning remains. Public ZIP excludes FFmpeg binaries (optional setup_audio.ps1 download), debug symbols, personal data, and reference material. Read-only reference hashes are unchanged; master remains adc29a1. Public tester validation is still required for startup, display scaling, replay import, and osu! playback of exported maps.

## Current batch

Recent Play consolidation: **IMPLEMENTED; NEEDS USER TEST**. User-requested presentation work before additional features; TG6 remains deferred. No item is ACCEPTED. Branch: feature/toolkit-integration; master remains adc29a1.

Summary, Training and Compare are combined under Insights. Errors is combined with Diagnosis. The six destinations are Overview, Insights, Diagnosis, Practice, Top errors and Advanced. The combined reading pages use full-width collapsible sections, one outer scrollbar, larger headings and consistent spacing. Repeated demand/mechanical paragraphs are condensed; all their values remain in Insights. Scoring, diagnosis rules, storage and practice generation are unchanged.

Current test build: OsuAimAnalyzer/bin/Release/net8.0-windows/win-x64/publish-recent-play/OsuAimAnalyzer.exe. All 133 tests pass. A synthetic layout render was inspected; actual history and DPI readability remain manual checks.

Manual checks: select a play, review Insights and Diagnosis, collapse/expand sections, resize the window and switch plays. Confirm comparison/training/evidence are accessible without tiny nested panes; verify Overview charts, Practice export and both tools still work. This does not complete all B3 presentation checks or start TG6.

## Delivered

Recent Play → Practice → Build preview → Export practice maps… now exports selected slowdown and spacing variants. Preserve pitch is the default; Change pitch with rate is explicit. The existing planner normally offers four variants with sparse evidence, up to five when eased AR has support, and explains omissions. Original source-relative mod/stat rules remain unchanged.

One shared staged exporter now renders deduplicated audio through a pinned FFmpeg distribution, updates generated AudioFilename references, records schema-2 package provenance, verifies hashes and publishes without overwriting. Cancellation or failure cleans owned staging. Open package remains an explicit user action. Original mapsets, replay history, scoring and SQLite schema are unchanged. See docs/AUDIO_RUNTIME.md.

## Verification

- Restore and Debug build: succeeded.
- Release tests: 133 passed, zero failed/skipped; includes the new reading-page regression and updated navigation tests.
- Windows x64 self-contained publish: OsuAimAnalyzer/bin/Release/net8.0-windows/win-x64/publish-recent-play/.
- Existing WFAC010 high-DPI warning remains.
- Reference hash comparison: zero changes; no reference files tracked. Master baseline remains adc29a1. Published audio tools match the verified local installation.
- Native tests cover pitch/frequency, duration/timestamp bursts, MP3/Ogg input, running-process cancellation, full-series/MainForm export, audio sharing and pitch separation, source mutation and cleanup. Existing diagnosis snapshots pass unchanged.
- Actual osu! import/listening is not automated; it remains NEEDS USER TEST.

## Manual checks

1. Close the prior app, then launch publish-recent-play/OsuAimAnalyzer.exe. Keep the tools subfolder alongside it.
2. In Recent Play → Practice, Build preview and Export practice maps…. Leave the supported variants checked; save a new .osz outside Songs.
3. Open package to import in osu!stable. Check the generated difficulties form a separate practice mapset with the original title/background and that the original map still plays unchanged.
4. Play mild/strong slowdown and combined spacing without adding DT/HT. Check audio synchronization at the start, middle and end. Preserve pitch should retain the song's pitch.
5. Choose Change pitch with rate, rebuild and export another package. Slowdown should now lower pitch. Try cancellation and an existing filename; neither should leave a partial finished package or overwrite a previous export.

## Limits and next action

Rate export supports MP3/Ogg/WAV and produces larger PCM WAV assets. Video/storyboard events, variables, storyboard samples and external .osb files are omitted before practice transforms/resource discovery. Their assets are never copied. Song audio, the map background, breaks and gameplay hitsounds are retained. This also removes optional-media blockers from slowdown export. Missing audio tools likewise leave spacing/stat export available. Source maps and already-exported packages are not modified.

Documents are capped at 16 MiB, assets at 256 MiB, resources at 1 GiB total and 20,000 entries; audio has a 30-minute duration ceiling and ten-minute process timeout. Existing reparse-path restrictions remain. Rechecks address normal editing races, not hostile concurrent filesystem changes. Cancellation after atomic publication retains the completed artifact.

Next action is real-history testing of the consolidated Recent Play pages. TG6/AIM-017 broader generation needs scope decisions and has not begun. No status is ACCEPTED.
