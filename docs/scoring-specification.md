# Scoring specification

**Status:** the engine is built and tested. **No scoring rule set is approved, so no session is scored in any
environment.** This file describes how scoring works once a research owner approves a specification. Section 8 lists
every scoring rule that stays disabled.

## 1. Principles

- **Deterministic.** A score is a pure function of three inputs: the submitted answers, the approved instrument
  version, and one approved scoring rule set. The same inputs always give the same result, and the arithmetic uses
  decimals.
- **Versioned.** Every result records:
  - the exact rule-set row (code and version);
  - the strategy's policy code;
  - the algorithm version.
- **Approved only.** A rule set runs only when Approved, with a recorded approver, date and source document. Proposed
  rule sets never run.
- **Separate instruments.** A family-voices rule set scores exactly one module. A household-assessment rule set scores
  the whole version. No combined score exists.
- **No AI or statistical models.** Clients display results and never compute them.

## 2. How a session is scored

1. A staff member calls `POST /api/v1/assessment-sessions/{id}/score`. Scoring does not run automatically yet (open
   question 32). `GET /api/v1/assessment-sessions/{id}/score-results` lists the results recorded for a session; the
   read is audited, because scores are sensitive personal information.
2. The session must be Submitted or already Scored. Otherwise the call returns 409 `scoring.session_not_submitted`.
3. The engine takes the approved scoring rule sets of the session's version whose target is the session's scope:
   - none → 409 `scoring.policy_not_approved`;
   - more than one → 409 `scoring.ambiguous_rules`.
4. If that rule set already scored the session, the existing result is returned (200). A new result returns 201.
   Returning an existing result reads scores and the answers in its contributions, so it is audited like the listing,
   as `scoring.score_results.read` on the `assessment_session`. If that audit event cannot be committed, the call
   returns the failure, not the result.
5. The definition's `policy` and `algorithmVersion` select a strategy. An unknown pair returns 409
   `scoring.policy_unsupported`.
6. The strategy validates its parameters against the items in scope, then computes from a snapshot. The calculation
   reads nothing from the database. If the stored definition no longer passes validation, the call returns 400 (see
   section 3).
7. The result, the session's move to Scored, an audit event and an outbox event commit in one transaction. Any failure
   saves nothing.

**Scope matching.** A rule set scores a session only when its target section equals the session's section. A
whole-version rule set scores whole-version sessions.
- A section-A session with approved rule sets for A and for B is scored with A. That is not ambiguous.
- A whole-version session with only a section rule set gets 409 `scoring.policy_not_approved`.

**Response ordering.** Contributions are returned in item-id order, and category scores in category-code order.

## 3. Definition schema `scoring.v1`

| Key | Required | Meaning |
| --- | --- | --- |
| `schema` | yes | `"scoring.v1"` |
| `target` | no | `{ "sectionCode": "A" }` for a family-voices module; absent or `null` for a household-assessment version |
| `policy` | yes | Strategy code (snake case) |
| `algorithmVersion` | yes | Whole number, 1 or more |
| `parameters` | yes | Object, validated by the strategy against the items in scope |

Unknown or duplicate keys and wrong types are refused with 400 `scoring.definition_invalid`, and the message names the
JSON path. A definition is validated when proposed. It is validated again when approved, against the version as it is
then (`ApproveRuleSet`): a draft edited after the proposal, for example by adding an item with an `Unspecified`
direction, is refused at approval.

At scoring time the definition errors (`scoring.definition_invalid`, `scoring.item_direction_unspecified`,
`scoring.item_scale_invalid`, `scoring.no_scored_items` and `scoring.tier_bands_invalid`) map to 400, and the endpoint
declares 400.

## 4. Policy `summed_item_values`, algorithm version 1

The items in scope are the target module's items (or the whole version's). Items marked `NotScored` are in scope but are
not scored.

