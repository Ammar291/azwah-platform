import { ApiError } from '@/api/client';
import type { ParticipantApi } from '@/api/participant/participantApi';
import { createMemoryDraftStore } from '@/drafts/memoryDraftStore';
import type { DraftStore } from '@/drafts/types';
import { createDeviceDrafts } from '@/test-utils/deviceDrafts';
import { createFakeConnectivity, type FakeConnectivity } from '@/test-utils/fakeConnectivity';
import { leadSession, SECTION_A } from '@/test-utils/sessionWorlds';

import { AssessmentEngine } from '../assessmentEngine';

const KEY = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

function engineFor(
  api: ParticipantApi,
  store: DraftStore = createMemoryDraftStore(),
  connectivity: FakeConnectivity = createFakeConnectivity(),
  paused = false,
  delayMs = 20,
) {
  const onSubmitted = jest.fn();
  const engine = new AssessmentEngine({
    store,
    api,
    connectivity,
    onSaved: jest.fn(),
    onRejected: jest.fn(),
    onSubmitted,
    paused,
    delayMs,
    retry: { baseMs: 40, maxMs: 80, maxAttempts: 3 },
    random: () => 0.5,
  });
  engine.start();
  return { engine, store, connectivity, onSubmitted };
}

/** Answers the two open required questions on the phone, as the person would. */
async function answerTheRest(engine: AssessmentEngine, sessionId: string) {
  const sync = engine.syncFor(sessionId);
  for (const item of SECTION_A.slice(2)) {
    await sync.choose(item.id, item.options[0]?.id ?? '');
  }
}

const wait = (ms = 60) => new Promise((resolve) => setTimeout(resolve, ms));

describe('submitting', () => {
  it('sends the answers first, then submits once with an Idempotency-Key, and only then says submitted', async () => {
    const world = await leadSession();
    const { engine, store, onSubmitted } = engineFor(world.api);
    await answerTheRest(engine, world.sessionId);

    const submitting = engine.submissions.submit(world.sessionId);
    expect(engine.submissions.getState(world.sessionId).status).toBe('sending');
    await submitting;

    expect(world.calls.map((call) => call.method)).toEqual(expect.arrayContaining(['saveAnswers', 'submitSession']));
    expect(world.calls.findIndex((c) => c.method === 'saveAnswers')).toBeLessThan(
      world.calls.findIndex((c) => c.method === 'submitSession'),
    );
    expect(world.count('submitSession')).toBe(1);
    expect(world.argsOf('submitSession')[0]?.[1]).toMatch(KEY);
    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'submitted', outcome: 'submitted' });
    expect(onSubmitted).toHaveBeenCalledWith(world.sessionId);
    expect(await store.listPending(world.sessionId)).toEqual([]);
    expect(await store.getSubmission(world.sessionId)).toBeNull();
    engine.dispose();
  });

  it('submits once however often it is asked at the same time', async () => {
    const world = await leadSession();
    const { engine } = engineFor(world.api);
    await answerTheRest(engine, world.sessionId);

    await Promise.all([1, 2, 3].map(() => engine.submissions.submit(world.sessionId)));
    await engine.submissions.submit(world.sessionId);

    expect(world.count('submitSession')).toBe(1);
    engine.dispose();
  });

  it('keeps the key when the answer to a submission is lost, so the retry cannot submit twice', async () => {
    const world = await leadSession();
    const { engine } = engineFor(world.api);
    await answerTheRest(engine, world.sessionId);
    world.lose('submitSession');

    await engine.submissions.submit(world.sessionId);
    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'queued', reason: 'retrying' });
    await wait(40);

    const [first, second] = world.argsOf('submitSession');
    expect(world.count('submitSession')).toBe(2);
    expect(second?.[1]).toBe(first?.[1]);
    // The server saw the same key and answered as it did the first time.
    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'submitted', outcome: 'submitted' });
    engine.dispose();
  });
});

