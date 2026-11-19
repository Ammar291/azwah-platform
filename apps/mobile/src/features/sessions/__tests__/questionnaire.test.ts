import { publishedVersionSchema } from '@/api/participant/schemas';
import { PLACEHOLDER_VERSION } from '@/test-utils/placeholderContent';

import { checkAnswers, isAnswerable, isValidAnswer, selectItems } from '../questionnaire';

const { items: SECTION_A } = selectItems(PLACEHOLDER_VERSION, 'A');
const { items: SECTION_B } = selectItems(PLACEHOLDER_VERSION, 'B');
const option = (itemIndex: number, optionIndex: number, items = SECTION_A) =>
  items[itemIndex]?.options[optionIndex]?.id ?? '';

describe('selectItems', () => {
  it("takes the session's section in display order, with options in display order", () => {
    const shuffled = {
      ...PLACEHOLDER_VERSION,
      sections: [...PLACEHOLDER_VERSION.sections].reverse().map((section) => ({
        ...section,
        items: [...section.items].reverse().map((item) => ({ ...item, options: [...item.options].reverse() })),
      })),
    };

    const { items, sectionTitle } = selectItems(shuffled, 'A');

    expect(items.map((item) => item.code)).toEqual(['a1', 'a2', 'a3', 'a4']);
    expect(items[0]?.options.map((o) => o.displayOrder)).toEqual([1, 2, 3, 4, 99]);
    expect(sectionTitle?.en).toBe('[DEV PLACEHOLDER] Section A');
  });

  it('takes every section for a whole-instrument session', () => {
    expect(selectItems(PLACEHOLDER_VERSION, null).items).toHaveLength(7);
  });
});

describe('item types', () => {
  const withType = (itemType: string) =>
    publishedVersionSchema.parse({
      ...PLACEHOLDER_VERSION,
      sections: [{ ...PLACEHOLDER_VERSION.sections[0], items: [{ ...PLACEHOLDER_VERSION.sections[0]?.items[0], itemType }] }],
    });

  it('reads a type this app does not know as unsupported instead of refusing the questionnaire', () => {
    const version = withType('free_text');

    expect(version.sections[0]?.items[0]).toMatchObject({ itemType: 'unsupported', options: [] });
    expect(isAnswerable(selectItems(version, 'A').items)).toBe(false);
  });

  it('answers a questionnaire made only of single-choice questions', () => {
    expect(isAnswerable(SECTION_A)).toBe(true);
    expect(isAnswerable([])).toBe(false);
  });
});

describe('checkAnswers', () => {
  it('lists required questions without an answer, in order', () => {
    const check = checkAnswers(SECTION_A, { [SECTION_A[1]?.id ?? '']: option(1, 0) });

    expect(check.missingRequired.map((item) => item.code)).toEqual(['a1', 'a3', 'a4']);
    expect(check).toMatchObject({ invalid: [], answered: 1, total: 4 });
  });

  it('does not ask for optional questions', () => {
    const answers = { [SECTION_B[0]?.id ?? '']: option(0, 0, SECTION_B), [SECTION_B[1]?.id ?? '']: option(1, 1, SECTION_B) };

    expect(checkAnswers(SECTION_B, answers)).toMatchObject({ missingRequired: [], invalid: [], answered: 2, total: 3 });
  });

  it("refuses an option of another question, and a question that isn't in the questionnaire", () => {
    const answers = {
      [SECTION_A[0]?.id ?? '']: option(1, 0),
      [SECTION_B[0]?.id ?? '']: option(0, 0, SECTION_B),
    };

    const check = checkAnswers(SECTION_A, answers);

    expect(check.invalid.sort()).toEqual([SECTION_A[0]?.id, SECTION_B[0]?.id].sort());
    expect(check.answered).toBe(0);
  });

  it('accepts the option outside the scale as an answer', () => {
    const outside = SECTION_A[0]?.options.find((o) => o.isNonSubstantive)?.id ?? '';

    expect(isValidAnswer(SECTION_A, SECTION_A[0]?.id ?? '', outside)).toBe(true);
    expect(isValidAnswer(SECTION_A, SECTION_A[0]?.id ?? '', 'not-an-option')).toBe(false);
    expect(isValidAnswer(SECTION_A, 'not-an-item', outside)).toBe(false);
  });
});
