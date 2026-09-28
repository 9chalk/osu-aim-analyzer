# TG1 — Shared contracts and native test foundation

Status: IMPLEMENTED; NEEDS USER TEST. Not ACCEPTED.

- Added immutable practice source identity, explicit transformation-option and published-package result contracts; these do not generate or modify maps.
- Exposed structured run-level diagnosis with numeric evidence and copied collections. Existing UI diagnosis text uses the same calculation/formatting path, with four pre-refactor snapshots verifying compatibility.
- Added a Windows .NET 8 xUnit test project: 43 cases for contract validation, immutable snapshots, existing mod/rate/AR/radius behavior, numeric evidence, empty/clean histories and in-memory SQLite equivalence.
- Debug build, Release tests and self-contained win-x64 publish pass. WFAC010 is a pre-existing DPI warning and is unchanged.
- No UI, score, schema, source-map or audio behavior changes intended. No Toolkit source copied; reference/ remains read-only. TG2 has not begun.

Manual validation: inspect Recent Play Diagnosis and detailed-window Diagnosis for existing and sparse-history runs, switch selections, and confirm familiar text/scores and normal window behavior. Generation is deliberately absent. See ../PROJECT_STATUS.md for limitations and the full checklist.
