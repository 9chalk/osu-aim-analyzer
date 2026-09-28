# osu! Aim Analyzer

A community tool for understanding your osu!standard aim through local osu!stable replays. Find recurring movement errors, compare attempts, and create practice maps.

**Windows x64 · Public beta · Local analysis**

Built with extensive AI assistance and player feedback. Findings are experimental training guidance, not official osu! ratings or proof of why a mistake happened.

## Features

### Replay analysis and history

- Import `.osr` replays or analyze your existing local replay history.
- Watch the replay folder for newly saved plays.
- Track aim performance and movement quality across sessions.
- Filter your history to compare similar maps and playing conditions.
- Compare repeated attempts on the same difficulty.

<!-- Add a Dashboard screenshot here. Example: ![Dashboard](docs/images/dashboard.png) -->

### Recent Play

- **Overview:** get a quick summary of the selected play.
- **Insights:** review performance, training guidance, and comparisons in one place.
- **Diagnosis:** explore recurring errors and comparisons with similar-map history.
- **Practice:** preview and export practice variations of the map.
- **Top errors:** inspect cursor paths around the worst aim transitions.
- **Advanced:** explore detailed measurements and breakdowns.
- Expand or collapse full-width information cards for easier reading.

<!-- Add a Recent Play screenshot here. -->

### Aim profiles and training

- See movement-quality trends and your recent aim profile.
- Explore conditions associated with errors, including speed and spacing.
- Get training targets and map recommendations when your history supports them.
- Review session training quotas.
- Optionally organize maps into osu! collections using proficiency rules.

<!-- Add an Aim analysis or training screenshot here. -->

### Practice maps

- Preview mild and stronger slowdown variants.
- Preview reduced-spacing and combined slowdown/spacing variants when valid.
- Get an eased-AR suggestion when qualifying history supports it.
- Export supported variants as an `.osz` package for manual import into osu!.
- Choose pitch-preserving audio or pitch that changes with playback speed.
- Keep source maps unchanged; generated packages are saved separately.
- Preserve map backgrounds and gameplay hitsounds; omit videos and storyboards.
- See explanations when a variant is unavailable.

**Slowdown exports require the optional audio setup below.** Replay analysis and spacing-only exports work without it. Suggestions depend on available evidence and map geometry; not every preview contains every variant.

<!-- Add a Practice preview or exported-map screenshot here. -->

## Getting started

1. Open [Releases](https://github.com/9chalk/osu-aim-analyzer/releases) and download the Windows x64 beta ZIP when available. The automatic **Source code** downloads are for development.
2. Extract the entire ZIP into a writable folder and launch `OsuAimAnalyzer.exe`. The release is self-contained; you do not need the .NET SDK.
3. Open **Settings / Import**, select your osu!stable, Songs, and replay folders, then use **Save + reindex**.
4. Start with **Import .osr files…** and a few replays whose matching beatmaps are installed. Use **Analyze replay history** for a larger import.
5. Select a play and explore Recent Play.

The beta is unsigned. Only download builds from this repository. osu!lazer and other game modes are not supported.

### Optional slowdown audio setup

The public beta ZIP does not bundle FFmpeg. From PowerShell in the extracted application folder, run:

```powershell
powershell -File .\setup_audio.ps1
```

This downloads a pinned, checksum-verified FFmpeg distribution into `tools/ffmpeg` beside the application. Internet access is required for setup. Restart the app afterward. See [audio runtime details](docs/AUDIO_RUNTIME.md).

In **Recent Play → Practice**, use **Build preview**, then **Export practice maps…**, then **Open package**. Timing changes are relative to the original unmodded map; played mods are context, not baked into exports. First test exported maps without DT/HT.

## Testing and feedback

See the [beta testing guide](docs/TESTING_BETA.md) and [report bugs or feedback](https://github.com/9chalk/osu-aim-analyzer/issues/new/choose). Include the build version, steps, expected result, and a screenshot where useful.

Analysis runs locally. Settings and history are stored under `%APPDATA%\OsuAimAnalyzer`. Review logs and screenshots for personal information before posting them. No replay library or beatmap media is bundled with the application.

## Development

Use Windows and the .NET 8 SDK:

```powershell
dotnet restore OsuAimAnalyzer/OsuAimAnalyzer.csproj
dotnet build OsuAimAnalyzer/OsuAimAnalyzer.csproj
powershell -File OsuAimAnalyzer/setup_audio.ps1
dotnet test OsuAimAnalyzer.Tests/OsuAimAnalyzer.Tests.csproj -c Release
dotnet run --project OsuAimAnalyzer/OsuAimAnalyzer.csproj
```

Read [Repository Guidelines](AGENTS.md) and [Architecture](docs/ARCHITECTURE.md). The README inside `OsuAimAnalyzer/` contains historical version notes; this page describes the current beta.

## License

[MIT](LICENSE): use, study, modify, and redistribute the application, including commercially, while preserving the license notice. Dependencies retain their own licenses; see [third-party notices](THIRD_PARTY_NOTICES.md). This is an independent community project, not an official osu! product.
