import { isApiError } from '@/api/client';
import { MAX_ANSWERS_PER_SAVE, type ParticipantApi } from '@/api/participant/participantApi';
import type { PublishedItem, SessionAnswers } from '@/api/participant/schemas';
import type { SyncPhase } from '@/drafts/saveStatus';
import type { DraftAnswer, DraftStore, SentRevision } from '@/drafts/types';
import type { Connectivity } from '@/network/connectivity';

import { isValidAnswer, selectItems } from './questionnaire';
import { DEFAULT_RETRY, retryDelay, type RetryPolicy } from './retryPolicy';
import { sameInstant } from './timestamps';

/** How long answering pauses before unsent answers go to the server. */
export const SYNC_DELAY_MS = 800;

/** Rounds of sending when answers keep changing during a save; the next pause sends whatever is left. */
const MAX_ROUNDS = 3;

/** A draft whose question changed on the server, to another option, since the draft was chosen (decision M-40). */
export interface AnswerConflict {
  itemId: string;
  revision: number;
  /** The option chosen on this phone. */
  local: string;
  /** The option the server has now. */
  server: string;
  serverModifiedAt: string;
}

/**
 * What a flush achieved. `saved`: the server has every answer from this phone. `conflicts`: everything else is saved,
 * but some answers wait for the person to choose. The rest say why answers are still unsent or can never be sent.
 */
export type FlushOutcome =
  | 'saved'
  | 'conflicts'
  | 'offline'
  | 'retrying'
  | 'failed'
  | 'unauthorized'
  | 'consent_required'
  | 'rejected'
  | 'closed'
  | 'gone';

export interface AnswerSyncSnapshot {
  /** The local store has been read (or could not be). */
  ready: boolean;
  /** itemId → optionId for answers chosen on this phone that the server does not have yet. */
  local: Readonly<Record<string, string>>;
  /** The server's answers as last known: a read, the last save's response, or the copy kept on this phone. */
  server: SessionAnswers | null;
  conflicts: readonly AnswerConflict[];
  /** Questions whose answer here was refused or invalid and has been removed: the person chooses again. */
  rejected: readonly string[];
  /** Answers on this phone removed because they could never be sent (the session closed or became unavailable). */
  discarded: number;
  /** Answers on this phone that could not be read (their key was lost) and were removed. */
  lost: number;
  phase: SyncPhase;
  /** When the next automatic attempt runs (epoch ms), while `waiting`. */
  retryAt: number | null;
  /** When the server last accepted answers from this phone. */
  lastSyncedAt: string | null;
  /** The last failure, for its translated problem. */
  error: unknown;
}

export interface AnswerSyncDependencies {
  store: DraftStore;
  api: Pick<ParticipantApi, 'saveAnswers' | 'getAnswers'>;
  connectivity: Connectivity;
  /** The server's answers after a save or a read, to refresh cached reads. */
  onSaved: (sessionId: string, answers: SessionAnswers) => void;
  /** Answers were refused or the session ended; cached reads should be refetched. */
  onRejected: (sessionId: string) => void;
  delayMs?: number;
  retry?: RetryPolicy;
  random?: () => number;
  now?: () => Date;
}

const INITIAL: AnswerSyncSnapshot = {
  ready: false,
  local: {},
  server: null,
  conflicts: [],
  rejected: [],
  discarded: 0,
  lost: 0,
  phase: 'idle',
  retryAt: null,
  lastSyncedAt: null,
  error: null,
};

/**
 * The assignment's expiry (its wave's close) has passed: the server takes no more answers or submission for it, and
 * keeps answering this code even after maintenance has abandoned the session.
 */
export const ASSIGNMENT_EXPIRED = 'assignments.assignment_expired';

/** 409 codes meaning the session will never take answers again. Other conflicts (persistence.*) are temporary. */
const CLOSED_CODES: ReadonlySet<string> = new Set(['sessions.not_in_progress', ASSIGNMENT_EXPIRED]);

/** The server refused answers because the time to answer has passed: the session is closed for good. */
export const closedByExpiry = (snapshot: Pick<AnswerSyncSnapshot, 'phase' | 'error'>): boolean =>
  snapshot.phase === 'closed' && isApiError(snapshot.error) && snapshot.error.code === ASSIGNMENT_EXPIRED;

