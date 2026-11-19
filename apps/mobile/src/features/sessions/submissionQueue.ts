import { isApiError } from '@/api/client';
import type { ParticipantApi } from '@/api/participant/participantApi';
import type { Submission } from '@/api/participant/schemas';
import type { DraftStore } from '@/drafts/types';
import type { Connectivity } from '@/network/connectivity';

import { ASSIGNMENT_EXPIRED, closedByExpiry, type AnswerSync, type FlushOutcome } from './answerSync';
import { createIdempotencyKey } from './idempotencyKey';
import { DEFAULT_RETRY, retryDelay, type RetryPolicy } from './retryPolicy';

/**
 * Why a submission cannot go ahead without the person. Keys under `submission.blocked`, except `expired` (the
 * problem `assignments.assignment_expired`) and `refused` (the problem itself).
 */
export type SubmissionBlock =
  | 'conflicts'
  | 'answers_rejected'
  | 'missing_answers'
  | 'consent_required'
  | 'not_available'
  | 'closed'
  | 'expired'
  | 'refused';

/**
 * One session's submission, as the screens show it. Only `submitted` means the server has confirmed it (decision M-41).
 */
export type SubmissionState =
  | { status: 'idle' }
  | {
      status: 'queued';
      /** offline: no connection. retrying: a failure, tried again at `retryAt`. signed_out: waits for sign-in. */
      reason: 'offline' | 'retrying' | 'signed_out' | 'waiting';
      retryAt: number | null;
    }
  | { status: 'sending' }
  | {
      status: 'submitted';
      outcome: Submission['outcome'];
      submittedAt: string;
      /** Answers on this phone that could not be included because the session was already submitted. */
      changesNotIncluded: number;
    }
  | { status: 'blocked'; reason: SubmissionBlock; error: unknown };

export interface SubmissionQueueDependencies {
  store: DraftStore;
  api: Pick<ParticipantApi, 'submitSession'>;
  connectivity: Connectivity;
  syncFor: (sessionId: string) => AnswerSync;
  /** The session is finished on this phone: its sync can go. */
  release: (sessionId: string) => void;
  /** The server confirmed a submission: participant data should be refetched. */
  onSubmitted: (sessionId: string) => void;
  retry?: RetryPolicy;
  random?: () => number;
  now?: () => Date;
  newKey?: () => string;
}

const IDLE: SubmissionState = { status: 'idle' };

/** Flush outcomes that leave the submission queued for a later attempt, and what to call the wait. */
const WAIT_FOR: Partial<Record<FlushOutcome, 'offline' | 'retrying' | 'signed_out'>> = {
  offline: 'offline',
  retrying: 'retrying',
  failed: 'retrying',
  unauthorized: 'signed_out',
};

/** Flush outcomes that need the person: the submission request is dropped and they submit again when ready. */
const BLOCKED_BY: Partial<Record<FlushOutcome, SubmissionBlock>> = {
  conflicts: 'conflicts',
  rejected: 'answers_rejected',
  consent_required: 'consent_required',
  gone: 'not_available',
};

/**
 * Submissions asked for on this phone and not yet confirmed (decision M-41). A request is written with a new
 * Idempotency-Key before anything is sent, so it survives a restart (when drafts are on the phone) and every retry,
 * here or after a restart, uses the same key and the server submits once (D-54). Each request waits until the person
 * is signed in, the phone is online and every answer is saved; one attempt per session runs at a time.
 */
export class SubmissionQueue {
  private readonly states = new Map<string, SubmissionState>();
  private readonly listeners = new Set<() => void>();
  private readonly running = new Map<string, Promise<void>>();
  private readonly timers = new Map<string, ReturnType<typeof setTimeout>>();
  private readonly failures = new Map<string, number>();
  private paused: boolean;

  constructor(
    private readonly deps: SubmissionQueueDependencies,
    { paused = false }: { paused?: boolean } = {},
  ) {
    this.paused = paused;
  }

  getState = (sessionId: string): SubmissionState => this.states.get(sessionId) ?? IDLE;

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  /** Asks for the session to be submitted. A request already waiting or running is not repeated. */
  async submit(sessionId: string): Promise<void> {
    const state = this.getState(sessionId);
    if (state.status === 'sending' || state.status === 'submitted' || this.running.has(sessionId)) {
      return this.running.get(sessionId);
    }
    this.set(sessionId, { status: 'sending' });
    try {
      await this.deps.store.queueSubmission(sessionId, this.newKey(), this.now());
    } catch {
      this.set(sessionId, { status: 'blocked', reason: 'refused', error: null });
      return;
    }
    this.failures.delete(sessionId);
    return this.process(sessionId);
  }

