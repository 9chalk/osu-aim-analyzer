# TG4 release notes

- Practice → Build preview → Export spacing/stat maps… now saves an explicitly selected subset to a new .osz package. Current previews normally offer Reduced spacing for export; slowdown variants still need TG5 audio rendering.
- Original title/background/media are retained. Generated IDs/version names and a provenance manifest separate exported difficulties from their source. No source difficulty or arbitrary mapset file is copied.
- Nested resources, basic unchanged storyboards/animations and local hitsound banks are included. Missing/unsupported resources, unsafe paths, junctions and overwrite attempts fail with an explanation.
- Staging, ZIP/source hash verification, cancellation cleanup and non-overwriting publication are shared infrastructure for later rate export. Open package is an explicit user action.
- 101 tests pass, including archive contents, multi-variant export, source immutability, cancellation, collisions, changed files, paths/junctions and UI completion. Synthetic media tests do not certify osu! import/playback.
- Manual: export one Reduced spacing variant to a new local folder outside Songs; optionally Open package in osu!; verify it appears as a separate local set with the original title/background/audio, the new practice difficulty and reduced spacing. Check the original map still works. Try cancel and an existing filename. No item is ACCEPTED.
- Build: bin/Release/net8.0-windows/win-x64/publish-tg4/OsuAimAnalyzer.exe. Existing WFAC010 warning remains.

Format references checked during implementation: [official .osu specification](https://osu.ppy.sh/wiki/en/Client/File_formats/osu_%28file_format%29) and [storyboard objects](https://osu.ppy.sh/wiki/en/Storyboard/Scripting/Objects). Local sample banks are retained conservatively; sprite/animation dependencies follow these formats. Unsupported variables/events are rejected instead of guessed.

## TG4 availability feedback fix

The user's screenshot showed two slowdown variants and no Reduced spacing result: spacing was rejected for out-of-bounds geometry. Export/Open appeared inert because neither operation was available. The Practice page now shows the exportable count and the actual Reduced spacing omission reason beside the toolbar. Clicking an unavailable action explains the prerequisite; package-open failures appear there too. Unsupported transforms remain blocked. A new STA regression exercises real button clicks for this two-slowdown case and dispatch for an eligible spacing preview. No TG5 implementation is included.
