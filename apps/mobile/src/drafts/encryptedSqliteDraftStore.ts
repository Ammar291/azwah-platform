import { z } from 'zod';

import type { SessionAnswers } from '@/api/participant/schemas';
import { UnreadableValueError, type FieldCipher } from '@/storage/protection/fieldCipher';
import type { SqlDatabase } from '@/storage/sql';

import { createSerialQueue } from './serialQueue';
import { sessionAnswersSchema, sessionCopyContentSchema } from './sessionCopySchema';
import type { DraftAnswer, DraftStore, DraftSyncState, QueuedSubmission, SessionCopy } from './types';

interface DraftRow {
  session_id: string;
  item_id: string;
  answer_cipher: string;
  revision: number;
  base_modified_at: string | null;
  updated_at: string;
  sync_state: DraftSyncState;
  synced_at: string | null;
}

interface CopyRow {
  session_id: string;
  content_cipher: string;
  answers_cipher: string;
  saved_at: string;
  last_synced_at: string | null;
}

interface SubmissionRow {
  session_id: string;
  idempotency_key: string;
  requested_at: string;
  attempts: number;
  last_attempt_at: string | null;
}

const DRAFT_COLUMNS = 'session_id, item_id, answer_cipher, revision, base_modified_at, updated_at, sync_state, synced_at';
const SUBMISSION_COLUMNS = 'session_id, idempotency_key, requested_at, attempts, last_attempt_at';

/** Associated data for each sealed value: a value opens only in the row it was written to. */
const draftContext = (sessionId: string, itemId: string) => `draft/${sessionId}/${itemId}`;
const copyContext = (sessionId: string, part: 'content' | 'answers') => `copy/${sessionId}/${part}`;

/** A copy that can never be used: sealed with a lost key, changed, or written by an app with other shapes. */
const isUnusableCopy = (error: unknown) =>
  error instanceof UnreadableValueError || error instanceof SyntaxError || error instanceof z.ZodError;

const toSubmission = (row: SubmissionRow): QueuedSubmission => ({
  sessionId: row.session_id,
  idempotencyKey: row.idempotency_key,
  requestedAt: row.requested_at,
  attempts: row.attempts,
  lastAttemptAt: row.last_attempt_at,
});

/**
 * Local session data in SQLite (migration 3). Every answer, and every copy of a session with its answers, is sealed
 * with AES-256-GCM before it is written (decision M-35); IDs, times, revisions and sync states are in clear. Values
 * that can no longer be opened (their key was lost) are deleted when read and counted for `takeLost`.
 */