| Parameter | Values | Meaning |
| --- | --- | --- |
| `reverseScoring` | `"mirror_scale"` only | An item the approved version marks `Reverse` counts (lowest + highest substantive value) − raw: 5 − raw on a 1–4 scale, 6 − raw on 1–5. A `Forward` item counts its raw value |
| `unanswered` | `"reject"` only | An unanswered scored item stops scoring (409 `scoring.unanswered_items`) |
| `total` | `true` or `false` | Whether a total is computed. `false` is category-only scoring: a Complete result may have no total, and a tier needs a total. `false` needs at least one scored item with a scoring category |
| `nonSubstantive` | Required exactly when a scored item offers a non-substantive option such as 99 | `maxShare` (0–1); `aggregation`: `"prorated_mean"` only; `rounding`: `mode` (`half_away_from_zero` or `half_to_even`) and `decimals` (0–3) |
| `tierBands` | Optional | `[{ "code", "min", "max" }]`: whole numbers, contiguous, covering exactly the totals the scope can produce. Needs `total: true` and, when `nonSubstantive` is present, `rounding.decimals` of 0 |

**The definition is refused when:**
- an item in scope has direction `Unspecified` (`scoring.item_direction_unspecified`);
- no item is scored (`scoring.no_scored_items`);
- `total` is `false` but no scored item has a scoring category, so the result would hold no total, no category and
  no tier (`scoring.definition_invalid` at `$.parameters.total`);
- a scored item's substantive values are not consecutive whole numbers (`scoring.item_scale_invalid`);
- the bands do not tile the range (`scoring.tier_bands_invalid`). The household assessment's 39–156 bands therefore
  can never apply to a module;
- `nonSubstantive` is present but no scored item offers a non-substantive option, or is missing when one does
  (`scoring.definition_invalid` at `$.parameters.nonSubstantive`);
- `nonSubstantive` is present and the scored items do not all share one substantive scale (the same minimum and
  maximum), also `scoring.definition_invalid` at `$.parameters.nonSubstantive`. Prorating across mixed scales can
  produce a total outside the scope's range.

**Calculation:**
- A non-substantive answer is recorded with its raw value (99) and no calculated value. It never enters a sum.
- When excluded answers ÷ scored items is greater than `maxShare`, the result is **Incomplete**
  (`non_substantive_share_exceeded`). It keeps its contributions but has no total, categories or tier.
- Otherwise the total depends on exclusions:
  - with nothing excluded, it is the plain sum;
  - with some answers excluded, it is the mean of the substantive answers × the number of scored items, rounded as
    approved.
- Category scores aggregate the same way over the items with that scoring category.
- When the total or any category has no substantive answer at all, the result is Incomplete (`no_substantive_answers`).
- A tier is given only when `tierBands` exist: the band containing the total, both bounds included.

## 5. Score results

`score_results` holds:
- the session;
- the version;
- the exact rule set;
- the policy code and algorithm version;
- the status (`Complete` or `Incomplete`) and the incomplete reason;
- the total;
- the tier;
- the computation time.

Each result has one contribution per scored item (raw value, calculated value or excluded, category), stored in
`score_contributions`, and its category scores in `score_category_scores`.

A result is unique per session and rule set and is never updated: a trigger refuses updates. Scoring the same session
with another approved rule set adds a result; nothing is overwritten.

## 6. The manual's proposed framework (not approved)

Source: the Abu Dhabi Family Survey manual (not included in this repository), "Scoring framework and social sector referral
plan". The document ends with "AI responses may include mistakes". A named research owner must confirm it first (open
question 30).

| Element | As written | How an approver would express it | Still missing |
| --- | --- | --- | --- |
| Code 99 | Filter out of the denominator; more than 30% of a module's items → "Incomplete / Private" | `nonSubstantive.maxShare: 0.3` | Whether "out of the denominator" means `prorated_mean`, and how to round (open question 28) |
| Module A bands | 10–22 High Risk, 23–37 Moderate Risk, 38–50 High Resilience | `tierBands` (they tile 10–50 for 10 items) | Approval (open question 3), tier codes |
| Module B bands | 13–28 High Burnout, 29–49 Moderate Burnout, 50–65 High Balance | `tierBands` (tile 13–65 for 13 items) | As above |
| Module C bands | 13–28 High Vulnerability, 29–49 Moderate Integration, 50–65 High Vitality | `tierBands` (tile 13–65) | As above |
| Item directions | Not stated | `Reverse` on the items concerned, in a new version | Which items are reverse-scored (open question 27) |

