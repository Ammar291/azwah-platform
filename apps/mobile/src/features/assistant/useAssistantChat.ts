import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useRef, useState } from 'react';

import { isApiError } from '@/api/client';
import { participantKeys, useAssistantAvailability } from '@/api/participant/queries';
import { useParticipantApi } from '@/api/participant/ParticipantApiContext';
import { createIdempotencyKey } from '@/features/sessions/idempotencyKey';
import { useLocalization } from '@/i18n/useLocalization';

import type { ChatMessage } from './chatStore';
import { useChatStore } from './ChatStoreContext';
import { conversationForRequest, memoryForRequest } from './conversation';

/**
 * Why the last question has no answer yet. `unanswered`: it was asked before the app closed and no reply came.
 * `unexpected`: the phone could not keep the question (so it was not asked) or the reply (so it is not shown). The
 * others are what stopped the last request.
 */
export type ChatProblem = 'unavailable' | 'offline' | 'rate_limited' | 'failed' | 'unanswered' | 'unexpected';

export interface AssistantChat {
  messages: ChatMessage[];
  /** The history has been read from the phone. */
  loaded: boolean;
  /** `false` when the server says no model is connected; `null` while unknown. */
  available: boolean | null;
  persistence: 'device' | 'memory';
  draft: string;
  setDraft: (text: string) => void;
  sending: boolean;
  problem: ChatProblem | null;
  /**
   * The server's safety screen matched a reply or an error (D-135): the crisis support card shows until it is dismissed
   * or the conversation is cleared. Kept in memory only.
   */
  crisisSupport: boolean;
  dismissCrisisSupport: () => void;
  send: () => void;
  /** Asks again for the reply to the last question (keeping it on the phone first if that had failed). */
  retry: () => void;
  clear: () => Promise<void>;
}

function problemOf(error: unknown): ChatProblem {
  if (!isApiError(error)) return 'failed';
  // 503 is how the assistant endpoint says no model is connected (D-108); no other 5xx code is read.
  if (error.status === 503) return 'unavailable';
  if (error.kind === 'rate_limited') return 'rate_limited';
  if (error.kind === 'network' || error.kind === 'timeout') return 'offline';
  return 'failed';
}

/** A question typed in the chat that has no reply after it. A call's last line is not one: calls answer themselves. */
function endsUnanswered(messages: readonly ChatMessage[]): boolean {
  const last = messages[messages.length - 1];
  return last?.role === 'user' && last.channel === 'chat';
}

/**
 * The conversation with the Azwah assistant (D-108, D-122, D-123). It lives on the phone with a rolling memory the
 * server writes; each question sends the memory and the latest messages, and the server adds what it holds about the
 * family, keeping nothing. The person's message is kept on the phone before it is sent, so a failed request can be
 * retried and a reply is never kept without its question; one question is in flight at a time, guarded synchronously
 * so two quick taps send it once.
 */
