import { FAMILY_VOICES, HOUSEHOLD_ASSESSMENT, itemsOf, toFixtureVersion } from '../content';
import { simulateDemoAnswers } from '../simulatedDemoAnswers';
import { simulateScoring } from '../simulatedScoring';
import { PLACEHOLDER_PACKAGE } from '@/test-utils/placeholderContent';

const asMap = (answers: { itemId: string; optionId: string }[]) =>
  new Map(answers.map((answer) => [answer.itemId, answer.optionId]));

describe('simulated demonstration answers (development fixtures only, D-130)', () => {
  it.each([
    ['low', 'vulnerable', 58],
    ['medium', 'transitional', 98],
    ['high', 'flourishing', 137],
  ] as const)('%s answers score %s at the middle of its band, as the server plans them', (level, tierCode, total) => {
    const answers = simulateDemoAnswers(HOUSEHOLD_ASSESSMENT, null, level);

    expect(answers).not.toBeNull();
    expect(answers?.map((answer) => answer.itemId)).toEqual(
      itemsOf(HOUSEHOLD_ASSESSMENT.version, null).map((item) => item.id),
    );
    expect(simulateScoring(HOUSEHOLD_ASSESSMENT, asMap(answers ?? []))).toMatchObject({ tierCode, totalScore: total });
  });

  it('answers every question with one of its own options', () => {
    const answers = simulateDemoAnswers(HOUSEHOLD_ASSESSMENT, null, 'medium') ?? [];

    for (const item of itemsOf(HOUSEHOLD_ASSESSMENT.version, null)) {
      const answer = answers.find((candidate) => candidate.itemId === item.id);
      expect(item.options.map((option) => option.id)).toContain(answer?.optionId);
    }
  });

  it('mirrors reverse questions and spreads the points without bands', () => {
    const pkg = {
      ...PLACEHOLDER_PACKAGE,
      version: {
        ...PLACEHOLDER_PACKAGE.version,
        sections: PLACEHOLDER_PACKAGE.version.sections.map((section) => ({
          ...section,
          items: section.items.map((item, index) => ({
            ...item,
            scoringDirection: section.code === 'A' && index === 1 ? 'reverse' : 'forward',
          })),
        })),
      },
    };
    const scoring = {
      schema: 'scoring.v1',
      target: { sectionCode: 'A' },
      policy: 'summed_item_values',
      algorithmVersion: 1,
      parameters: { reverseScoring: 'mirror_scale', unanswered: 'reject', total: true },
    };
    const fixture = toFixtureVersion(pkg, scoring);
    const valueOf = (answer: { itemId: string; optionId: string }) =>
      fixture.items.get(answer.itemId)?.values.get(answer.optionId);

    const high = simulateDemoAnswers(fixture, 'A', 'high') ?? [];

    expect(high.map(valueOf)).toEqual([4, 1, 4, 4]);
    expect(simulateScoring(fixture, asMap(high))?.totalScore).toBe(16);
    expect(simulateDemoAnswers(fixture, 'B', 'high')).toBeNull();
  });

  it('gives nothing for a version without a scoring proposal, as the server answers 409', () => {
    expect(simulateDemoAnswers(FAMILY_VOICES, 'A', 'high')).toBeNull();
  });
});
