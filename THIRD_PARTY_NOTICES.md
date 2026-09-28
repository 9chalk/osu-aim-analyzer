# Third-party notices

The application source uses the MIT license. Dependencies retain their upstream terms. License copies and provenance are in `licenses/` and accompany the public beta package.

| Component | Version | License |
| --- | --- | --- |
| .NET / Windows Desktop runtime | 8.0.28 | MIT and included third-party notices |
| Microsoft.Data.Sqlite / Core | 8.0.10 | MIT |
| SharpCompress | 0.50.4 | MIT |
| SQLitePCLRaw packages | 2.1.6 | Apache-2.0; retain NOTICE |
| Native SQLite | bundled by SQLitePCLRaw | Public domain; see upstream notices |
| System.Memory dependency | 4.5.3 | Microsoft MIT; .NET runtime supplies the runtime implementation |

The public beta ZIP does **not** include FFmpeg binaries. Optional `setup_audio.ps1` downloads the pinned Gyan FFmpeg 9.0.2 essentials distribution for the user's local installation, retaining its GPLv3 license, vendor README, and provenance. See [audio runtime documentation](docs/AUDIO_RUNTIME.md). Redistributing those binaries requires separate compliance with their terms.

Optional song-selection memory-reader DLLs are not bundled in the public beta. Their setup script downloads separate upstream dependencies only when explicitly run.
