import { act, renderHook, waitFor } from '@testing-library/react-native';
import type { ReactNode } from 'react';

import type { ParticipantApi } from '@/api/participant/participantApi';
import type { AssistantReply } from '@/api/participant/schemas';
import { createFixtureParticipantApi } from '@/dev-fixtures/fixtureParticipantApi';
import { PLACEHOLDER_CATALOG } from '@/test-utils/placeholderContent';
import { createTestProviders, settleQueries } from '@/test-utils/renderWithProviders';

import { createMemoryChatStore, type ChatMessage, type ChatStore } from '../chatStore';
import { useAssistantChat } from '../useAssistantChat';

afterEach(settleQueries);

const fixtureApi = () =>
  createFixtureParticipantApi({ getSubject: () => 'dev-mock|household_lead', latencyMs: 0, catalog: PLACEHOLDER_CATALOG });

const said = (id: number, role: ChatMessage['role'], text: string, channel: ChatMessage['channel'] = 'chat'): ChatMessage => ({
  id: `m${id}`,
  role,
  text,
  createdAt: new Date(Date.UTC(2026, 10, 9, 9, 0, id)).toISOString(),
  channel,
});

const source = {
  id: '0199e0a1-0000-7000-9000-000000002002',
  name: 'Household budget and debt advice',
  provider: 'Sample provider B',
  url: 'https://example.org/azwah-dev/dev_debt_advice',
};

async function renderChat(options: { api?: ParticipantApi; chat?: ChatStore } = {}) {
  const providers = await createTestProviders(options);
  const rendered = await renderHook(() => useAssistantChat(), {
    wrapper: ({ children }: { children: ReactNode }) => providers.wrap(children),
  });
  await waitFor(() => expect(rendered.result.current.loaded).toBe(true));
  return { ...rendered, chat: providers.chat };
}

async function type(result: { current: ReturnType<typeof useAssistantChat> }, text: string) {
  await act(async () => result.current.setDraft(text));
}

