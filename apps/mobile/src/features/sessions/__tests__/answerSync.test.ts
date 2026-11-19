import { ApiError } from '@/api/client';
import type { AnswerRequest, PublishedItem, SessionAnswers } from '@/api/participant/schemas';
import { createMemoryDraftStore } from '@/drafts/memoryDraftStore';
import type { DraftStore } from '@/drafts/types';
import { createDeviceDrafts } from '@/test-utils/deviceDrafts';
import { createFakeConnectivity } from '@/test-utils/fakeConnectivity';

import { AnswerSync, type AnswerSyncDependencies } from '../answerSync';
import { AssessmentEngine } from '../assessmentEngine';

const SESSION = 's1';
const DELAY = 20;
const RETRY = { baseMs: 40, maxMs: 80, maxAttempts: 3 };

/**
 * A server holding one session's answers, timed as the platform times them: a save's response carries the server
 * clock's 100 ns precision, a later read the microseconds PostgreSQL keeps. `changeElsewhere` stands for another phone
 * of the same person answering.
 */
function fakeServer() {
  const answers = new Map<string, { optionId: string; lastModifiedAt: string }>();
  let clock = 0;
  const stamp = () => `2026-10-15T09:00:${String(++clock).padStart(2, '0')}.1234567+00:00`;
  const asStored = (time: string) => time.replace('.1234567+00:00', '.123457Z');
  const view = (read = false): SessionAnswers => ({
    sessionId: SESSION,
    status: 'in_progress',
    responses: [...answers].map(([itemId, { optionId, lastModifiedAt }]) => {
      const time = read ? asStored(lastModifiedAt) : lastModifiedAt;
      return { itemId, optionId, lastModifiedAt: time, answeredAt: time };
    }),
  });
  const write = (list: readonly AnswerRequest[]) => {
    const at = stamp();
    list.forEach(({ itemId, optionId }) => answers.set(itemId, { optionId, lastModifiedAt: at }));
  };
  return {
    view,
    saveAnswers: jest.fn(async (_sessionId: string, list: readonly AnswerRequest[]) => {
      write(list);
      return view();
    }),
    getAnswers: jest.fn(async () => view(true)),
    changeElsewhere: (itemId: string, optionId: string) => write([{ itemId, optionId }]),
  };
}

type Server = ReturnType<typeof fakeServer>;

function setup(options: { store?: DraftStore; server?: Server; deps?: Partial<AnswerSyncDependencies> } = {}) {
  const store = options.store ?? createMemoryDraftStore();
  const server = options.server ?? fakeServer();
  const connectivity = createFakeConnectivity();
  const onSaved = jest.fn();
  const onRejected = jest.fn();
  const sync = new AnswerSync(SESSION, {
    store,
    api: server,
    connectivity,
    onSaved,
    onRejected,
    delayMs: DELAY,
    retry: RETRY,
    random: () => 0.5,
    ...options.deps,
  });
  return { sync, store, server, connectivity, onSaved, onRejected };
}

const wait = (ms = DELAY * 3) => new Promise((resolve) => setTimeout(resolve, ms));
const optionIds = (store: DraftStore) => store.listPending(SESSION).then((drafts) => drafts.map((d) => d.optionId));

