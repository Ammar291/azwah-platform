import {
  ASSISTANT_MAX_MEMORY_LENGTH,
  ASSISTANT_MAX_MESSAGE_LENGTH,
  ASSISTANT_MAX_MESSAGES,
  ASSISTANT_MAX_TOTAL_LENGTH,
} from '@/api/participant/participantApi';
import type { AssistantMessage } from '@/api/participant/schemas';

import type { ChatMessage } from './chatStore';

/**
 * What is sent to the assistant and at the start of a call (D-123): the latest messages within the server's limits,
 * with where each was said. A long message is cut to the per-message limit, empty ones are left out, and the oldest
 * are dropped until the whole fits; the latest message is always kept. A question sends the conversation with itself
 * last; a call sends whatever came last (0 to 40 messages).
 */
export function conversationForRequest(messages: readonly ChatMessage[]): AssistantMessage[] {
  const recent = messages
    .map(({ role, text, channel }) => ({ role, text: text.trim().slice(0, ASSISTANT_MAX_MESSAGE_LENGTH), channel }))
    .filter((message) => message.text.length > 0)
    .slice(-ASSISTANT_MAX_MESSAGES);
  let total = recent.reduce((sum, message) => sum + message.text.length, 0);
  while (recent.length > 1 && total > ASSISTANT_MAX_TOTAL_LENGTH) {
    total -= recent.shift()?.text.length ?? 0;
  }
  return recent;
}

/** The kept memory as the server accepts it: trimmed, at most 2,000 characters, null when there is nothing. */
export function memoryForRequest(memory: string | null | undefined): string | null {
  const text = memory?.trim().slice(0, ASSISTANT_MAX_MEMORY_LENGTH).trim() ?? '';
  return text.length > 0 ? text : null;
}
