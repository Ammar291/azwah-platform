import type { SessionAnswers } from '@/api/participant/schemas';
import { PLACEHOLDER_VERSION } from '@/test-utils/placeholderContent';
import { DRAFT_KEY_NAME } from '@/storage/protection/fieldCipher';
import { createDeviceDrafts, type DeviceDrafts } from '@/test-utils/deviceDrafts';

import { createMemoryDraftStore } from '../memoryDraftStore';
import type { DraftStore, SaveDraftAnswer, SessionCopyContent } from '../types';

const T1 = new Date('2026-10-14T09:00:00.000Z');
const T2 = new Date('2026-10-14T09:01:00.000Z');
const T3 = new Date('2026-10-14T09:02:00.000Z');
const S1 = '0199e0a1-0000-7000-9000-0000000000a1';
const SERVER_TIME = '2026-10-14T08:00:00.123456Z';

const save = (answer: Partial<SaveDraftAnswer> & Pick<SaveDraftAnswer, 'itemId' | 'optionId'>): SaveDraftAnswer => ({
  sessionId: 's1',
  baseModifiedAt: null,
  at: T1,
  ...answer,
});

/** What was sent for a stored draft: its revision and time identify it. */
const sent = (draft: { itemId: string; revision: number; updatedAt: string }) => ({
  itemId: draft.itemId,
  revision: draft.revision,
  updatedAt: draft.updatedAt,
  serverModifiedAt: SERVER_TIME,
});

const COPY: SessionCopyContent = {
  session: {
    id: S1,
    assignmentId: '0199e0a1-0000-7000-9000-0000000000a2',
    memberId: '0199e0a1-0000-7000-9000-0000000000a3',
    instrumentVersionId: PLACEHOLDER_VERSION.versionId,
    versionNumber: 1,
    sectionCode: 'A',
    status: 'in_progress',
    startedAt: '2026-10-14T08:00:00Z',
    submittedAt: null,
    scoredAt: null,
    answeredCount: 0,
    requiredCount: 4,
  },
  assignment: {
    id: '0199e0a1-0000-7000-9000-0000000000a2',
    memberId: '0199e0a1-0000-7000-9000-0000000000a3',
    instrumentCode: PLACEHOLDER_VERSION.instrumentCode,
    instrumentName: PLACEHOLDER_VERSION.name,
    versionNumber: 1,
    sectionCode: 'A',
    sectionTitle: null,
    waveId: '0199e0a1-0000-7000-9000-0000000000a4',
    waveCode: 'dev-wave-2',
    status: 'in_progress',
    assignedAt: '2026-10-14T08:00:00Z',
    expiresAt: null,
    completedAt: null,
    activeSessionId: S1,
    instrumentKind: 'family_voices',
  },
  version: PLACEHOLDER_VERSION,
};

const FIRST_ITEM = PLACEHOLDER_VERSION.sections[0]?.items[0];
/** Option IDs of the placeholder's first question: copies are read back through the API's schemas, so GUIDs. */
const OPTION = (index: number) => FIRST_ITEM?.options[index]?.id ?? '';

const serverAnswers = (optionId: string): SessionAnswers => ({
  sessionId: S1,
  status: 'in_progress',
  responses: [{ itemId: FIRST_ITEM?.id ?? '', optionId, answeredAt: SERVER_TIME, lastModifiedAt: SERVER_TIME }],
});

interface Harness {
  store: DraftStore;
  close: () => void;
}

const device = async (): Promise<Harness> => {
  const created = await createDeviceDrafts();
  return { store: created.drafts, close: created.close };
};

const memory = async (): Promise<Harness> => ({ store: createMemoryDraftStore(), close: () => undefined });