  /** Withdraws a request that is not being sent, so the answers can be changed. False while it is being sent. */
  async cancel(sessionId: string): Promise<boolean> {
    if (this.running.has(sessionId)) {
      return false;
    }
    this.clearTimer(sessionId);
    await this.deps.store.removeSubmission(sessionId);
    this.failures.delete(sessionId);
    this.set(sessionId, IDLE);
    return true;
  }

  /** Forgets a finished or blocked state once the person has seen it. */
  dismiss(sessionId: string): void {
    const { status } = this.getState(sessionId);
    if (status === 'submitted' || status === 'blocked') {
      this.set(sessionId, IDLE);
    }
  }

  /** Signed in (and the drafts confirmed as this person's): every stored request is tried. */
  async resume(): Promise<void> {
    this.paused = false;
    let queued;
    try {
      queued = await this.deps.store.listSubmissions();
    } catch {
      return;
    }
    await Promise.all(queued.map(({ sessionId }) => this.process(sessionId)));
  }

  /** Signed out or expired: nothing is sent until someone signs in again. Requests stay stored. */
  pause(): void {
    this.paused = true;
    for (const sessionId of [...this.timers.keys()]) {
      this.clearTimer(sessionId);
    }
    for (const [sessionId, state] of this.states) {
      if (state.status === 'queued') {
        this.set(sessionId, { status: 'queued', reason: 'signed_out', retryAt: null });
      }
    }
  }

  /** The connection came back or the app came to the foreground. */
  wake(): void {
    if (this.paused) return;
    this.failures.clear();
    void this.resume();
  }

  /** Forgets every state and timer: the local data behind them is being deleted. */
  reset(): void {
    for (const sessionId of [...this.timers.keys()]) {
      this.clearTimer(sessionId);
    }
    this.failures.clear();
    this.states.clear();
    this.listeners.forEach((listener) => listener());
  }

  dispose(): void {
    this.paused = true;
    for (const sessionId of [...this.timers.keys()]) {
      this.clearTimer(sessionId);
    }
    this.listeners.clear();
  }

  private process(sessionId: string): Promise<void> {
    const current = this.running.get(sessionId);
    if (current) {
      return current;
    }
    const run = this.attempt(sessionId).finally(() => this.running.delete(sessionId));
    this.running.set(sessionId, run);
    return run;
  }

  private async attempt(sessionId: string): Promise<void> {
    const { store, api, connectivity } = this.deps;
    this.clearTimer(sessionId);
    const queued = await store.getSubmission(sessionId).catch(() => null);
    if (!queued) {
      if (this.getState(sessionId).status !== 'submitted') this.set(sessionId, IDLE);
      return;
    }
    if (this.paused) {
      this.set(sessionId, { status: 'queued', reason: 'signed_out', retryAt: null });
      return;
    }
    if (connectivity.getState() === 'offline') {
      this.set(sessionId, { status: 'queued', reason: 'offline', retryAt: null });
      return;
    }
    this.set(sessionId, { status: 'sending' });

    // Every answer first: the server submits what it has.
    const sync = this.deps.syncFor(sessionId);
    const flushed = await sync.flush();
    if (closedByExpiry(sync.getSnapshot())) {
      // The time to answer has passed: the server would refuse the submission the same way, so it is not sent.
      await this.expired(sessionId, sync.getSnapshot().error);
      return;
    }
    const changesNotIncluded = flushed === 'closed' ? sync.getSnapshot().discarded : 0;
    const wait = WAIT_FOR[flushed];
    if (wait) {
      this.set(sessionId, { status: 'queued', reason: wait, retryAt: null });
      if (wait === 'retrying') this.retryLater(sessionId);
      return;
    }
    const block = BLOCKED_BY[flushed];
    if (block) {
      if (block !== 'not_available') await store.removeSubmission(sessionId).catch(() => undefined);
      this.set(sessionId, { status: 'blocked', reason: block, error: null });
      return;
    }

    await store.recordSubmissionAttempt(sessionId, this.now()).catch(() => undefined);
    let key = queued.idempotencyKey;
    for (let tries = 0; tries < 2; tries += 1) {
      try {
        const submission = await api.submitSession(sessionId, key);
        await this.finish(sessionId, submission, changesNotIncluded);
        return;
      } catch (error) {
        if (isApiError(error) && error.code === 'requests.idempotency_key_reused' && tries === 0) {
          // The key clashed with another request (it should never happen): one fresh key, once.
          key = this.newKey();
          await store.replaceSubmissionKey(sessionId, key).catch(() => undefined);
          continue;
        }
        await this.failed(sessionId, error);
        return;
      }
    }
  }

