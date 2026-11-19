import { z } from 'zod';

import {
  scoreReportPartSchema,
  scoreReportSchema,
  scoreReportSummarySchema,
  type ScoreReport,
  type ScoreReportPart,
  type ScoreReportSummary,
} from '@/api/participant/schemas';
import { createSerialQueue } from '@/drafts/serialQueue';
import { UnreadableValueError, type FieldCipher } from '@/storage/protection/fieldCipher';
import type { SqlDatabase } from '@/storage/sql';

type ReportLanguage = ScoreReport['language'];

/** Whether the person agreed to have the report written as they answer (D-113). */
export type ReportChoice = 'on' | 'off';

/** A piece of a report written ahead, with the answers it was written from, so a changed answer shows it is stale. */
export type StoredReportPart =
  | { key: string; fingerprint: string; kind: 'section'; part: ScoreReportPart }
  | { key: string; fingerprint: string; kind: 'summary'; summary: ScoreReportSummary };

const storedPartSchema = z.union([
  z.object({ key: z.string(), fingerprint: z.string(), kind: z.literal('section'), part: scoreReportPartSchema }),
  z.object({ key: z.string(), fingerprint: z.string(), kind: z.literal('summary'), summary: scoreReportSummarySchema }),
]);

/** Written score reports, kept on this phone only (D-112, D-113): one for each session and language, and their parts. */
export interface ScoreReportStore {
  /** `device`: sealed in the phone's database; `memory`: no working keystore, so gone when the app closes. */
  readonly persistence: 'device' | 'memory';
  /** The saved report, or null when there is none or it can no longer be opened (its key deleted). */
  get(sessionId: string, language: ReportLanguage): Promise<ScoreReport | null>;
  /** Saves a report, replacing the one for the same session and language. */
  save(report: ScoreReport): Promise<void>;
  /** The saved parts for a session and language. A part that can no longer be opened is left out. */
  listParts(sessionId: string, language: ReportLanguage): Promise<StoredReportPart[]>;
  /** Saves a part, replacing the one with the same key. */
  savePart(sessionId: string, language: ReportLanguage, part: StoredReportPart): Promise<void>;
  /** Deletes a session's parts, once the finished report holds their words or the person has said to stop. */
  deleteParts(sessionId: string): Promise<void>;
  getChoice(sessionId: string): Promise<ReportChoice | null>;
  setChoice(sessionId: string, choice: ReportChoice): Promise<void>;
  /** Deletes every report, part and choice. It does not touch the key, which the drafts share. */
  clearAll(): Promise<void>;
}

const reportContext = (sessionId: string, language: ReportLanguage) => `score-report/${sessionId}/${language}`;
const partContext = (sessionId: string, language: ReportLanguage, key: string) =>
  `score-report-part/${sessionId}/${language}/${key}`;

/**
 * Reports sealed with the same keystore key as drafts (src/storage/protection). Clearing drafts deletes that key, so
 * whoever clears them clears this store first (createDraftOwnership); rows left behind could not be opened anyway.
 */
