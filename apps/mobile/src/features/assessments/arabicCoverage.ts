import type { PublishedVersion } from '@/api/participant/schemas';
import type { BilingualText } from '@/i18n/format';

/** A text with no Arabic: `pickText` shows its English instead. Only draft content has any (D-40). */
export const lacksArabic = (text: BilingualText | null | undefined): boolean => text != null && !text.ar;

/**
 * Whether anything a questionnaire shows (its name, the titles of the sections asked, their questions and answer
 * options) has no Arabic yet, so an Arabic reader can be told why some of it appears in English. Display only: the
 * content itself is never changed or translated here.
 */
export function versionLacksArabic(version: PublishedVersion, sectionCode: string | null): boolean {
  if (lacksArabic(version.name)) {
    return true;
  }
  return version.sections
    .filter((section) => sectionCode === null || section.code === sectionCode)
    .some(
      (section) =>
        lacksArabic(section.title) ||
        section.items.some((item) => lacksArabic(item.prompt) || item.options.some((option) => lacksArabic(option.label))),
    );
}
