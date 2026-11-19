import type { QueryClient } from '@tanstack/react-query';
import { AppState } from 'react-native';

import type { ParticipantApi } from '@/api/participant/participantApi';
import { invalidateParticipantData, participantKeys } from '@/api/participant/queries';
import type { SessionAnswers } from '@/api/participant/schemas';
import type { DraftStore } from '@/drafts/types';
import type { Connectivity, ConnectionState } from '@/network/connectivity';

import { AnswerSync } from './answerSync';
import type { RetryPolicy } from './retryPolicy';
import { SubmissionQueue } from './submissionQueue';

/** Tells the engine when the app comes back to the foreground. */
export interface AppActivity {
  subscribe(onActive: () => void): () => void;
}

export function createAppStateActivity(): AppActivity {
  return {
    subscribe(onActive) {
      const subscription = AppState.addEventListener('change', (state) => {
        if (state === 'active') onActive();
      });
      return () => subscription.remove();
    },
  };
}

export interface AssessmentEngineDependencies {
  store: DraftStore;
  api: Pick<ParticipantApi, 'saveAnswers' | 'getAnswers' | 'submitSession'>;
  connectivity: Connectivity;
  activity?: AppActivity;
  onSaved: (sessionId: string, answers: SessionAnswers) => void;
  onRejected: (sessionId: string) => void;
  onSubmitted: (sessionId: string) => void;
  /** Nothing is sent until `resume()`: the app starts paused until the drafts are confirmed as the signed-in person's. */
  paused?: boolean;
  delayMs?: number;
  retry?: RetryPolicy;
  random?: () => number;
  now?: () => Date;
}

/**
 * Everything that moves answers from this phone to the server: one AnswerSync per session (shared by every screen and
 * the submission queue) and the queue itself. Retries start again when the connection returns or the app comes to
 * the foreground; nothing is sent while paused (signed out).
 */
export class AssessmentEngine {
  readonly store: DraftStore;
  readonly connectivity: Connectivity;
  readonly submissions: SubmissionQueue;
  private readonly syncs = new Map<string, AnswerSync>();
  private stopListening: (() => void)[] = [];
  private connection: ConnectionState;
  private paused: boolean;

  constructor(private readonly deps: AssessmentEngineDependencies) {
    this.store = deps.store;
    this.connectivity = deps.connectivity;
    this.connection = deps.connectivity.getState();
    this.paused = deps.paused ?? false;
    this.submissions = new SubmissionQueue(
      {
        store: deps.store,
        api: deps.api,
        connectivity: deps.connectivity,
        syncFor: (sessionId) => this.syncFor(sessionId),
        release: (sessionId) => this.release(sessionId),
        onSubmitted: deps.onSubmitted,
        retry: deps.retry,
        random: deps.random,
        now: deps.now,
      },
      { paused: this.paused },
    );
  }

  /** The shared sync for a session, created on first use and let go once nothing uses it (its drafts stay). */
  syncFor(sessionId: string): AnswerSync {
    let sync = this.syncs.get(sessionId);
    if (!sync) {
      const created: AnswerSync = new AnswerSync(
        sessionId,
        {
          store: this.deps.store,
          api: this.deps.api,
          connectivity: this.deps.connectivity,
          onSaved: this.deps.onSaved,
          onRejected: this.deps.onRejected,
          delayMs: this.deps.delayMs,
          retry: this.deps.retry,
          random: this.deps.random,
          now: this.deps.now,
        },
        () => {
          if (this.syncs.get(sessionId) === created) {
            this.syncs.delete(sessionId);
            created.dispose();
          }
        },
      );
      this.syncs.set(sessionId, created);
      sync = created;
    }
    return sync;
  }

  /** The session's sync if one is running; never creates one. */
  existingSync(sessionId: string): AnswerSync | undefined {
    return this.syncs.get(sessionId);
  }

  /** Forgets one session's sync (after submission) or all of them (sign-out). Their drafts stay in the store. */
  release(sessionId?: string): void {
    for (const [id, sync] of [...this.syncs]) {
      if (sessionId === undefined || id === sessionId) {
        sync.dispose();
        this.syncs.delete(id);
      }
    }
  }