  private async finish(sessionId: string, submission: Submission, changesNotIncluded: number): Promise<void> {
    this.failures.delete(sessionId);
    // The server has the answers: nothing about the session needs to stay on the phone. A failure here changes
    // nothing that matters, so it never turns the confirmed submission into an error.
    await this.deps.store.discardSession(sessionId).catch(() => undefined);
    this.deps.release(sessionId);
    this.set(sessionId, {
      status: 'submitted',
      outcome: submission.outcome,
      submittedAt: submission.submittedAt,
      changesNotIncluded,
    });
    this.deps.onSubmitted(sessionId);
  }

  private async failed(sessionId: string, error: unknown): Promise<void> {
    const { store, connectivity } = this.deps;
    const block = (reason: SubmissionBlock, keep = false) => {
      this.set(sessionId, { status: 'blocked', reason, error });
      return keep ? Promise.resolve() : store.removeSubmission(sessionId).catch(() => undefined);
    };
    if (!isApiError(error)) {
      await block('refused');
      return;
    }
    switch (error.kind) {
      case 'network':
      case 'timeout':
        if (connectivity.getState() === 'offline') {
          this.set(sessionId, { status: 'queued', reason: 'offline', retryAt: null });
          return;
        }
        this.retryLater(sessionId);
        return;
      case 'server':
      case 'rate_limited':
        this.retryLater(sessionId);
        return;
      case 'unauthorized':
        this.set(sessionId, { status: 'queued', reason: 'signed_out', retryAt: null });
        return;
      case 'forbidden':
        await block('consent_required');
        return;
      case 'not_found':
        await store.discardSession(sessionId).catch(() => undefined);
        this.deps.release(sessionId);
        await block('not_available', true);
        return;
      case 'conflict':
        if (error.code === 'sessions.required_items_unanswered') {
          await block('missing_answers');
        } else if (error.code === 'sessions.not_in_progress') {
          await store.discardSession(sessionId).catch(() => undefined);
          this.deps.release(sessionId);
          await block('closed', true);
        } else if (error.code === ASSIGNMENT_EXPIRED) {
          await this.expired(sessionId, error);
        } else if (error.code?.startsWith('persistence.')) {
          this.retryLater(sessionId);
        } else {
          await block('refused');
        }
        return;
      default:
        await block('refused');
    }
  }

  /**
   * The time to answer has passed (the server says `assignments.assignment_expired`, and keeps saying it): the
   * submission can never go through. Like a closed session, nothing about it stays on the phone, the request with it,
   * and nothing is retried.
   */
  private async expired(sessionId: string, error: unknown): Promise<void> {
    this.failures.delete(sessionId);
    await this.deps.store.discardSession(sessionId).catch(() => undefined);
    this.deps.release(sessionId);
    this.set(sessionId, { status: 'blocked', reason: 'expired', error });
  }

  private retryLater(sessionId: string): void {
    const failures = (this.failures.get(sessionId) ?? 0) + 1;
    this.failures.set(sessionId, failures);
    const delay = retryDelay(this.deps.retry ?? DEFAULT_RETRY, failures, this.deps.random);
    if (delay === null) {
      // Out of automatic attempts: still queued, tried again when the connection returns, the app comes back to
      // the foreground or the person taps retry.
      this.set(sessionId, { status: 'queued', reason: 'waiting', retryAt: null });
      return;
    }
    this.set(sessionId, { status: 'queued', reason: 'retrying', retryAt: Date.now() + delay });
    this.timers.set(
      sessionId,
      setTimeout(() => {
        this.timers.delete(sessionId);
        void this.process(sessionId);
      }, delay),
    );
  }

  /** Tries a waiting request now (the retry button). */
  retryNow(sessionId: string): Promise<void> {
    this.failures.delete(sessionId);
    return this.process(sessionId);
  }

  private clearTimer(sessionId: string): void {
    const timer = this.timers.get(sessionId);
    if (timer) {
      clearTimeout(timer);
      this.timers.delete(sessionId);
    }
  }

  private newKey(): string {
    return (this.deps.newKey ?? createIdempotencyKey)();
  }

  private now(): Date {
    return this.deps.now?.() ?? new Date();
  }

  private set(sessionId: string, state: SubmissionState): void {
    this.states.set(sessionId, state);
    this.listeners.forEach((listener) => listener());
  }
}