const entryOf = (answers: SessionAnswers | null, itemId: string) =>
  answers?.responses.find((response) => response.itemId === itemId);

const without = <T>(record: Readonly<Record<string, T>>, keys: Iterable<string>) => {
  const next = { ...record };
  for (const key of keys) delete next[key];
  return next;
};

type Classified = { same: SentRevision[]; conflicts: AnswerConflict[]; send: DraftAnswer[] };

/**
 * Sorts drafts against the server's latest answers: already there (no need to send), changed on the server since the
 * draft was chosen (a conflict, never sent over the newer answer), or to send.
 */
function classify(pending: readonly DraftAnswer[], server: SessionAnswers | null): Classified {
  const result: Classified = { same: [], conflicts: [], send: [] };
  for (const draft of pending) {
    const current = entryOf(server, draft.itemId);
    if (current && current.optionId === draft.optionId) {
      result.same.push({
        itemId: draft.itemId,
        revision: draft.revision,
        updatedAt: draft.updatedAt,
        serverModifiedAt: current.lastModifiedAt,
      });
    } else if (current && !sameInstant(current.lastModifiedAt, draft.baseModifiedAt)) {
      result.conflicts.push({
        itemId: draft.itemId,
        revision: draft.revision,
        local: draft.optionId,
        server: current.optionId,
        serverModifiedAt: current.lastModifiedAt,
      });
    } else {
      result.send.push(draft);
    }
  }
  return result;
}

/**
 * The single source of truth for one session's answers on this phone (decisions M-39, M-40). Every screen showing the
 * session, and the submission queue, share it, so saves never overlap. A choice is stored locally at once (one row
 * per question, so a later choice replaces an earlier one), then sent after a pause; only the sent revision is marked
 * synced. Failures retry with bounded, jittered backoff; nothing is sent while offline; drafts that would overwrite a
 * newer server answer are held as conflicts for the person to settle.
 */
export class AnswerSync {
  private snapshot: AnswerSyncSnapshot = INITIAL;
  private readonly listeners = new Set<() => void>();
  private readonly writes = new Set<Promise<unknown>>();
  private timer: ReturnType<typeof setTimeout> | null = null;
  /** Flushes run one after another on this chain, so two saves never overlap. */
  private tail: Promise<unknown> = Promise.resolve();
  /** A flush queued but not started yet; later callers share it instead of queueing another. */
  private next: Promise<FlushOutcome> | null = null;
  private flushes = 0;
  /**
   * The server's answers were read or returned while online and nothing since could have hidden a change: going
   * offline, a network failure or the app coming back makes them stale, and the next send reads them again first.
   */
  private serverFresh = false;
  private items: readonly PublishedItem[] | null = null;
  private failures = 0;
  /** Sign-in or consent must change before automatic sending can help. */
  private blocked = false;
  /**
   * Set once the session is known to take no more answers (submitted or closed elsewhere, or a CLOSED_CODES refusal),
   * with the refusal: nothing is sent again, and answers chosen later are removed the same way.
   */
  private closedBy: { error: unknown } | null = null;
  private disposed = false;

  constructor(
    private readonly sessionId: string,
    private readonly deps: AnswerSyncDependencies,
    /** Called once no screen uses this session and nothing is waiting, so the engine can let it go. */
    private readonly onIdle: () => void = () => undefined,
  ) {
    void this.load();
  }

