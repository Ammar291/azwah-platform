import { toFixtureVersion, type FixtureCatalog, type FixtureVersion } from '@/dev-fixtures/content';

/*
 * TEST-ONLY PLACEHOLDER CONTENT. Not a questionnaire, not approved, not from any source document: every string says
 * so in both languages. It is small on purpose (section A: four required questions; section B: three, the last one
 * optional; four options and one outside the scale), so screen tests stay short and deterministic. It has no scoring
 * proposal, so a submitted session stays pending exactly as the real API leaves it while no rule set is approved;
 * the household lead's earlier session carries a canned recorded result (total 8, band `dev_placeholder_tier`) so
 * the result screens have something to show. The running app never serves any of it: the fixture API's default
 * catalog is the platform's draft content (src/dev-fixtures/content.ts). Never copy this into the platform.
 */

export const PLACEHOLDER_INSTRUMENT_CODE = 'dev_placeholder';

const EN = '[DEV PLACEHOLDER]';
const AR = '[عنصر نائب للتطوير]';

const option = (value: number) => ({
  code: `opt_${value}`,
  displayOrder: value,
  value,
  isNonSubstantive: false,
  label: { en: `[DEV] Option ${value}`, ar: `[تجريبي] الخيار ${value}` },
});

const outsideScale = () => ({
  code: 'opt_outside',
  displayOrder: 99,
  value: 99,
  isNonSubstantive: true,
  label: { en: '[DEV] Option outside the scale', ar: '[تجريبي] خيار خارج المقياس' },
});

const item = (section: string, index: number, isRequired: boolean) => ({
  code: `${section.toLowerCase()}${index}`,
  displayOrder: index,
  itemType: 'single_choice' as const,
  isRequired,
  scoringCategory: null,
  scoringDirection: 'forward' as const,
  prompt: {
    en: `${EN} Statement ${section}${index}. Placeholder text, not approved questionnaire content.`,
    ar: `${AR} العبارة ${section}${index}. نص مؤقت وليس محتوى استبيان معتمدًا.`,
  },
  options: [option(1), option(2), option(3), option(4), outsideScale()],
});

/** The placeholder as a content package, for tests that build a fixture version with a scoring definition of their own. */
export const PLACEHOLDER_PACKAGE = {
  instrument: {
    code: PLACEHOLDER_INSTRUMENT_CODE,
    kind: 'family_voices' as const,
    name: { en: `${EN} Sample questionnaire`, ar: `${AR} استبيان تجريبي` },
    description: { en: `${EN} Synthetic content for building the app.`, ar: `${AR} محتوى مصطنع لبناء التطبيق.` },
  },
  version: {
    contentReferenceAr: 'dev-placeholder',
    contentReferenceEn: 'dev-placeholder',
    sections: [
      {
        code: 'A',
        displayOrder: 1,
        title: { en: `${EN} Section A`, ar: `${AR} القسم أ` },
        items: [item('A', 1, true), item('A', 2, true), item('A', 3, true), item('A', 4, true)],
      },
      {
        code: 'B',
        displayOrder: 2,
        title: { en: `${EN} Section B`, ar: `${AR} القسم ب` },
        items: [item('B', 1, true), item('B', 2, true), item('B', 3, false)],
      },
    ],
  },
};

export const PLACEHOLDER_FIXTURE: FixtureVersion = toFixtureVersion(PLACEHOLDER_PACKAGE, null);

/** The shape the API returns for a published version; it says "draft" because nothing here is approved. */
export const PLACEHOLDER_VERSION = PLACEHOLDER_FIXTURE.version;

/**
 * Section A in progress (an earlier wave finished, with a canned result), section B not started: the worlds every
 * screen test is written against.
 */
export const PLACEHOLDER_CATALOG: FixtureCatalog = {
  versions: [PLACEHOLDER_FIXTURE],
  lead: {
    first: {
      fixture: PLACEHOLDER_FIXTURE,
      sectionCode: 'A',
      earlierResult: { totalScore: 8, tierCode: 'dev_placeholder_tier', categoryScores: [] },
    },
    second: { fixture: PLACEHOLDER_FIXTURE, sectionCode: 'B' },
  },
};
