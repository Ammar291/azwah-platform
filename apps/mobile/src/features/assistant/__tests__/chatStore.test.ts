import { createEncryptedSqliteDraftStore } from '@/drafts/encryptedSqliteDraftStore';
import { migrate } from '@/storage/migrations';
import { createFieldCipher } from '@/storage/protection/fieldCipher';
import { createMemorySecretStore } from '@/storage/testing/memorySecretStore';
import { createNodeAesGcm } from '@/storage/testing/nodeAesGcm';
import { NodeSqliteDatabase } from '@/storage/testing/NodeSqliteDatabase';

import {
  CHAT_HISTORY_LIMIT,
  createEncryptedChatStore,
  createMemoryChatStore,
  type ChatMessage,
  type ChatStore,
} from '../chatStore';
import { conversationForRequest, memoryForRequest } from '../conversation';

const message = (index: number, role: ChatMessage['role'] = 'user', channel: ChatMessage['channel'] = 'chat'): ChatMessage => ({
  id: `m${index}`,
  role,
  text: `Message ${index}: how can our family spend more time together?`,
  createdAt: new Date(Date.UTC(2026, 9, 29, 9, 0, index)).toISOString(),
  channel,
});

const source = {
  id: '0199e0a1-0000-7000-9000-000000002002',
  name: 'Household budget and debt advice',
  provider: 'Sample provider B',
  url: 'https://example.org/azwah-dev/dev_debt_advice',
};

const reply = (index: number): ChatMessage => ({
  ...message(index, 'assistant'),
  text: `Reply ${index}: the debt advice service can help.`,
  actions: ['services', 'sos'],
  sources: [source],
});

async function phone() {
  const db = new NodeSqliteDatabase();
  await migrate(db);
  const secrets = createMemorySecretStore();
  const cipher = () => createFieldCipher({ aes: createNodeAesGcm(), secrets });
  // One cipher for both, as createServices wires them: clearing the drafts deletes the key for both.
  const shared = cipher();
  return { db, chat: createEncryptedChatStore(db, shared), drafts: createEncryptedSqliteDraftStore(db, shared), cipher };
}

describe('the encrypted chat store', () => {
  it('keeps the conversation in order across a restart, and never in clear text', async () => {
    const { db, chat, cipher } = await phone();
    await chat.append(message(1));
    await chat.append(message(2, 'assistant'));

    const reopened = createEncryptedChatStore(db, cipher());
    expect(await reopened.list()).toEqual([message(1), message(2, 'assistant')]);
    const rows = await db.getAllAsync<{ text_cipher: string }>('SELECT text_cipher FROM assistant_messages');
    expect(rows.map((row) => row.text_cipher.startsWith('v1:'))).toEqual([true, true]);
    expect(JSON.stringify(rows)).not.toContain('family');
    db.close();
  });

  it("keeps where each message was said and a reply's suggested screens and sources, sealed", async () => {
    const { db, chat, cipher } = await phone();
    await chat.append(message(1, 'user', 'voice'));
    await chat.append(reply(2));

    expect(await createEncryptedChatStore(db, cipher()).list()).toEqual([message(1, 'user', 'voice'), reply(2)]);
    const rows = await db.getAllAsync<{ channel: string; extras_cipher: string | null }>(
      'SELECT channel, extras_cipher FROM assistant_messages ORDER BY position',
    );
    expect(rows.map((row) => row.channel)).toEqual(['voice', 'chat']);
    expect(rows[0]?.extras_cipher).toBeNull();
    expect(rows[1]?.extras_cipher?.startsWith('v1:')).toBe(true);
    expect(JSON.stringify(rows)).not.toMatch(/debt|services|example\.org/);
    db.close();
  });

  it("adds a call's lines in order in one go", async () => {
    const { db, chat } = await phone();
    await chat.append(message(1));

    await chat.appendMany([message(2, 'user', 'voice'), message(3, 'assistant', 'voice'), message(4, 'user', 'voice')]);

    expect((await chat.list()).map((item) => `${item.id}:${item.channel}`)).toEqual([
      'm1:chat',
      'm2:voice',
      'm3:voice',
      'm4:voice',
    ]);
    db.close();
  });

  it('adds none of the lines when one cannot be written', async () => {
    const { db, chat } = await phone();
    await chat.append(message(1));

    // The same id twice breaks the primary key on the second insert, inside the one transaction.
    await expect(chat.appendMany([message(2, 'user', 'voice'), message(2, 'assistant', 'voice')])).rejects.toThrow();

    expect((await chat.list()).map((item) => item.id)).toEqual(['m1']);
    db.close();
  });

  it(`keeps only the latest ${CHAT_HISTORY_LIMIT} messages`, async () => {
    const { db, chat } = await phone();
    for (let index = 1; index <= CHAT_HISTORY_LIMIT - 2; index += 1) {
      await chat.append(message(index));
    }
    await chat.appendMany([message(1001), message(1002), message(1003), message(1004), message(1005)]);

    const kept = await chat.list();
    expect(kept).toHaveLength(CHAT_HISTORY_LIMIT);
    expect(kept[0]?.id).toBe('m4');
    expect(kept.at(-1)?.id).toBe('m1005');
    db.close();
  });

  it('keeps the memory sealed, replaces it, and deletes it', async () => {
    const { db, chat, cipher } = await phone();
    expect(await chat.getMemory()).toBeNull();

    await chat.setMemory('The family asked about debts and family time.');
    await chat.setMemory('The family asked about debts.');

    expect(await createEncryptedChatStore(db, cipher()).getMemory()).toBe('The family asked about debts.');
    const rows = await db.getAllAsync<{ text_cipher: string }>('SELECT text_cipher FROM assistant_memory');
    expect(rows).toHaveLength(1);
    expect(rows[0]?.text_cipher.startsWith('v1:')).toBe(true);
    expect(JSON.stringify(rows)).not.toContain('debts');

    await chat.setMemory(null);
    expect(await chat.getMemory()).toBeNull();
    db.close();
  });

  it('clears the conversation and its memory without touching the key the drafts share', async () => {
    const { db, chat, drafts } = await phone();
    await chat.append(message(1));
    await chat.setMemory('A summary');
    await drafts.saveAnswer({ sessionId: 's1', itemId: 'i1', optionId: 'o1', baseModifiedAt: null, at: new Date() });

    await chat.clearAll();

    expect(await chat.list()).toEqual([]);
    expect(await chat.getMemory()).toBeNull();
    expect((await drafts.listPending('s1')).map((draft) => draft.optionId)).toEqual(['o1']);
    db.close();
  });

  it('leaves out messages and the memory whose key was deleted with the drafts', async () => {
    const { db, chat, drafts } = await phone();
    await chat.append(reply(1));
    await chat.setMemory('A summary');

    await drafts.clearAll();

    expect(await chat.list()).toEqual([]);
    expect(await chat.getMemory()).toBeNull();
    db.close();
  });

  it('tells subscribers when the messages change, until they unsubscribe', async () => {
    const { db, chat } = await phone();
    await notifies(chat);
    db.close();
  });
});

