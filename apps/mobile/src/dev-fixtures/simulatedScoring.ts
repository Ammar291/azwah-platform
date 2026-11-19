import type { ScoreScale } from '@/api/participant/schemas';

import type { FixtureScore, FixtureVersion } from './content';

/*
 * DEVELOPMENT FIXTURES ONLY. Simulates what the server's `summed_item_values` strategy would record for a version's
 * proposed scoring definition, so the result screen can be exercised before any rule set is approved. It mirrors
 * scoring-specification.md section 4 for definitions without a non-substantive policy: the items in scope are the
 * target module's, or the whole version's; a forward item counts its raw value, a reverse item counts
 * (lowest + highest value) − raw, categories sum their items, and the tier is the band whose bounds include the
 * total. The real app computes nothing: the server records results from approved rule sets only, and the
 * development banner labels every fixture screen as simulated.
 */

export type SimulatedScore = FixtureScore;

/**
 * The simulated result for `answers` (item id → option id), or null when the version has no scoring proposal, a
 * scored item in scope is unanswered or has no approved direction, or an answer is not a substantive option of its
 * item. Null leaves the session "submitted", as the server leaves a session it cannot score.
 */
export function simulateScoring(fixture: FixtureVersion, answers: ReadonlyMap<string, string>): SimulatedScore | null {
  const definition = fixture.scoring;
  if (!definition) {
    return null;
  }

  let total = 0;
  const categories = new Map<string, { score: number; itemCount: number }>();
  for (const [itemId, meta] of fixture.items) {
    if (definition.target !== null && meta.sectionCode !== definition.target.sectionCode) {
      continue;
    }
    if (meta.direction === 'not_scored') {
      continue;
    }
    if (meta.direction === 'unspecified') {
      return null;
    }
    const optionId = answers.get(itemId);
    const raw = optionId === undefined ? undefined : meta.values.get(optionId);
    if (raw === undefined) {
      return null;
    }
    const scale = [...meta.values.values()];
    const calculated = meta.direction === 'reverse' ? Math.min(...scale) + Math.max(...scale) - raw : raw;
    total += calculated;
    if (meta.category !== null) {
      const category = categories.get(meta.category) ?? { score: 0, itemCount: 0 };
      categories.set(meta.category, { score: category.score + calculated, itemCount: category.itemCount + 1 });
    }
  }

  const { parameters } = definition;
  const tierCode = parameters.total
    ? (parameters.tierBands?.find((band) => total >= band.min && total <= band.max)?.code ?? null)
    : null;
  return {
    totalScore: parameters.total ? total : null,
    tierCode,
    categoryScores: [...categories]
      .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
      .map(([categoryCode, category]) => ({ categoryCode, ...category })),
    // A result carries its grading scale only when the rule set has bands (D-111), as the server sends it.
    scale: parameters.tierBands?.length ? simulateScale(fixture) : null,
  };
}

/**
 * The scale the server would send with a trend (D-132) or a result (D-111) for a version's proposed scoring
 * definition: the lowest and highest totals the scored items in scope can give, and the bands from lowest to highest.
 * Null without a proposal or a total. Development fixtures only, like simulateScoring.
 */
export function simulateScale(fixture: FixtureVersion): ScoreScale | null {
  const definition = fixture.scoring;
  if (!definition?.parameters.total) {
    return null;
  }
  let minimum = 0;
  let maximum = 0;
  for (const meta of fixture.items.values()) {
    if (definition.target !== null && meta.sectionCode !== definition.target.sectionCode) {
      continue;
    }
    if (meta.direction === 'not_scored') {
      continue;
    }
    const values = [...meta.values.values()];
    minimum += Math.min(...values);
    maximum += Math.max(...values);
  }
  const bands = [...(definition.parameters.tierBands ?? [])]
    .sort((a, b) => a.min - b.min)
    .map((band) => ({ code: band.code, min: band.min, max: band.max }));
  return { minimum, maximum, bands };
}
