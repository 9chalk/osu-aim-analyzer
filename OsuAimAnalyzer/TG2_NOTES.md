# TG2 release notes

- Recent Play readouts now fill the inner height; the play header is shown on Overview only. Layout refreshes immediately when switching pages.
- Added preserving beatmap documents, resource references and pure rate/stat/spacing preview infrastructure. Existing analysis parsing is reused; transformed previews do not inherit source hashes or cached star ratings.
- No practice UI, export, audio renderer, scoring change or database migration.
- 71 tests pass; Debug build and self-contained Windows x64 publish succeed. Existing WFAC010 warning remains.
- Manual testing: select a recent play, switch readout pages and resize, then return to Overview. Check Top errors/Advanced with a real replay. This is NEEDS USER TEST, not ACCEPTED.
- Conservative transform limits: standard maps, spacing reductions with exit proxies, no out-of-bounds clamping, no storyboard/video retiming. Resource references are not an export-completeness guarantee. See docs/PRODUCT_SPEC.md and PROJECT_STATUS.md.
- Test executable: bin/Release/net8.0-windows/win-x64/publish-tg2/OsuAimAnalyzer.exe. The previous publish directory is unchanged because that build was running.
