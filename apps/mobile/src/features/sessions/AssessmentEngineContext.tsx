import { createContext, useContext, useSyncExternalStore } from 'react';

import type { AssessmentEngine } from './assessmentEngine';
import type { SubmissionState } from './submissionQueue';

const AssessmentEngineContext = createContext<AssessmentEngine | null>(null);

export const AssessmentEngineProvider = AssessmentEngineContext.Provider;

export function useAssessmentEngine(): AssessmentEngine {
  const engine = useContext(AssessmentEngineContext);
  if (!engine) {
    throw new Error('useAssessmentEngine must be used inside AssessmentEngineProvider.');
  }
  return engine;
}

/** A session's submission as the queue sees it, re-rendering as it moves. */
export function useSubmissionState(sessionId: string | null): SubmissionState {
  const { submissions } = useAssessmentEngine();
  const read = () => (sessionId ? submissions.getState(sessionId) : IDLE);
  return useSyncExternalStore(submissions.subscribe, read, read);
}

const IDLE: SubmissionState = { status: 'idle' };
