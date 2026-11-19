import { z } from 'zod';

import {
  assistantActionsSchema,
  assistantSourcesSchema,
  type AssistantAction,
  type AssistantChannel,
  type AssistantRole,
  type AssistantSource,
} from '@/api/participant/schemas';
import { createSerialQueue } from '@/drafts/serialQueue';
import { UnreadableValueError, type FieldCipher } from '@/storage/protection/fieldCipher';
import type { SqlDatabase } from '@/storage/sql';

export interface ChatMessage {
  id: string;
  role: AssistantRole;
  text: string;
  createdAt: string;
  /** Typed in the chat, or said in a call with the consultant and saved when the call ended (D-123). */
  channel: AssistantChannel;
  /** The app screens a reply suggested (D-122), shown as buttons with the app's own labels. */
  actions?: AssistantAction[];
  /** The directory entries a reply named, shown as cards that open their links. */
  sources?: AssistantSource[];
}

/** The conversation with the Azwah assistant, kept on this phone only (D-108, D-123). */
export interface ChatStore {
  /** `device`: sealed in the phone's database; `memory`: no working keystore, so gone when the app closes. */
  readonly persistence: 'device' | 'memory';
  /** Oldest first. A message that can no longer be opened (its key deleted) is left out. */
  list(): Promise<ChatMessage[]>;
  /** Adds a message, dropping the oldest beyond CHAT_HISTORY_LIMIT. */
  append(message: ChatMessage): Promise<void>;
  /** Adds messages in order, all or none (a call's lines when it ends), dropping the oldest beyond the limit. */
  appendMany(messages: readonly ChatMessage[]): Promise<void>;
  /** The rolling summary the server wrote (D-123), or null when there is none or it can no longer be opened. */
  getMemory(): Promise<string | null>;
  /** Replaces the summary; null deletes it. */
  setMemory(text: string | null): Promise<void>;
  /** Deletes the whole conversation and its memory. It does not touch the key, which the drafts share. */
  clearAll(): Promise<void>;
  /**
   * Calls `listener` after the messages change (added or cleared), whoever changed them: the chat re-reads them when
   * a call or a sign-out changed the conversation behind it. Returns the unsubscribe.
   */
  subscribe(listener: () => void): () => void;
}

/** The phone keeps at most this many messages. */
export const CHAT_HISTORY_LIMIT = 200;

const messageContext = (id: string) => `assistant/${id}`;
const extrasContext = (id: string) => `assistant/${id}/extras`;
/** Not `assistant/…`: no message id can ever open the memory. */
const MEMORY_CONTEXT = 'assistant-memory';

/** What extras_cipher holds, validated again when read: the column is ours, but nothing read back is trusted blindly. */
const extrasSchema = z.object({
  actions: assistantActionsSchema.default([]),
  sources: assistantSourcesSchema.default([]),
});

interface Row {
  id: string;
  role: AssistantRole;
  channel: AssistantChannel;
  text_cipher: string;
  extras_cipher: string | null;
  created_at: string;
}

function hasExtras(message: ChatMessage): boolean {
  return (message.actions?.length ?? 0) > 0 || (message.sources?.length ?? 0) > 0;
}

/** A copy without empty extras, so stored and remembered messages look the same. */
function normalized(message: ChatMessage): ChatMessage {
  const { actions, sources, ...rest } = message;
  return {
    ...rest,
    ...(actions?.length ? { actions: [...actions] } : {}),
    ...(sources?.length ? { sources: sources.map((source) => ({ ...source })) } : {}),
  };
}

function createListeners() {
  const listeners = new Set<() => void>();
  return {
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    notify() {
      for (const listener of [...listeners]) {
        try {
          listener();
        } catch {
          // A listener's failure is its own; the write has happened.
        }
      }
    },
  };
}

/**
 * Messages sealed with the same keystore key as drafts (src/storage/protection). Clearing drafts deletes that key, so
 * whoever clears them clears this store too (createDraftOwnership); rows left behind could not be opened anyway.
 */