describe('submitting offline', () => {
  it('queues the submission on the phone, says it is not submitted, and sends it when the connection returns', async () => {
    const world = await leadSession();
    const phone = await createDeviceDrafts();
    const { engine, connectivity } = engineFor(world.api, phone.drafts);
    await answerTheRest(engine, world.sessionId);
    connectivity.set('offline');

    await engine.submissions.submit(world.sessionId);

    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'queued', reason: 'offline' });
    expect(world.count('submitSession')).toBe(0);
    expect(await phone.drafts.getSubmission(world.sessionId)).toMatchObject({ idempotencyKey: expect.stringMatching(KEY) });
    expect(await phone.drafts.listPending(world.sessionId)).toHaveLength(2);

    connectivity.set('online');
    await wait();

    expect(world.count('submitSession')).toBe(1);
    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'submitted' });
    expect(await phone.drafts.getSubmission(world.sessionId)).toBeNull();
    engine.dispose();
    phone.close();
  });

  it('submits a queued request after the app restarts, with the key it was given before', async () => {
    const world = await leadSession();
    const phone = await createDeviceDrafts();
    const before = engineFor(world.api, phone.drafts, createFakeConnectivity('offline'));
    await answerTheRest(before.engine, world.sessionId);
    await before.engine.submissions.submit(world.sessionId);
    const { idempotencyKey } = (await phone.drafts.getSubmission(world.sessionId)) ?? {};
    before.engine.dispose();

    // The app opens again, online, and the person signs in: queued requests are processed.
    const after = engineFor(world.api, phone.restart().drafts, createFakeConnectivity('online'), true);
    after.engine.resume();
    await wait();

    expect(world.argsOf('submitSession')).toEqual([[world.sessionId, idempotencyKey]]);
    expect(after.engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'submitted' });
    expect(await world.api.getSession(world.sessionId)).toMatchObject({ status: 'submitted' });
    after.engine.dispose();
    phone.close();
  });

  it('waits for sign-in while signed out, and keeps the request', async () => {
    const world = await leadSession();
    const { engine, store } = engineFor(world.api, createMemoryDraftStore(), createFakeConnectivity(), true);
    await answerTheRest(engine, world.sessionId);

    await engine.submissions.submit(world.sessionId);

    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'queued', reason: 'signed_out' });
    expect(world.count('submitSession')).toBe(0);
    engine.resume();
    await wait();
    expect(world.count('submitSession')).toBe(1);
    expect(await store.getSubmission(world.sessionId)).toBeNull();
    engine.dispose();
  });

  it('can be cancelled while it waits, so answers can change again', async () => {
    const world = await leadSession();
    const { engine, store } = engineFor(world.api, createMemoryDraftStore(), createFakeConnectivity('offline'));
    await answerTheRest(engine, world.sessionId);
    await engine.submissions.submit(world.sessionId);

    await expect(engine.submissions.cancel(world.sessionId)).resolves.toBe(true);

    expect(engine.submissions.getState(world.sessionId).status).toBe('idle');
    expect(await store.getSubmission(world.sessionId)).toBeNull();
    expect(await store.listPending(world.sessionId)).toHaveLength(2);
    engine.dispose();
  });
});

