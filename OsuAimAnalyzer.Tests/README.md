# Native TG1 tests

Run on Windows with .NET 8:

```powershell
dotnet test OsuAimAnalyzer.Tests/OsuAimAnalyzer.Tests.csproj -c Release
```

All inputs are synthetic. Database checks use SQLite :memory:, not the application data folder. Snapshot text was captured with invariant culture from BuildRunDiagnosis at planning baseline 41e9c34 before its refactor. Tests normalize line endings only and compare complete output, including recent sequence-response text. The capture switch was removed after baseline collection; changes to snapshots require an explained intentional behavior change, not automatic regeneration.

The suite covers the new contracts and existing math/diagnosis boundaries, not the full application or Toolkit Python suite. Real-history UI validation remains a user test; see ../PROJECT_STATUS.md. No test executes or writes to reference/.