export function createEncryptedScoreReportStore(db: SqlDatabase, cipher: FieldCipher): ScoreReportStore {
  const serial = createSerialQueue();
  return {
    persistence: 'device',

    get: (sessionId, language) =>
      serial(async () => {
        const row = await db.getFirstAsync<{ report_cipher: string }>(
          'SELECT report_cipher FROM score_reports WHERE session_id = ? AND language = ?',
          sessionId,
          language,
        );
        if (!row) return null;
        try {
          const parsed = scoreReportSchema.safeParse(
            JSON.parse(await cipher.decrypt(row.report_cipher, reportContext(sessionId, language))),
          );
          return parsed.success ? parsed.data : null;
        } catch (error) {
          if (error instanceof UnreadableValueError || error instanceof SyntaxError) {
            return null;
          }
          throw error;
        }
      }),

    save: (report) =>
      serial(async () => {
        const sealed = await cipher.encrypt(JSON.stringify(report), reportContext(report.sessionId, report.language));
        await db.runAsync(
          'INSERT OR REPLACE INTO score_reports (session_id, language, report_cipher, created_at) VALUES (?, ?, ?, ?)',
          report.sessionId,
          report.language,
          sealed,
          report.generatedAt,
        );
      }),

    listParts: (sessionId, language) =>
      serial(async () => {
        const rows = await db.getAllAsync<{ part_key: string; part_cipher: string }>(
          'SELECT part_key, part_cipher FROM score_report_parts WHERE session_id = ? AND language = ?',
          sessionId,
          language,
        );
        const parts: StoredReportPart[] = [];
        for (const row of rows) {
          try {
            const parsed = storedPartSchema.safeParse(
              JSON.parse(await cipher.decrypt(row.part_cipher, partContext(sessionId, language, row.part_key))),
            );
            if (parsed.success) parts.push(parsed.data);
          } catch (error) {
            if (!(error instanceof UnreadableValueError || error instanceof SyntaxError)) {
              throw error;
            }
          }
        }
        return parts;
      }),

    savePart: (sessionId, language, part) =>
      serial(async () => {
        const sealed = await cipher.encrypt(JSON.stringify(part), partContext(sessionId, language, part.key));
        await db.runAsync(
          'INSERT OR REPLACE INTO score_report_parts (session_id, language, part_key, part_cipher) VALUES (?, ?, ?, ?)',
          sessionId,
          language,
          part.key,
          sealed,
        );
      }),

    deleteParts: (sessionId) =>
      serial(async () => {
        await db.runAsync('DELETE FROM score_report_parts WHERE session_id = ?', sessionId);
      }),

    getChoice: (sessionId) =>
      serial(async () => {
        const row = await db.getFirstAsync<{ choice: ReportChoice }>(
          'SELECT choice FROM score_report_choices WHERE session_id = ?',
          sessionId,
        );
        return row?.choice ?? null;
      }),

    setChoice: (sessionId, choice) =>
      serial(async () => {
        await db.runAsync('INSERT OR REPLACE INTO score_report_choices (session_id, choice) VALUES (?, ?)', sessionId, choice);
      }),

    clearAll: () =>
      serial(async () => {
        await db.runAsync('DELETE FROM score_reports');
        await db.runAsync('DELETE FROM score_report_parts');
        await db.runAsync('DELETE FROM score_report_choices');
      }),
  };
}

/** Without a working keystore a report and its parts live only as long as the app runs. */
export function createMemoryScoreReportStore(): ScoreReportStore {
  const reports = new Map<string, ScoreReport>();
  const parts = new Map<string, Map<string, StoredReportPart>>();
  const choices = new Map<string, ReportChoice>();
  return {
    persistence: 'memory',
    get: async (sessionId, language) => reports.get(reportContext(sessionId, language)) ?? null,
    save: async (report) => {
      reports.set(reportContext(report.sessionId, report.language), report);
    },
    listParts: async (sessionId, language) => [...(parts.get(reportContext(sessionId, language))?.values() ?? [])],
    savePart: async (sessionId, language, part) => {
      const context = reportContext(sessionId, language);
      parts.set(context, (parts.get(context) ?? new Map<string, StoredReportPart>()).set(part.key, part));
    },
    deleteParts: async (sessionId) => {
      for (const language of ['en', 'ar'] as const) parts.delete(reportContext(sessionId, language));
    },
    getChoice: async (sessionId) => choices.get(sessionId) ?? null,
    setChoice: async (sessionId, choice) => {
      choices.set(sessionId, choice);
    },
    clearAll: async () => {
      reports.clear();
      parts.clear();
      choices.clear();
    },
  };
}

/** The same rule as drafts (decision M-35): on the phone only when sealed, otherwise in memory. */
export function createScoreReportStore(db: SqlDatabase, protection: FieldCipher | null): ScoreReportStore {
  return protection ? createEncryptedScoreReportStore(db, protection) : createMemoryScoreReportStore();
}