export function createEncryptedSqliteDraftStore(db: SqlDatabase, cipher: FieldCipher): DraftStore {
  const serial = createSerialQueue();
  const lost = new Map<string, number>();
  const countLost = (sessionId: string, count: number) => lost.set(sessionId, (lost.get(sessionId) ?? 0) + count);

  async function readDrafts(sessionId: string, pendingOnly: boolean): Promise<DraftAnswer[]> {
    const rows = await db.getAllAsync<DraftRow>(
      `SELECT ${DRAFT_COLUMNS} FROM draft_answers WHERE session_id = ?${pendingOnly ? " AND sync_state = 'pending'" : ''}
       ORDER BY updated_at, item_id`,
      sessionId,
    );
    const answers: DraftAnswer[] = [];
    for (const row of rows) {
      let optionId: string;
      try {
        optionId = await cipher.decrypt(row.answer_cipher, draftContext(row.session_id, row.item_id));
      } catch (error) {
        if (!(error instanceof UnreadableValueError)) {
          throw error;
        }
        // Can never be read again: remove it and say so, rather than failing every read of the session.
        await db.runAsync(
          'DELETE FROM draft_answers WHERE session_id = ? AND item_id = ? AND answer_cipher = ?',
          row.session_id,
          row.item_id,
          row.answer_cipher,
        );
        countLost(sessionId, 1);
        continue;
      }
      answers.push({
        sessionId: row.session_id,
        itemId: row.item_id,
        optionId,
        revision: row.revision,
        baseModifiedAt: row.base_modified_at,
        updatedAt: row.updated_at,
        syncState: row.sync_state,
        syncedAt: row.synced_at,
      });
    }
    return answers;
  }

  async function readDraft(sessionId: string, itemId: string): Promise<DraftAnswer | null> {
    return (await readDrafts(sessionId, false)).find((draft) => draft.itemId === itemId) ?? null;
  }

  async function readSubmission(sessionId: string): Promise<QueuedSubmission | null> {
    const row = await db.getFirstAsync<SubmissionRow>(
      `SELECT ${SUBMISSION_COLUMNS} FROM pending_submissions WHERE session_id = ?`,
      sessionId,
    );
    return row ? toSubmission(row) : null;
  }

  async function sealAnswers(sessionId: string, answers: SessionAnswers): Promise<string> {
    return cipher.encrypt(JSON.stringify(answers), copyContext(sessionId, 'answers'));
  }

  return {
    persistence: 'device',
    encryptedAtRest: true,

    listAnswers: (sessionId) => serial(() => readDrafts(sessionId, false)),

    listPending: (sessionId) => serial(() => readDrafts(sessionId, true)),

    saveAnswer: ({ sessionId, itemId, optionId, baseModifiedAt, at }) =>
      serial(async () => {
        const current = await readDraft(sessionId, itemId);
        if (current?.optionId === optionId && current.syncState === 'pending') {
          // Choosing an unsent option again changes nothing, so it is not sent twice.
          return current;
        }
        const sealed = await cipher.encrypt(optionId, draftContext(sessionId, itemId));
        await db.runAsync(
          `INSERT INTO draft_answers (${DRAFT_COLUMNS}) VALUES (?, ?, ?, 1, ?, ?, 'pending', NULL)
           ON CONFLICT (session_id, item_id) DO UPDATE SET
             answer_cipher = excluded.answer_cipher,
             revision = draft_answers.revision + 1,
             base_modified_at = excluded.base_modified_at,
             updated_at = excluded.updated_at,
             sync_state = 'pending',
             synced_at = NULL`,
          sessionId,
          itemId,
          sealed,
          baseModifiedAt,
          at.toISOString(),
        );
        const saved = await readDraft(sessionId, itemId);
        if (!saved) {
          throw new Error('A saved draft answer could not be read back.');
        }
        return saved;
      }),

    markSynced: (sessionId, sent, at) =>
      serial(async () => {
        // One statement per answer, each atomic on its own. Expo SQLite's transactions are not exclusive, so a
        // rollback could undo an answer saved meanwhile; a crash between statements only means a harmless resend.
        for (const { itemId, revision, updatedAt, serverModifiedAt } of sent) {
          await db.runAsync(
            `UPDATE draft_answers SET
               base_modified_at = ?,
               sync_state = CASE WHEN revision = ? AND updated_at = ? THEN 'synced' ELSE sync_state END,
               synced_at = CASE WHEN revision = ? AND updated_at = ? THEN ? ELSE synced_at END
             WHERE session_id = ? AND item_id = ?`,
            serverModifiedAt,
            revision,
            updatedAt,
            revision,
            updatedAt,
            at.toISOString(),
            sessionId,
            itemId,
          );
        }
      }),

    rebase: (sessionId, itemId, revision, baseModifiedAt) =>
      serial(async () => {
        await db.runAsync(
          'UPDATE draft_answers SET base_modified_at = ? WHERE session_id = ? AND item_id = ? AND revision = ?',
          baseModifiedAt,
          sessionId,
          itemId,
          revision,
        );
        const row = await db.getFirstAsync<{ base_modified_at: string | null }>(
          'SELECT base_modified_at FROM draft_answers WHERE session_id = ? AND item_id = ? AND revision = ?',
          sessionId,
          itemId,
          revision,
        );
        return row?.base_modified_at === baseModifiedAt;
      }),

    discardAnswer: (sessionId, itemId, revision) =>
      serial(async () => {
        await db.runAsync(
          'DELETE FROM draft_answers WHERE session_id = ? AND item_id = ? AND revision = ?',
          sessionId,
          itemId,
          revision,
        );
      }),

    saveCopy: (sessionId, content, serverAnswers, at) =>
      serial(async () => {
        const contentCipher = await cipher.encrypt(JSON.stringify(content), copyContext(sessionId, 'content'));
        const answersCipher = await sealAnswers(sessionId, serverAnswers);
        await db.runAsync(
          `INSERT INTO session_copies (session_id, instrument_version_id, content_cipher, answers_cipher, saved_at, last_synced_at)
           VALUES (?, ?, ?, ?, ?, NULL)
           ON CONFLICT (session_id) DO UPDATE SET
             instrument_version_id = excluded.instrument_version_id,
             content_cipher = excluded.content_cipher,
             answers_cipher = excluded.answers_cipher,
             saved_at = excluded.saved_at`,
          sessionId,
          content.session.instrumentVersionId,
          contentCipher,
          answersCipher,
          at.toISOString(),
        );
      }),

    saveServerAnswers: (sessionId, serverAnswers, syncedAt) =>
      serial(async () => {
        const exists = await db.getFirstAsync('SELECT session_id FROM session_copies WHERE session_id = ?', sessionId);
        if (!exists) {
          return;
        }
        const answersCipher = await sealAnswers(sessionId, serverAnswers);
        await db.runAsync(
          `UPDATE session_copies SET answers_cipher = ?, last_synced_at = COALESCE(?, last_synced_at)
           WHERE session_id = ?`,
          answersCipher,
          syncedAt?.toISOString() ?? null,
          sessionId,
        );
      }),

    loadCopy: (sessionId) =>
      serial(async (): Promise<SessionCopy | null> => {
        const row = await db.getFirstAsync<CopyRow>(
          'SELECT session_id, content_cipher, answers_cipher, saved_at, last_synced_at FROM session_copies WHERE session_id = ?',
          sessionId,
        );
        if (!row) {
          return null;
        }
        try {
          const content = sessionCopyContentSchema.parse(
            JSON.parse(await cipher.decrypt(row.content_cipher, copyContext(sessionId, 'content'))),
          );
          const serverAnswers = sessionAnswersSchema.parse(
            JSON.parse(await cipher.decrypt(row.answers_cipher, copyContext(sessionId, 'answers'))),
          );
          if (content.session.id !== sessionId || serverAnswers.sessionId !== sessionId) {
            throw new UnreadableValueError();
          }
          return { content, serverAnswers, savedAt: row.saved_at, lastSyncedAt: row.last_synced_at };
        } catch (error) {
          if (!isUnusableCopy(error)) {
            throw error;
          }
          // Unreadable or from an older app: the server is the record, so the copy is simply dropped.
          await db.runAsync('DELETE FROM session_copies WHERE session_id = ?', sessionId);
          return null;
        }
      }),

    queueSubmission: (sessionId, idempotencyKey, at) =>
      serial(async () => {
        await db.runAsync(
          `INSERT INTO pending_submissions (${SUBMISSION_COLUMNS}) VALUES (?, ?, ?, 0, NULL)
           ON CONFLICT (session_id) DO NOTHING`,
          sessionId,
          idempotencyKey,
          at.toISOString(),
        );
        const queued = await readSubmission(sessionId);
        if (!queued) {
          throw new Error('A queued submission could not be read back.');
        }
        return queued;
      }),

    replaceSubmissionKey: (sessionId, idempotencyKey) =>
      serial(async () => {
        await db.runAsync(
          'UPDATE pending_submissions SET idempotency_key = ? WHERE session_id = ?',
          idempotencyKey,
          sessionId,
        );
      }),

    recordSubmissionAttempt: (sessionId, at) =>
      serial(async () => {
        await db.runAsync(
          'UPDATE pending_submissions SET attempts = attempts + 1, last_attempt_at = ? WHERE session_id = ?',
          at.toISOString(),
          sessionId,
        );
      }),

    getSubmission: (sessionId) => serial(() => readSubmission(sessionId)),

    listSubmissions: () =>
      serial(async () =>
        (
          await db.getAllAsync<SubmissionRow>(
            `SELECT ${SUBMISSION_COLUMNS} FROM pending_submissions ORDER BY requested_at, session_id`,
          )
        ).map(toSubmission),
      ),

    removeSubmission: (sessionId) =>
      serial(async () => {
        await db.runAsync('DELETE FROM pending_submissions WHERE session_id = ?', sessionId);
      }),

    listSessionsWithPending: () =>
      serial(async () =>
        (
          await db.getAllAsync<{ session_id: string }>(
            "SELECT DISTINCT session_id FROM draft_answers WHERE sync_state = 'pending' ORDER BY session_id",
          )
        ).map((row) => row.session_id),
      ),

    discardSession: (sessionId) =>
      serial(async () => {
        await db.runAsync('DELETE FROM draft_answers WHERE session_id = ?', sessionId);
        await db.runAsync('DELETE FROM session_copies WHERE session_id = ?', sessionId);
        await db.runAsync('DELETE FROM pending_submissions WHERE session_id = ?', sessionId);
      }),

    clearAll: () =>
      serial(async () => {
        await db.runAsync('DELETE FROM draft_answers');
        await db.runAsync('DELETE FROM session_copies');
        await db.runAsync('DELETE FROM pending_submissions');
        lost.clear();
        // Anything left in free pages or the write-ahead log can no longer be decrypted (decision M-36).
        await cipher.destroyKey();
      }),

    takeLost(sessionId) {
      const count = lost.get(sessionId) ?? 0;
      lost.delete(sessionId);
      return count;
    },
  };
}