describe('AnswerSync saving', () => {
  it('restores unsent drafts after a restart, checks them against the server, then sends them', async () => {
    const store = createMemoryDraftStore();
    await store.saveAnswer({ sessionId: SESSION, itemId: 'i1', optionId: 'o1', baseModifiedAt: null, at: new Date() });
    const { sync, server } = setup({ store });

    await wait();

    expect(server.getAnswers).toHaveBeenCalledTimes(1);
    expect(server.saveAnswers).toHaveBeenCalledWith(SESSION, [{ itemId: 'i1', optionId: 'o1' }]);
    expect(sync.getSnapshot()).toMatchObject({ ready: true, local: {}, phase: 'synced' });
    expect(await store.listPending(SESSION)).toEqual([]);
  });

  it('writes each choice locally at once and sends quick successive choices in one save', async () => {
    const { sync, store, server } = setup();
    sync.observeServer(server.view());
    await wait();

    await sync.choose('i1', 'o1');
    expect(await optionIds(store)).toEqual(['o1']);
    await sync.choose('i2', 'o2');
    await sync.choose('i1', 'o3');
    await wait();

    expect(server.saveAnswers).toHaveBeenCalledTimes(1);
    expect(server.saveAnswers.mock.calls[0]?.[1]).toEqual(
      expect.arrayContaining([
        { itemId: 'i1', optionId: 'o3' },
        { itemId: 'i2', optionId: 'o2' },
      ]),
    );
  });

  it('never runs two saves at once; a flush also sends what was chosen during its save', async () => {
    const server = fakeServer();
    const release: (() => void)[] = [];
    const save = server.saveAnswers.getMockImplementation();
    server.saveAnswers.mockImplementation(
      (id, list) => new Promise((resolve) => release.push(() => resolve(save?.(id, list) ?? server.view()))),
    );
    const { sync } = setup({ server, deps: { delayMs: 60_000 } });
    sync.observeServer(server.view());
    await wait();

    await sync.choose('i1', 'o1');
    const first = sync.flush();
    await wait(0);
    await sync.choose('i2', 'o2');
    const second = sync.flush();
    await wait(0);

    expect(server.saveAnswers).toHaveBeenCalledTimes(1);
    release[0]?.();
    await wait(0);
    expect(server.saveAnswers).toHaveBeenCalledTimes(2);
    expect(server.saveAnswers.mock.calls[1]?.[1]).toEqual([{ itemId: 'i2', optionId: 'o2' }]);
    release[1]?.();
    await expect(first).resolves.toBe('saved');
    await expect(second).resolves.toBe('saved');
    expect(server.saveAnswers).toHaveBeenCalledTimes(2);
  });

  it('does not send an answer the server already has (deduplication)', async () => {
    const { sync, store, server } = setup();
    server.changeElsewhere('i1', 'o1');
    sync.observeServer(server.view());
    await wait();
    await sync.choose('i1', 'o2');
    await sync.choose('i1', 'o1');

    await expect(sync.flush()).resolves.toBe('saved');

    expect(server.saveAnswers).not.toHaveBeenCalled();
    expect(await store.listPending(SESSION)).toEqual([]);
  });

  it('records when the server last accepted answers, also in the copy kept on the phone', async () => {
    const now = new Date('2026-10-15T10:30:00.000Z');
    const store = createMemoryDraftStore();
    const saveServerAnswers = jest.spyOn(store, 'saveServerAnswers');
    const { sync, server } = setup({ store, deps: { now: () => now } });
    sync.observeServer(server.view());
    await wait();

    await sync.choose('i1', 'o1');
    await sync.flush();

    expect(sync.getSnapshot().lastSyncedAt).toBe(now.toISOString());
    expect(saveServerAnswers).toHaveBeenLastCalledWith(SESSION, server.view(), now);
  });

  it('sends at once when the last screen showing the session closes', async () => {
    const { sync, server } = setup({ deps: { delayMs: 60_000 } });
    sync.observeServer(server.view());
    const leave = sync.subscribe(() => undefined);
    await wait();

    await sync.choose('i1', 'o1');
    leave();
    await wait(0);

    expect(server.saveAnswers).toHaveBeenCalledWith(SESSION, [{ itemId: 'i1', optionId: 'o1' }]);
  });
});

