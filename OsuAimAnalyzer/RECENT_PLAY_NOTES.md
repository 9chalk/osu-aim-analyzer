# Recent Play — fewer tabs, clearer reading

IMPLEMENTED; NEEDS USER TEST. No item is ACCEPTED.

- Summary, Training and Compare now live under **Insights**, in At a glance, Training focus and Compared with earlier attempts sections.
- Errors now lives inside **Diagnosis**, alongside similar-map evidence.
- Overview charts, Practice, Top errors and Advanced remain accessible. Nine destinations become six.
- Full-width collapsible sections use one page scrollbar. Larger headings, softer secondary text and consistent spacing improve scanning. Readout text stays selectable.
- Repeated demand/mechanical paragraphs are removed; their values remain in Insights. Scoring, diagnosis rules, storage and export behavior are unchanged.

Validation: 133 tests pass; restore/build and Windows x64 publish succeed with the existing WFAC010 warning. Synthetic visual review plus narrow/wide, scrolling, collapse/expand, content-update and navigation tests pass. Real-data/DPI readability still needs user testing.

Launch publish-recent-play/OsuAimAnalyzer.exe. Select a play and try Insights/Diagnosis, collapse/expand headings, resize and switch plays. Check Practice and the detailed tools as usual.