async function notifies(chat: ChatStore) {
  const listener = jest.fn();
  const unsubscribe = chat.subscribe(listener);

  await chat.append(message(1));
  await chat.appendMany([message(2, 'user', 'voice'), message(3, 'assistant', 'voice')]);
  await chat.setMemory('A summary');
  await chat.clearAll();
  expect(listener).toHaveBeenCalledTimes(3);

  unsubscribe();
  await chat.append(message(4));
  expect(listener).toHaveBeenCalledTimes(3);
}

describe('the memory chat store', () => {
  it('keeps the latest messages and the memory until cleared', async () => {
    const chat = createMemoryChatStore();
    for (let index = 1; index <= CHAT_HISTORY_LIMIT + 1; index += 1) {
      await chat.append(message(index));
    }
    await chat.appendMany([message(500, 'user', 'voice'), reply(501)]);
    await chat.setMemory('A summary');
    expect(chat.persistence).toBe('memory');
    expect(await chat.list()).toHaveLength(CHAT_HISTORY_LIMIT);
    expect((await chat.list()).slice(-2)).toEqual([message(500, 'user', 'voice'), reply(501)]);
    expect(await chat.getMemory()).toBe('A summary');

    await chat.clearAll();
    expect(await chat.list()).toEqual([]);
    expect(await chat.getMemory()).toBeNull();
  });

  it('tells subscribers when the messages change', async () => {
    await notifies(createMemoryChatStore());
  });
});

describe('what a question or a call sends', () => {
  it("stays within the server's limits: 40 messages, 4,000 characters each, 20,000 in all, the latest last", () => {
    const long = (index: number, role: ChatMessage['role']): ChatMessage => ({ ...message(index, role), text: 'x'.repeat(9000) });
    const conversation = [
      ...Array.from({ length: 24 }, (_, index) => long(index, index % 2 === 0 ? 'user' : 'assistant')),
      message(99),
    ];

    const sent = conversationForRequest(conversation);

    expect(sent.every((item) => item.text.length <= 4000)).toBe(true);
    expect(sent.reduce((sum, item) => sum + item.text.length, 0)).toBeLessThanOrEqual(20000);
    expect(sent).toHaveLength(5);
    expect(sent.at(-1)).toEqual({ role: 'user', text: message(99).text, channel: 'chat' });
  });

  it('sends the latest 40 messages with where each was said, and nothing for an empty conversation', () => {
    const conversation = Array.from({ length: 60 }, (_, index) =>
      message(index, index % 2 === 0 ? 'user' : 'assistant', index % 3 === 0 ? 'voice' : 'chat'),
    );

    const sent = conversationForRequest(conversation);

    expect(sent).toHaveLength(40);
    expect(sent[0]).toEqual({ role: 'user', text: message(20).text, channel: 'chat' });
    expect(sent.at(-1)).toEqual({ role: 'assistant', text: message(59).text, channel: 'chat' });
    expect(sent.filter((item) => item.channel === 'voice')).toHaveLength(13);
    expect(conversationForRequest([])).toEqual([]);
    expect(conversationForRequest([{ ...message(1), text: '   ' }])).toEqual([]);
  });

  it('sends the memory trimmed to 2,000 characters, and nothing when there is none', () => {
    expect(memoryForRequest('  A summary  ')).toBe('A summary');
    expect(memoryForRequest('y'.repeat(2500))).toHaveLength(2000);
    expect(memoryForRequest('   ')).toBeNull();
    expect(memoryForRequest(null)).toBeNull();
  });
});