**Observation for the research owner** (not a designation): in some items a higher answer reads as worse. These are
Q05, Q07, Q10 and Q13 in Module A, and Q14, Q15, Q19, Q21, Q23, Q24 and Q25 in Module B. The direction of Q29
(grandparent help) in Module C is unclear.

## 7. The household assessment (4 options, 39 items)

The brief proposed 5 − raw reverse scoring, a total of 39–156 and bands of 39–78, 79–117 and 118–156. Since
2026-10-19 the demo's instrument is in the platform as draft version 1 of `household_assessment`
(`assessment-rules.md` section 3), and the brief's scoring is committed as
`backend/FamilyResilience.Infrastructure/Content/household-assessment.scoring.json`:

```json
{
  "schema": "scoring.v1",
  "target": null,
  "policy": "summed_item_values",
  "algorithmVersion": 1,
  "parameters": {
    "reverseScoring": "mirror_scale",
    "unanswered": "reject",
    "total": true,
    "tierBands": [
      { "code": "vulnerable", "min": 39, "max": 78 },
      { "code": "transitional", "min": 79, "max": 117 },
      { "code": "flourishing", "min": 118, "max": 156 }
    ]
  }
}
```

The development seed proposes it as the scoring rule set `household_assessment_total` v1 (status Proposed), validated
against the draft. `HouseholdAssessmentScoringTests` runs the platform's own strategy on it: every calculated value 1
gives 39 (`vulnerable`), every value 4 gives 156 (`flourishing`), every raw answer 3 gives 107 (`transitional`) with
the seven area scores 19, 14, 14, 14, 16, 14 and 16, and the band edges 78/79 and 117/118 fall in the band that names
them. **None of this runs:** the version is a draft and the rule set is Proposed (open questions 1, 2 and 4). The
codebook's 39 question IDs belong to the family-voices instrument. They are not this instrument.

## 8. Disabled scoring rules

| Rule | Why it is disabled | Needed to enable it |
| --- | --- | --- |
| Module A scoring (Q04–Q13) | No scoring rule set; every item's direction is `Unspecified`; the aggregation of non-substantive (99) answers is unspecified (open question 28) | Directions (27), excluded-answer aggregation and rounding (28), bands (3), source confirmation (30), Arabic (31); then a new version, a proposed rule set and an approval |
| Module B scoring (Q14–Q26) | Same | Same |
| Module C scoring (Q27–Q39) | Same | Same |
| Household assessment scoring | The content is a draft and its rule set `household_assessment_total` v1 is Proposed; neither has a named approver | Open questions 1, 2, 4; then approval of the version and of the rule set (section 9) |
| Tiers for any instrument | No approved bands | Open questions 2 (household bands), 3 (module bands) and 30 (source provenance); an approved scoring rule set with `tierBands` |
| Referral thresholds (manual section 3) | Out of scope; no partner integration exists | Open questions 13, 33 |

## 9. Enabling a scoring rule

1. The research owner decides and signs off:
   - item directions and categories;
   - the excluded-answer aggregation and rounding;
   - the bands.
2. Import a corrected content document as a new draft version (`POST /api/v1/instruments/{id}/versions`). Set the
   directions in the document, complete the Arabic there and in the instrument's name and description
   (`PUT /api/v1/instruments/{id}/text`), and approve the version (`POST /api/v1/instrument-versions/{id}/approve`).
   Copying a draft (`POST /api/v1/instrument-versions/{id}/drafts`) duplicates the content as it is; no endpoint edits
   an item.
3. Propose the scoring rule set (`POST /api/v1/instrument-versions/{id}/rule-sets`, kind `scoring`). It is validated
   against the version.
4. Approve it (`POST /api/v1/rule-sets/{id}/approve`), naming the source document. It is validated again.

To correct or withdraw an approved scoring rule set, retire it (`POST /api/v1/rule-sets/{id}/retire`), then propose and
approve a replacement with a new code or version number. A second approved scoring rule set for the same scope makes
scoring fail with `scoring.ambiguous_rules` until one is retired. Results already recorded keep the rule set they name.

**Consequence:** a session answered on a version whose directions are `Unspecified` can never be scored by the
platform, because no valid scoring rule set can exist for that version. Deciding directions before data collection
starts avoids that.
