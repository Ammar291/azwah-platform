import { z } from 'zod';

import type { PublishedItem, PublishedVersion } from '@/api/participant/schemas';

import familyVoicesPackage from './content/abu-dhabi-family-survey.v1.json';
import householdScoringDefinition from './content/household-assessment.scoring.json';
import householdAssessmentPackage from './content/household-assessment.v1.json';

/*
 * DEVELOPMENT FIXTURES ONLY. The platform's own draft content, served the way the API serves a published version.
 * The files under ./content are byte-for-byte copies of backend/FamilyResilience.Infrastructure/Content
 * (content.test.ts fails when they drift). Every version here says "draft", because nothing is approved (open
 * questions 1, 2 and 31): nothing that checks the status can mistake it for approved content. The option values and
 * scoring directions the API never sends to clients stay in a side table for the fixture's simulated scoring.
 */

const text = z.object({ ar: z.string().nullable(), en: z.string() });

/** The shape of a committed content file (InstrumentContentPackage on the server). */
export const contentPackageSchema = z.object({
  instrument: z.object({
    code: z.string(),
    kind: z.enum(['household_assessment', 'family_voices']),
    name: text,
    description: text,
  }),
  version: z.object({
    contentReferenceAr: z.string(),
    contentReferenceEn: z.string(),
    sections: z.array(
      z.object({
        code: z.string(),
        displayOrder: z.number().int(),
        title: text,
        items: z.array(
          z.object({
            code: z.string(),
            displayOrder: z.number().int(),
            itemType: z.enum(['single_choice']),
            isRequired: z.boolean(),
            scoringCategory: z.string().nullable(),
            scoringDirection: z.enum(['forward', 'reverse', 'not_scored', 'unspecified']),
            prompt: text,
            options: z.array(
              z.object({
                code: z.string(),
                displayOrder: z.number().int(),
                value: z.number().int(),
                isNonSubstantive: z.boolean(),
                label: text,
              }),
            ),
          }),
        ),
      }),
    ),
  }),
});
export type ContentPackage = z.infer<typeof contentPackageSchema>;

/** A `scoring.v1` definition as the server validates it (scoring-specification.md section 3). */
export const scoringDefinitionSchema = z.object({
  schema: z.literal('scoring.v1'),
  target: z.object({ sectionCode: z.string() }).nullable(),
  policy: z.literal('summed_item_values'),
  algorithmVersion: z.number().int(),
  parameters: z.object({
    reverseScoring: z.literal('mirror_scale'),
    unanswered: z.literal('reject'),
    total: z.boolean(),
    tierBands: z.array(z.object({ code: z.string(), min: z.number().int(), max: z.number().int() })).optional(),
  }),
});
export type ScoringDefinition = z.infer<typeof scoringDefinitionSchema>;

/** What the server knows about an item that a published version does not say: its direction, category and values. */
export interface FixtureItemMeta {
  code: string;
  sectionCode: string;
  direction: 'forward' | 'reverse' | 'not_scored' | 'unspecified';
  category: string | null;
  /** Option id → the option's value, for substantive options only; a non-substantive option (99) has no value here. */
  values: ReadonlyMap<string, number>;
}

export interface FixtureVersion {
  version: PublishedVersion;
  /** Item id → scoring metadata. */
  items: ReadonlyMap<string, FixtureItemMeta>;
  /** The proposed scoring definition for the whole version, or null when none exists. */
  scoring: ScoringDefinition | null;
}

let sequence = 0x100;
const id = () => `0199e0a1-0000-7000-8000-${(sequence++).toString(16).padStart(12, '0')}`;