describe('AnswerSync conflicts', () => {
  it('holds a draft whose question changed elsewhere since it was chosen, and sends the rest', async () => {
    const { sync, store, server } = setup();
    server.changeElsewhere('i1', 'o1');
    sync.observeServer(server.view());
    await wait();
    await sync.choose('i1', 'o2');
    await sync.choose('i2', 'o2');
    server.changeElsewhere('i1', 'o3');
    sync.observeServer(server.view());

    await expect(sync.flush()).resolves.toBe('conflicts');

    expect(server.saveAnswers).toHaveBeenCalledWith(SESSION, [{ itemId: 'i2', optionId: 'o2' }]);
    expect(server.view().responses.find((r) => r.itemId === 'i1')?.optionId).toBe('o3');
    expect(sync.getSnapshot().conflicts).toEqual([
      expect.objectContaining({ itemId: 'i1', local: 'o2', server: 'o3' }),
    ]);
    expect(await optionIds(store)).toEqual(['o2']);
  });

  it('finds the conflict after a restart, when drafts made offline meet a newer server answer', async () => {
    const server = fakeServer();
    server.changeElsewhere('i1', 'o1');
    const base = server.view().responses[0]?.lastModifiedAt ?? null;
    const store = createMemoryDraftStore();
    await store.saveAnswer({ sessionId: SESSION, itemId: 'i1', optionId: 'o2', baseModifiedAt: base, at: new Date() });
    server.changeElsewhere('i1', 'o4');

    const { sync } = setup({ store, server });
    await wait();

    expect(server.saveAnswers).not.toHaveBeenCalled();
    expect(sync.getSnapshot().conflicts).toEqual([expect.objectContaining({ itemId: 'i1', local: 'o2', server: 'o4' })]);
  });

  it("keeps this phone's answer when the person says so, and sends it over the server's", async () => {
    const { sync, server } = setup();
    server.changeElsewhere('i1', 'o1');
    sync.observeServer(server.view());
    await wait();
    await sync.choose('i1', 'o2');
    server.changeElsewhere('i1', 'o3');
    sync.observeServer(server.view());
    await sync.flush();

    await sync.keepLocal('i1');
    await expect(sync.flush()).resolves.toBe('saved');

    expect(server.view().responses.find((r) => r.itemId === 'i1')?.optionId).toBe('o2');
    expect(sync.getSnapshot().conflicts).toEqual([]);
  });

  it("keeps the server's answer when the person says so, and forgets this phone's", async () => {
    const { sync, store, server } = setup();
    server.changeElsewhere('i1', 'o1');
    sync.observeServer(server.view());
    await wait();
    await sync.choose('i1', 'o2');
    server.changeElsewhere('i1', 'o3');
    sync.observeServer(server.view());
    await sync.flush();

    await sync.keepServer('i1');

    expect(sync.getSnapshot()).toMatchObject({ conflicts: [], local: {} });
    expect(await store.listPending(SESSION)).toEqual([]);
    await expect(sync.flush()).resolves.toBe('saved');
    expect(server.view().responses.find((r) => r.itemId === 'i1')?.optionId).toBe('o3');
  });

  it("never mistakes this phone's own save for a change made elsewhere", async () => {
    const server = fakeServer();
    const release: (() => void)[] = [];
    const save = server.saveAnswers.getMockImplementation();
    server.saveAnswers.mockImplementation(
      (id, list) => new Promise((resolve) => release.push(() => resolve(save?.(id, list) ?? server.view()))),
    );
    const { sync } = setup({ server, deps: { delayMs: 60_000 } });
    sync.observeServer(server.view());
    await wait();

    await sync.choose('i1', 'o1');
    const first = sync.flush();
    await wait(0);
    // Changed again while the first answer is on its way.
    await sync.choose('i1', 'o2');
    release[0]?.();
    await wait(0);
    release[1]?.();

    await expect(first).resolves.toBe('saved');
    expect(sync.getSnapshot().conflicts).toEqual([]);
    expect(server.view().responses[0]?.optionId).toBe('o2');
  });
});

