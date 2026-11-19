import type { DraftPersistence } from './types';

/**
 * Where the latest answers are:
 * - `idle`: nothing has happened yet this run;
 * - `pending`: saved locally, about to be sent;
 * - `syncing`: being sent;
 * - `synced`: everything sendable is on the server;
 * - `offline`: no connection, sent when it returns;
 * - `waiting`: a failure, retried automatically after a backoff;
 * - `failed`: not sent, and not retried automatically (manual retry, or blocked by sign-in or consent);
 * - `rejected`: refused for good, so the person chooses again;
 * - `closed`: the session no longer takes answers (submitted elsewhere or closed);
 * - `gone`: the session is no longer available to this person.
 */
export type SyncPhase =
  | 'idle'
  | 'pending'
  | 'syncing'
  | 'synced'
  | 'offline'
  | 'waiting'
  | 'failed'
  | 'rejected'
  | 'closed'
  | 'gone';

/** Keys under `session.saveStatus` in the translation files. */
export type SaveStatus =
  | 'none'
  | 'savedOnDevice'
  | 'keptInMemory'
  | 'saving'
  | 'saved'
  | 'offlineOnDevice'
  | 'offlineInMemory'
  | 'retrying'
  | 'notSaved'
  | 'rejected'
  | 'conflict';

export interface SaveStatusInput {
  persistence: DraftPersistence;
  phase: SyncPhase;
  hasAnswers: boolean;
  hasConflicts: boolean;
}

/**
 * What to tell the person about their answers. Never claims "saved" before the server has them, and says plainly
 * when they live only in the app's memory.
 */
export function describeSaveStatus({ persistence, phase, hasAnswers, hasConflicts }: SaveStatusInput): SaveStatus {
  if (hasConflicts) {
    return 'conflict';
  }
  const onDevice = persistence === 'device';
  switch (phase) {
    case 'idle':
      return hasAnswers ? 'saved' : 'none';
    case 'pending':
      return onDevice ? 'savedOnDevice' : 'keptInMemory';
    case 'syncing':
      return 'saving';
    case 'synced':
      return 'saved';
    case 'offline':
      return onDevice ? 'offlineOnDevice' : 'offlineInMemory';
    case 'waiting':
      return 'retrying';
    case 'failed':
      return 'notSaved';
    case 'rejected':
      return 'rejected';
    case 'closed':
    case 'gone':
      return 'none';
  }
}
