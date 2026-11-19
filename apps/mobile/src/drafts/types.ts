import type { Assignment, PublishedVersion, Session, SessionAnswers } from '@/api/participant/schemas';

export type DraftSyncState = 'pending' | 'synced';

/** One answer on this phone: the latest option chosen for an item in a session. */
export interface DraftAnswer {
  sessionId: string;
  itemId: string;
  optionId: string;
  /** Starts at 1 and goes up each time the option changes, so a sync marks only what it actually sent. */
  revision: number;
  /**
   * The server's `lastModifiedAt` for this item when the option was chosen (null: the server had no answer). A
   * server answer changed since then, to another option, makes the draft a conflict (decision M-40).
   */
  baseModifiedAt: string | null;
  updatedAt: string;
  syncState: DraftSyncState;
  syncedAt: string | null;
}

export interface SaveDraftAnswer {
  sessionId: string;
  itemId: string;
  optionId: string;
  baseModifiedAt: string | null;
  at: Date;
}

/** An answer as it was sent (its revision and local time identify it), and when the server recorded it. */
export interface SentRevision {
  itemId: string;
  revision: number;
  updatedAt: string;
  serverModifiedAt: string;
}

/** What a session screen needs, as read from the server while signed in (decision M-38). */
export interface SessionCopyContent {
  session: Session;
  assignment: Assignment;
  version: PublishedVersion;
}

export interface SessionCopy {
  content: SessionCopyContent;
  /** The server's answers as last known: from a read or from the last save's response. */
  serverAnswers: SessionAnswers;
  savedAt: string;
  /** When the server last accepted answers from this phone. */
  lastSyncedAt: string | null;
}

/** A submission asked for on this phone and not yet confirmed by the server (decision M-41). Holds no answers. */
export interface QueuedSubmission {
  sessionId: string;
  /** Sent with every attempt, so the server submits once (D-54). */
  idempotencyKey: string;
  requestedAt: string;
  attempts: number;
  lastAttemptAt: string | null;
}

export type DraftPersistence = 'device' | 'memory';

/**
 * Everything kept locally about sessions being answered: draft answers, a copy of each open session for offline use,
 * and submissions waiting for the server. Two implementations: SQLite with every answer encrypted (only when the
 * keystore works), and memory. See createDraftStore for which one runs.
 */
export interface DraftStore {
  readonly persistence: DraftPersistence;
  /** True only for the SQLite store, whose answers are sealed with AES-256-GCM (decision M-35). */
  readonly encryptedAtRest: boolean;

  listAnswers(sessionId: string): Promise<DraftAnswer[]>;
  /** Answers changed since they were last sent, oldest first. */
  listPending(sessionId: string): Promise<DraftAnswer[]>;
  /**
   * Stores the option for the item. Choosing again the option of a draft not yet sent changes nothing; choosing the
   * option of a synced row makes a new pending draft, because the server's answer may have changed since.
   */
  saveAnswer(answer: SaveDraftAnswer): Promise<DraftAnswer>;
  /**
   * Marks sent answers as synced, unless they changed again after they were sent (matched by revision and time, so a
   * draft made again after a discard is never taken for the sent one). Any other draft of the same item is re-based
   * on the server's record of the sent one, which came from this phone and so is no conflict.
   */
  markSynced(sessionId: string, sent: readonly SentRevision[], at: Date): Promise<void>;
  /** Keeps a draft over a newer server answer: its base becomes that answer's time. False if the draft changed. */
  rebase(sessionId: string, itemId: string, revision: number, baseModifiedAt: string): Promise<boolean>;
  /** Drops one draft so the server's answer stands, unless it changed since `revision`. */
  discardAnswer(sessionId: string, itemId: string, revision: number): Promise<void>;

  saveCopy(sessionId: string, content: SessionCopyContent, serverAnswers: SessionAnswers, at: Date): Promise<void>;
  /** Replaces the copy's server answers (no copy, no change); `syncedAt` when they come from this phone's save. */
  saveServerAnswers(sessionId: string, serverAnswers: SessionAnswers, syncedAt: Date | null): Promise<void>;
  loadCopy(sessionId: string): Promise<SessionCopy | null>;

  /** Records a submission request. An existing one for the session is kept, with its key. */
  queueSubmission(sessionId: string, idempotencyKey: string, at: Date): Promise<QueuedSubmission>;
  replaceSubmissionKey(sessionId: string, idempotencyKey: string): Promise<void>;
  recordSubmissionAttempt(sessionId: string, at: Date): Promise<void>;
  getSubmission(sessionId: string): Promise<QueuedSubmission | null>;
  listSubmissions(): Promise<QueuedSubmission[]>;
  removeSubmission(sessionId: string): Promise<void>;

  /** Sessions with answers not yet sent, so they can be sent without their screen being open. */
  listSessionsWithPending(): Promise<string[]>;
  /** Removes the session's drafts, copy and queued submission. */
  discardSession(sessionId: string): Promise<void>;
  /** Removes everything, then the key, so nothing left behind can be read (decision M-36). */
  clearAll(): Promise<void>;
  /**
   * How many of the session's values could not be read (their key was lost) and were deleted since the last call.
   * Reported to the person, never silent.
   */
  takeLost(sessionId: string): number;
}
