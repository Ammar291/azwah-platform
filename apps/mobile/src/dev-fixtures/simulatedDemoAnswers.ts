import type { AnswerRequest, DemoLevel } from '@/api/participant/schemas';

import { itemsOf, type FixtureVersion } from './content';

/*
 * DEVELOPMENT FIXTURES ONLY. Simulates the server's demonstration answers (D-130, DemoAnswerPlan in
 * backend/FamilyResilience.Domain/Scoring) for a version's proposed scoring definition, so the demo buttons work in
 * fixtures mode. The band asked for is the lowest, the highest, or for "medium" the one holding the middle of the
 * possible totals; the total aimed at is the middle of that band (rounded down), or with no bands the lowest, middle or
 * highest total. The points above each item's lowest value are spread evenly, reverse items are answered on the
 * mirrored side, unscored items take their first substantive option, and 99 is never chosen. The real app decides
 * nothing: in API mode the server chooses the answers from its approved rules.
 */

/**
 * The answers for a session of `fixture` in `sectionCode` (null: the whole version), in item order; null when the
 * version has no scoring proposal for that scope or an item in it has no direction, as the server then answers 409.
 */
export function simulateDemoAnswers(
  fixture: FixtureVersion,
  sectionCode: string | null,
  level: DemoLevel,
): AnswerRequest[] | null {
  const definition = fixture.scoring;
  if (!definition || (definition.target?.sectionCode ?? null) !== sectionCode) {
    return null;
  }

  const items = itemsOf(fixture.version, sectionCode);
  const scored: { id: string; low: number; high: number; reverse: boolean }[] = [];
  for (const item of items) {
    const meta = fixture.items.get(item.id);
    if (!meta || meta.direction === 'unspecified') return null;
    if (meta.direction === 'not_scored') continue;
    const values = [...meta.values.values()];
    scored.push({ id: item.id, low: Math.min(...values), high: Math.max(...values), reverse: meta.direction === 'reverse' });
  }
  if (scored.length === 0) return null;

  const minimum = scored.reduce((sum, item) => sum + item.low, 0);
  const maximum = scored.reduce((sum, item) => sum + item.high, 0);
  const middle = minimum + Math.floor((maximum - minimum) / 2);
  const bands = [...(definition.parameters.tierBands ?? [])].sort((a, b) => a.min - b.min);
  const band =
    bands.length === 0
      ? null
      : level === 'low'
        ? bands[0]
        : level === 'high'
          ? bands[bands.length - 1]
          : bands.find((candidate) => candidate.min <= middle && middle <= candidate.max);
  const target = band
    ? band.min + Math.floor((band.max - band.min) / 2)
    : level === 'low'
      ? minimum
      : level === 'high'
        ? maximum
        : middle;

  const extra = target - minimum;
  const shares = scored.map((item, index) =>
    Math.min(
      Math.floor((extra * (index + 1)) / scored.length) - Math.floor((extra * index) / scored.length),
      item.high - item.low,
    ),
  );
  let left = extra - shares.reduce((sum, share) => sum + share, 0);
  scored.forEach((item, index) => {
    const added = Math.min(item.high - item.low - (shares[index] ?? 0), left);
    shares[index] = (shares[index] ?? 0) + added;
    left -= added;
  });

  const calculated = new Map(scored.map((item, index) => [item.id, item.low + (shares[index] ?? 0)]));
  const byId = new Map(scored.map((item) => [item.id, item]));
  return items.map((item) => {
    const meta = fixture.items.get(item.id);
    const scale = byId.get(item.id);
    const value = calculated.get(item.id);
    const raw = scale && value !== undefined ? (scale.reverse ? scale.low + scale.high - value : value) : undefined;
    const option =
      raw === undefined
        ? (item.options.find((candidate) => !candidate.isNonSubstantive) ?? item.options[0])
        : item.options.find((candidate) => meta?.values.get(candidate.id) === raw);
    return { itemId: item.id, optionId: option?.id ?? '' };
  });
}