describe('AnswerSync with the server as it really answers', () => {
  it('never sees a conflict where only the precision of the server time differs between a save and a read', async () => {
    const store = createMemoryDraftStore();
    const server = fakeServer();
    const first = setup({ store, server });
    first.sync.observeServer(server.view(true));
    await wait();
    await first.sync.choose('i1', 'o1');
    await first.sync.flush();
    // Changed again, and the app is closed before it is sent.
    await first.sync.choose('i1', 'o2');
    first.sync.dispose();

    const { sync } = setup({ store, server });
    await expect(sync.flush()).resolves.toBe('saved');

    expect(sync.getSnapshot().conflicts).toEqual([]);
    expect(server.view().responses[0]?.optionId).toBe('o2');
  });

  it('sends an option chosen again over a newer answer from elsewhere, rather than keeping the newer one silently', async () => {
    const { sync, server } = setup();
    sync.observeServer(server.view(true));
    await wait();
    await sync.choose('i1', 'o1');
    await sync.flush();
    server.changeElsewhere('i1', 'o2');
    sync.observeServer(server.view(true));

    await sync.choose('i1', 'o1');
    await expect(sync.flush()).resolves.toBe('saved');

    expect(server.view().responses[0]?.optionId).toBe('o1');
  });

  it('runs one save at a time even when flushes are asked for together', async () => {
    const { sync, server } = setup({ deps: { delayMs: 60_000 } });
    sync.observeServer(server.view(true));
    await wait();
    void sync.choose('i1', 'o1');

    const outcomes = await Promise.all([sync.flush(), sync.flush(), sync.flush()]);

    expect(outcomes).toEqual(['saved', 'saved', 'saved']);
    expect(server.saveAnswers).toHaveBeenCalledTimes(1);
  });
});