  getSnapshot = (): AnswerSyncSnapshot => this.snapshot;

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
      if (this.listeners.size > 0) return;
      // The last screen showing this session closed: send what waits for the pause now. A backoff keeps its timer.
      const work = this.timer && this.snapshot.phase === 'pending' ? this.flush() : this.settled();
      void work.finally(() => this.idleCheck());
    };
  };

  /** The server's answers as just read by a screen. */
  observeServer = (answers: SessionAnswers): void => {
    // Cached reads shown while offline say nothing about changes made elsewhere meanwhile.
    this.serverFresh = this.deps.connectivity.getState() !== 'offline';
    if (answers !== this.snapshot.server) {
      this.update({ server: answers });
      void this.refreshConflicts();
    }
  };

  /** The session's questions, so drafts that could never be accepted are caught before sending (decision M-44). */
  observeQuestions = (items: readonly PublishedItem[]): void => {
    this.items = items;
  };

  choose = async (itemId: string, optionId: string): Promise<void> => {
    const conflict = this.snapshot.conflicts.find((candidate) => candidate.itemId === itemId);
    if (conflict && conflict.local === optionId) {
      // Choosing this phone's answer again is the same decision as "keep the answer from this phone".
      return this.keepLocal(itemId);
    }
    const previous = this.snapshot.local[itemId];
    this.update({
      local: { ...this.snapshot.local, [itemId]: optionId },
      // A new choice settles a conflict and replaces a refused answer: the person has decided again.
      conflicts: this.snapshot.conflicts.filter((candidate) => candidate.itemId !== itemId),
      rejected: this.snapshot.rejected.filter((id) => id !== itemId),
    });
    const write = this.deps.store.saveAnswer({
      sessionId: this.sessionId,
      itemId,
      optionId,
      baseModifiedAt: entryOf(this.snapshot.server, itemId)?.lastModifiedAt ?? null,
      at: this.now(),
    });
    this.writes.add(write);
    try {
      const draft = await write;
      if (draft.syncState === 'pending') {
        // A closed session stays closed: the next flush removes this answer too, and counts it.
        const phase = this.closedBy ? 'closed' : this.snapshot.phase === 'offline' ? 'offline' : 'pending';
        this.update({ phase });
        this.schedule();
      }
    } catch {
      // Not stored, so not shown as chosen, unless a newer choice for the item has replaced this one meanwhile.
      if (this.snapshot.local[itemId] === optionId) {
        const local = without(this.snapshot.local, [itemId]);
        if (previous !== undefined) local[itemId] = previous;
        this.update({ local });
      }
      this.update({ phase: 'failed', error: null });
    } finally {
      this.writes.delete(write);
    }
  };

  /** Settles a conflict for this phone's answer: it is re-based on the server's answer and sent over it. */
  keepLocal = async (itemId: string): Promise<void> => {
    const conflict = this.snapshot.conflicts.find((candidate) => candidate.itemId === itemId);
    if (!conflict) return;
    if (await this.deps.store.rebase(this.sessionId, itemId, conflict.revision, conflict.serverModifiedAt)) {
      this.update({
        conflicts: this.snapshot.conflicts.filter((candidate) => candidate.itemId !== itemId),
        phase: 'pending',
      });
      this.schedule(0);
    } else {
      await this.refreshConflicts();
    }
  };

  /** Settles a conflict for the server's answer: this phone's draft is removed. */
  keepServer = async (itemId: string): Promise<void> => {
    const conflict = this.snapshot.conflicts.find((candidate) => candidate.itemId === itemId);
    if (!conflict) return;
    await this.deps.store.discardAnswer(this.sessionId, itemId, conflict.revision);
    this.update({
      local: this.snapshot.local[itemId] === conflict.local ? without(this.snapshot.local, [itemId]) : this.snapshot.local,
      conflicts: this.snapshot.conflicts.filter((candidate) => candidate.itemId !== itemId),
    });
    await this.refreshConflicts();
  };

  /** Resolves once no write to the local store and no save to the server is still running. */
  settled = async (): Promise<void> => {
    await Promise.allSettled([...this.writes]);
    let tail: Promise<unknown>;
    do {
      tail = this.tail;
      await tail;
    } while (tail !== this.tail);
  };

  /**
   * Sends every unsent answer, after any write still running and after any save still running: flushes are chained,
   * and a flush asked for while another waits to start is the same flush.
   */
  flush = (): Promise<FlushOutcome> => {
    this.clearTimer();
    if (this.next) {
      return this.next;
    }
    this.flushes += 1;
    const run = this.tail.then(async () => {
      this.next = null;
      await Promise.allSettled([...this.writes]);
      return this.sendRounds();
    });
    this.next = run;
    this.tail = run.catch(() => undefined);
    return run.finally(() => {
      this.flushes -= 1;
    });
  };

  /** Something may have changed on the server unseen (the phone went offline): read it again before the next send. */
  markStale(): void {
    this.serverFresh = false;
  }

  /** The connection came back or the app came to the foreground: start the backoff again and try now. */
  wake(): void {
    if (this.disposed || this.blocked) return;
    this.serverFresh = false;
    this.failures = 0;
    const { phase } = this.snapshot;
    if (phase === 'offline' || phase === 'waiting' || phase === 'failed' || phase === 'pending') {
      this.schedule(0);
    }
  }

  /** Stops timers; the drafts stay in the store. */
  dispose(): void {
    this.disposed = true;
    this.clearTimer();
    this.listeners.clear();
  }

  private now(): Date {
    return this.deps.now?.() ?? new Date();
  }

  private idleCheck(): void {
    // Checked a microtask later, so a screen that unsubscribes and subscribes again keeps the same instance.
    queueMicrotask(() => {
      // Unsent answers keep it alive, so they are sent when the connection returns even with no screen open.
      const unsent = Object.keys(this.snapshot.local).length > 0;
      if (this.listeners.size === 0 && !this.timer && this.flushes === 0 && this.writes.size === 0 && !unsent) {
        this.onIdle();
      }
    });
  }

  private async load(): Promise<void> {
    const { store } = this.deps;
    try {
      const [pending, copy] = await Promise.all([store.listPending(this.sessionId), store.loadCopy(this.sessionId)]);
      if (copy && !this.items) {
        this.items = selectItems(copy.content.version, copy.content.session.sectionCode).items;
      }
      const restored = Object.fromEntries(pending.map((draft) => [draft.itemId, draft.optionId]));
      // A choice or a server read that arrived while loading is newer than anything restored.
      this.update({
        ready: true,
        local: { ...restored, ...this.snapshot.local },
        server: this.snapshot.server ?? copy?.serverAnswers ?? null,
        lastSyncedAt: this.snapshot.lastSyncedAt ?? copy?.lastSyncedAt ?? null,
        lost: this.snapshot.lost + store.takeLost(this.sessionId),
        phase: pending.length > 0 ? 'pending' : this.snapshot.phase,
      });
      if (pending.length > 0) {
        await this.refreshConflicts();
        this.schedule();
      }
    } catch {
      this.update({ ready: true, phase: 'failed', error: null });
    }
  }

  /** Recomputes conflicts against the server's latest answers, without sending anything. */
  private async refreshConflicts(): Promise<void> {
    try {
      const { server } = this.snapshot;
      const pending = await this.deps.store.listPending(this.sessionId);
      // A session that no longer takes answers has nothing to choose between.
      const { conflicts } = server?.status === 'in_progress' || server === null ? classify(pending, server) : { conflicts: [] };
      this.update({ conflicts });
    } catch {
      // The next send reads them again.
    }
  }

  private async sendRounds(): Promise<FlushOutcome> {
    for (let round = 1; ; round += 1) {
      const outcome = await this.send();
      if (outcome !== 'more' || round >= MAX_ROUNDS) {
        if (outcome === 'more') {
          this.schedule();
          return 'retrying';
        }
        return outcome;
      }
    }
  }

  private async send(): Promise<FlushOutcome | 'more'> {
    const { store, api, connectivity } = this.deps;
    let pending: DraftAnswer[];
    try {
      pending = await store.listPending(this.sessionId);
    } catch {
      this.update({ phase: 'failed', error: null });
      return 'failed';
    }
    const lost = store.takeLost(this.sessionId);
    if (lost > 0) {
      this.update({ lost: this.snapshot.lost + lost });
    }
    pending = await this.dropInvalid(pending);

    if (this.closedBy) {
      // Closed for good: asking the server again could only be refused the same way.
      return this.close(pending, this.closedBy.error);
    }
    if (pending.length === 0) {
      // Nothing waits, so the server has every stored answer (a choice that failed to store was undone).
      this.update({ phase: this.snapshot.phase === 'idle' ? 'idle' : 'synced', conflicts: [], retryAt: null, error: null });
      this.failures = 0;
      return 'saved';
    }
    if (connectivity.getState() === 'offline') {
      this.update({ phase: 'offline', retryAt: null });
      return 'offline';
    }

    try {
      if (!this.serverFresh) {
        // Drafts restored after a restart or kept offline are checked against the server before anything is sent.
        const latest = await api.getAnswers(this.sessionId);
        this.serverFresh = true;
        this.update({ server: latest });
        this.deps.onSaved(this.sessionId, latest);
        await store.saveServerAnswers(this.sessionId, latest, null).catch(() => undefined);
      }

      if (this.snapshot.server && this.snapshot.server.status !== 'in_progress') {
        // Submitted or closed elsewhere: nothing here can be added, and there is nothing left to choose between.
        return await this.close(pending, null);
      }

      const { same, conflicts, send } = classify(pending, this.snapshot.server);
      if (same.length > 0) {
        await store.markSynced(this.sessionId, same, this.now());
        this.forgetSent(same.map(({ itemId }) => itemId), pending);
      }
      this.update({ conflicts });
      if (send.length === 0) {
        this.failures = 0;
        this.update({ phase: 'synced', retryAt: null, error: null });
        return conflicts.length > 0 ? 'conflicts' : 'saved';
      }

      this.update({ phase: 'syncing', retryAt: null });
      for (let start = 0; start < send.length; start += MAX_ANSWERS_PER_SAVE) {
        const chunk = send.slice(start, start + MAX_ANSWERS_PER_SAVE);
        let saved: SessionAnswers;
        try {
          saved = await api.saveAnswers(
            this.sessionId,
            chunk.map(({ itemId, optionId }) => ({ itemId, optionId })),
          );
        } catch (error) {
          return await this.failed(error, chunk, pending);
        }
        const at = this.now();
        const sent = chunk.flatMap((draft): SentRevision[] => {
          const recorded = entryOf(saved, draft.itemId);
          return recorded
            ? [
                {
                  itemId: draft.itemId,
                  revision: draft.revision,
                  updatedAt: draft.updatedAt,
                  serverModifiedAt: recorded.lastModifiedAt,
                },
              ]
            : [];
        });
        await store.markSynced(this.sessionId, sent, at);
        this.forgetSent(sent.map(({ itemId }) => itemId), chunk);
        this.update({ server: saved, lastSyncedAt: at.toISOString() });
        this.deps.onSaved(this.sessionId, saved);
        await store.saveServerAnswers(this.sessionId, saved, at).catch(() => undefined);
      }

      this.failures = 0;
      const remaining = classify(await store.listPending(this.sessionId), this.snapshot.server);
      this.update({ conflicts: remaining.conflicts, error: null });
      if (remaining.send.length > 0 || remaining.same.length > 0) {
        // Chosen while the save ran: send them too.
        this.update({ phase: 'pending' });
        return 'more';
      }
      this.update({ phase: 'synced' });
      return remaining.conflicts.length > 0 ? 'conflicts' : 'saved';
    } catch (error) {
      return this.failed(error, [], pending);
    }
  }

  /** Removes sent answers from the local overlay, unless a newer choice for the question replaced them meanwhile. */
  private forgetSent(itemIds: readonly string[], drafts: readonly DraftAnswer[]): void {
    const sentOption = new Map(drafts.map((draft) => [draft.itemId, draft.optionId]));
    const done = itemIds.filter((itemId) => this.snapshot.local[itemId] === sentOption.get(itemId));
    if (done.length > 0) {
      this.update({ local: without(this.snapshot.local, done) });
    }
  }

  /** Drafts that could never be accepted (not a question of this session, or not one of its options) are removed. */
  private async dropInvalid(pending: DraftAnswer[]): Promise<DraftAnswer[]> {
    const items = this.items;
    if (!items) return pending;
    const invalid = pending.filter((draft) => !isValidAnswer(items, draft.itemId, draft.optionId));
    if (invalid.length === 0) return pending;
    await Promise.all(invalid.map((draft) => this.deps.store.discardAnswer(this.sessionId, draft.itemId, draft.revision)));
    this.markRejected(invalid);
    return pending.filter((draft) => !invalid.includes(draft));
  }

  private markRejected(drafts: readonly DraftAnswer[]): void {
    const itemIds = drafts.map((draft) => draft.itemId);
    this.update({
      local: without(this.snapshot.local, itemIds),
      rejected: [...new Set([...this.snapshot.rejected, ...itemIds])],
    });
  }

  private async failed(error: unknown, chunk: readonly DraftAnswer[], pending: readonly DraftAnswer[]): Promise<FlushOutcome> {
    const { store, connectivity } = this.deps;
    if (!isApiError(error)) {
      this.update({ phase: 'failed', error: null });
      return 'failed';
    }
    switch (error.kind) {
      case 'network':
      case 'timeout':
        // Whatever happened on the server meanwhile went unseen: read it again before the next send.
        this.serverFresh = false;
        if (connectivity.getState() === 'offline') {
          this.update({ phase: 'offline', retryAt: null, error });
          return 'offline';
        }
        return this.retryLater(error);
      case 'server':
      case 'rate_limited':
        return this.retryLater(error);
      case 'conflict':
        if (error.code === undefined || !CLOSED_CODES.has(error.code)) {
          return this.retryLater(error);
        }
        return this.close(pending, error);
      case 'not_found':
        // No longer available to this person (D-48 answers 404): nothing about the session stays on the phone.
        await store.discardSession(this.sessionId).catch(() => undefined);
        this.update({ local: {}, conflicts: [], discarded: this.snapshot.discarded + pending.length, phase: 'gone', error });
        this.deps.onRejected(this.sessionId);
        return 'gone';
      case 'bad_request':
        // The server refused the request's answers; the same ones can never succeed. Those questions are asked again.
        await Promise.all(chunk.map((draft) => store.discardAnswer(this.sessionId, draft.itemId, draft.revision))).catch(
          () => undefined,
        );
        this.markRejected(chunk);
        this.update({ phase: 'rejected', error });
        this.deps.onRejected(this.sessionId);
        return 'rejected';
      case 'unauthorized':
        // Signing in again resumes; the drafts are kept for the same person (decision M-23).
        this.blocked = true;
        this.update({ phase: 'failed', retryAt: null, error });
        return 'unauthorized';
      case 'forbidden':
        this.blocked = true;
        this.update({ phase: 'failed', retryAt: null, error });
        return 'consent_required';
      default:
        this.update({ phase: 'failed', retryAt: null, error });
        return 'failed';
    }
  }

  /**
   * Submitted, closed elsewhere or past its time to answer: these answers can never be added. They are removed, and
   * the person is told. The session stays closed for this sync.
   */
  private async close(pending: readonly DraftAnswer[], error: unknown): Promise<FlushOutcome> {
    const first = this.closedBy === null;
    this.closedBy = { error };
    await Promise.all(
      pending.map((draft) => this.deps.store.discardAnswer(this.sessionId, draft.itemId, draft.revision)),
    ).catch(() => undefined);
    this.update({ local: {}, conflicts: [], discarded: this.snapshot.discarded + pending.length, phase: 'closed', error });
    if (first || pending.length > 0) {
      this.deps.onRejected(this.sessionId);
    }
    return 'closed';
  }

  private retryLater(error: unknown): FlushOutcome {
    this.failures += 1;
    const delay = retryDelay(this.deps.retry ?? DEFAULT_RETRY, this.failures, this.deps.random);
    if (delay === null) {
      this.update({ phase: 'failed', retryAt: null, error });
      return 'failed';
    }
    this.schedule(delay);
    this.update({ phase: 'waiting', retryAt: Date.now() + delay, error });
    return 'retrying';
  }

  private schedule(delay: number = this.deps.delayMs ?? SYNC_DELAY_MS): void {
    if (this.disposed) return;
    this.clearTimer();
    this.blocked = false;
    this.timer = setTimeout(() => {
      this.timer = null;
      void this.flush().finally(() => {
        if (this.listeners.size === 0) this.idleCheck();
      });
    }, delay);
  }

  private clearTimer(): void {
    if (this.timer) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }

  private update(patch: Partial<AnswerSyncSnapshot>): void {
    this.snapshot = { ...this.snapshot, ...patch };
    this.listeners.forEach((listener) => listener());
  }
}
