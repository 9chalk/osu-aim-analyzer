# TG3 release notes

- Added Dashboard → Recent Play → Practice → Build preview.
- Up to five distinct in-memory practice recipes show changes, evidence/fallback reasons, achieved head spacing, uncertainty and later audio needs.
- Sparse evidence normally yields four labeled conservative variants. A fifth eased-AR recipe requires compatible evidence; OD/CS/HP remain unchanged. Conflicting or unsupported recipes are explained rather than forced.
- Targets use the unmodded source; selected-play mods are displayed and never automatically baked in. Pitch selection records intent only.
- Background work supports cancellation, stale-selection rejection, retries and source MD5 verification. No map/audio files are generated and no scores/schema change.
- 87 native tests pass, including the MainForm workflow and prior diagnosis snapshots. Existing WFAC010 DPI warning remains.
- Manual checks: build a preview on a known play, read each recipe, try a sparse-history/modded play, cancel/retry, switch plays and resize. TG3 is NEEDS USER TEST, not ACCEPTED.
- Test build: bin/Release/net8.0-windows/win-x64/publish-tg3/OsuAimAnalyzer.exe.
