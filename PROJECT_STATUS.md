# Project Status

## Current batch

B2 — Play-scoped repeated-pattern aggregation: **IMPLEMENTED; NEEDS USER TEST**. AIM-008 is corrected; no item is ACCEPTED. Branch: feature/toolkit-integration; master remains adc29a1. The user confirmed the TG5 optional-media omission build works and requested continued development. TG6 remains deferred.

Historical streaks now stay inside one play and display their owning play ID/object range. Aggregate diagnoses/counts, scoring and storage are unchanged. Unknown play IDs contribute counts but cannot establish streaks. Twelve new tests cover boundaries, interleaving, deterministic ties, input preservation and profile reset. All 132 tests pass; previous diagnosis snapshots are unchanged.

Test build: OsuAimAnalyzer/bin/Release/net8.0-windows/win-x64/publish-streak-fix/OsuAimAnalyzer.exe. Inspect Aim Analysis's Why control breaks panel/history summary, switch history filters and compare Recent Play/detail summaries. Historical streak length or owner can change because unrelated plays no longer combine. Full text is available in the profile tooltip. Startup, real-history rendering and narrow/DPI layouts remain manual checks.

Next defined work is B3 presentation verification. Broader TG6 generation still needs map/category/output-count scope decisions.

## Delivered

Recent Play → Practice → Build preview → Export practice maps… now exports selected slowdown and spacing variants. Preserve pitch is the default; Change pitch with rate is explicit. The existing planner normally offers four variants with sparse evidence, up to five when eased AR has support, and explains omissions. Original source-relative mod/stat rules remain unchanged.

One shared staged exporter now renders deduplicated audio through a pinned FFmpeg distribution, updates generated AudioFilename references, records schema-2 package provenance, verifies hashes and publishes without overwriting. Cancellation or failure cleans owned staging. Open package remains an explicit user action. Original mapsets, replay history, scoring and SQLite schema are unchanged. See docs/AUDIO_RUNTIME.md.

## Verification

- Restore and Debug build: succeeded.
- Release tests: 132 passed, zero failed/skipped; 12 new B2 tests.
- Windows x64 self-contained publish: OsuAimAnalyzer/bin/Release/net8.0-windows/win-x64/publish-streak-fix/.
- Existing WFAC010 high-DPI warning remains.
- Reference hash comparison: zero changes; no reference files tracked. Master baseline remains adc29a1. Published audio tools match the verified local installation.
- Native tests cover pitch/frequency, duration/timestamp bursts, MP3/Ogg input, running-process cancellation, full-series/MainForm export, audio sharing and pitch separation, source mutation and cleanup. Existing diagnosis snapshots pass unchanged.
- Actual osu! import/listening is not automated; it remains NEEDS USER TEST.

## Manual checks

1. Close the prior app, then launch publish-streak-fix/OsuAimAnalyzer.exe. Keep the tools subfolder alongside it.
2. In Recent Play → Practice, Build preview and Export practice maps…. Leave the supported variants checked; save a new .osz outside Songs.
3. Open package to import in osu!stable. Check the generated difficulties form a separate practice mapset with the original title/background and that the original map still plays unchanged.
4. Play mild/strong slowdown and combined spacing without adding DT/HT. Check audio synchronization at the start, middle and end. Preserve pitch should retain the song's pitch.
5. Choose Change pitch with rate, rebuild and export another package. Slowdown should now lower pitch. Try cancellation and an existing filename; neither should leave a partial finished package or overwrite a previous export.

## Limits and next action

Rate export supports MP3/Ogg/WAV and produces larger PCM WAV assets. Video/storyboard events, variables, storyboard samples and external .osb files are omitted before practice transforms/resource discovery. Their assets are never copied. Song audio, the map background, breaks and gameplay hitsounds are retained. This also removes optional-media blockers from slowdown export. Missing audio tools likewise leave spacing/stat export available. Source maps and already-exported packages are not modified.

Documents are capped at 16 MiB, assets at 256 MiB, resources at 1 GiB total and 20,000 entries; audio has a 30-minute duration ceiling and ten-minute process timeout. Existing reparse-path restrictions remain. Rechecks address normal editing races, not hostile concurrent filesystem changes. Cancellation after atomic publication retains the completed artifact.

Next action is B2 real-history UI testing, followed by B3 presentation verification. TG6/AIM-017 broader generation needs scope decisions and has not begun. No status is ACCEPTED.
