import { isApiError } from '@/api/client';
import type { ParticipantApi } from '@/api/participant/participantApi';

import type { PlannedPart } from './reportPlan';
import type { ScoreReportStore } from './reportStore';

type Language = 'en' | 'ar';
type PreparerApi = Pick<ParticipantApi, 'createScoreReportPart' | 'createScoreReportSummary'>;

/** Calls that may have failed for a moment (measured: a dropped connection now and then) get one more try. */
const ATTEMPTS = 2;

/** Writes a report's parts in the background as the person answers, and keeps them sealed on the phone (D-113). */
export interface ReportPreparer {
  /** Writes whatever is missing or stale. It never rejects, and outlives the screen that asked. */
  update(sessionId: string, language: Language, parts: readonly PlannedPart[]): void;
  /** Resolves when nothing for this session is being written. */
  settled(sessionId: string): Promise<void>;
  /** Stops caring about everything under way: nothing finished after this is saved (a sign-out, another account). */
  reset(): void;
}

/** The server will keep refusing this session, or no model is connected: asking again would not help. */
function permanent(error: unknown): boolean {
  return isApiError(error) && (error.status === 403 || error.status === 404 || error.status === 409 || error.status === 503);
}

/**
 * One call at a time for each part, and only for answers no saved part covers. A part that changes while it is being
 * written is written once more when that call returns, so a person changing their mind costs at most one extra call,
 * however often they change it. Each section is on its own, so a change rewrites only that section.
 */
export function createReportPreparer({ api, store }: { api: PreparerApi; store: ScoreReportStore }): ReportPreparer {
  let generation = 0;
  let lookups = 0;
  const running = new Map<string, Promise<void>>();
  const wanted = new Map<string, PlannedPart>();
  const refused = new Set<string>();

  const identity = (sessionId: string, language: Language, key: string) => `${sessionId}|${language}|${key}`;

  async function write(sessionId: string, language: Language, part: PlannedPart) {
    if (part.sectionCode === null) {
      const summary = await api.createScoreReportSummary(sessionId, language);
      return { key: part.key, fingerprint: part.fingerprint, kind: 'summary' as const, summary };
    }
    const written = await api.createScoreReportPart(sessionId, part.sectionCode, language);
    return { key: part.key, fingerprint: part.fingerprint, kind: 'section' as const, part: written };
  }

  async function run(sessionId: string, language: Language, id: string, started: number) {
    let done: string | null = null;
    for (;;) {
      const part = wanted.get(id);
      if (!part || part.fingerprint === done || started !== generation || refused.has(sessionId)) return;
      done = part.fingerprint;
      for (let attempt = 1; attempt <= ATTEMPTS; attempt += 1) {
        try {
          const written = await write(sessionId, language, part);
          if (started !== generation) return;
          await store.savePart(sessionId, language, written);
          break;
        } catch (error) {
          if (permanent(error)) {
            refused.add(sessionId);
            return;
          }
        }
      }
    }
  }

  return {
    update(sessionId, language, parts) {
      if (refused.has(sessionId)) return;
      const started = generation;
      const looking = store.listParts(sessionId, language).then(
        (saved) => {
          if (started !== generation) return;
          const current = new Map(saved.map((part) => [part.key, part.fingerprint]));
          for (const part of parts) {
            const id = identity(sessionId, language, part.key);
            wanted.set(id, part);
            if (current.get(part.key) === part.fingerprint || running.has(id)) continue;
            running.set(
              id,
              run(sessionId, language, id, started).finally(() => running.delete(id)),
            );
          }
        },
        () => undefined,
      );
      // Counted as work under way from this moment, so `settled` never misses what this is about to start.
      const id = identity(sessionId, language, `looking:${(lookups += 1)}`);
      running.set(
        id,
        looking.finally(() => running.delete(id)),
      );
    },

    async settled(sessionId) {
      // One piece of work can start another (a changed answer), so look again until it is quiet.
      for (;;) {
        const under = [...running.entries()].filter(([id]) => id.startsWith(`${sessionId}|`)).map(([, work]) => work);
        if (under.length === 0) return;
        await Promise.all(under);
      }
    },

    reset() {
      generation += 1;
      wanted.clear();
      refused.clear();
    },
  };
}
