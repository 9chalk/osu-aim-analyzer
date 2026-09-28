# Practice Audio Runtime

TG5 uses the **Gyan FFmpeg 9.0.2 essentials Windows build**, including ffmpeg.exe and ffprobe.exe. It is an external process dependency used only for rate export. Spacing-only export and replay analysis work without it.

## Install and publish

Run `powershell -File OsuAimAnalyzer/setup_audio.ps1` from the repository root before building/testing. The script downloads the pinned archive, verifies SHA-256, then installs two binaries plus the vendor LICENSE and README into ignored `OsuAimAnalyzer/tools/ffmpeg/`. It writes distribution.json with the archive/source provenance. The project copies these sidecars into build and publish output; distribute the **whole publish folder**, not just OsuAimAnalyzer.exe. In a published folder, setup_audio.ps1 installs beside the application. Nothing downloads at app startup.

- [Pinned archive](https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip)
- SHA-256: `60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`
- [Distribution information](https://www.gyan.dev/ffmpeg/builds/) — vendor identifies this build as GPLv3; preserve its included license/readme.
- [FFmpeg source revision](https://github.com/FFmpeg/FFmpeg/commit/946fcce07b)

Updates require an explicit version/checksum change and native regression tests; there is no automatic update or arbitrary PATH fallback. Build scripts warn if the runtime is absent. Audio tests intentionally fail without the tools rather than silently skipping coverage.

## Rendering contract

MP3, Ogg and WAV are accepted with forced demuxers, file/pipe protocols, and no shell. Output is 44.1 kHz stereo PCM16 WAV to avoid lossy encoder delay. [atempo](https://ffmpeg.org/ffmpeg-filters.html#atempo) preserves pitch; [asetrate](https://ffmpeg.org/ffmpeg-filters.html#asetrate), after resampling to a known sample rate, changes pitch with speed. Timing is relative to the original source; played mods are not baked in.

Each process has a ten-minute timeout, bounded diagnostic capture and cancellation/child-tree termination. Rates must be 0.5–2.0; durations must be positive and at most 30 minutes; predicted output must fit the 256 MiB asset cap. Output duration must match source/rate within max(80 ms, 0.1%). The exporter also enforces its aggregate resource limits. These are validation tolerances, not a claim of sample-exact phase alignment. Automated tone/burst tests measure frequency and timestamp behavior; osu! listening/import remains manual.

Practice export omits video, storyboard events/variables/sound effects, external .osb files and their assets. There is no optional-media retiming. Gameplay hitsounds and the map background are preserved. Output WAVs can be large. Rendering uses an owned source snapshot; content/rate/pitch/pipeline jobs are shared only within one package, with no persistent cache or database change.