describe('the assistant chat', () => {
  it('sends a question once when Send is pressed twice before the screen updates', async () => {
    const fixture = fixtureApi();
    const requestAssistantReply = jest.fn(fixture.requestAssistantReply);
    const { result, chat } = await renderChat({ api: { ...fixture, requestAssistantReply } });
    await type(result, 'Ideas for family time?');

    await act(async () => {
      result.current.send();
      result.current.send();
    });

    await waitFor(() => expect(result.current.messages).toHaveLength(2));
    await waitFor(() => expect(result.current.sending).toBe(false));
    expect(requestAssistantReply).toHaveBeenCalledTimes(1);
    expect((await chat.list()).map((message) => message.role)).toEqual(['user', 'assistant']);
  });

  it('says something went wrong when saving the question fails, keeps it on the screen, and sends it on retry', async () => {
    const chat = createMemoryChatStore();
    jest.spyOn(chat, 'append').mockRejectedValueOnce(new Error('The disk is full.'));
    const fixture = fixtureApi();
    const requestAssistantReply = jest.fn(fixture.requestAssistantReply);
    const { result } = await renderChat({ chat, api: { ...fixture, requestAssistantReply } });
    await type(result, 'Are you there?');

    await act(async () => result.current.send());

    // The phone could not keep it, so it was not asked (D-139): the unexpected-error notice, not a failed request.
    await waitFor(() => expect(result.current.problem).toBe('unexpected'));
    expect(result.current.sending).toBe(false);
    expect(result.current.messages.map((message) => message.text)).toEqual(['Are you there?']);
    expect(requestAssistantReply).not.toHaveBeenCalled();

    await act(async () => result.current.retry());

    await waitFor(() => expect(result.current.messages).toHaveLength(2));
    expect(result.current.problem).toBeNull();
    expect(requestAssistantReply).toHaveBeenCalledTimes(1);
    expect((await chat.list()).map((message) => message.role)).toEqual(['user', 'assistant']);
  });

  it('keeps a question the phone could not keep before a newer one, so no reply is kept without its question', async () => {
    const chat = createMemoryChatStore();
    jest.spyOn(chat, 'append').mockRejectedValueOnce(new Error('The disk is full.'));
    const { result } = await renderChat({ chat });
    await type(result, 'First?');
    await act(async () => result.current.send());
    await waitFor(() => expect(result.current.problem).toBe('unexpected'));

    await type(result, 'Second?');
    await act(async () => result.current.send());

    await waitFor(() => expect(result.current.messages).toHaveLength(3));
    expect(result.current.problem).toBeNull();
    expect((await chat.list()).map((message) => [message.role, message.role === 'user' ? message.text : null])).toEqual([
      ['user', 'First?'],
      ['user', 'Second?'],
      ['assistant', null],
    ]);
  });

  it('says something went wrong when the reply cannot be kept, and shows only what was kept', async () => {
    const chat = createMemoryChatStore();
    const append = chat.append.bind(chat);
    jest
      .spyOn(chat, 'append')
      .mockImplementationOnce(append)
      .mockRejectedValueOnce(new Error('The disk is full.'));
    const { result } = await renderChat({ chat });
    await type(result, 'Are you there?');

    await act(async () => result.current.send());

    await waitFor(() => expect(result.current.problem).toBe('unexpected'));
    expect(result.current.messages.map((message) => message.role)).toEqual(['user']);
    expect((await chat.list()).map((message) => message.role)).toEqual(['user']);
  });

  it('offers to ask again for a question left without an answer when the app closed', async () => {
    const chat = createMemoryChatStore();
    await chat.appendMany([said(1, 'user', 'Hello'), said(2, 'assistant', 'Hello, how can I help?'), said(3, 'user', 'How do check-ins work?')]);
    const { result } = await renderChat({ chat });

    expect(result.current.problem).toBe('unanswered');
    await act(async () => result.current.retry());

    await waitFor(() => expect(result.current.messages).toHaveLength(4));
    expect(result.current.problem).toBeNull();
    expect(result.current.messages[3]?.role).toBe('assistant');
  });

  it("does not call a call's last line an unanswered question", async () => {
    const chat = createMemoryChatStore();
    await chat.appendMany([said(1, 'assistant', 'Hello', 'voice'), said(2, 'user', 'Thank you, bye', 'voice')]);
    const { result } = await renderChat({ chat });

    expect(result.current.messages).toHaveLength(2);
    expect(result.current.problem).toBeNull();
  });

  it("sends the kept memory with each question, and keeps the reply's memory, screens and sources", async () => {
    const chat = createMemoryChatStore();
    await chat.setMemory('Earlier: the family asked about family time.');
    const requestAssistantReply = jest.fn(
      async (): Promise<AssistantReply> => ({
        reply: 'The debt advice service can help.',
        generatedAt: '2026-11-09T10:00:00.000Z',
        actions: ['services'],
        sources: [source],
        memory: 'The family asked about family time, then debts.',
      }),
    );
    const { result } = await renderChat({ chat, api: { ...fixtureApi(), requestAssistantReply } });
    await type(result, 'Where can I get help with debts?');

    await act(async () => result.current.send());

    await waitFor(() => expect(result.current.messages).toHaveLength(2));
    expect(requestAssistantReply).toHaveBeenCalledWith({
      language: 'en',
      memory: 'Earlier: the family asked about family time.',
      messages: [{ role: 'user', text: 'Where can I get help with debts?', channel: 'chat' }],
    });
    expect(await chat.getMemory()).toBe('The family asked about family time, then debts.');
    const expected = {
      role: 'assistant',
      text: 'The debt advice service can help.',
      channel: 'chat',
      actions: ['services'],
      sources: [source],
    };
    expect(result.current.messages[1]).toMatchObject(expected);
    expect((await chat.list())[1]).toMatchObject(expected);
  });

  it('keeps the memory when a reply does not refresh it, and sends none when there is none', async () => {
    const chat = createMemoryChatStore();
    const requestAssistantReply = jest.fn(
      async (): Promise<AssistantReply> => ({ reply: 'Hello.', generatedAt: '2026-11-09T10:00:00.000Z', actions: [], sources: [], memory: null }),
    );
    const { result } = await renderChat({ chat, api: { ...fixtureApi(), requestAssistantReply } });
    await type(result, 'Hello');

    await act(async () => result.current.send());

    await waitFor(() => expect(result.current.messages).toHaveLength(2));
    expect(requestAssistantReply.mock.calls[0]).toEqual([
      { language: 'en', memory: null, messages: [{ role: 'user', text: 'Hello', channel: 'chat' }] },
    ]);
    expect(await chat.getMemory()).toBeNull();
    expect(result.current.messages[1]).not.toHaveProperty('actions');
  });

  it("shows a call's lines saved while the chat was open, and an emptied conversation", async () => {
    const { result, chat } = await renderChat();

    await act(async () => chat.appendMany([said(1, 'user', 'We argue a lot', 'voice'), said(2, 'assistant', 'Try a weekly talk.', 'voice')]));
    await waitFor(() => expect(result.current.messages.map((message) => message.channel)).toEqual(['voice', 'voice']));
    expect(result.current.problem).toBeNull();

    await act(async () => chat.clearAll());
    await waitFor(() => expect(result.current.messages).toEqual([]));
  });
});
