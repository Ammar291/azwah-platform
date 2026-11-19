import type { ChatStore } from '@/features/assistant/chatStore';
import type { ScoreReportStore } from '@/features/score-report/reportStore';
import type { AssessmentEngine } from '@/features/sessions/assessmentEngine';
import type { DataFileSharing } from '@/storage/exportFiles';
import type { SettingsRepository } from '@/storage/settings';

export interface DraftOwnership {
  /** Someone signed in: keep the local data if it is this account's, otherwise delete it first. */
  claim(subject: string): Promise<void>;
  /** Signed out: delete everything once writes still running have finished. */
  forget(): Promise<void>;
}

/**
 * Drafts, session copies, queued submissions, the assistant conversation and written score reports hold sensitive
 * information and belong to one account. An expired session
 * keeps them for the same person to carry on, but a different account signing in on the phone never sees them. The
 * owner is the token subject (an account identifier, not a credential), kept in settings. Deleting everything also
 * deletes the key, so nothing left behind can be read (decision M-36). A data export still in the cache goes too.
 * Failures leave the owner unchanged, so the next sign-in by anyone else still deletes the data.
 */
export function createDraftOwnership(
  engine: Pick<AssessmentEngine, 'store' | 'settle' | 'reset'>,
  settings: Pick<SettingsRepository, 'get' | 'set' | 'remove'>,
  exportFiles?: Pick<DataFileSharing, 'clearAll'>,
  chat?: Pick<ChatStore, 'clearAll'>,
  reports?: Pick<ScoreReportStore, 'clearAll'>,
): DraftOwnership {
  const deleteAll = async () => {
    await exportFiles?.clearAll();
    // Before the drafts: clearing them deletes the key the conversation and the reports are sealed with too.
    await chat?.clearAll();
    await reports?.clearAll();
    await engine.settle();
    engine.reset();
    await engine.store.clearAll();
  };
  return {
    async claim(subject) {
      try {
        if ((await settings.get('drafts_owner')) === subject) {
          return;
        }
        await deleteAll();
        await settings.set('drafts_owner', subject);
      } catch {
        // Owner unchanged: checked again at the next sign-in.
      }
    },
    async forget() {
      try {
        await deleteAll();
        await settings.remove('drafts_owner');
      } catch {
        // Owner kept, so any other account signing in next still deletes this data.
      }
    },
  };
}
