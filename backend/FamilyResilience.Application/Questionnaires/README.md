# Questionnaires module

**Owns:** `Instrument`, `InstrumentVersion` (sections, items, options), `RuleSet`; their repositories

**Status:** Domain, persistence, content import, drafts, approval, scheduling and retirement, rule-set proposal,
approval and retirement, and the Administrator-only endpoints are built, including the read-only lists the dashboard's
configuration view uses (an instrument's versions without content, a version's rule sets; D-74). Development seeds the
39-item household assessment (from the demo, with the brief's scoring as a proposed rule set) and the Abu Dhabi Family
Survey codebook (with Arabic gaps, plus the proposed module eligibility rule sets), both as unapproved drafts. Nothing
is approved.

**Other modules may use:** Approved, effective instrument versions and approved rule sets

**Must not:** Activate a version or rule set without a recorded approver, date and source; contain invented content;
change approved content (create a new version); invent Arabic translations