describe('AnswerSync failures', () => {
  it('retries a lost connection with growing, jittered delays, then stops and waits for the person', async () => {
    const { sync, store, server } = setup();
    server.saveAnswers.mockRejectedValue(new ApiError('network'));
    sync.observeServer(server.view());
    await wait();
    await sync.choose('i1', 'o1');

    await expect(sync.flush()).resolves.toBe('retrying');
    expect(sync.getSnapshot()).toMatchObject({ phase: 'waiting', retryAt: expect.any(Number) });
    // random 0.5: 20 ms, then 40 ms (ceilings 40 and 80), then the attempts are used up.
    await wait(25);
    expect(server.saveAnswers).toHaveBeenCalledTimes(2);
    await wait(45);
    expect(server.saveAnswers).toHaveBeenCalledTimes(3);
    await wait(45);
    expect(server.saveAnswers).toHaveBeenCalledTimes(4);
    await wait(100);

    expect(server.saveAnswers).toHaveBeenCalledTimes(4);
    expect(sync.getSnapshot().phase).toBe('failed');
    expect(await optionIds(store)).toEqual(['o1']);
  });

  it('sends nothing while offline, and keeps the answers on the phone', async () => {
    const { sync, store, server, connectivity } = setup();
    sync.observeServer(server.view());
    await wait();
    connectivity.set('offline');

    await sync.choose('i1', 'o1');
    await expect(sync.flush()).resolves.toBe('offline');

    expect(server.saveAnswers).not.toHaveBeenCalled();
    expect(sync.getSnapshot().phase).toBe('offline');
    expect(await optionIds(store)).toEqual(['o1']);
  });

  it.each([
    ['an expired sign-in', new ApiError('unauthorized', 401), 'unauthorized'],
    ['missing consent', new ApiError('forbidden', 403, 'sessions.participation_consent_required'), 'consent_required'],
  ] as const)('keeps drafts after %s and does not retry on its own', async (_name, error, outcome) => {
    const { sync, store, server } = setup();
    server.saveAnswers.mockRejectedValue(error);
    sync.observeServer(server.view());
    await wait();
    await sync.choose('i1', 'o1');

    await expect(sync.flush()).resolves.toBe(outcome);
    await wait(200);

    expect(server.saveAnswers).toHaveBeenCalledTimes(1);
    expect(sync.getSnapshot().phase).toBe('failed');
    expect(await optionIds(store)).toEqual(['o1']);
  });

  it.each([
    ['a temporary conflict', new ApiError('conflict', 409, 'persistence.concurrency_conflict')],
    ['a server fault', new ApiError('server', 503)],
    ['a rate limit', new ApiError('rate_limited', 429, 'requests.rate_limited')],
  ])('retries after %s', async (_name, error) => {
    const { sync, server } = setup();
    server.saveAnswers.mockRejectedValueOnce(error);
    sync.observeServer(server.view());
    await wait();
    await sync.choose('i1', 'o1');

    await expect(sync.flush()).resolves.toBe('retrying');
    await wait(60);

    expect(server.saveAnswers).toHaveBeenCalledTimes(2);
    expect(sync.getSnapshot().phase).toBe('synced');
  });

  it("removes answers the server refuses for good, marks those questions to choose again, and doesn't block", async () => {
    const { sync, store, server, onRejected } = setup();
    server.saveAnswers.mockRejectedValueOnce(new ApiError('bad_request', 400, 'sessions.option_not_in_item'));
    sync.observeServer(server.view());
    await wait();
    await sync.choose('i1', 'stale-option');

    await expect(sync.flush()).resolves.toBe('rejected');

    expect(sync.getSnapshot()).toMatchObject({ local: {}, phase: 'rejected', rejected: ['i1'] });
    expect(await store.listPending(SESSION)).toEqual([]);
    expect(onRejected).toHaveBeenCalledWith(SESSION);
    await expect(sync.flush()).resolves.toBe('saved');
  });

  it('catches an answer that could never be accepted before sending it', async () => {
    const { sync, server } = setup();
    const items = [
      { id: 'i1', options: [{ id: 'o1' }, { id: 'o2' }], itemType: 'single_choice' },
    ] as unknown as PublishedItem[];
    sync.observeQuestions(items);
    sync.observeServer(server.view());
    await wait();
    await sync.choose('i1', 'o9');
    await sync.choose('i9', 'o1');

    await sync.flush();

    expect(server.saveAnswers).not.toHaveBeenCalled();
    expect([...sync.getSnapshot().rejected].sort()).toEqual(['i1', 'i9']);
  });

  it('says how many answers could not be added once the session was submitted elsewhere', async () => {
    const { sync, store, server, onRejected } = setup();
    server.saveAnswers.mockRejectedValue(new ApiError('conflict', 409, 'sessions.not_in_progress'));
    sync.observeServer(server.view());
    await wait();
    await sync.choose('i1', 'o1');
    await sync.choose('i2', 'o1');

    await expect(sync.flush()).resolves.toBe('closed');

    expect(sync.getSnapshot()).toMatchObject({ phase: 'closed', discarded: 2, local: {} });
    expect(await store.listPending(SESSION)).toEqual([]);
    expect(onRejected).toHaveBeenCalledWith(SESSION);
  });

  it('treats an expired assignment as closed: the answers are removed and counted, and nothing is sent again', async () => {
    const { sync, store, server, onRejected } = setup();
    server.saveAnswers.mockRejectedValue(new ApiError('conflict', 409, 'assignments.assignment_expired'));
    sync.observeServer(server.view());
    await wait();
    await sync.choose('i1', 'o1');
    await sync.choose('i2', 'o1');

    await expect(sync.flush()).resolves.toBe('closed');

    expect(sync.getSnapshot()).toMatchObject({ phase: 'closed', discarded: 2, local: {}, retryAt: null });
    expect(sync.getSnapshot().error).toMatchObject({ code: 'assignments.assignment_expired' });
    expect(await store.listPending(SESSION)).toEqual([]);
    expect(onRejected).toHaveBeenCalledWith(SESSION);
    // No backoff runs, and a later choice is removed the same way without asking the server again.
    await wait(RETRY.maxMs * 2);
    await sync.choose('i3', 'o1');
    await expect(sync.flush()).resolves.toBe('closed');
    expect(sync.getSnapshot()).toMatchObject({ phase: 'closed', discarded: 3, local: {} });
    expect(await store.listPending(SESSION)).toEqual([]);
    expect(server.saveAnswers).toHaveBeenCalledTimes(1);
  });

  it('removes everything about a session that is no longer available to this person', async () => {
    const store = createMemoryDraftStore();
    const { sync, server } = setup({ store });
    await store.queueSubmission(SESSION, 'k1', new Date());
    server.saveAnswers.mockRejectedValue(new ApiError('not_found', 404, 'sessions.session_not_found'));
    sync.observeServer(server.view());
    await wait();
    await sync.choose('i1', 'o1');

    await expect(sync.flush()).resolves.toBe('gone');

    expect(sync.getSnapshot()).toMatchObject({ phase: 'gone', discarded: 1 });
    expect(await store.listPending(SESSION)).toEqual([]);
    expect(await store.getSubmission(SESSION)).toBeNull();
  });

  it('reports answers on the phone whose key was lost, instead of failing', async () => {
    const phone = await createDeviceDrafts();
    await phone.drafts.saveAnswer({ sessionId: SESSION, itemId: 'i1', optionId: 'o1', baseModifiedAt: null, at: new Date() });
    await phone.secrets.remove('azwah.drafts.key.v1');
    const { sync } = setup({ store: phone.restart().drafts });

    await wait();

    expect(sync.getSnapshot()).toMatchObject({ ready: true, lost: 1, local: {} });
    phone.close();
  });

  it('undoes a choice it could not store', async () => {
    const store = createMemoryDraftStore();
    jest.spyOn(store, 'saveAnswer').mockRejectedValueOnce(new Error('disk full'));
    const { sync, server } = setup({ store });
    await wait();

    await sync.choose('i1', 'o1');

    expect(sync.getSnapshot()).toMatchObject({ local: {}, phase: 'failed' });
    await wait();
    expect(server.saveAnswers).not.toHaveBeenCalled();
  });

  it('keeps a newer choice when an older one for the same item fails to store', async () => {
    const store = createMemoryDraftStore();
    let failFirst: (error: Error) => void = () => undefined;
    const original = store.saveAnswer.bind(store);
    jest
      .spyOn(store, 'saveAnswer')
      .mockImplementationOnce(() => new Promise((_resolve, reject) => (failFirst = reject)))
      .mockImplementation(original);
    const { sync } = setup({ store, deps: { delayMs: 60_000 } });
    await wait();

    const first = sync.choose('i1', 'A');
    await sync.choose('i1', 'B');
    failFirst(new Error('disk busy'));
    await first;

    expect(sync.getSnapshot().local).toEqual({ i1: 'B' });
    expect(await optionIds(store)).toEqual(['B']);
  });

  it('is ready, with a failure shown, when the drafts cannot be read', async () => {
    const store = createMemoryDraftStore();
    jest.spyOn(store, 'listPending').mockRejectedValueOnce(new Error('locked'));
    const { sync } = setup({ store });

    await wait();

    expect(sync.getSnapshot()).toMatchObject({ ready: true, phase: 'failed' });
  });
});

