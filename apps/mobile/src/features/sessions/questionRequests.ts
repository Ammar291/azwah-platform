import { useSyncExternalStore } from 'react';

/** A question the review screen asked the questionnaire to show. The key makes each request distinct. */
export interface QuestionRequest {
  index: number;
  key: string;
}

let sequence = 0;
const requests = new Map<string, QuestionRequest>();
const listeners = new Set<() => void>();

const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
};

/**
 * Asks the questionnaire for a session to show one question. The review screen calls this and then goes back, so the
 * questionnaire already under it moves to the question instead of a second questionnaire being opened.
 */
export function requestQuestion(sessionId: string, index: number): void {
  sequence += 1;
  requests.set(sessionId, { index, key: String(sequence) });
  listeners.forEach((listener) => listener());
}

/** Called once the questionnaire has moved, so a later visit starts at the first unanswered question again. */
export function clearQuestionRequest(sessionId: string, key: string): void {
  if (requests.get(sessionId)?.key === key) {
    requests.delete(sessionId);
    listeners.forEach((listener) => listener());
  }
}

export function useQuestionRequest(sessionId: string): QuestionRequest | null {
  return useSyncExternalStore(
    subscribe,
    () => requests.get(sessionId) ?? null,
    () => requests.get(sessionId) ?? null,
  );
}
