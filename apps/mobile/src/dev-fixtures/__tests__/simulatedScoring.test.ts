import { PLACEHOLDER_PACKAGE } from '@/test-utils/placeholderContent';

import {
  FAMILY_VOICES,
  HOUSEHOLD_ASSESSMENT,
  itemsOf,
  toFixtureVersion,
  type FixtureItemMeta,
  type FixtureVersion,
} from '../content';
import { simulateScoring } from '../simulatedScoring';

/** The test placeholder with a section-scoped definition: section A summed, one band over its whole range. */
const SECTION_A_SCORED = toFixtureVersion(PLACEHOLDER_PACKAGE, {
  schema: 'scoring.v1',
  target: { sectionCode: 'A' },
  policy: 'summed_item_values',
  algorithmVersion: 1,
  parameters: {
    reverseScoring: 'mirror_scale',
    unanswered: 'reject',
    total: true,
    tierBands: [{ code: 'section_a_band', min: 4, max: 16 }],
  },
});

/** Answers every item of the version with the option whose value `rawFor` chooses. */
function answerAll(fixture: FixtureVersion, rawFor: (meta: FixtureItemMeta) => number): Map<string, string> {
  const answers = new Map<string, string>();
  for (const item of itemsOf(fixture.version, null)) {
    const meta = fixture.items.get(item.id);
    if (!meta) throw new Error(`no metadata for ${item.code}`);
    const raw = rawFor(meta);
    const option = item.options.find((candidate) => meta.values.get(candidate.id) === raw);
    if (!option) throw new Error(`no option valued ${raw} on ${item.code}`);
    answers.set(item.id, option.id);
  }
  return answers;
}

const byDirection = (forwardRaw: number, reverseRaw: number) => (meta: FixtureItemMeta) =>
  meta.direction === 'reverse' ? reverseRaw : forwardRaw;

const q01Raised = (forwardRaw: number, reverseRaw: number, shift: number) => (meta: FixtureItemMeta) =>
  meta.code === 'Q01' ? forwardRaw + shift : byDirection(forwardRaw, reverseRaw)(meta);

describe('simulated scoring (fixtures only)', () => {
  it.each([
    [3, 3, 107, 'transitional'],
    [1, 4, 39, 'vulnerable'],
    [4, 1, 156, 'flourishing'],
    [1, 1, 69, 'vulnerable'],
    [4, 4, 126, 'flourishing'],
  ])('forward raw %i and reverse raw %i give %i (%s), as the platform does', (forwardRaw, reverseRaw, total, tier) => {
    const score = simulateScoring(HOUSEHOLD_ASSESSMENT, answerAll(HOUSEHOLD_ASSESSMENT, byDirection(forwardRaw, reverseRaw)));

    expect(score?.totalScore).toBe(total);
    expect(score?.tierCode).toBe(tier);
  });

  it.each([
    [2, 3, 0, 78, 'vulnerable'],
    [2, 3, 1, 79, 'transitional'],
    [3, 2, 0, 117, 'transitional'],
    [3, 2, 1, 118, 'flourishing'],
  ])('band edges belong to the band that names them (%i, %i, Q01 +%i → %i %s)', (forwardRaw, reverseRaw, shift, total, tier) => {
    const score = simulateScoring(HOUSEHOLD_ASSESSMENT, answerAll(HOUSEHOLD_ASSESSMENT, q01Raised(forwardRaw, reverseRaw, shift)));

    expect(score?.totalScore).toBe(total);
    expect(score?.tierCode).toBe(tier);
  });

  it('sums each of the seven areas, ordered by category code', () => {
    const score = simulateScoring(HOUSEHOLD_ASSESSMENT, answerAll(HOUSEHOLD_ASSESSMENT, () => 3));

    expect(score?.categoryScores).toEqual([
      { categoryCode: 'communication', score: 19, itemCount: 7 },
      { categoryCode: 'connectedness', score: 14, itemCount: 5 },
      { categoryCode: 'extended_family', score: 14, itemCount: 5 },
      { categoryCode: 'outlook', score: 14, itemCount: 5 },
      { categoryCode: 'seniors', score: 14, itemCount: 5 },
      { categoryCode: 'wealth_debt', score: 16, itemCount: 6 },
      { categoryCode: 'work_life', score: 16, itemCount: 6 },
    ]);
  });

  it('gives no result when a scored item is unanswered', () => {
    const answers = answerAll(HOUSEHOLD_ASSESSMENT, () => 3);
    answers.delete(itemsOf(HOUSEHOLD_ASSESSMENT.version, null)[38]?.id ?? '');

    expect(simulateScoring(HOUSEHOLD_ASSESSMENT, answers)).toBeNull();
  });

  it('gives no result for an answer that is not one of the item’s options', () => {
    const answers = answerAll(HOUSEHOLD_ASSESSMENT, () => 3);
    answers.set(itemsOf(HOUSEHOLD_ASSESSMENT.version, null)[0]?.id ?? '', 'not-an-option');

    expect(simulateScoring(HOUSEHOLD_ASSESSMENT, answers)).toBeNull();
  });

  it('scores only the target section of a section-scoped definition', () => {
    const sectionA = itemsOf(SECTION_A_SCORED.version, 'A');
    const sectionB = itemsOf(SECTION_A_SCORED.version, 'B');
    const answers = new Map([...sectionA, ...sectionB].map((item) => [item.id, item.options[1]?.id ?? '']));

    expect(simulateScoring(SECTION_A_SCORED, answers)).toEqual({
      totalScore: 8,
      tierCode: 'section_a_band',
      categoryScores: [],
      scale: { minimum: 4, maximum: 16, bands: [{ code: 'section_a_band', min: 4, max: 16 }] },
    });
  });

  it('gives the household assessment its 39 to 156 scale, with the proposed bands', () => {
    const items = itemsOf(HOUSEHOLD_ASSESSMENT.version, null);
    const answers = new Map(items.map((item) => [item.id, item.options[0]?.id ?? '']));

    expect(simulateScoring(HOUSEHOLD_ASSESSMENT, answers)?.scale).toEqual({
      minimum: 39,
      maximum: 156,
      bands: [
        { code: 'vulnerable', min: 39, max: 78 },
        { code: 'transitional', min: 79, max: 117 },
        { code: 'flourishing', min: 118, max: 156 },
      ],
    });
  });

  it('gives no result when an answer is the option outside the scale', () => {
    const sectionA = itemsOf(SECTION_A_SCORED.version, 'A');
    const answers = new Map(sectionA.map((item) => [item.id, item.options[1]?.id ?? '']));
    answers.set(sectionA[0]?.id ?? '', sectionA[0]?.options.find((option) => option.isNonSubstantive)?.id ?? '');

    expect(simulateScoring(SECTION_A_SCORED, answers)).toBeNull();
  });

  it('gives no result for a version without a scoring proposal', () => {
    const moduleA = itemsOf(FAMILY_VOICES.version, 'A');
    const answers = new Map(moduleA.map((item) => [item.id, item.options[0]?.id ?? '']));

    expect(simulateScoring(FAMILY_VOICES, answers)).toBeNull();
  });
});
