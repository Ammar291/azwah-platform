import { matchesSearch, normalizeSearchText } from '../emergencySearch';

const police = { texts: ['Police', 'الشرطة', 'Crime, accidents, danger.', 'Across the UAE'], dial: '999' };
const dubai = {
  texts: ['Dubai Foundation for Women and Children', 'مؤسسة دبي لرعاية النساء والأطفال', 'Dubai', 'دبي'],
  dial: '800111',
};

describe('emergency search', () => {
  it('matches everything while the query is empty or blank', () => {
    expect(matchesSearch(police, '')).toBe(true);
    expect(matchesSearch(police, '   ')).toBe(true);
  });

  it('matches words in either language, ignoring case and surrounding spaces', () => {
    expect(matchesSearch(police, '  POLICE ')).toBe(true);
    expect(matchesSearch(police, 'الشرطة')).toBe(true);
    expect(matchesSearch(dubai, 'women')).toBe(true);
    expect(matchesSearch(police, 'women')).toBe(false);
  });

  it('needs every word of the query, in any order and any field', () => {
    expect(matchesSearch(dubai, 'children dubai')).toBe(true);
    expect(matchesSearch(dubai, 'children sharjah')).toBe(false);
  });

  it('finds Arabic without diacritics, tatweel or a particular alef or taa marbuta', () => {
    expect(matchesSearch(police, 'شرطه')).toBe(true);
    expect(matchesSearch(police, 'الشُّرطة')).toBe(true);
    expect(normalizeSearchText('إمارة أبوظبـي')).toBe('اماره ابوظبي');
  });

  it('finds a number typed with spaces or Arabic-Indic digits', () => {
    expect(matchesSearch(dubai, '800 111')).toBe(true);
    expect(matchesSearch(dubai, '800-111')).toBe(true);
    expect(matchesSearch(police, '٩٩٩')).toBe(true);
    expect(matchesSearch(police, '998')).toBe(false);
    expect(matchesSearch(dubai, '111')).toBe(true);
  });
});