describe('AssessmentEngine', () => {
  const engineFor = (store: DraftStore = createMemoryDraftStore(), server: Server = fakeServer()) => {
    const connectivity = createFakeConnectivity();
    const engine = new AssessmentEngine({
      store,
      api: { ...server, submitSession: jest.fn() },
      connectivity,
      onSaved: jest.fn(),
      onRejected: jest.fn(),
      onSubmitted: jest.fn(),
      delayMs: DELAY,
      retry: RETRY,
      random: () => 0.5,
    });
    engine.start();
    return { engine, connectivity, server, store };
  };

  it('gives every screen of a session the same sync until it is released', () => {
    const { engine } = engineFor();
    const first = engine.syncFor(SESSION);

    expect(engine.syncFor(SESSION)).toBe(first);
    expect(engine.syncFor('s2')).not.toBe(first);
    engine.release(SESSION);
    expect(engine.syncFor(SESSION)).not.toBe(first);
    engine.dispose();
  });

  it('forgets a session once nothing uses it, but not while a screen re-subscribes at once', async () => {
    const { engine } = engineFor();
    const sync = engine.syncFor(SESSION);
    await wait();

    const first = sync.subscribe(() => undefined);
    first();
    const second = sync.subscribe(() => undefined);
    await wait();
    expect(engine.syncFor(SESSION)).toBe(sync);

    second();
    await wait();
    expect(engine.syncFor(SESSION)).not.toBe(sync);
    engine.dispose();
  });

  it('sends answers kept offline as soon as the connection returns', async () => {
    const { engine, connectivity, server, store } = engineFor();
    const sync = engine.syncFor(SESSION);
    const stay = sync.subscribe(() => undefined);
    sync.observeServer(server.view());
    connectivity.set('offline');
    await sync.choose('i1', 'o1');
    await wait();
    expect(server.saveAnswers).not.toHaveBeenCalled();

    connectivity.set('online');
    await wait();

    expect(server.saveAnswers).toHaveBeenCalledWith(SESSION, [{ itemId: 'i1', optionId: 'o1' }]);
    expect(await store.listPending(SESSION)).toEqual([]);
    stay();
    engine.dispose();
  });

  it('reads the server again before sending answers changed offline, so a change made elsewhere meanwhile is a conflict', async () => {
    const { engine, connectivity, server } = engineFor();
    const sync = engine.syncFor(SESSION);
    const stay = sync.subscribe(() => undefined);
    sync.observeServer(server.view(true));
    await sync.choose('i1', 'o1');
    await sync.flush();
    const reads = server.getAnswers.mock.calls.length;

    connectivity.set('offline');
    await sync.choose('i1', 'o2');
    server.changeElsewhere('i1', 'o3');
    connectivity.set('online');
    await wait();

    expect(server.getAnswers.mock.calls.length).toBeGreaterThan(reads);
    expect(sync.getSnapshot().conflicts).toEqual([expect.objectContaining({ itemId: 'i1', local: 'o2', server: 'o3' })]);
    expect(server.view().responses[0]?.optionId).toBe('o3');
    stay();
    engine.dispose();
  });

  it('sends answers made offline when the connection returns, even after their screen was closed', async () => {
    const { engine, connectivity, server, store } = engineFor();
    const sync = engine.syncFor(SESSION);
    const leave = sync.subscribe(() => undefined);
    sync.observeServer(server.view(true));
    connectivity.set('offline');
    await sync.choose('i1', 'o1');
    leave();
    await wait();

    connectivity.set('online');
    await wait();

    expect(server.saveAnswers).toHaveBeenCalledWith(SESSION, [{ itemId: 'i1', optionId: 'o1' }]);
    expect(await store.listPending(SESSION)).toEqual([]);
    engine.dispose();
  });

  it('sends answers left unsent by an earlier run as soon as it starts, without the screen being opened', async () => {
    const store = createMemoryDraftStore();
    await store.saveAnswer({ sessionId: SESSION, itemId: 'i1', optionId: 'o1', baseModifiedAt: null, at: new Date() });

    const { engine, server } = engineFor(store);
    await wait();

    expect(server.saveAnswers).toHaveBeenCalledWith(SESSION, [{ itemId: 'i1', optionId: 'o1' }]);
    expect(await store.listPending(SESSION)).toEqual([]);
    engine.dispose();
  });

  it('waits for writes still running before sign-out deletes the drafts', async () => {
    const store = createMemoryDraftStore();
    let finishWrite: () => void = () => undefined;
    const original = store.saveAnswer.bind(store);
    jest
      .spyOn(store, 'saveAnswer')
      .mockImplementationOnce((answer) => new Promise((resolve) => (finishWrite = () => resolve(original(answer)))));
    const { engine } = engineFor(store);
    const sync = engine.syncFor(SESSION);
    await wait();

    void sync.choose('i1', 'o1');
    let settled = false;
    const waiting = engine.settle().then(() => (settled = true));
    await wait();
    expect(settled).toBe(false);

    finishWrite();
    await waiting;
    expect(settled).toBe(true);
    engine.dispose();
  });
});
