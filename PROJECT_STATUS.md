# Project Status

## Current batch

TG1 — Shared contracts and native test foundation: **IMPLEMENTED; NEEDS USER TEST**.
Branch: feature/toolkit-integration. Master baseline remains adc29a1. No item is ACCEPTED. TG2 is NOT STARTED.

## Delivered

Immutable source identity, explicit source/generated difficulty and rate/spacing/pitch options, a durable-package result contract, and detached structured diagnosis evidence. Existing run diagnosis formatting and formulas are preserved. One native xUnit project adds 43 test cases with synthetic fixtures and in-memory SQLite; no personal replay assets are used.

## Verification

- Baseline and TG1 Debug application builds: succeeded.
- Release test suite: 43 passed, zero failed/skipped.
- Release win-x64 self-contained single-file publish: succeeded.
- Existing WFAC010 high-DPI warning: unchanged; no DPI behavior changed.
- Real-history UI smoke checks: NEEDS USER TEST; not performed by automation.
- Reference folder: read-only; content hashes checked against the audit inventory. No reference files are tracked.

## Manual checks

1. Run the application and select an existing analyzed replay in Dashboard Recent Play.
2. Open Diagnosis; check the text, contributors and training-response section against familiar results. Repeat with sparse history and a different selected replay.
3. Open the detailed diagnostics window's Diagnosis tab; confirm consistent content and normal navigation/resizing.
4. Confirm existing displayed proficiency/Aim Performance and selected-map Quick Checker still behave normally. No practice generator or new tab should appear.

## Remaining risks and decisions

Synthetic tests cannot certify all real replay/history combinations or WinForms layouts. Existing diagnosis fallback and confidence semantics, the lifetime streak issue AIM-008, and DPI warning remain unchanged. Contracts perform no map I/O; future exporters must enforce supported format ranges, check source freshness and ensure published-file lifetime. Five-variant policy, pitch/stat compensation, low-evidence thresholds and played-mod baking remain decisions for later batches. No audio renderer, map writer, schema change or Toolkit runtime dependency was added.
