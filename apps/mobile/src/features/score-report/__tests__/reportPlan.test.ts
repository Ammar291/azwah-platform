import type { PublishedVersion, ScoreReportPart, ScoreReportSummary, SessionAnswers } from '@/api/participant/schemas';

import { assembleReport, planReport, SUMMARY_KEY } from '../reportPlan';
import type { StoredReportPart } from '../reportStore';

const item = (id: string, isRequired = true) => ({ id, isRequired }) as PublishedVersion['sections'][number]['items'][number];

const VERSION = {
  sections: [
    { id: 's2', code: 'B', displayOrder: 2, items: [item('b1')] },
    { id: 's1', code: 'A', displayOrder: 1, items: [item('a1'), item('a2'), item('a3', false)] },
  ],
} as unknown as PublishedVersion;

const answers = (pairs: Record<string, string>): SessionAnswers => ({
  sessionId: 'session',
  status: 'in_progress',
  responses: Object.entries(pairs).map(([itemId, optionId]) => ({
    itemId,
    optionId,
    answeredAt: '2026-11-05T09:00:00Z',
    lastModifiedAt: '2026-11-05T09:00:00Z',
  })),
});

const point = (title: string) => ({ title, detail: 'D' });
const part = (sectionCode: string): ScoreReportPart => ({
  sectionCode,
  language: 'en',
  strengths: [point(`strength ${sectionCode}`)],
  weaknesses: [point(`weakness ${sectionCode}`)],
  nextSteps: [{ sectionCode: 'wealth_debt', area: sectionCode, reason: 'R', steps: ['S'], standing: 0.5, authorities: [] }],
  modelName: 'synthetic-model',
  generatedAt: '2026-11-05T09:00:00Z',
  standing: 0.5,
});
const summary: ScoreReportSummary = {
  language: 'en',
  summary: 'A synthetic summary.',
  modelName: 'synthetic-model',
  generatedAt: '2026-11-05T09:01:00Z',
};

const RECORDED = {
  sessionId: 'session',
  language: 'en' as const,
  totalScore: 96,
  tierCode: 'transitional',
  scale: { minimum: 39, maximum: 156, bands: [{ code: 'transitional', min: 39, max: 156 }] },
};

/** Every part of `plan`, written from the answers it names. */
function written(plan: ReturnType<typeof planReport>): StoredReportPart[] {
  return plan.parts.map((planned) =>
    planned.sectionCode === null
      ? { key: planned.key, fingerprint: planned.fingerprint, kind: 'summary', summary }
      : { key: planned.key, fingerprint: planned.fingerprint, kind: 'section', part: part(planned.sectionCode) },
  );
}

describe('planning a report', () => {
  it('holds nothing until a section is answered', () => {
    expect(planReport(VERSION, null, answers({ a1: 'x' }))).toEqual({ parts: [], complete: false });
  });

  it('holds a section once its required questions are answered, without its optional ones', () => {
    const plan = planReport(VERSION, null, answers({ a1: 'x', a2: 'y' }));

    expect(plan.parts.map((planned) => planned.sectionCode)).toEqual(['A']);
    expect(plan.complete).toBe(false);
  });

  it('adds the summary once every section is answered, sections in display order', () => {
    const plan = planReport(VERSION, null, answers({ a1: 'x', a2: 'y', b1: 'z' }));

    expect(plan.parts.map((planned) => planned.key)).toEqual(['section:A', 'section:B', SUMMARY_KEY]);
    expect(plan.complete).toBe(true);
  });

  it('keeps to the section of a section-scoped session', () => {
    const plan = planReport(VERSION, 'B', answers({ b1: 'z' }));

    expect(plan.parts.map((planned) => planned.key)).toEqual(['section:B', SUMMARY_KEY]);
    expect(plan.complete).toBe(true);
  });

  it('changes only the changed section and the summary when an answer changes', () => {
    const before = planReport(VERSION, null, answers({ a1: 'x', a2: 'y', b1: 'z' })).parts;
    const after = planReport(VERSION, null, answers({ a1: 'x', a2: 'y', b1: 'changed' })).parts;

    const changed = (key: string) =>
      before.find((planned) => planned.key === key)?.fingerprint !== after.find((planned) => planned.key === key)?.fingerprint;
    expect([changed('section:A'), changed('section:B'), changed(SUMMARY_KEY)]).toEqual([false, true, true]);
  });

  it('does not depend on the order the server lists the answers', () => {
    const one = planReport(VERSION, null, answers({ a1: 'x', a2: 'y', b1: 'z' }));
    const other = planReport(VERSION, null, answers({ b1: 'z', a2: 'y', a1: 'x' }));

    expect(other).toEqual(one);
  });
});

