import { screen, userEvent, waitFor, within } from '@testing-library/react-native';
import { Keyboard } from 'react-native';

import Assistant from '../../../../app/(app)/(drawer)/assistant';
import { ApiError } from '@/api/client';
import type { ParticipantApi } from '@/api/participant/participantApi';
import { createFixtureParticipantApi } from '@/dev-fixtures/fixtureParticipantApi';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';
import { PLACEHOLDER_CATALOG } from '@/test-utils/placeholderContent';

import { createMemoryChatStore, type ChatMessage } from '../chatStore';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
jest.mock('expo-web-browser', () => ({ openBrowserAsync: jest.fn(async () => ({ type: 'opened' })) }));
const { navigation, router, resetRouterMock } =
  jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');
const { openBrowserAsync } = jest.requireMock<{ openBrowserAsync: jest.Mock }>('expo-web-browser');

beforeEach(() => resetRouterMock());
afterEach(settleQueries);

/** The fixture API for the household lead, with some calls replaced. */
function api(overrides: Partial<ParticipantApi> = {}): ParticipantApi {
  return {
    ...createFixtureParticipantApi({ getSubject: () => 'dev-mock|household_lead', latencyMs: 0, catalog: PLACEHOLDER_CATALOG }),
    ...overrides,
  };
}

const said = (id: number, role: ChatMessage['role'], text: string, channel: ChatMessage['channel'] = 'chat'): ChatMessage => ({
  id: `m${id}`,
  role,
  text,
  createdAt: new Date(Date.UTC(2026, 9, 29, 9, 0, id)).toISOString(),
  channel,
});

const bubbles = () =>
  screen.queryAllByTestId(/^assistant-message-/).map((bubble) => bubble.props.accessibilityLabel as string);

async function ask(question: string) {
  const user = userEvent.setup();
  await user.type(await screen.findByLabelText('Your question'), question);
  await user.press(screen.getByRole('button', { name: 'Send' }));
}

