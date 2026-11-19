import { ApiError } from '@/api/client';
import type { ScoreReportPart, ScoreReportSummary } from '@/api/participant/schemas';

import type { PlannedPart } from '../reportPlan';
import { createReportPreparer } from '../reportPreparer';
import { createMemoryScoreReportStore } from '../reportStore';

const SESSION = 'session';
const at = '2026-11-05T09:00:00Z';

const written = (sectionCode: string): ScoreReportPart => ({
  sectionCode,
  language: 'en',
  strengths: [],
  weaknesses: [],
  nextSteps: [],
  modelName: 'synthetic-model',
  generatedAt: at,
  standing: 0.5,
});
const SUMMARY: ScoreReportSummary = { language: 'en', summary: 'A summary.', modelName: 'synthetic-model', generatedAt: at };

const planned = (code: string, fingerprint = 'one'): PlannedPart => ({ key: `section:${code}`, sectionCode: code, fingerprint });
const summaryPlanned = (fingerprint = 'one'): PlannedPart => ({ key: 'summary', sectionCode: null, fingerprint });

function setup() {
  const store = createMemoryScoreReportStore();
  const api = {
    createScoreReportPart: jest.fn(async (_session: string, code: string) => written(code)),
    createScoreReportSummary: jest.fn(async () => SUMMARY),
  };
  return { store, api, preparer: createReportPreparer({ api, store }) };
}

const nextTurn = () => new Promise<void>((resolve) => setImmediate(() => resolve()));

async function settle(preparer: ReturnType<typeof setup>['preparer']) {
  await nextTurn();
  await preparer.settled(SESSION);
}

describe('writing a report ahead', () => {
  it('writes each part once and saves it sealed in the store', async () => {
    const { store, api, preparer } = setup();

    preparer.update(SESSION, 'en', [planned('A'), planned('B'), summaryPlanned()]);
    await settle(preparer);

    expect(api.createScoreReportPart.mock.calls.map((call) => call[1]).sort()).toEqual(['A', 'B']);
    expect(api.createScoreReportSummary).toHaveBeenCalledTimes(1);
    expect((await store.listParts(SESSION, 'en')).map((part) => part.key).sort()).toEqual(['section:A', 'section:B', 'summary']);
  });

  it('runs parts side by side, so the last section and the summary finish together', async () => {
    const { api, preparer } = setup();
    const release: (() => void)[] = [];
    api.createScoreReportPart.mockImplementation(
      (_session, code) => new Promise((resolve) => release.push(() => resolve(written(code)))),
    );
    api.createScoreReportSummary.mockImplementation(() => new Promise((resolve) => release.push(() => resolve(SUMMARY))));

    preparer.update(SESSION, 'en', [planned('A'), summaryPlanned()]);
    await nextTurn();

    expect(api.createScoreReportPart).toHaveBeenCalledTimes(1);
    expect(api.createScoreReportSummary).toHaveBeenCalledTimes(1);
    release.forEach((finish) => finish());
    await settle(preparer);
  });

  it('asks for nothing when the saved parts already cover the answers', async () => {
    const { api, preparer } = setup();
    preparer.update(SESSION, 'en', [planned('A')]);
    await settle(preparer);

    preparer.update(SESSION, 'en', [planned('A')]);
    await settle(preparer);

    expect(api.createScoreReportPart).toHaveBeenCalledTimes(1);
  });

  it('rewrites only the section whose answers changed', async () => {
    const { api, preparer } = setup();
    preparer.update(SESSION, 'en', [planned('A'), planned('B')]);
    await settle(preparer);

    preparer.update(SESSION, 'en', [planned('A'), planned('B', 'changed')]);
    await settle(preparer);

    expect(api.createScoreReportPart.mock.calls.map((call) => call[1]).sort()).toEqual(['A', 'B', 'B']);
  });

  it('writes once more after a change made while it was writing, however many changes there were', async () => {
    const { store, api, preparer } = setup();
    let finish: (part: ScoreReportPart) => void = () => undefined;
    api.createScoreReportPart.mockImplementationOnce(() => new Promise((resolve) => (finish = resolve)));

    preparer.update(SESSION, 'en', [planned('A', 'first')]);
    await nextTurn();
    preparer.update(SESSION, 'en', [planned('A', 'second')]);
    await nextTurn();
    preparer.update(SESSION, 'en', [planned('A', 'third')]);
    await nextTurn();
    finish(written('A'));
    await settle(preparer);

    expect(api.createScoreReportPart).toHaveBeenCalledTimes(2);
    expect((await store.listParts(SESSION, 'en'))[0]?.fingerprint).toBe('third');
  });

  it('tries once more after a dropped connection', async () => {
    const { store, api, preparer } = setup();
    api.createScoreReportPart.mockRejectedValueOnce(new ApiError('network'));

    preparer.update(SESSION, 'en', [planned('A')]);
    await settle(preparer);

    expect(api.createScoreReportPart).toHaveBeenCalledTimes(2);
    expect(await store.listParts(SESSION, 'en')).toHaveLength(1);
  });

  it('gives up on a part after two failures, saving nothing, and never rejects', async () => {
    const { store, api, preparer } = setup();
    api.createScoreReportPart.mockRejectedValue(new ApiError('server', 502));

    preparer.update(SESSION, 'en', [planned('A')]);
    await settle(preparer);

    expect(api.createScoreReportPart).toHaveBeenCalledTimes(2);
    expect(await store.listParts(SESSION, 'en')).toEqual([]);
  });

  it.each([403, 404, 409, 503])('stops asking for this session after a %i', async (status) => {
    const { api, preparer } = setup();
    api.createScoreReportPart.mockRejectedValue(new ApiError('server', status));

    preparer.update(SESSION, 'en', [planned('A')]);
    await settle(preparer);
    preparer.update(SESSION, 'en', [planned('A', 'changed')]);
    await settle(preparer);

    expect(api.createScoreReportPart).toHaveBeenCalledTimes(1);
  });

  it('saves nothing that finishes after a sign-out', async () => {
    const { store, api, preparer } = setup();
    let finish: (part: ScoreReportPart) => void = () => undefined;
    api.createScoreReportPart.mockImplementationOnce(() => new Promise((resolve) => (finish = resolve)));

    preparer.update(SESSION, 'en', [planned('A')]);
    await nextTurn();
    preparer.reset();
    finish(written('A'));
    await settle(preparer);

    expect(await store.listParts(SESSION, 'en')).toEqual([]);
  });
});