describe('joining a report', () => {
  const plan = planReport(VERSION, null, answers({ a1: 'x', a2: 'y', b1: 'z' }));

  it('joins the parts in section order with the score the server recorded', () => {
    const report = assembleReport(RECORDED, plan, written(plan).reverse());

    expect(report).toMatchObject({ ...RECORDED, summary: 'A synthetic summary.', modelName: 'synthetic-model', generatedAt: '2026-11-05T09:01:00Z' });
    expect(report?.strengths.map((entry) => entry.title)).toEqual(['strength A', 'strength B']);
    expect(report?.weaknesses.map((entry) => entry.title)).toEqual(['weakness A', 'weakness B']);
    expect(report?.nextSteps.map((entry) => entry.area)).toEqual(['A', 'B']);
  });

  it('puts the area the server says stands lowest first, keeping section order for equals', () => {
    const three = planReport(
      {
        sections: [
          { id: 's1', code: 'A', displayOrder: 1, items: [item('a1')] },
          { id: 's2', code: 'B', displayOrder: 2, items: [item('b1')] },
          { id: 's3', code: 'C', displayOrder: 3, items: [item('c1')] },
        ],
      } as unknown as PublishedVersion,
      null,
      answers({ a1: 'x', b1: 'y', c1: 'z' }),
    );
    const standing: Record<string, number> = { A: 0.5, B: 0.1, C: 0.5 };
    const stored = written(three).map((entry) =>
      entry.kind === 'section'
        ? {
            ...entry,
            part: {
              ...entry.part,
              nextSteps: entry.part.nextSteps.map((step) => ({ ...step, standing: standing[entry.part.sectionCode] ?? 1 })),
            },
          }
        : entry,
    );

    expect(assembleReport(RECORDED, three, stored)?.nextSteps.map((step) => step.area)).toEqual(['B', 'A', 'C']);
  });

  it('keeps the two strongest areas strengths, the two weakest areas weaknesses and the three weakest steps (D-145)', () => {
    const codes = ['A', 'B', 'C', 'D', 'E'];
    const five = planReport(
      {
        sections: codes.map((code, index) => ({ id: `s${code}`, code, displayOrder: index + 1, items: [item(code.toLowerCase())] })),
      } as unknown as PublishedVersion,
      null,
      answers(Object.fromEntries(codes.map((code) => [code.toLowerCase(), 'x']))),
    );
    // Where each area stands, as the server says: E and B strongest, D and A weakest.
    const standing: Record<string, number> = { A: 0.2, B: 0.8, C: 0.5, D: 0.1, E: 0.9 };
    const stored = written(five).map((entry) => {
      if (entry.kind !== 'section') return entry;
      const at = standing[entry.part.sectionCode] ?? 1;
      return {
        ...entry,
        part: { ...entry.part, standing: at, nextSteps: entry.part.nextSteps.map((step) => ({ ...step, standing: at })) },
      };
    });

    const report = assembleReport(RECORDED, five, stored);

    expect(report?.strengths.map((entry) => entry.title)).toEqual(['strength E', 'strength B']);
    expect(report?.weaknesses.map((entry) => entry.title)).toEqual(['weakness D', 'weakness A']);
    expect(report?.nextSteps.map((entry) => entry.area)).toEqual(['D', 'A', 'C']);
  });

  it('waits for a part that is missing', () => {
    expect(assembleReport(RECORDED, plan, written(plan).slice(1))).toBeNull();
  });

  it('waits for the summary', () => {
    expect(assembleReport(RECORDED, plan, written(plan).filter((entry) => entry.key !== SUMMARY_KEY))).toBeNull();
  });

  it('refuses a part written from answers that have since changed', () => {
    const stale = written(plan).map((entry) => (entry.key === 'section:B' ? { ...entry, fingerprint: 'old' } : entry));

    expect(assembleReport(RECORDED, plan, stale)).toBeNull();
  });

  it('refuses an unfinished questionnaire', () => {
    const partial = planReport(VERSION, null, answers({ a1: 'x', a2: 'y' }));

    expect(assembleReport(RECORDED, partial, written(partial))).toBeNull();
  });
});