describe('the Azwah assistant', () => {
  it('carries no simulated notice or development banner, and still answers (D-142)', async () => {
    await renderWithProviders(<Assistant />);

    expect(await screen.findByRole('header', { name: 'Azwah assistant' })).toBeOnTheScreen();
    expect(screen.queryByText(/simulated|Development/)).toBeNull();
    await ask('How does the family survey work?');

    await waitFor(() => expect(bubbles()).toHaveLength(2));
  });

  it('answers a question and keeps the conversation on this phone', async () => {
    const { chat } = await renderWithProviders(<Assistant />);

    expect(screen.getByRole('header', { name: 'Azwah assistant' })).toBeOnTheScreen();
    expect(screen.getByText(/Answers use what Azwah holds about your family/)).toBeOnTheScreen();
    await ask('How does the family survey work?');

    await waitFor(() => expect(bubbles()).toHaveLength(2));
    expect(bubbles()[0]).toBe('You: How does the family survey work?');
    // The fixture's reply says itself that it is simulated: that is what the stand-in server answers.
    expect(bubbles()[1]).toMatch(/^Azwah assistant: This is a simulated reply: no AI model is connected\./);
    expect((await chat.list()).map((message) => message.role)).toEqual(['user', 'assistant']);
    expect(screen.getByLabelText('Your question')).toHaveDisplayValue('');
  });

  it('shows the conversation kept on the phone, and clears it only when confirmed', async () => {
    const user = userEvent.setup();
    const chat = createMemoryChatStore();
    await chat.append(said(1, 'user', 'Hello'));
    await chat.append(said(2, 'assistant', 'Hello, how can I help?'));
    await renderWithProviders(<Assistant />, { chat });

    await waitFor(() => expect(bubbles()).toEqual(['You: Hello', 'Azwah assistant: Hello, how can I help?']));
    await user.press(screen.getByRole('button', { name: 'Clear' }));
    const dialog = within(screen.getByTestId('confirmation-dialog'));
    expect(dialog.getByText("It's deleted from this phone.")).toBeOnTheScreen();
    await user.press(dialog.getByRole('button', { name: 'Clear' }));

    await waitFor(() => expect(bubbles()).toEqual([]));
    expect(await chat.list()).toEqual([]);
  });

  it('sends only the latest 40 messages, with the question last, and keeps the memory a long chat brings', async () => {
    const chat = createMemoryChatStore();
    await chat.appendMany(
      Array.from({ length: 50 }, (_, index) => said(index + 1, index % 2 === 0 ? 'user' : 'assistant', `Message ${index + 1}`)),
    );
    const fixture = api();
    const requestAssistantReply = jest.fn(fixture.requestAssistantReply);
    await renderWithProviders(<Assistant />, { chat, api: { ...fixture, requestAssistantReply } });
    await waitFor(() => expect(bubbles()).toHaveLength(50));

    await ask('One more question');

    await waitFor(() => expect(requestAssistantReply).toHaveBeenCalledTimes(1));
    const [{ messages, language }] = requestAssistantReply.mock.calls[0]!;
    expect(language).toBe('en');
    expect(messages).toHaveLength(40);
    expect(messages.at(-1)).toEqual({ role: 'user', text: 'One more question', channel: 'chat' });
    await waitFor(() => expect(bubbles()).toHaveLength(52));
    expect(await chat.getMemory()).toBe('[Simulated summary for development] The family exchanged 40 messages with the assistant.');
  });

  it("shows the screens a reply suggests as buttons with the app's own labels", async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Assistant />);

    await ask('I feel unsafe at home');

    const actions = within(await screen.findByTestId('suggested-actions'));
    await user.press(actions.getByRole('button', { name: 'SOS' }));
    expect(router.push).toHaveBeenCalledWith('/emergency');
  });

  it('shows the directory entries a reply names as cards that open their page', async () => {
    const user = userEvent.setup();
    const { chat } = await renderWithProviders(<Assistant />);

    await ask('Where can I get help with debts?');

    const card = await screen.findByRole('button', {
      name: 'Example household budget and debt advice (sample entry), Sample provider B',
    });
    expect(screen.getByText('From the support directory')).toBeOnTheScreen();
    await user.press(card);
    expect(openBrowserAsync).toHaveBeenCalledWith('https://example.org/azwah-dev/dev_debt_advice');
    expect((await chat.list())[1]?.sources).toHaveLength(1);
  });

  it('marks the lines of a call with the consultant, and offers no screen it is already on', async () => {
    const chat = createMemoryChatStore();
    await chat.appendMany([
      said(1, 'user', 'We argue a lot', 'voice'),
      { ...said(2, 'assistant', 'Try a weekly family talk.', 'voice'), actions: ['assistant', 'resilience'] },
    ]);
    await renderWithProviders(<Assistant />, { chat });

    await waitFor(() => expect(bubbles()).toHaveLength(2));
    expect(screen.getAllByText('From a call')).toHaveLength(2);
    expect(screen.getByRole('button', { name: 'Family resilience' })).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Assistant' })).toBeNull();
    expect(screen.queryByTestId('assistant-problem')).toBeNull();
  });

  it('says when a question left by a closed app has no answer, and asks again', async () => {
    const user = userEvent.setup();
    const chat = createMemoryChatStore();
    await chat.append(said(1, 'user', 'How does a check-in work?'));
    await renderWithProviders(<Assistant />, { chat });

    expect(await screen.findByText('No answer yet.')).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Try again' }));

    await waitFor(() => expect(bubbles()).toHaveLength(2));
    expect(screen.queryByTestId('assistant-problem')).toBeNull();
  });

  it('says when no model is connected, and nothing can be sent', async () => {
    await renderWithProviders(<Assistant />, {
      api: api({ getAssistantAvailability: async () => ({ assistant: false, consultant: false, scoreReport: false }) }),
    });

    expect(await screen.findByTestId('assistant-unavailable')).toBeOnTheScreen();
    expect(screen.getByText('Not available yet')).toBeOnTheScreen();
    expect(screen.getByLabelText('Your question')).toHaveProp('editable', false);
    expect(screen.getByRole('button', { name: 'Send' })).toBeDisabled();
  });

  it('keeps the question when the server says no model is connected', async () => {
    const { chat } = await renderWithProviders(<Assistant />, {
      api: api({ requestAssistantReply: () => Promise.reject(new ApiError('server', 503)) }),
    });

    await ask('Are you there?');

    expect(await screen.findByTestId('assistant-unavailable')).toBeOnTheScreen();
    expect(bubbles()).toEqual(['You: Are you there?']);
    expect((await chat.list()).map((message) => message.text)).toEqual(['Are you there?']);
  });

  it('says so when the question cannot be kept on the phone, and asks it on another try', async () => {
    const user = userEvent.setup();
    const memory = createMemoryChatStore();
    const append = jest.fn(memory.append).mockRejectedValueOnce(new Error('storage'));
    const fixture = api();
    const requestAssistantReply = jest.fn(fixture.requestAssistantReply);
    await renderWithProviders(<Assistant />, {
      api: { ...fixture, requestAssistantReply },
      chat: { ...memory, append },
    });

    await ask('Ideas for family time?');

    // Never silent: the question is on screen, was not asked, and the screen says something went wrong.
    expect(await screen.findByTestId('assistant-problem')).toHaveTextContent(/Something went wrong/);
    expect(bubbles()).toEqual(['You: Ideas for family time?']);
    expect(requestAssistantReply).not.toHaveBeenCalled();

    await user.press(screen.getByRole('button', { name: 'Try again' }));

    await waitFor(() => expect(bubbles()).toHaveLength(2));
    expect(requestAssistantReply).toHaveBeenCalledTimes(1);
    expect(screen.queryByTestId('assistant-problem')).toBeNull();
    // The question was kept on the retry, before its reply: the phone never holds a reply without its question.
    expect((await memory.list()).map((message) => message.role)).toEqual(['user', 'assistant']);
  });

  it('says so when a reply cannot be kept on the phone, shows only what was kept, and asks again', async () => {
    const user = userEvent.setup();
    const memory = createMemoryChatStore();
    // The question is kept; the reply is not.
    const append = jest
      .fn(memory.append)
      .mockImplementationOnce(memory.append)
      .mockRejectedValueOnce(new Error('storage'));
    const fixture = api();
    const requestAssistantReply = jest.fn(fixture.requestAssistantReply);
    await renderWithProviders(<Assistant />, {
      api: { ...fixture, requestAssistantReply },
      chat: { ...memory, append },
    });

    await ask('Ideas for family time?');

    expect(await screen.findByTestId('assistant-problem')).toHaveTextContent(/Something went wrong/);
    expect(bubbles()).toEqual(['You: Ideas for family time?']);
    expect((await memory.list()).map((message) => message.role)).toEqual(['user']);

    await user.press(screen.getByRole('button', { name: 'Try again' }));

    await waitFor(() => expect(bubbles()).toHaveLength(2));
    expect(requestAssistantReply).toHaveBeenCalledTimes(2);
    expect((await memory.list()).map((message) => message.role)).toEqual(['user', 'assistant']);
  });

  it('shows the crisis support card in the conversation after the last message, closing the keyboard, until dismissed', async () => {
    const user = userEvent.setup();
    const fixture = api();
    const requestAssistantReply: ParticipantApi['requestAssistantReply'] = async (request) => ({
      ...(await fixture.requestAssistantReply(request)),
      actions: ['sos'],
      safetyNotice: 'crisis_support',
    });
    await renderWithProviders(<Assistant />, { api: { ...fixture, requestAssistantReply } });
    expect(screen.queryByTestId('crisis-support-card')).toBeNull();
    const dismissKeyboard = jest.spyOn(Keyboard, 'dismiss').mockImplementation(() => undefined);

    await ask('I feel unsafe');

    expect(await screen.findByTestId('crisis-support-card')).toBeOnTheScreen();
    expect(dismissKeyboard).toHaveBeenCalledTimes(1);
    // After the reply's bubble, so the screen following the end of the conversation brings it into view.
    const page = JSON.stringify(screen.toJSON());
    expect(page.indexOf('assistant-message-assistant')).toBeLessThan(page.indexOf('crisis-support-card'));
    expect(page.indexOf('crisis-support-card')).toBeLessThan(page.indexOf('assistant-input'));
    expect(screen.getByRole('button', { name: 'Dial 999' })).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Dismiss' }));
    expect(screen.queryByTestId('crisis-support-card')).toBeNull();
  });

  it('shows the card after a 502 or a 503 that carries the notice, and keeps the usual problem notice', async () => {
    const fixture = api();
    const requestAssistantReply = jest
      .fn<ReturnType<ParticipantApi['requestAssistantReply']>, Parameters<ParticipantApi['requestAssistantReply']>>()
      .mockRejectedValueOnce(new ApiError('server', 502, undefined, 'crisis_support'))
      .mockImplementation(fixture.requestAssistantReply);
    await renderWithProviders(<Assistant />, { api: { ...fixture, requestAssistantReply } });

    await ask('I feel unsafe');

    expect(await screen.findByTestId('crisis-support-card')).toBeOnTheScreen();
    expect(screen.getByTestId('assistant-problem')).toBeOnTheScreen();
  });

  it('shows the card with the unavailable notice when a 503 carries it', async () => {
    await renderWithProviders(<Assistant />, {
      api: api({ requestAssistantReply: () => Promise.reject(new ApiError('server', 503, undefined, 'crisis_support')) }),
    });

    await ask('I feel unsafe');

    expect(await screen.findByTestId('crisis-support-card')).toBeOnTheScreen();
    expect(screen.getByTestId('assistant-unavailable')).toBeOnTheScreen();
  });

  it('shows no card after a reply or an error without the notice', async () => {
    const fixture = api();
    const requestAssistantReply = jest
      .fn<ReturnType<ParticipantApi['requestAssistantReply']>, Parameters<ParticipantApi['requestAssistantReply']>>()
      .mockRejectedValueOnce(new ApiError('server', 502))
      .mockImplementation(async (request) => ({ ...(await fixture.requestAssistantReply(request)), safetyNotice: null }));
    const user = userEvent.setup();
    await renderWithProviders(<Assistant />, { api: { ...fixture, requestAssistantReply } });

    await ask('Ideas for family time?');
    expect(await screen.findByTestId('assistant-problem')).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Try again' }));

    await waitFor(() => expect(bubbles()).toHaveLength(2));
    expect(screen.queryByTestId('crisis-support-card')).toBeNull();
  });

  it('removes the card when the conversation is cleared', async () => {
    const user = userEvent.setup();
    const fixture = api();
    const requestAssistantReply: ParticipantApi['requestAssistantReply'] = async (request) => ({
      ...(await fixture.requestAssistantReply(request)),
      safetyNotice: 'crisis_support',
    });
    await renderWithProviders(<Assistant />, { api: { ...fixture, requestAssistantReply } });
    await ask('I feel unsafe');
    expect(await screen.findByTestId('crisis-support-card')).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Clear' }));
    await user.press(within(screen.getByTestId('confirmation-dialog')).getByRole('button', { name: 'Clear' }));

    await waitFor(() => expect(screen.queryByTestId('crisis-support-card')).toBeNull());
  });

  it('offers to try again when the connection or the rate limit stops a reply', async () => {
    const user = userEvent.setup();
    const fixture = api();
    const requestAssistantReply = jest
      .fn<ReturnType<ParticipantApi['requestAssistantReply']>, Parameters<ParticipantApi['requestAssistantReply']>>()
      .mockRejectedValueOnce(new ApiError('network'))
      .mockRejectedValueOnce(new ApiError('rate_limited', 429))
      .mockImplementation(fixture.requestAssistantReply);
    await renderWithProviders(<Assistant />, { api: { ...fixture, requestAssistantReply } });

    await ask('Ideas for family time?');
    expect(await screen.findByText("Can't reach Azwah. Check your connection.")).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    expect(
      await screen.findByText("Too many questions. Wait a minute."),
    ).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Try again' }));

    await waitFor(() => expect(bubbles()).toHaveLength(2));
    expect(screen.queryByTestId('assistant-problem')).toBeNull();
  });

  it('puts an example question in the box, links to the emergency numbers, and opens the side menu', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Assistant />);

    await user.press(await screen.findByRole('button', { name: 'How does a check-in work?' }));
    expect(screen.getByLabelText('Your question')).toHaveDisplayValue('How does a check-in work?');
    await user.press(screen.getByRole('button', { name: 'Emergency numbers' }));
    await user.press(screen.getByRole('button', { name: 'Open the menu' }));

    expect(router.push).toHaveBeenCalledWith('/emergency');
    expect(navigation.dispatch).toHaveBeenCalledWith({ type: 'OPEN_DRAWER' });
  });

  it('reads in Arabic', async () => {
    await renderWithProviders(<Assistant />, { language: 'ar' });

    expect(screen.getByRole('header', { name: 'مساعد عزوة' })).toBeOnTheScreen();
    expect(await screen.findByLabelText('سؤالك')).toBeOnTheScreen();
  });
});
