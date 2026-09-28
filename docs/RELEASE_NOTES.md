# Release notes

## v0.1.0-beta.1

First public Windows x64 testing release of the current Aim Analyzer.

- Local osu!stable replay import, history, aim profiles, and training guidance.
- Six Recent Play destinations, with full-width collapsible reading sections.
- Cursor-path inspection for the worst aim transitions.
- Practice previews and separate .osz exports for supported spacing/stat and slowdown variants.
- Optional pitch-preserving or rate-shifted audio, installed separately using setup_audio.ps1.
- Public feature guide, tester instructions, issue templates, MIT license, and dependency notices.

Public packaging excludes FFmpeg binaries, personal history, reference material, and debug symbols. Videos and storyboards are omitted from generated practice packages. Application scoring and database schema are unchanged by release preparation.

Status: IMPLEMENTED; NEEDS USER TEST. No additional backlog item is ACCEPTED. Test startup, display scaling, replay import, Recent Play navigation, and exported-map synchronization in osu!stable. The window title retains its historical internal v31 label; use the release ZIP version when reporting issues.
