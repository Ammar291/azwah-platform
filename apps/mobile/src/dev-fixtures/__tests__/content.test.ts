import { publishedVersionSchema } from '@/api/participant/schemas';
import { PLACEHOLDER_CATALOG, PLACEHOLDER_FIXTURE } from '@/test-utils/placeholderContent';

import { FAMILY_VOICES, HOUSEHOLD_ASSESSMENT, fixtureVersion, itemsOf, PLATFORM_CATALOG } from '../content';
import familyVoices from '../content/abu-dhabi-family-survey.v1.json';
import householdScoring from '../content/household-assessment.scoring.json';
import householdAssessment from '../content/household-assessment.v1.json';

// Node's fs and path, typed locally: the app's TypeScript setup deliberately has no Node types.
// eslint-disable-next-line @typescript-eslint/no-require-imports -- Node built-in in a test; no Node types (above)
const { readFileSync } = require('fs') as { readFileSync(path: string, encoding: 'utf8'): string };
// eslint-disable-next-line @typescript-eslint/no-require-imports -- as above
const { join } = require('path') as { join(...parts: string[]): string };
declare const __dirname: string;

const PLATFORM_CONTENT = join(__dirname, '..', '..', '..', '..', '..', 'backend', 'FamilyResilience.Infrastructure', 'Content');
const platformFile = (name: string): unknown => JSON.parse(readFileSync(join(PLATFORM_CONTENT, name), 'utf8'));

const codes = (count: number) => Array.from({ length: count }, (_, index) => `Q${String(index + 1).padStart(2, '0')}`);

describe('fixture content', () => {
  it.each([
    ['household-assessment.v1.json', householdAssessment],
    ['household-assessment.scoring.json', householdScoring],
    ['abu-dhabi-family-survey.v1.json', familyVoices],
  ])('copies the platform content file %s unchanged', (name, copy) => {
    expect(copy).toEqual(platformFile(name));
  });

  it('maps the household assessment to a draft published version with scoring metadata', () => {
    const { version, items, scoring } = HOUSEHOLD_ASSESSMENT;

    publishedVersionSchema.parse(version);
    expect(version.instrumentCode).toBe('household_assessment');
    expect(version.kind).toBe('household_assessment');
    expect(version.status).toBe('draft');
    expect(version.sections).toHaveLength(7);

    const all = itemsOf(version, null);
    expect(all.map((item) => item.code)).toEqual(codes(39));
    expect(all.every((item) => item.options.length === 4 && item.isRequired)).toBe(true);
    expect(items.size).toBe(39);
    expect([...items.values()].filter((meta) => meta.direction === 'reverse').map((meta) => meta.code)).toEqual([
      'Q04', 'Q07', 'Q11', 'Q16', 'Q21', 'Q25', 'Q26', 'Q32', 'Q36', 'Q38',
    ]);
    const first = all[0];
    expect(items.get(first?.id ?? '')?.category).toBe('communication');
    expect([...(items.get(first?.id ?? '')?.values.values() ?? [])]).toEqual([1, 2, 3, 4]);
    expect(scoring?.parameters.tierBands?.map((band) => band.code)).toEqual(['vulnerable', 'transitional', 'flourishing']);
  });

  it('maps Module A of the codebook with its missing Arabic as null and no scoring', () => {
    const { version, scoring } = FAMILY_VOICES;

    publishedVersionSchema.parse(version);
    expect(version.kind).toBe('family_voices');
    expect(version.status).toBe('draft');
    const moduleA = itemsOf(version, 'A');
    expect(moduleA).toHaveLength(10);
    expect(moduleA.flatMap((item) => item.options).some((option) => option.label.ar === null)).toBe(true);
    expect(scoring).toBeNull();
  });

  it('finds a version by instrument code and number in the catalog asked for', () => {
    expect(fixtureVersion('household_assessment', 1)).toBe(HOUSEHOLD_ASSESSMENT);
    expect(fixtureVersion('abu_dhabi_family_survey', 1)).toBe(FAMILY_VOICES);
    expect(fixtureVersion('household_assessment', 2)).toBeNull();
    expect(fixtureVersion('dev_placeholder', 1)).toBeNull();
    expect(fixtureVersion('dev_placeholder', 1, PLACEHOLDER_CATALOG)).toBe(PLACEHOLDER_FIXTURE);
    expect(fixtureVersion('household_assessment', 1, PLACEHOLDER_CATALOG)).toBeNull();
  });

  it("serves the platform's content by default: the test placeholder never reaches the app", () => {
    expect(PLATFORM_CATALOG.versions.map((fixture) => fixture.version.instrumentCode)).toEqual([
      'household_assessment',
      'abu_dhabi_family_survey',
    ]);
    expect(PLATFORM_CATALOG.lead.first).toEqual({ fixture: HOUSEHOLD_ASSESSMENT, sectionCode: null });
    expect(PLATFORM_CATALOG.lead.second).toEqual({ fixture: FAMILY_VOICES, sectionCode: 'A' });
  });
});
