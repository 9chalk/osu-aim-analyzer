# Beta testing guide

## First session

Download and extract the Windows x64 release ZIP, then launch `OsuAimAnalyzer.exe`. In **Settings / Import**, configure your osu!stable, Songs, and replay folders. Select **Save + reindex**, then import a few `.osr` files with matching installed maps before importing a large history.

## Things to try

- Start and resize the app, including at your usual Windows display scaling.
- Import a replay, select it, and compare its details with the map you played.
- Open Recent Play's Overview, Insights, Diagnosis, Practice, Top errors, and Advanced pages. Check readability, scrolling, and expandable cards.
- Compare repeated attempts and inspect history filters and aim-profile trends.
- Build a practice preview. Unavailable variants should have an explanation.
- Export a spacing variant, open its package, and import it into osu!. Confirm the original difficulty is unchanged.
- After optional audio setup, export a slowdown variant with each pitch option. Check music, object timing, sliders, and hitsounds in osu! without DT/HT.
- Try cancelling an export and exporting again. Existing destination files should not be silently overwritten.

## Known limits

- Windows x64 and osu!standard on osu!stable only.
- The beta is unsigned and has no automatic updater. Extract newer builds into a new folder; back up `%APPDATA%\OsuAimAnalyzer` before testing upgrades with valuable history.
- Aim diagnoses and local difficulty/PP estimates are experimental, not official ratings or guaranteed training outcomes.
- Sparse history can limit recommendations; invalid geometry can exclude practice variants.
- Slowdown export requires `powershell -File .\setup_audio.ps1` from the extracted app folder, followed by restarting the app. FFmpeg is downloaded separately.
- Generated audio uses WAV and can be large. Video/storyboard media is intentionally omitted.
- Missing source replays or exact beatmaps can prevent detailed cursor reconstruction.

## Reporting a problem

Use the repository's bug-report template. Include the release version, Windows/display scaling, relevant mods, reproducible steps, expected and actual results, and screenshots. The window title may retain a historical internal version label; report the downloaded release version.

Review screenshots and logs for usernames and local paths before posting them. Do not upload your entire replay history, database, or Songs folder. Settings/history normally live under `%APPDATA%\OsuAimAnalyzer`; startup crash logs can also appear under `%LOCALAPPDATA%\OsuAimAnalyzer`.
