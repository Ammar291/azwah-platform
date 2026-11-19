import type { DraftAnswer, DraftStore, QueuedSubmission, SessionCopy } from './types';

const byUpdate = (a: DraftAnswer, b: DraftAnswer) =>
  a.updatedAt === b.updatedAt ? a.itemId.localeCompare(b.itemId) : a.updatedAt.localeCompare(b.updatedAt);

/** JSON round trip, as a database would give back: callers never share objects with the store. */
const copyOf = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

/**
 * Local session data kept only while the app runs: used when the phone's keystore or cipher cannot protect answers
 * on the device (decision M-35), and in tests.
 */
export function createMemoryDraftStore(): DraftStore {
  const sessions = new Map<string, Map<string, DraftAnswer>>();
  const copies = new Map<string, SessionCopy>();
  const submissions = new Map<string, QueuedSubmission>();

  const sessionOf = (sessionId: string) => {
    let answers = sessions.get(sessionId);
    if (!answers) {
      answers = new Map();
      sessions.set(sessionId, answers);
    }
    return answers;
  };

  const listAnswers = async (sessionId: string) =>
    [...(sessions.get(sessionId)?.values() ?? [])].map((answer) => ({ ...answer })).sort(byUpdate);

  return {
    persistence: 'memory',
    encryptedAtRest: false,

    listAnswers,

    async listPending(sessionId) {
      return (await listAnswers(sessionId)).filter((answer) => answer.syncState === 'pending');
    },

    async saveAnswer({ sessionId, itemId, optionId, baseModifiedAt, at }) {
      const answers = sessionOf(sessionId);
      const current = answers.get(itemId);
      if (current?.optionId === optionId && current.syncState === 'pending') {
        return { ...current };
      }
      const next: DraftAnswer = {
        sessionId,
        itemId,
        optionId,
        revision: (current?.revision ?? 0) + 1,
        baseModifiedAt,
        updatedAt: at.toISOString(),
        syncState: 'pending',
        syncedAt: null,
      };
      answers.set(itemId, next);
      return { ...next };
    },

    async markSynced(sessionId, sent, at) {
      const answers = sessions.get(sessionId);
      for (const { itemId, revision, updatedAt, serverModifiedAt } of sent) {
        const current = answers?.get(itemId);
        if (current && current.revision === revision && current.updatedAt === updatedAt) {
          answers?.set(itemId, {
            ...current,
            baseModifiedAt: serverModifiedAt,
            syncState: 'synced',
            syncedAt: at.toISOString(),
          });
        } else if (current) {
          answers?.set(itemId, { ...current, baseModifiedAt: serverModifiedAt });
        }
      }
    },

    async rebase(sessionId, itemId, revision, baseModifiedAt) {
      const answers = sessions.get(sessionId);
      const current = answers?.get(itemId);
      if (!current || current.revision !== revision) {
        return false;
      }
      answers?.set(itemId, { ...current, baseModifiedAt });
      return true;
    },

    async discardAnswer(sessionId, itemId, revision) {
      const answers = sessions.get(sessionId);
      if (answers?.get(itemId)?.revision === revision) {
        answers.delete(itemId);
      }
    },

    async saveCopy(sessionId, content, serverAnswers, at) {
      copies.set(sessionId, {
        content: copyOf(content),
        serverAnswers: copyOf(serverAnswers),
        savedAt: at.toISOString(),
        lastSyncedAt: copies.get(sessionId)?.lastSyncedAt ?? null,
      });
    },

    async saveServerAnswers(sessionId, serverAnswers, syncedAt) {
      const copy = copies.get(sessionId);
      if (copy) {
        copies.set(sessionId, {
          ...copy,
          serverAnswers: copyOf(serverAnswers),
          lastSyncedAt: syncedAt?.toISOString() ?? copy.lastSyncedAt,
        });
      }
    },

    async loadCopy(sessionId) {
      const copy = copies.get(sessionId);
      return copy ? copyOf(copy) : null;
    },

    async queueSubmission(sessionId, idempotencyKey, at) {
      const existing = submissions.get(sessionId);
      if (existing) {
        return { ...existing };
      }
      const queued: QueuedSubmission = {
        sessionId,
        idempotencyKey,
        requestedAt: at.toISOString(),
        attempts: 0,
        lastAttemptAt: null,
      };
      submissions.set(sessionId, queued);
      return { ...queued };
    },

    async replaceSubmissionKey(sessionId, idempotencyKey) {
      const existing = submissions.get(sessionId);
      if (existing) {
        submissions.set(sessionId, { ...existing, idempotencyKey });
      }
    },

    async recordSubmissionAttempt(sessionId, at) {
      const existing = submissions.get(sessionId);
      if (existing) {
        submissions.set(sessionId, { ...existing, attempts: existing.attempts + 1, lastAttemptAt: at.toISOString() });
      }
    },

    async getSubmission(sessionId) {
      const existing = submissions.get(sessionId);
      return existing ? { ...existing } : null;
    },

    async listSubmissions() {
      return [...submissions.values()]
        .map((submission) => ({ ...submission }))
        .sort((a, b) => a.requestedAt.localeCompare(b.requestedAt) || a.sessionId.localeCompare(b.sessionId));
    },

    async removeSubmission(sessionId) {
      submissions.delete(sessionId);
    },

    async listSessionsWithPending() {
      return [...sessions]
        .filter(([, answers]) => [...answers.values()].some((answer) => answer.syncState === 'pending'))
        .map(([sessionId]) => sessionId)
        .sort();
    },

    async discardSession(sessionId) {
      sessions.delete(sessionId);
      copies.delete(sessionId);
      submissions.delete(sessionId);
    },

    async clearAll() {
      sessions.clear();
      copies.clear();
      submissions.clear();
    },

    takeLost() {
      return 0;
    },
  };
}
