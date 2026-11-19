import { FAMILY_VOICES, HOUSEHOLD_ASSESSMENT } from '@/dev-fixtures/content';
import { PLACEHOLDER_VERSION } from '@/test-utils/placeholderContent';

import { lacksArabic, versionLacksArabic } from '../arabicCoverage';

describe('Arabic coverage of a questionnaire', () => {
  it('knows a text without Arabic', () => {
    expect(lacksArabic({ en: 'Yes', ar: null })).toBe(true);
    expect(lacksArabic({ en: 'Yes', ar: '' })).toBe(true);
    expect(lacksArabic({ en: 'Yes', ar: 'نعم' })).toBe(false);
    expect(lacksArabic(null)).toBe(false);
  });

  it('finds nothing missing in content that has Arabic throughout', () => {
    expect(versionLacksArabic(PLACEHOLDER_VERSION, null)).toBe(false);
    expect(versionLacksArabic(HOUSEHOLD_ASSESSMENT.version, null)).toBe(false);
  });

  it('finds the gaps the family survey codebook leaves', () => {
    expect(versionLacksArabic(FAMILY_VOICES.version, 'A')).toBe(true);
  });

  it('looks only at the section asked, apart from the name', () => {
    const [first, second] = PLACEHOLDER_VERSION.sections;
    if (!first || !second) throw new Error('The placeholder changed.');
    const gapInSecond = {
      ...PLACEHOLDER_VERSION,
      sections: [
        first,
        {
          ...second,
          items: second.items.map((item, index) =>
            index === 0
              ? { ...item, options: item.options.map((option) => ({ ...option, label: { ...option.label, ar: null } })) }
              : item,
          ),
        },
      ],
    };

    expect(versionLacksArabic(gapInSecond, first.code)).toBe(false);
    expect(versionLacksArabic(gapInSecond, second.code)).toBe(true);
    expect(versionLacksArabic(gapInSecond, null)).toBe(true);
    expect(versionLacksArabic({ ...PLACEHOLDER_VERSION, name: { en: 'Name', ar: null } }, first.code)).toBe(true);
  });
});
