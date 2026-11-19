/** What one row of the emergency list can be found by: its words in both languages and the digits it dials. */
export interface SearchableNumber {
  texts: readonly string[];
  dial: string;
}

const ARABIC_MARKS = /[ً-ٰٟـ]/g;
const ARABIC_INDIC_ZERO = 0x0660;

/**
 * Lower case, Western digits, and Arabic without diacritics or tatweel, with every alef, alef maqsura and taa marbuta
 * folded, so "الشُّرطة", "الشرطه" and "الشرطة" all match.
 */
export function normalizeSearchText(text: string): string {
  return text
    .toLowerCase()
    .replace(/[٠-٩]/g, (digit) => String(digit.charCodeAt(0) - ARABIC_INDIC_ZERO))
    .replace(ARABIC_MARKS, '')
    .replace(/[إأآٱ]/g, 'ا')
    .replace(/ى/g, 'ي')
    .replace(/ة/g, 'ه')
    .replace(/\s+/g, ' ')
    .trim();
}

/**
 * Whether a row matches what the person typed. A query of digits (spaces and dashes allowed) matches the number it
 * dials; otherwise every word must appear in one of its texts, in either language. An empty query matches every row.
 */
export function matchesSearch(row: SearchableNumber, query: string): boolean {
  const normalized = normalizeSearchText(query);
  if (normalized === '') {
    return true;
  }
  const digits = normalized.replace(/[\s-]/g, '');
  if (/^\d+$/.test(digits)) {
    return row.dial.includes(digits);
  }
  const texts = row.texts.map(normalizeSearchText);
  return normalized.split(' ').every((word) => texts.some((text) => text.includes(word)));
}