  /**
   * The server submitted the session itself (a demonstration's one tap, D-143): nothing about it needs to stay on this
   * phone, and every participant read is refetched, as after a submission from the queue.
   */
  async submittedOnServer(sessionId: string): Promise<void> {
    this.release(sessionId);
    await this.deps.store.discardSession(sessionId).catch(() => undefined);
    this.deps.onSubmitted(sessionId);
  }

  /** Resolves once every session's writes and saves have finished. */
  async settle(): Promise<void> {
    await Promise.all([...this.syncs.values()].map((sync) => sync.settled()));
  }

  /** Listens for the connection and the foreground. Safe to call again after `stop()`. */
  start(): void {
    this.stop();
    this.stopListening.push(
      this.connectivity.subscribe(() => {
        const next = this.connectivity.getState();
        const cameBack = this.connection === 'offline' && next !== 'offline';
        this.connection = next;
        if (next === 'offline') {
          // Changes made elsewhere while offline go unseen: each sync reads the server again before it sends.
          this.syncs.forEach((sync) => sync.markStale());
        }
        if (cameBack) this.wake();
      }),
    );
    if (this.deps.activity) {
      this.stopListening.push(this.deps.activity.subscribe(() => this.wake()));
    }
    if (!this.paused) {
      void this.submissions.resume();
      void this.sendStoredDrafts();
    }
  }

  stop(): void {
    this.stopListening.forEach((stop) => stop());
    this.stopListening = [];
  }

  /** Signed out: no sync or submission runs until `resume()`. Drafts and queued submissions stay stored. */
  pause(): void {
    this.paused = true;
    this.submissions.pause();
    this.release();
  }

  /** Signed in, with the drafts confirmed as this person's: queued submissions are tried, unsent answers sent. */
  resume(): void {
    this.paused = false;
    void this.submissions.resume();
    void this.sendStoredDrafts();
  }

  /** Tries waiting work now: the connection returned or the app came back to the foreground. */
  wake(): void {
    if (this.paused) return;
    this.syncs.forEach((sync) => sync.wake());
    void this.sendStoredDrafts();
    this.submissions.wake();
  }

  /** Forgets every sync and every submission state, before the local data is deleted (sign-out, another account). */
  reset(): void {
    this.release();
    this.submissions.reset();
  }

  /**
   * Answers left unsent by a session whose screen is closed, or by an earlier run of the app, are sent too: a sync
   * is started for each such session (it reads the server first, so conflicts are still caught).
   */
  private async sendStoredDrafts(): Promise<void> {
    let sessionIds: string[];
    try {
      sessionIds = await this.deps.store.listSessionsWithPending();
    } catch {
      return;
    }
    if (this.paused) return;
    for (const sessionId of sessionIds) {
      if (!this.syncs.has(sessionId)) {
        this.syncFor(sessionId);
      }
    }
  }

  dispose(): void {
    this.stop();
    this.release();
    this.submissions.dispose();
  }
}

/** The app's engine: saves and refusals keep TanStack Query's cached reads in step with the server. */
export function createAppAssessmentEngine(
  services: { drafts: DraftStore; api: ParticipantApi; connectivity: Connectivity; activity?: AppActivity },
  queryClient: QueryClient,
  options: Pick<AssessmentEngineDependencies, 'paused' | 'delayMs' | 'retry' | 'random' | 'now'> = {},
): AssessmentEngine {
  return new AssessmentEngine({
    store: services.drafts,
    api: services.api,
    connectivity: services.connectivity,
    activity: services.activity,
    onSaved: (sessionId, answers) => queryClient.setQueryData(participantKeys.answers(sessionId), answers),
    onRejected: (sessionId) => {
      void queryClient.invalidateQueries({ queryKey: participantKeys.answers(sessionId) });
      void queryClient.invalidateQueries({ queryKey: participantKeys.session(sessionId) });
      void queryClient.invalidateQueries({ queryKey: participantKeys.localCopy(sessionId) });
    },
    onSubmitted: () => void invalidateParticipantData(queryClient),
    ...options,
  });
}