describe('submission outcomes', () => {
  it('stops for conflicts and sends nothing', async () => {
    const world = await leadSession();
    // No save before the other phone's change: the drafts must still be unsent when it lands.
    const { engine } = engineFor(world.api, createMemoryDraftStore(), createFakeConnectivity(), false, 60_000);
    const sync = engine.syncFor(world.sessionId);
    sync.observeServer(await world.api.getAnswers(world.sessionId));
    await answerTheRest(engine, world.sessionId);
    // Another phone answers the same question differently in the meantime.
    const third = SECTION_A[2];
    await world.api.saveAnswers(world.sessionId, [{ itemId: third?.id ?? '', optionId: third?.options[3]?.id ?? '' }]);
    sync.observeServer(await world.api.getAnswers(world.sessionId));

    await engine.submissions.submit(world.sessionId);

    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'blocked', reason: 'conflicts' });
    expect(world.count('submitSession')).toBe(0);
    engine.dispose();
  });

  it('says how many changes were not included when the session was already submitted elsewhere', async () => {
    const world = await leadSession();
    const { engine } = engineFor(world.api);
    await world.api.saveAnswers(
      world.sessionId,
      SECTION_A.map((item) => ({ itemId: item.id, optionId: item.options[1]?.id ?? '' })),
    );
    await world.api.submitSession(world.sessionId, 'another-phone');
    await answerTheRest(engine, world.sessionId);

    await engine.submissions.submit(world.sessionId);

    expect(engine.submissions.getState(world.sessionId)).toMatchObject({
      status: 'submitted',
      outcome: 'already_submitted',
      changesNotIncluded: 2,
    });
    engine.dispose();
  });

  it.each([
    ['missing consent', new ApiError('forbidden', 403, 'sessions.participation_consent_required'), 'consent_required'],
    ['missing answers', new ApiError('conflict', 409, 'sessions.required_items_unanswered'), 'missing_answers'],
    ['a session closed before it was submitted', new ApiError('conflict', 409, 'sessions.not_in_progress'), 'closed'],
    ['an assignment whose time to answer has passed', new ApiError('conflict', 409, 'assignments.assignment_expired'), 'expired'],
    ['a session no longer available', new ApiError('not_found', 404, 'sessions.session_not_found'), 'not_available'],
    ['an unexpected refusal', new ApiError('bad_request', 400), 'refused'],
  ] as const)('stops with a reason after %s', async (_name, error, reason) => {
    const world = await leadSession();
    const { engine, store } = engineFor(world.api);
    await answerTheRest(engine, world.sessionId);
    world.fail('submitSession', error);

    await engine.submissions.submit(world.sessionId);

    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'blocked', reason });
    expect(await store.getSubmission(world.sessionId)).toBeNull();
    engine.dispose();
  });

  it('stops without submitting when saving the answers finds the time to answer has passed, and never retries', async () => {
    const world = await leadSession();
    const phone = await createDeviceDrafts();
    world.fail('saveAnswers', new ApiError('conflict', 409, 'assignments.assignment_expired'));
    const { engine } = engineFor(world.api, phone.drafts);
    await answerTheRest(engine, world.sessionId);

    await engine.submissions.submit(world.sessionId);
    await wait(200);

    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'blocked', reason: 'expired' });
    expect(world.count('saveAnswers')).toBe(1);
    expect(world.count('submitSession')).toBe(0);
    expect(await phone.drafts.getSubmission(world.sessionId)).toBeNull();
    expect(await phone.drafts.listPending(world.sessionId)).toEqual([]);
    engine.dispose();
    phone.close();
  });

  it('stops a submission queued offline once the server says the time to answer has passed', async () => {
    const world = await leadSession();
    const phone = await createDeviceDrafts();
    const { engine, connectivity } = engineFor(world.api, phone.drafts, createFakeConnectivity('offline'));
    await answerTheRest(engine, world.sessionId);
    await engine.submissions.submit(world.sessionId);
    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'queued', reason: 'offline' });
    world.fail('saveAnswers', new ApiError('conflict', 409, 'assignments.assignment_expired'));

    connectivity.set('online');
    await wait(200);

    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'blocked', reason: 'expired' });
    expect(world.count('submitSession')).toBe(0);
    expect(await phone.drafts.getSubmission(world.sessionId)).toBeNull();
    expect(await phone.drafts.listPending(world.sessionId)).toEqual([]);
    engine.wake();
    await wait(200);
    expect(world.count('saveAnswers')).toBe(1);
    expect(world.count('submitSession')).toBe(0);
    engine.dispose();
    phone.close();
  });

  it('keeps the request for the same person after an expired sign-in', async () => {
    const world = await leadSession();
    const { engine, store } = engineFor(world.api);
    await answerTheRest(engine, world.sessionId);
    world.fail('submitSession', new ApiError('unauthorized', 401));

    await engine.submissions.submit(world.sessionId);

    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'queued', reason: 'signed_out' });
    expect(await store.getSubmission(world.sessionId)).not.toBeNull();
    engine.dispose();
  });

  it.each([
    ['a server fault', new ApiError('server', 503)],
    ['a rate limit', new ApiError('rate_limited', 429, 'requests.rate_limited')],
  ])('retries %s with backoff and submits', async (_name, error) => {
    const world = await leadSession();
    const { engine } = engineFor(world.api);
    await answerTheRest(engine, world.sessionId);
    world.fail('submitSession', error);

    await engine.submissions.submit(world.sessionId);
    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'queued', reason: 'retrying' });
    await wait();

    expect(world.count('submitSession')).toBe(2);
    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'submitted' });
    engine.dispose();
  });

  it('makes one new key when the server says the key was used for something else', async () => {
    const world = await leadSession();
    const { engine } = engineFor(world.api);
    await answerTheRest(engine, world.sessionId);
    world.fail('submitSession', new ApiError('conflict', 409, 'requests.idempotency_key_reused'));

    await engine.submissions.submit(world.sessionId);

    const [first, second] = world.argsOf('submitSession');
    expect(second?.[1]).not.toBe(first?.[1]);
    expect(engine.submissions.getState(world.sessionId)).toMatchObject({ status: 'submitted' });
    engine.dispose();
  });
});