export function createEncryptedChatStore(db: SqlDatabase, cipher: FieldCipher): ChatStore {
  const serial = createSerialQueue();
  const listeners = createListeners();

  const readExtras = async (row: Row): Promise<Pick<ChatMessage, 'actions' | 'sources'>> => {
    if (!row.extras_cipher) return {};
    try {
      const parsed = extrasSchema.safeParse(JSON.parse(await cipher.decrypt(row.extras_cipher, extrasContext(row.id))));
      if (!parsed.success) return {};
      return {
        ...(parsed.data.actions.length ? { actions: parsed.data.actions } : {}),
        ...(parsed.data.sources.length ? { sources: parsed.data.sources } : {}),
      };
    } catch (error) {
      // The message itself still opens; only what it suggested is lost.
      if (error instanceof UnreadableValueError || error instanceof SyntaxError) return {};
      throw error;
    }
  };

  const appendMany = (messages: readonly ChatMessage[]) =>
    serial(async () => {
      if (messages.length === 0) return;
      const sealed: { message: ChatMessage; text: string; extras: string | null }[] = [];
      for (const message of messages) {
        sealed.push({
          message,
          text: await cipher.encrypt(message.text, messageContext(message.id)),
          extras: hasExtras(message)
            ? await cipher.encrypt(
                JSON.stringify({ actions: message.actions ?? [], sources: message.sources ?? [] }),
                extrasContext(message.id),
              )
            : null,
        });
      }
      await db.withTransactionAsync(async () => {
        const last = await db.getFirstAsync<{ position: number | null }>(
          'SELECT MAX(position) AS position FROM assistant_messages',
        );
        let position = last?.position ?? 0;
        for (const { message, text, extras } of sealed) {
          position += 1;
          await db.runAsync(
            'INSERT INTO assistant_messages (id, position, role, channel, text_cipher, extras_cipher, created_at) VALUES (?, ?, ?, ?, ?, ?, ?)',
            message.id,
            position,
            message.role,
            message.channel,
            text,
            extras,
            message.createdAt,
          );
        }
        await db.runAsync('DELETE FROM assistant_messages WHERE position <= ?', position - CHAT_HISTORY_LIMIT);
      });
      listeners.notify();
    });

  return {
    persistence: 'device',

    list: () =>
      serial(async () => {
        const rows = await db.getAllAsync<Row>(
          'SELECT id, role, channel, text_cipher, extras_cipher, created_at FROM assistant_messages ORDER BY position',
        );
        const messages: ChatMessage[] = [];
        for (const row of rows) {
          try {
            messages.push({
              id: row.id,
              role: row.role,
              text: await cipher.decrypt(row.text_cipher, messageContext(row.id)),
              createdAt: row.created_at,
              channel: row.channel,
              ...(await readExtras(row)),
            });
          } catch (error) {
            if (!(error instanceof UnreadableValueError)) {
              throw error;
            }
          }
        }
        return messages;
      }),

    append: (message) => appendMany([message]),
    appendMany,

    getMemory: () =>
      serial(async () => {
        const row = await db.getFirstAsync<{ text_cipher: string }>('SELECT text_cipher FROM assistant_memory WHERE id = 1');
        if (!row) return null;
        try {
          return await cipher.decrypt(row.text_cipher, MEMORY_CONTEXT);
        } catch (error) {
          if (error instanceof UnreadableValueError) return null;
          throw error;
        }
      }),

    setMemory: (text) =>
      serial(async () => {
        if (text === null) {
          await db.runAsync('DELETE FROM assistant_memory');
          return;
        }
        const sealed = await cipher.encrypt(text, MEMORY_CONTEXT);
        await db.runAsync(
          'INSERT OR REPLACE INTO assistant_memory (id, text_cipher, updated_at) VALUES (1, ?, ?)',
          sealed,
          new Date().toISOString(),
        );
      }),

    clearAll: () =>
      serial(async () => {
        await db.withTransactionAsync(async () => {
          await db.runAsync('DELETE FROM assistant_messages');
          await db.runAsync('DELETE FROM assistant_memory');
        });
        listeners.notify();
      }),

    subscribe: listeners.subscribe,
  };
}

/** Without a working keystore the conversation lives only as long as the app runs. */
export function createMemoryChatStore(): ChatStore {
  let messages: ChatMessage[] = [];
  let memory: string | null = null;
  const listeners = createListeners();
  const appendMany = async (added: readonly ChatMessage[]) => {
    if (added.length === 0) return;
    messages = [...messages, ...added.map(normalized)].slice(-CHAT_HISTORY_LIMIT);
    listeners.notify();
  };
  return {
    persistence: 'memory',
    list: async () => messages.map(normalized),
    append: (message) => appendMany([message]),
    appendMany,
    getMemory: async () => memory,
    setMemory: async (text) => {
      memory = text;
    },
    clearAll: async () => {
      messages = [];
      memory = null;
      listeners.notify();
    },
    subscribe: listeners.subscribe,
  };
}

/** The same rule as drafts (decision M-35): on the phone only when sealed, otherwise in memory. */
export function createChatStore(db: SqlDatabase, protection: FieldCipher | null): ChatStore {
  return protection ? createEncryptedChatStore(db, protection) : createMemoryChatStore();
}
