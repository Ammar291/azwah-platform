import { screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import type { Instrument, InstrumentVersion, InstrumentVersionSummary, RuleSet } from '@/lib/api/schemas';
import { problem, stubBff } from '@/test/bff';
import { paged } from '@/test/fixtures';
import { renderWithProviders } from '@/test/render';

import { InstrumentView } from './instrument-view';
import { InstrumentsView } from './instruments-view';
import { RuleSetView } from './rule-set-view';
import { VersionView } from './version-view';

vi.mock('next/navigation', async () => (await import('@/test/navigation')).nextNavigation);

const INSTRUMENT_ID = '0190f0e2-3333-7000-8000-000000000001';
const VERSION_ID = '0190f0e2-4444-7000-8000-000000000001';
const DRAFT_ID = '0190f0e2-4444-7000-8000-000000000002';
const RULE_SET_ID = '0190f0e2-5555-7000-8000-000000000001';

const instrument: Instrument = {
  id: INSTRUMENT_ID,
  code: 'family_voices',
  kind: 'family_voices',
  name: { ar: null, en: 'Family voices' },
  description: { ar: null, en: 'Modules A to C' },
  status: 'active',
  hasCompleteText: false,
};

const approvedSummary: InstrumentVersionSummary = {
  id: VERSION_ID,
  instrumentId: INSTRUMENT_ID,
  versionNumber: 1,
  status: 'approved',
  approvedBy: 'research-owner',
  approvedAt: '2026-09-01T09:00:00+00:00',
  approvalSource: 'Approval minutes 2026-09',
  effectiveFrom: '2026-09-02T00:00:00+00:00',
  effectiveTo: null,
  retiredAt: null,
  sectionCount: 3,
  itemCount: 36,
};

const draftSummary: InstrumentVersionSummary = {
  ...approvedSummary,
  id: DRAFT_ID,
  versionNumber: 2,
  status: 'draft',
  approvedBy: null,
  approvedAt: null,
  approvalSource: null,
  effectiveFrom: null,
};

const version: InstrumentVersion = {
  id: VERSION_ID,
  instrumentId: INSTRUMENT_ID,
  versionNumber: 1,
  status: 'approved',
  contentReferenceAr: 'ref-ar',
  contentReferenceEn: 'ref-en',
  approvedBy: 'research-owner',
  approvedAt: '2026-09-01T09:00:00+00:00',
  approvalSource: 'Approval minutes 2026-09',
  effectiveFrom: '2026-09-02T00:00:00+00:00',
  effectiveTo: null,
  retiredAt: null,
  hasCompleteText: true,
  sections: [
    {
      id: '0190f0e2-6666-7000-8000-000000000001',
      code: 'module_a',
      displayOrder: 1,
      title: { ar: 'الوحدة أ', en: 'Module A' },
      items: [
        {
          id: '0190f0e2-7777-7000-8000-000000000001',
          code: 'Q04',
          displayOrder: 1,
          itemType: 'single_choice',
          isRequired: true,
          scoringCategory: null,
          scoringDirection: 'unspecified',
          prompt: { ar: 'سؤال', en: 'A question' },
          options: [
            { id: '0190f0e2-8888-7000-8000-000000000001', code: 'o1', displayOrder: 1, value: 1, isNonSubstantive: false, label: { ar: 'نعم', en: 'Yes' } },
            { id: '0190f0e2-8888-7000-8000-000000000002', code: 'o99', displayOrder: 2, value: 99, isNonSubstantive: true, label: { ar: 'أفضّل عدم الإجابة', en: 'Prefer not to answer' } },
          ],
        },
      ],
    },
  ],
};

const ruleSet: RuleSet = {
  id: RULE_SET_ID,
  kind: 'eligibility',
  code: 'module_a_eligibility',
  versionNumber: 1,
  instrumentVersionId: VERSION_ID,
  status: 'proposed',
  definition: { schema: 'eligibility.v1', target: { section: 'module_a' } },
  approvedBy: null,
  approvedAt: null,
  approvalSource: null,
  retiredAt: null,
};

function expectReadOnly() {
  expect(screen.getByText(/Read-only\. Content and rules change only through the platform approval steps/)).toBeInTheDocument();
  expect(screen.queryByRole('textbox')).toBeNull();
  expect(screen.queryByRole('button', { name: /edit|approve|retire|save|delete/i })).toBeNull();
}

describe('InstrumentsView', () => {
  it('lists questionnaires with links, read-only', async () => {
    stubBff({ instruments: paged([instrument]) });

    renderWithProviders(<InstrumentsView />);

    const table = await screen.findByRole('table', { name: 'Questionnaires' });
    expect(within(table).getByRole('link', { name: 'family_voices' })).toHaveAttribute('href', `/configuration/instruments/${INSTRUMENT_ID}`);
    expect(within(table).getByText('Arabic missing')).toBeInTheDocument();
    expectReadOnly();
  });

  it('shows the missing Arabic name plainly in Arabic', async () => {
    stubBff({ instruments: paged([instrument]) });

    renderWithProviders(<InstrumentsView />, { language: 'ar' });

    const table = await screen.findByRole('table', { name: 'الاستبيانات' });
    expect(within(table).getByText('Family voices')).toBeInTheDocument();
    expect(within(table).getByText('لا يوجد نص عربي بعد')).toBeInTheDocument();
  });

  it('shows an empty state when nothing is imported', async () => {
    stubBff({ instruments: paged([]) });

    renderWithProviders(<InstrumentsView />);

    expect(await screen.findByRole('heading', { name: 'No questionnaires yet' })).toBeInTheDocument();
  });
});

describe('InstrumentView', () => {
  it('lists versions newest first with their status and approval', async () => {
    stubBff({ [`instruments/${INSTRUMENT_ID}`]: instrument, [`instruments/${INSTRUMENT_ID}/versions`]: paged([draftSummary, approvedSummary]) });

    renderWithProviders(<InstrumentView instrumentId={INSTRUMENT_ID} />);

    const table = await screen.findByRole('table', { name: 'Versions of this questionnaire' });
    const [draft, approved] = within(table).getAllByRole('row').slice(1);
    expect(within(draft).getByText('Draft')).toBeInTheDocument();
    expect(within(draft).getByText('Not approved')).toBeInTheDocument();
    expect(within(approved).getByText('Approved')).toBeInTheDocument();
    expect(within(approved).getByText('1 Sept 2026 by research-owner')).toBeInTheDocument();
    expect(within(approved).getByText('3 sections, 36 items')).toBeInTheDocument();
    expect(within(approved).getByRole('link', { name: 'Version 1' })).toHaveAttribute('href', `/configuration/versions/${VERSION_ID}`);
    expectReadOnly();
  });

  it('counts one section in the singular', async () => {
    stubBff({
      [`instruments/${INSTRUMENT_ID}`]: instrument,
      [`instruments/${INSTRUMENT_ID}/versions`]: paged([{ ...approvedSummary, sectionCount: 1, itemCount: 4 }]),
    });

    renderWithProviders(<InstrumentView instrumentId={INSTRUMENT_ID} />);

    expect(await screen.findByText('1 section, 4 items')).toBeInTheDocument();
  });

  it('counts sections and items in grammatical Arabic', async () => {
    stubBff({
      [`instruments/${INSTRUMENT_ID}`]: instrument,
      [`instruments/${INSTRUMENT_ID}/versions`]: paged([
        { ...approvedSummary, sectionCount: 7, itemCount: 39 },
        { ...draftSummary, sectionCount: 1, itemCount: 2 },
      ]),
    });

    renderWithProviders(<InstrumentView instrumentId={INSTRUMENT_ID} />, { language: 'ar' });

    expect(await screen.findByText('7 أقسام، و39 بندًا')).toBeInTheDocument();
    expect(screen.getByText('قسم واحد، وبندان')).toBeInTheDocument();
  });

  it('shows not found for an unknown questionnaire', async () => {
    stubBff({ [`instruments/${INSTRUMENT_ID}`]: problem(404, 'questionnaires.instrument_not_found') });

    renderWithProviders(<InstrumentView instrumentId={INSTRUMENT_ID} />);

    expect(await screen.findByRole('heading', { name: 'Not found' })).toBeInTheDocument();
  });
});

describe('VersionView', () => {
  it('shows the content, directions and non-substantive answers, and links the rule sets', async () => {
    stubBff({
      [`instrument-versions/${VERSION_ID}`]: version,
      [`instrument-versions/${VERSION_ID}/rule-sets`]: [ruleSet],
      [`instruments/${INSTRUMENT_ID}`]: instrument,
    });

    renderWithProviders(<VersionView versionId={VERSION_ID} />);

    expect(await screen.findByRole('heading', { name: 'family_voices, version 1', level: 1 })).toBeInTheDocument();
    expect(await screen.findByText('A question')).toBeInTheDocument();
    expect(screen.getByText('Not yet specified')).toBeInTheDocument();
    expect(screen.getByText('Not a point on the scale')).toBeInTheDocument();
    const rules = screen.getByRole('table', { name: 'Rule sets of this version' });
    expect(within(rules).getByRole('link', { name: 'module_a_eligibility' })).toHaveAttribute('href', `/configuration/rule-sets/${RULE_SET_ID}`);
    expect(within(rules).getByText('Proposed')).toBeInTheDocument();
    expectReadOnly();
    // Every description in a description list has its term; "Required" is a sentence of its own.
    for (const description of document.querySelectorAll('dd')) expect(description.previousElementSibling?.tagName).toBe('DT');
    expect(screen.getByText('Required').tagName).toBe('P');
  });

  it('says when a version has no rule sets', async () => {
    stubBff({
      [`instrument-versions/${VERSION_ID}`]: version,
      [`instrument-versions/${VERSION_ID}/rule-sets`]: [],
      [`instruments/${INSTRUMENT_ID}`]: instrument,
    });

    renderWithProviders(<VersionView versionId={VERSION_ID} />);

    expect(await screen.findByText('This version has no rule sets.')).toBeInTheDocument();
  });
});

describe('RuleSetView', () => {
  it('shows the stored definition as formatted JSON, read-only', async () => {
    stubBff({ [`rule-sets/${RULE_SET_ID}`]: ruleSet });

    renderWithProviders(<RuleSetView ruleSetId={RULE_SET_ID} />);

    expect(await screen.findByRole('heading', { name: 'module_a_eligibility, version 1', level: 1 })).toBeInTheDocument();
    const definition = screen.getByLabelText('Definition');
    expect(definition.tagName).toBe('PRE');
    expect(definition.textContent).toBe(JSON.stringify(ruleSet.definition, null, 2));
    expect(screen.getByRole('link', { name: 'Open the version' })).toHaveAttribute('href', `/configuration/versions/${VERSION_ID}`);
    expectReadOnly();
  });

  it('shows not found for an unknown rule set', async () => {
    stubBff({ [`rule-sets/${RULE_SET_ID}`]: problem(404, 'questionnaires.rule_set_not_found') });

    renderWithProviders(<RuleSetView ruleSetId={RULE_SET_ID} />);

    expect(await screen.findByRole('heading', { name: 'Not found' })).toBeInTheDocument();
  });
});
