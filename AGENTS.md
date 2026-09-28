# Repository Guidelines

## Project Structure & Module Organization

`OsuAimAnalyzer/` contains a single .NET 8 Windows Forms project, `OsuAimAnalyzer.csproj`, for analyzing osu!standard replays. `Program.cs` starts the app; `MainForm.cs`, other `*Form.cs` files, and `*Control.cs` files implement the UI. Replay and beatmap parsing live in `ReplayReader.cs` and `BeatmapParser.cs`; analysis and scoring live in the engine classes, `ProductionScoring.cs`, `ScoringConfig.cs`, and `TuningConfig.cs`. `AnalyzerDatabase.cs` manages SQLite persistence.

Build/setup scripts, the application manifest, README, references, and version notes are alongside the source. The root ZIP preserves the original v32 source snapshot; edit extracted files. There is currently no test project or dedicated assets directory.

## Build, Test, and Development Commands

Use Windows with the .NET 8 SDK. From the repository root:

- `dotnet restore OsuAimAnalyzer/OsuAimAnalyzer.csproj` restores dependencies.
- `dotnet build OsuAimAnalyzer/OsuAimAnalyzer.csproj` compiles the application.
- `dotnet run --project OsuAimAnalyzer/OsuAimAnalyzer.csproj` launches the UI.
- `OsuAimAnalyzer\build-release.bat` publishes a self-contained Windows x64 executable under `OsuAimAnalyzer/bin/Release/net8.0-windows/win-x64/publish/`.

`build-debug.bat` is an interactive shortcut for `dotnet run`. The optional `setup_native.ps1` downloads dependencies for the song-select memory reader.

## Coding Style & Naming Conventions

Follow surrounding C# style: four-space indentation, braces on separate lines, and file-scoped `namespace OsuAimAnalyzer;`. Use PascalCase for types and public members, camelCase for parameters and locals, and preserve existing private-field conventions. Match filenames to their primary types. Nullable reference types and implicit usings are enabled. No repository formatter or lint configuration is currently defined; avoid unrelated formatting changes.

## Testing Guidelines

No automated test framework or coverage threshold is configured. Build changes and manually verify affected workflows with local `.osr` replays and matching `.osu` beatmaps. For UI changes, check startup, resizing, and affected views. For scoring changes, compare results against known inputs. If adding tests, use a separate test project and descriptive names such as `Method_Scenario_ExpectedResult`; document its test command.

## Commit & Pull Request Guidelines

The initial commit is `Baseline before agentic development`; no formal commit convention is established. Use concise, descriptive messages and focused commits. PRs should explain behavior changes, validation performed, and related issues; include screenshots for UI changes. Explicitly identify scoring or database compatibility changes. Preserve scoring/config presets and useful test fixtures; exclude generated output and personal replay history.

## Reference Boundary

Everything in reference/ is read-only comparison material, excluded from Git. Do not edit, run installers in, generate caches in, or copy source wholesale from that tree. Production code lives in OsuAimAnalyzer/. Use docs/TOOLKIT_V1_3_PORTING_AUDIT.md for reuse decisions. Toolkit integration work belongs on feature/toolkit-integration; preserve the master baseline. Planning does not authorize feature implementation.
