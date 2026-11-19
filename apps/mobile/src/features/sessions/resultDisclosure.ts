/**
 * What participants may be shown of their result (decision M-46). The server decides scores, tiers and support
 * recommendations, and sends score values to the household's head only (D-103); whether the app shows them is a
 * separate approval. The owner approved the total score and the band on 2026-10-28 (D-103). Area scores and support
 * categories stay hidden: no wording for them is approved (open questions 3 and 13). Turn a flag on only with a
 * recorded approval in docs/decisions.md. When on, the app shows the API's value exactly as returned; it never computes
 * or infers one.
 */
export interface ResultDisclosure {
  /** The total score. */
  score: boolean;
  /** The tier (result band). */
  tier: boolean;
  /** The per-area (category) scores. */
  categoryScores: boolean;
  /** The support categories the result was routed to. */
  recommendations: boolean;
}

export const RESULT_DISCLOSURE: ResultDisclosure = { score: true, tier: true, categoryScores: false, recommendations: false };

export const discloseAnything = (disclosure: ResultDisclosure) =>
  disclosure.score || disclosure.tier || disclosure.categoryScores || disclosure.recommendations;