describe.each([
  ['encrypted SQLite', device, 'device', true],
  ['memory', memory, 'memory', false],
] as const)('%s draft store', (_name, create, persistence, encrypted) => {
  let harness: Harness;
  let store: DraftStore;

  beforeEach(async () => {
    harness = await create();
    store = harness.store;
  });

  afterEach(() => harness.close());

  it(`reports where drafts live (${persistence}) and whether they are encrypted (${encrypted})`, () => {
    expect(store.persistence).toBe(persistence);
    expect(store.encryptedAtRest).toBe(encrypted);
  });

  it('starts empty', async () => {
    await expect(store.listAnswers('s1')).resolves.toEqual([]);
    await expect(store.loadCopy('s1')).resolves.toBeNull();
    await expect(store.listSubmissions()).resolves.toEqual([]);
  });

  it('stores a new answer as pending at revision 1, with the server change it was based on', async () => {
    await expect(store.saveAnswer(save({ itemId: 'i1', optionId: 'o1', baseModifiedAt: SERVER_TIME }))).resolves.toEqual({
      sessionId: 's1',
      itemId: 'i1',
      optionId: 'o1',
      revision: 1,
      baseModifiedAt: SERVER_TIME,
      updatedAt: T1.toISOString(),
      syncState: 'pending',
      syncedAt: null,
    });
  });

  it('keeps one answer per session and item, with the latest option', async () => {
    await store.saveAnswer(save({ itemId: 'i1', optionId: 'o1' }));
    await store.saveAnswer(save({ itemId: 'i1', optionId: 'o2', at: T2 }));

    const answers = await store.listAnswers('s1');

    expect(answers).toHaveLength(1);
    expect(answers[0]).toMatchObject({ optionId: 'o2', revision: 2, updatedAt: T2.toISOString() });
  });

  it('changes nothing when an unsent option is chosen again', async () => {
    await store.saveAnswer(save({ itemId: 'i1', optionId: 'o1' }));

    const again = await store.saveAnswer(save({ itemId: 'i1', optionId: 'o1', at: T3 }));

    expect(again).toMatchObject({ revision: 1, syncState: 'pending', updatedAt: T1.toISOString() });
    expect(await store.listPending('s1')).toHaveLength(1);
  });

  it('makes a new draft when a synced option is chosen again, since the server may have changed meanwhile', async () => {
    const first = await store.saveAnswer(save({ itemId: 'i1', optionId: 'o1' }));
    await store.markSynced('s1', [sent(first)], T2);

    const again = await store.saveAnswer(save({ itemId: 'i1', optionId: 'o1', baseModifiedAt: 'later', at: T3 }));

    expect(again).toMatchObject({ revision: 2, syncState: 'pending', baseModifiedAt: 'later' });
  });

  it('gives each of several quick choices for one question its own revision', async () => {
    await Promise.all(['o1', 'o2', 'o3'].map((optionId, index) => store.saveAnswer(save({ itemId: 'i1', optionId, at: new Date(T1.getTime() + index) }))));

    expect(await store.listPending('s1')).toEqual([expect.objectContaining({ optionId: 'o3', revision: 3 })]);
  });

  it('keeps sessions apart', async () => {
    await store.saveAnswer(save({ itemId: 'i1', optionId: 'o1' }));
    await store.saveAnswer(save({ sessionId: 's2', itemId: 'i1', optionId: 'o9' }));

    expect((await store.listAnswers('s1')).map((a) => a.optionId)).toEqual(['o1']);
    expect((await store.listAnswers('s2')).map((a) => a.optionId)).toEqual(['o9']);
  });

  it('lists pending answers oldest first and marks sent revisions as synced on the server time', async () => {
    await store.saveAnswer(save({ itemId: 'i2', optionId: 'o1', at: T2 }));
    await store.saveAnswer(save({ itemId: 'i1', optionId: 'o1', at: T1 }));

    expect((await store.listPending('s1')).map((a) => a.itemId)).toEqual(['i1', 'i2']);

    const [first] = await store.listPending('s1');
    await store.markSynced('s1', first ? [sent(first)] : [], T3);

    expect((await store.listPending('s1')).map((a) => a.itemId)).toEqual(['i2']);
    const synced = (await store.listAnswers('s1')).find((a) => a.itemId === 'i1');
    expect(synced).toMatchObject({ syncState: 'synced', syncedAt: T3.toISOString(), baseModifiedAt: SERVER_TIME });
  });

  it('marks hundreds of sent answers in one call, only in their own session', async () => {
    const items = Array.from({ length: 250 }, (_, index) => `i${index}`);
    for (const itemId of items) {
      await store.saveAnswer(save({ itemId, optionId: 'o1' }));
    }
    await store.saveAnswer(save({ sessionId: 's2', itemId: 'i0', optionId: 'o1' }));

    await store.markSynced('s1', (await store.listPending('s1')).map(sent), T2);

    expect(await store.listPending('s1')).toEqual([]);
    expect(await store.listPending('s2')).toHaveLength(1);
  });

  it('keeps an answer changed while the older one was sent pending, re-based on the sent one', async () => {
    await store.saveAnswer(save({ itemId: 'i1', optionId: 'o1' }));
    const sending = (await store.listPending('s1')).map(sent);
    await store.saveAnswer(save({ itemId: 'i1', optionId: 'o2', at: T2 }));

    await store.markSynced('s1', sending, T3);

    expect(await store.listPending('s1')).toEqual([
      expect.objectContaining({ itemId: 'i1', optionId: 'o2', revision: 2, syncState: 'pending', baseModifiedAt: SERVER_TIME }),
    ]);
  });

  it('never takes a draft made again after a discard for the one that was sent', async () => {
    const sending = (await Promise.all([store.saveAnswer(save({ itemId: 'i1', optionId: 'o1' }))])).map(sent);
    await store.discardAnswer('s1', 'i1', 1);
    await store.saveAnswer(save({ itemId: 'i1', optionId: 'o3', at: T2 }));

    await store.markSynced('s1', sending, T3);

    expect(await store.listPending('s1')).toEqual([
      expect.objectContaining({ optionId: 'o3', revision: 1, syncState: 'pending', baseModifiedAt: SERVER_TIME }),
    ]);
  });

  it('lists the sessions that have unsent answers', async () => {
    const synced = await store.saveAnswer(save({ sessionId: 's1', itemId: 'i1', optionId: 'o1' }));
    await store.saveAnswer(save({ sessionId: 's3', itemId: 'i1', optionId: 'o1' }));
    await store.saveAnswer(save({ sessionId: 's2', itemId: 'i1', optionId: 'o1' }));
    await store.markSynced('s1', [sent(synced)], T2);

    await expect(store.listSessionsWithPending()).resolves.toEqual(['s2', 's3']);
  });

  it('marks a changed answer pending again after it was synced', async () => {
    const first = await store.saveAnswer(save({ itemId: 'i1', optionId: 'o1' }));
    await store.markSynced('s1', [sent(first)], T2);

    await store.saveAnswer(save({ itemId: 'i1', optionId: 'o3', at: T3 }));

    expect(await store.listPending('s1')).toEqual([
      expect.objectContaining({ optionId: 'o3', revision: 2, syncState: 'pending', syncedAt: null }),
    ]);
  });

  it('re-bases or drops one draft only while it is unchanged', async () => {
    await store.saveAnswer(save({ itemId: 'i1', optionId: 'o1' }));
    await store.saveAnswer(save({ itemId: 'i2', optionId: 'o1' }));

    await expect(store.rebase('s1', 'i1', 1, SERVER_TIME)).resolves.toBe(true);
    await expect(store.rebase('s1', 'i1', 7, 'later')).resolves.toBe(false);
    await store.discardAnswer('s1', 'i2', 7);
    expect(await store.listPending('s1')).toHaveLength(2);
    await store.discardAnswer('s1', 'i2', 1);

    expect(await store.listPending('s1')).toEqual([expect.objectContaining({ itemId: 'i1', baseModifiedAt: SERVER_TIME })]);
  });

  it('keeps a copy of a session with the server answers, and updates the answers after a save', async () => {
    await store.saveCopy(S1, COPY, serverAnswers(OPTION(1)), T1);
    await store.saveServerAnswers(S1, serverAnswers(OPTION(2)), T2);

    const copy = await store.loadCopy(S1);

    expect(copy).toEqual({
      content: COPY,
      serverAnswers: serverAnswers(OPTION(2)),
      savedAt: T1.toISOString(),
      lastSyncedAt: T2.toISOString(),
    });
  });

  it('ignores server answers for a session it has no copy of', async () => {
    await store.saveServerAnswers(S1, serverAnswers(OPTION(1)), T1);

    await expect(store.loadCopy(S1)).resolves.toBeNull();
  });

  it('queues one submission per session and keeps its key', async () => {
    const first = await store.queueSubmission('s1', 'key-1', T1);
    const again = await store.queueSubmission('s1', 'key-2', T2);
    await store.recordSubmissionAttempt('s1', T3);

    expect(first).toEqual({ sessionId: 's1', idempotencyKey: 'key-1', requestedAt: T1.toISOString(), attempts: 0, lastAttemptAt: null });
    expect(again.idempotencyKey).toBe('key-1');
    expect(await store.getSubmission('s1')).toMatchObject({ idempotencyKey: 'key-1', attempts: 1, lastAttemptAt: T3.toISOString() });

    await store.replaceSubmissionKey('s1', 'key-3');
    expect((await store.listSubmissions()).map((queued) => queued.idempotencyKey)).toEqual(['key-3']);
    await store.removeSubmission('s1');
    await expect(store.getSubmission('s1')).resolves.toBeNull();
  });

  it('discards everything about one session, and can clear everything', async () => {
    await store.saveAnswer(save({ itemId: 'i1', optionId: 'o1' }));
    await store.saveAnswer(save({ sessionId: 's2', itemId: 'i1', optionId: 'o1' }));
    await store.saveCopy(S1, COPY, serverAnswers(OPTION(1)), T1);
    await store.queueSubmission('s1', 'key-1', T1);
    await store.queueSubmission('s2', 'key-2', T1);

    await store.discardSession('s1');
    await store.discardSession(S1);
    expect(await store.listAnswers('s1')).toEqual([]);
    expect(await store.loadCopy(S1)).toBeNull();
    expect((await store.listSubmissions()).map((queued) => queued.sessionId)).toEqual(['s2']);
    expect(await store.listAnswers('s2')).toHaveLength(1);

    await store.clearAll();
    expect(await store.listAnswers('s2')).toEqual([]);
    expect(await store.listSubmissions()).toEqual([]);
  });
});

