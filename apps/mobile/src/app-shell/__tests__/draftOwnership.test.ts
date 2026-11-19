import type { ScoreReport } from '@/api/participant/schemas';
import { createMemoryDraftStore } from '@/drafts/memoryDraftStore';
import { createMemoryChatStore } from '@/features/assistant/chatStore';
import { createMemoryScoreReportStore } from '@/features/score-report/reportStore';
import { AssessmentEngine } from '@/features/sessions/assessmentEngine';
import { createSettingsRepository } from '@/storage/settings';
import { FakeSqlDatabase } from '@/storage/testing/FakeSqlDatabase';
import { createFakeConnectivity } from '@/test-utils/fakeConnectivity';

import { createDraftOwnership } from '../draftOwnership';

const AT = new Date('2026-10-14T09:00:00Z');
const REPORT: ScoreReport = {
  sessionId: 's1',
  language: 'en',
  totalScore: 96,
  tierCode: 'transitional',
  scale: { minimum: 39, maximum: 156, bands: [{ code: 'transitional', min: 39, max: 156 }] },
  summary: 'A synthetic summary.',
  strengths: [],
  weaknesses: [],
  nextSteps: [],
  modelName: 'synthetic-model',
  generatedAt: AT.toISOString(),
};

async function setup() {
  const store = createMemoryDraftStore();
  const db = new FakeSqlDatabase();
  const settings = createSettingsRepository(db);
  await store.saveAnswer({ sessionId: 's1', itemId: 'i1', optionId: 'o1', baseModifiedAt: null, at: AT });
  await store.queueSubmission('s1', 'key-1', AT);
  const engine = new AssessmentEngine({
    store,
    api: { saveAnswers: jest.fn(), getAnswers: jest.fn(), submitSession: jest.fn() },
    connectivity: createFakeConnectivity(),
    onSaved: jest.fn(),
    onRejected: jest.fn(),
    onSubmitted: jest.fn(),
    paused: true,
  });
  const exportFiles = { clearAll: jest.fn(async () => undefined) };
  const chat = createMemoryChatStore();
  await chat.append({ id: 'm1', role: 'user', text: 'How is my family doing?', createdAt: AT.toISOString(), channel: 'chat' });
  const reports = createMemoryScoreReportStore();
  await reports.save(REPORT);
  return {
    store,
    db,
    settings,
    engine,
    exportFiles,
    chat,
    reports,
    ownership: createDraftOwnership(engine, settings, exportFiles, chat, reports),
  };
}

describe('draft ownership', () => {
  it('keeps written score reports for the same account, and deletes them for another and at sign-out', async () => {
    const { reports, settings, ownership } = await setup();
    await settings.set('drafts_owner', 'subject-a');

    await ownership.claim('subject-a');
    expect(await reports.get(REPORT.sessionId, 'en')).not.toBeNull();

    await ownership.claim('subject-b');
    expect(await reports.get(REPORT.sessionId, 'en')).toBeNull();

    await reports.save(REPORT);
    await ownership.forget();
    expect(await reports.get(REPORT.sessionId, 'en')).toBeNull();
  });

  it('keeps the assistant conversation and its memory for the same account, and deletes them for another and at sign-out', async () => {
    const { chat, settings, ownership } = await setup();
    await settings.set('drafts_owner', 'subject-a');
    await chat.setMemory('The family asked about family time.');

    await ownership.claim('subject-a');
    expect(await chat.list()).toHaveLength(1);
    expect(await chat.getMemory()).toBe('The family asked about family time.');

    await ownership.claim('subject-b');
    expect(await chat.list()).toEqual([]);
    expect(await chat.getMemory()).toBeNull();

    await chat.append({ id: 'm2', role: 'user', text: 'Hello', createdAt: AT.toISOString(), channel: 'chat' });
    await chat.setMemory('Another summary');
    await ownership.forget();
    expect(await chat.list()).toEqual([]);
    expect(await chat.getMemory()).toBeNull();
  });

  it('keeps drafts for the account that wrote them, after an expired session', async () => {
    const { store, settings, ownership } = await setup();
    await settings.set('drafts_owner', 'subject-a');

    await ownership.claim('subject-a');

    expect(await store.listAnswers('s1')).toHaveLength(1);
  });

  it('deletes drafts before another account uses the device', async () => {
    const { store, settings, ownership } = await setup();
    await settings.set('drafts_owner', 'subject-a');

    await ownership.claim('subject-b');

    expect(await store.listAnswers('s1')).toEqual([]);
    expect(await store.listSubmissions()).toEqual([]);
    await expect(settings.get('drafts_owner')).resolves.toBe('subject-b');
  });

  it('keeps a queued submission for the account that asked for it', async () => {
    const { store, settings, ownership } = await setup();
    await settings.set('drafts_owner', 'subject-a');

    await ownership.claim('subject-a');

    expect(await store.listSubmissions()).toEqual([expect.objectContaining({ sessionId: 's1', idempotencyKey: 'key-1' })]);
  });

  it('deletes drafts of unknown ownership at the first sign-in', async () => {
    const { store, ownership } = await setup();

    await ownership.claim('subject-a');

    expect(await store.listAnswers('s1')).toEqual([]);
  });

  it('deletes drafts and forgets the owner on sign-out', async () => {
    const { store, settings, ownership } = await setup();
    await settings.set('drafts_owner', 'subject-a');

    await ownership.forget();

    expect(await store.listAnswers('s1')).toEqual([]);
    await expect(settings.get('drafts_owner')).resolves.toBeNull();
  });

  it('forgets what it showed about a queued submission once the local data is deleted', async () => {
    const { engine, settings, ownership } = await setup();
    await settings.set('drafts_owner', 'subject-a');
    await engine.submissions.submit('s1');
    expect(engine.submissions.getState('s1').status).toBe('queued');

    await ownership.forget();

    expect(engine.submissions.getState('s1').status).toBe('idle');
  });

  it('keeps the owner when deleting fails, so another account still triggers the deletion', async () => {
    const { store, settings, ownership } = await setup();
    await settings.set('drafts_owner', 'subject-a');
    jest.spyOn(store, 'clearAll').mockRejectedValueOnce(new Error('locked'));

    await expect(ownership.forget()).resolves.toBeUndefined();

    await expect(settings.get('drafts_owner')).resolves.toBe('subject-a');
    await ownership.claim('subject-b');
    expect(await store.listAnswers('s1')).toEqual([]);
  });

  it('deletes a data export left on the phone at sign-out and before another account uses it', async () => {
    const { settings, ownership, exportFiles } = await setup();
    await settings.set('drafts_owner', 'subject-a');

    await ownership.claim('subject-a');
    expect(exportFiles.clearAll).not.toHaveBeenCalled();
    await ownership.forget();
    expect(exportFiles.clearAll).toHaveBeenCalledTimes(1);
    await ownership.claim('subject-b');

    expect(exportFiles.clearAll).toHaveBeenCalledTimes(2);
  });
});