export function useAssistantChat(): AssistantChat {
  const store = useChatStore();
  const api = useParticipantApi();
  const { language } = useLocalization();
  const queryClient = useQueryClient();
  const availability = useAssistantAvailability();
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [draft, setDraft] = useState('');
  const [sending, setSending] = useState(false);
  const [problem, setProblem] = useState<ChatProblem | null>(null);
  const [crisisSupport, setCrisisSupport] = useState(false);
  const mounted = useRef(true);
  /** Set the moment a question is sent, before any state renders: the double-tap guard. */
  const inFlight = useRef(false);
  // Questions on screen that the phone has not kept yet, oldest first. Each attempt to ask keeps them first, so a
  // reply is never kept without the question it answers.
  const unsaved = useRef<ChatMessage[]>([]);

  // Reads the conversation from the phone: at the start, and whenever a call or a sign-out changed it behind the chat.
  const load = useCallback(
    () =>
      store.list().then(
        (history) => {
          if (!mounted.current) return;
          const pending = unsaved.current.filter((question) => !history.some((message) => message.id === question.id));
          const shown = pending.length > 0 ? [...history, ...pending] : history;
          setMessages(shown);
          setLoaded(true);
          setProblem((current) => {
            if (endsUnanswered(shown)) return current ?? 'unanswered';
            return current === 'unavailable' ? current : null;
          });
        },
        () => {
          if (mounted.current) setLoaded(true);
        },
      ),
    [store],
  );

  useEffect(() => {
    mounted.current = true;
    void load();
    // The chat's own writes happen while a question is in flight; only changes made elsewhere are read back.
    const unsubscribe = store.subscribe(() => {
      if (!inFlight.current) void load();
    });
    return () => {
      mounted.current = false;
      unsubscribe();
    };
  }, [load, store]);

  /** Keeps the questions not kept yet, then asks for the reply. `inFlight` is already set. */
  const deliver = useCallback(
    async (conversation: ChatMessage[]) => {
      const fail = (next: ChatProblem) => {
        if (mounted.current) setProblem(next);
      };
      try {
        // The phone could not keep a question: it is not asked yet. It stays on screen, the screen says something
        // went wrong, and Retry keeps it and asks it.
        while (unsaved.current.length > 0) {
          const [question] = unsaved.current;
          try {
            if (question) await store.append(question);
          } catch {
            fail('unexpected');
            return;
          }
          unsaved.current = unsaved.current.slice(1);
        }
        let answer: ChatMessage;
        let refreshed: string | null;
        try {
          const memory = memoryForRequest(await store.getMemory().catch(() => null));
          const response = await api.requestAssistantReply({
            language,
            memory,
            messages: conversationForRequest(conversation),
          });
          if (response.safetyNotice === 'crisis_support' && mounted.current) setCrisisSupport(true);
          answer = {
            id: createIdempotencyKey(),
            role: 'assistant',
            text: response.reply,
            createdAt: response.generatedAt,
            channel: 'chat',
            ...(response.actions.length > 0 ? { actions: response.actions } : {}),
            ...(response.sources.length > 0 ? { sources: response.sources } : {}),
          };
          refreshed = memoryForRequest(response.memory);
        } catch (error) {
          const next = problemOf(error);
          if (isApiError(error) && error.safetyNotice === 'crisis_support' && mounted.current) setCrisisSupport(true);
          if (next === 'unavailable') {
            void queryClient.invalidateQueries({ queryKey: participantKeys.assistantAvailability() });
          }
          fail(next);
          return;
        }
        if (refreshed) {
          // A memory that cannot be kept only means the next request carries the older one.
          await store.setMemory(refreshed).catch(() => undefined);
        }
        try {
          await store.append(answer);
        } catch {
          // A reply the phone could not keep is not shown: the screen shows what is kept, and Retry asks again.
          fail('unexpected');
          return;
        }
        if (mounted.current) setMessages((current) => [...current, answer]);
      } finally {
        inFlight.current = false;
        if (mounted.current) setSending(false);
      }
    },
    [api, language, queryClient, store],
  );

  const send = useCallback(() => {
    const text = draft.trim();
    if (!text || inFlight.current) return;
    inFlight.current = true;
    const question: ChatMessage = {
      id: createIdempotencyKey(),
      role: 'user',
      text,
      createdAt: new Date().toISOString(),
      channel: 'chat',
    };
    const conversation = [...messages, question];
    setMessages(conversation);
    setDraft('');
    setProblem(null);
    setSending(true);
    // Kept on the phone before it is sent (in `deliver`), so a failed request can be retried.
    unsaved.current = [...unsaved.current, question];
    void deliver(conversation);
  }, [deliver, draft, messages]);

  const retry = useCallback(() => {
    const last = messages[messages.length - 1];
    if (inFlight.current || last?.role !== 'user') return;
    inFlight.current = true;
    setProblem(null);
    setSending(true);
    void deliver(messages);
  }, [deliver, messages]);

  const clear = useCallback(async () => {
    await store.clearAll();
    unsaved.current = [];
    setMessages([]);
    setProblem(null);
    setCrisisSupport(false);
  }, [store]);

  const dismissCrisisSupport = useCallback(() => setCrisisSupport(false), []);

  return {
    messages,
    loaded,
    available: availability.data ? availability.data.assistant : null,
    persistence: store.persistence,
    draft,
    setDraft,
    sending,
    problem,
    crisisSupport,
    dismissCrisisSupport,
    send,
    retry,
    clear,
  };
}
