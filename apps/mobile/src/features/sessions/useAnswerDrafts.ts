import { useEffect, useMemo, useSyncExternalStore } from 'react';

import { useDraftStore } from '@/drafts/DraftStoreContext';
import { describeSaveStatus, type SaveStatus } from '@/drafts/saveStatus';
import { useAppLock } from '@/security/AppLockContext';

import type { AnswerSyncSnapshot, FlushOutcome } from './answerSync';
import { useAssessmentEngine } from './AssessmentEngineContext';
import type { SessionContent } from './useSessionContent';

export { SYNC_DELAY_MS } from './answerSync';

export interface AnswerDrafts {
  /** itemId → optionId: the server's latest answers overlaid with the unsent ones chosen on this phone. */
  answers: Readonly<Record<string, string>>;
  sync: AnswerSyncSnapshot;
  ready: boolean;
  saveStatus: SaveStatus;
  choose: (itemId: string, optionId: string) => Promise<void>;
  keepLocal: (itemId: string) => Promise<void>;
  keepServer: (itemId: string) => Promise<void>;
  /** Sends every unsent answer now. */
  flush: () => Promise<FlushOutcome>;
}

/**
 * A screen's view of one session's answers. Screens showing the same session share one AnswerSync, so the
 * questionnaire and the review never send at the same time and submission waits for every save.
 */
export function useAnswerDrafts(content: SessionContent): AnswerDrafts {
  const engine = useAssessmentEngine();
  const store = useDraftStore();
  const { touched } = useAppLock();
  const sessionId = content.session.id;
  const sync = useMemo(() => engine.syncFor(sessionId), [engine, sessionId]);
  const snapshot = useSyncExternalStore(sync.subscribe, sync.getSnapshot, sync.getSnapshot);

  useEffect(() => {
    sync.observeQuestions(content.items);
  }, [sync, content.items]);

  // Only answers read from the server count as its latest; the copy on the phone may be older.
  useEffect(() => {
    if (content.source === 'server') {
      sync.observeServer(content.serverAnswers);
    }
  }, [sync, content.source, content.serverAnswers]);

  const server = snapshot.server ?? content.serverAnswers;
  const answers = useMemo(
    () => ({
      ...Object.fromEntries(server.responses.map((answer) => [answer.itemId, answer.optionId])),
      ...snapshot.local,
    }),
    [server, snapshot.local],
  );

  return {
    answers,
    sync: snapshot,
    ready: snapshot.ready,
    saveStatus: describeSaveStatus({
      persistence: store.persistence,
      phase: snapshot.phase,
      hasAnswers: Object.keys(answers).length > 0,
      hasConflicts: snapshot.conflicts.length > 0,
    }),
    // Choosing is activity for the app lock's idle timer: keyboard and screen-reader users do not always touch.
    choose: (itemId, optionId) => {
      touched();
      return sync.choose(itemId, optionId);
    },
    keepLocal: (itemId) => {
      touched();
      return sync.keepLocal(itemId);
    },
    keepServer: (itemId) => {
      touched();
      return sync.keepServer(itemId);
    },
    flush: sync.flush,
  };
}
