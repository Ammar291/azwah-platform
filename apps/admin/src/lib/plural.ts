import { fill } from './format';
import type { Language } from './i18n';

/** The wordings of a counted noun, one per plural category of the language (CLDR: English uses two, Arabic all six). */
export type PluralForms = Record<Intl.LDMLPluralRule, string>;

const RULES: Record<Language, Intl.PluralRules> = { en: new Intl.PluralRules('en'), ar: new Intl.PluralRules('ar') };

/**
 * A count with its noun in the right form for the language ("1 item", "4 items"; Arabic has its own forms for one, two,
 * three to ten, and eleven and over). `text` is the count as it should be shown, already formatted.
 */
export function plural(language: Language, forms: PluralForms, count: number, text: string): string {
  return fill(forms[RULES[language].select(count)], { count: text });
}
