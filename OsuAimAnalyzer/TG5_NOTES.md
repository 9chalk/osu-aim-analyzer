# TG5 — Slowdown audio export

Status: **IMPLEMENTED; NEEDS USER TEST**, not ACCEPTED.

Practice now offers Export practice maps… for selected slowdown/spacing variants. Preserve pitch remains the default; Change pitch with rate lowers pitch during slowdown. Rebuild the preview after changing this choice. Shared audio is rendered once per source-content/rate/pitch combination, packaged with generated maps, and recorded in provenance.

The release folder includes the pinned FFmpeg/ffprobe tools and their vendor license/provenance. Keep the whole folder together. Missing tools leave spacing-only export available. Unsupported video/storyboard retiming fails explicitly; no selected difficulty is silently dropped. PCM WAV output can make packages large.

No production scoring, SQLite schema, original map files or reference material changes. TG6 is not started.

Validation: 120 native tests pass; application build and self-contained Windows x64 publish succeed. WFAC010 is pre-existing. New tests exercise actual synthetic audio rendering and cancellation, pitch/duration/timing, MP3/Ogg input, complete selected-series export, deduplication, mutation and cleanup. Real osu! import/playback remains manual.

Launch publish-tg5/OsuAimAnalyzer.exe; open Recent Play → Practice, Build preview, Export practice maps…, then Open package. Check all selected variants, title/background, separate identities, start/middle/end sync, both pitch policies and original-map preservation. See ../PROJECT_STATUS.md and ../docs/AUDIO_RUNTIME.md for limits.
