# Project Status

## Current batch

TG5 — Audio rendering and complete selected-map series: **IMPLEMENTED; NEEDS USER TEST**. No item is ACCEPTED. TG6 remains deferred.
Branch: feature/toolkit-integration. Master baseline remains adc29a1.
The user confirmed TG5 works and requested that practice exports omit all video/storyboard media to avoid unnecessary Songs-folder growth. This omission follow-up is IMPLEMENTED; NEEDS USER TEST.

## Delivered

Recent Play → Practice → Build preview → Export practice maps… now exports selected slowdown and spacing variants. Preserve pitch is the default; Change pitch with rate is explicit. The existing planner normally offers four variants with sparse evidence, up to five when eased AR has support, and explains omissions. Original source-relative mod/stat rules remain unchanged.

One shared staged exporter now renders deduplicated audio through a pinned FFmpeg distribution, updates generated AudioFilename references, records schema-2 package provenance, verifies hashes and publishes without overwriting. Cancellation or failure cleans owned staging. Open package remains an explicit user action. Original mapsets, replay history, scoring and SQLite schema are unchanged. See docs/AUDIO_RUNTIME.md.

## Verification

- Restore and Debug build: succeeded.
- Release tests: 120 passed, zero failed/skipped; 15 added since the spacing build.
- Windows x64 self-contained publish: OsuAimAnalyzer/bin/Release/net8.0-windows/win-x64/publish-practice-lite/.
- Existing WFAC010 high-DPI warning remains.
- Reference hash comparison: zero changes; no reference files tracked. Master baseline remains adc29a1. Published audio tools match the verified local installation.
- Native tests cover pitch/frequency, duration/timestamp bursts, MP3/Ogg input, running-process cancellation, full-series/MainForm export, audio sharing and pitch separation, source mutation and cleanup. Existing diagnosis snapshots pass unchanged.
- Actual osu! import/listening is not automated; it remains NEEDS USER TEST.

## Manual checks

1. Close the prior app, then launch publish-practice-lite/OsuAimAnalyzer.exe. Keep the tools subfolder alongside it.
2. In Recent Play → Practice, Build preview and Export practice maps…. Leave the supported variants checked; save a new .osz outside Songs.
3. Open package to import in osu!stable. Check the generated difficulties form a separate practice mapset with the original title/background and that the original map still plays unchanged.
4. Play mild/strong slowdown and combined spacing without adding DT/HT. Check audio synchronization at the start, middle and end. Preserve pitch should retain the song's pitch.
5. Choose Change pitch with rate, rebuild and export another package. Slowdown should now lower pitch. Try cancellation and an existing filename; neither should leave a partial finished package or overwrite a previous export.

## Limits and next action

Rate export supports MP3/Ogg/WAV and produces larger PCM WAV assets. Video/storyboard events, variables, storyboard samples and external .osb files are omitted before practice transforms/resource discovery. Their assets are never copied. Song audio, the map background, breaks and gameplay hitsounds are retained. This also removes optional-media blockers from slowdown export. Missing audio tools likewise leave spacing/stat export available. Source maps and already-exported packages are not modified.

Documents are capped at 16 MiB, assets at 256 MiB, resources at 1 GiB total and 20,000 entries; audio has a 30-minute duration ceiling and ten-minute process timeout. Existing reparse-path restrictions remain. Rechecks address normal editing races, not hostile concurrent filesystem changes. Cancellation after atomic publication retains the completed artifact.

Next action is TG5 user testing. TG6/AIM-017 broader generation needs scope decisions and has not begun. No status is ACCEPTED.