/** A content package (and its scoring proposal, or null) as the fixture API serves it: a draft, never approved. */
export function toFixtureVersion(pkg: unknown, scoring: unknown): FixtureVersion {
  const { instrument, version } = contentPackageSchema.parse(pkg);
  const items = new Map<string, FixtureItemMeta>();
  const published: PublishedVersion = {
    instrumentCode: instrument.code,
    kind: instrument.kind,
    name: instrument.name,
    description: instrument.description,
    versionId: id(),
    versionNumber: 1,
    status: 'draft',
    effectiveFrom: '2026-01-01T00:00:00Z',
    effectiveTo: null,
    sections: version.sections.map((section) => ({
      id: id(),
      code: section.code,
      displayOrder: section.displayOrder,
      title: section.title,
      items: section.items.map((item) => {
        const itemId = id();
        const values = new Map<string, number>();
        const options = item.options.map((option) => {
          const optionId = id();
          if (!option.isNonSubstantive) {
            values.set(optionId, option.value);
          }
          return {
            id: optionId,
            code: option.code,
            displayOrder: option.displayOrder,
            isNonSubstantive: option.isNonSubstantive,
            label: option.label,
          };
        });
        items.set(itemId, {
          code: item.code,
          sectionCode: section.code,
          direction: item.scoringDirection,
          category: item.scoringCategory,
          values,
        });
        return {
          id: itemId,
          code: item.code,
          displayOrder: item.displayOrder,
          itemType: item.itemType,
          isRequired: item.isRequired,
          prompt: item.prompt,
          options,
        };
      }),
    })),
  };
  return { version: published, items, scoring: scoring === null ? null : scoringDefinitionSchema.parse(scoring) };
}

/** The 39-item household assessment: draft version 1, with the brief's scoring proposed (D-99). */
export const HOUSEHOLD_ASSESSMENT: FixtureVersion = toFixtureVersion(householdAssessmentPackage, householdScoringDefinition);

/** The Abu Dhabi Family Survey codebook: draft version 1, Arabic gaps and all, with no scoring proposal (D-41). */
export const FAMILY_VOICES: FixtureVersion = toFixtureVersion(familyVoicesPackage, null);

/** What the server records for a scored session, as far as the fixture reproduces it. */
export interface FixtureScore {
  totalScore: number | null;
  tierCode: string | null;
  categoryScores: { categoryCode: string; score: number; itemCount: number }[];
  /** The grading scale of the rule set, when it has bands. */
  scale?: { minimum: number; maximum: number; bands: { code: string; min: number; max: number }[] } | null;
}

export interface FixtureAssignment {
  fixture: FixtureVersion;
  /** A module for a family-voices version; null for a whole household-assessment version. */
  sectionCode: string | null;
  /**
   * A result to record for the household lead's earlier session when the version has no scoring proposal, as if a
   * rule set had once been approved for it. Test catalogs only; the platform catalog simulates from its proposal.
   */
  earlierResult?: FixtureScore;
}

/** What a fixture API serves and assigns. The app uses the platform catalog; screen tests pass the small placeholder. */
export interface FixtureCatalog {
  versions: readonly FixtureVersion[];
  /**
   * The household lead's two questionnaires: `first` has an earlier wave finished (scored when the version has a
   * proposal) and the current wave in progress; `second` is not started. Confirming a roster scripts the same two.
   */
  lead: { first: FixtureAssignment; second: FixtureAssignment };
}

/** The platform's draft content: the household assessment for every adult, and the codebook's Module A. */
export const PLATFORM_CATALOG: FixtureCatalog = {
  versions: [HOUSEHOLD_ASSESSMENT, FAMILY_VOICES],
  lead: {
    first: { fixture: HOUSEHOLD_ASSESSMENT, sectionCode: null },
    second: { fixture: FAMILY_VOICES, sectionCode: 'A' },
  },
};

export function fixtureVersion(
  instrumentCode: string,
  versionNumber: number,
  catalog: FixtureCatalog = PLATFORM_CATALOG,
): FixtureVersion | null {
  return (
    catalog.versions.find(
      (candidate) =>
        candidate.version.instrumentCode === instrumentCode && candidate.version.versionNumber === versionNumber,
    ) ?? null
  );
}

const byOrder = <T extends { displayOrder: number }>(a: T, b: T) => a.displayOrder - b.displayOrder;

/** The items of one section, or of every section when `sectionCode` is null, in display order with sorted options. */
export function itemsOf(version: PublishedVersion, sectionCode: string | null): PublishedItem[] {
  return [...version.sections]
    .sort(byOrder)
    .filter((section) => sectionCode === null || section.code === sectionCode)
    .flatMap((section) =>
      [...section.items].sort(byOrder).map((item) => ({ ...item, options: [...item.options].sort(byOrder) })),
    );
}