describe('encrypted SQLite draft store on the phone', () => {
  let phone: DeviceDrafts;

  beforeEach(async () => {
    phone = await createDeviceDrafts();
  });

  afterEach(() => phone.close());

  const everything = async () =>
    JSON.stringify([
      await phone.db.getAllAsync('SELECT * FROM draft_answers'),
      await phone.db.getAllAsync('SELECT * FROM session_copies'),
      await phone.db.getAllAsync('SELECT * FROM pending_submissions'),
    ]);

  it('never writes an answer, a question, or the copy in clear text', async () => {
    const optionId = PLACEHOLDER_VERSION.sections[0]?.items[0]?.options[2]?.id ?? '';
    await phone.drafts.saveAnswer(save({ sessionId: S1, itemId: 'i1', optionId }));
    await phone.drafts.saveCopy(S1, COPY, serverAnswers(optionId), T1);

    const stored = await everything();

    expect(stored).not.toContain(optionId);
    expect(stored).not.toContain('Statement A1');
    expect(stored).not.toContain('Sample questionnaire');
    expect(stored).toMatch(/"answer_cipher":"v1:/);
    expect(stored).toMatch(/"content_cipher":"v1:/);
  });

  it('gives back drafts, the copy and the queued submission after the app is reopened', async () => {
    await phone.drafts.saveAnswer(save({ sessionId: S1, itemId: 'i1', optionId: 'o1', baseModifiedAt: SERVER_TIME }));
    await phone.drafts.saveCopy(S1, COPY, serverAnswers(OPTION(0)), T1);
    await phone.drafts.queueSubmission(S1, 'key-1', T2);

    const reopened = phone.restart().drafts;

    expect(await reopened.listPending(S1)).toEqual([
      expect.objectContaining({ itemId: 'i1', optionId: 'o1', baseModifiedAt: SERVER_TIME }),
    ]);
    expect((await reopened.loadCopy(S1))?.content).toEqual(COPY);
    expect(await reopened.getSubmission(S1)).toMatchObject({ idempotencyKey: 'key-1' });
  });

  it('removes and reports answers whose key was lost, and drops an unreadable copy, without failing', async () => {
    await phone.drafts.saveAnswer(save({ sessionId: S1, itemId: 'i1', optionId: 'o1' }));
    await phone.drafts.saveAnswer(save({ sessionId: S1, itemId: 'i2', optionId: 'o2' }));
    await phone.drafts.saveCopy(S1, COPY, serverAnswers(OPTION(0)), T1);
    await phone.secrets.remove(DRAFT_KEY_NAME);

    const reopened = phone.restart().drafts;

    await expect(reopened.listPending(S1)).resolves.toEqual([]);
    expect(reopened.takeLost(S1)).toBe(2);
    expect(reopened.takeLost(S1)).toBe(0);
    await expect(reopened.loadCopy(S1)).resolves.toBeNull();
    expect(await phone.db.getAllAsync('SELECT * FROM draft_answers')).toEqual([]);
  });

  it('refuses a sealed answer moved to another question', async () => {
    await phone.drafts.saveAnswer(save({ sessionId: S1, itemId: 'i1', optionId: 'o1' }));
    await phone.db.runAsync("UPDATE draft_answers SET item_id = 'i2'");

    await expect(phone.drafts.listPending(S1)).resolves.toEqual([]);
    expect(phone.drafts.takeLost(S1)).toBe(1);
  });

  it('deletes the key with everything else, so nothing left behind can be read', async () => {
    await phone.drafts.saveAnswer(save({ sessionId: S1, itemId: 'i1', optionId: 'o1' }));
    const sealed = (await phone.db.getFirstAsync<{ answer_cipher: string }>('SELECT answer_cipher FROM draft_answers'))?.answer_cipher;

    await phone.drafts.clearAll();

    expect(await phone.secrets.get(DRAFT_KEY_NAME)).toBeNull();
    await phone.db.runAsync(
      "INSERT INTO draft_answers VALUES (?, 'i1', ?, 1, NULL, '2026-10-14T09:00:00.000Z', 'pending', NULL)",
      S1,
      sealed ?? '',
    );
    await expect(phone.drafts.listPending(S1)).resolves.toEqual([]);
    expect(phone.drafts.takeLost(S1)).toBe(1);
  });

  it('drops a copy written by an app whose shapes no longer match', async () => {
    await phone.drafts.saveCopy(S1, { ...COPY, session: { ...COPY.session, status: 'retired' as never } }, serverAnswers(OPTION(1)), T1);

    await expect(phone.drafts.loadCopy(S1)).resolves.toBeNull();
    expect(await phone.db.getAllAsync('SELECT * FROM session_copies')).toEqual([]);
  });
});
