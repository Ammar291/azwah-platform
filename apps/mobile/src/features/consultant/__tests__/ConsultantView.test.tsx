import { act, screen, userEvent, waitFor, within } from '@testing-library/react-native';
import { Linking } from 'react-native';

import { createMemoryChatStore, type ChatMessage } from '@/features/assistant/chatStore';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';

import type { ConsultantCallbacks, ConsultantConnector, ConsultantStart } from '../consultantConnector';
import { ConsultantView } from '../ConsultantView';
import { level } from '../pcm';
import { CONNECT_TIMEOUT_MS } from '../useConsultantCall';
import type { VoiceAudio, VoiceAudioCallbacks, VoiceAudioCapabilities } from '../voiceAudio';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { router, resetRouterMock } = jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

jest.mock('expo-audio', () => ({
  useAudioStream: () => ({ stream: { start: async () => undefined, stop: () => undefined }, isStreaming: false }),
  requestRecordingPermissionsAsync: jest.fn(async () => ({ granted: true })),
  setAudioModeAsync: jest.fn(async () => undefined),
}));
const audioModule = jest.requireMock<{ requestRecordingPermissionsAsync: jest.Mock }>('expo-audio');

beforeEach(() => {
  resetRouterMock();
  audioModule.requestRecordingPermissionsAsync.mockResolvedValue({ granted: true });
});
afterEach(settleQueries);

/** A connector the test drives by hand: it records what the app sends and exposes the callbacks. */
function scripted() {
  const state: {
    callbacks: ConsultantCallbacks | null;
    start: ConsultantStart | null;
    sent: ArrayBuffer[];
    ended: number;
    language: string | null;
  } = { callbacks: null, start: null, sent: [], ended: 0, language: null };
  const connector: ConsultantConnector = async (language, start, callbacks) => {
    state.callbacks = callbacks;
    state.start = start;
    state.language = language;
    return {
      sendAudio: (pcm) => state.sent.push(pcm),
      end: () => {
        state.ended += 1;
        callbacks.onClosed();
      },
    };
  };
  const server = () => {
    if (!state.callbacks) throw new Error('Not connected yet.');
    return state.callbacks;
  };
  /** The loudness of each frame sent, in order. */
  const sentLevels = () => state.sent.map((frame) => Math.round(level(new Int16Array(frame))));
  return { connector, state, server, sentLevels };
}

/** The phone's audio, driven by the test: it records what plays and feeds the microphone. */
function fakeVoice(capabilities: VoiceAudioCapabilities = { echoCancellation: true, streaming: true }) {
  const state = {
    rates: [] as number[],
    played: [] as ArrayBuffer[],
    flushed: 0,
    cleared: 0,
    stopped: 0,
    playing: false,
    callbacks: null as VoiceAudioCallbacks | null,
  };
  const setPlaying = (playing: boolean) => {
    state.playing = playing;
    state.callbacks?.onPlayingChange(playing);
  };
  const voice: VoiceAudio = {
    kind: capabilities.echoCancellation ? 'native' : 'expo_audio',
    async start({ sampleRate }, callbacks) {
      state.rates.push(sampleRate);
      state.callbacks = callbacks;
      return {
        capabilities,
        sampleRate,
        play: (pcm) => state.played.push(pcm),
        flush: () => {
          state.flushed += 1;
        },
        clear: () => {
          state.cleared += 1;
          setPlaying(false);
        },
        isPlaying: () => state.playing,
        stop: () => {
          state.stopped += 1;
        },
      };
    },
  };
  /** 100 ms of microphone at 24 kHz at the given loudness. */
  const speak = (amplitude: number) => state.callbacks?.onInput(new Int16Array(2400).fill(amplitude));
  return { voice, state, setPlaying, speak };
}

/** 100 ms of the consultant's voice at 24 kHz. */
const reply = (amplitude: number) => new Int16Array(2400).fill(amplitude).buffer;

const said = (id: number, role: ChatMessage['role'], text: string, channel: ChatMessage['channel'] = 'chat'): ChatMessage => ({
  id: `m${id}`,
  role,
  text,
  createdAt: new Date(Date.UTC(2026, 10, 9, 9, 0, id)).toISOString(),
  channel,
});

async function startCall(options: { capabilities?: VoiceAudioCapabilities; chat?: ReturnType<typeof createMemoryChatStore> } = {}) {
  const user = userEvent.setup();
  const call = scripted();
  const audio = fakeVoice(options.capabilities);
  const chat = options.chat ?? createMemoryChatStore();
  const rendering = await renderWithProviders(<ConsultantView voice={audio.voice} />, { consultant: call.connector, chat });
  await user.press(screen.getByRole('button', { name: 'Start talking' }));
  return { user, call, audio, chat, rendering };
}

async function goLive(call: ReturnType<typeof scripted>) {
  await act(async () => call.server().onReady({ inputSampleRate: 24000, outputSampleRate: 24000 }));
}

const status = () => screen.getByTestId('consultant-status');

describe('the Azwah consultant', () => {
  it("starts with the chat's memory and latest messages, streams the microphone, speaks with captions, and ends", async () => {
    const chat = createMemoryChatStore();
    await chat.appendMany([said(1, 'user', 'How can we argue less?'), said(2, 'assistant', 'Try a weekly talk.', 'voice')]);
    await chat.setMemory('The family asked about arguments.');
    const { user, call, audio } = await startCall({ chat });

    expect(screen.getByText('Connecting…')).toBeOnTheScreen();
    expect(call.state.language).toBe('en');
    expect(call.state.start).toEqual({
      memory: 'The family asked about arguments.',
      messages: [
        { role: 'user', text: 'How can we argue less?', channel: 'chat' },
        { role: 'assistant', text: 'Try a weekly talk.', channel: 'voice' },
      ],
    });
    await goLive(call);
    expect(audio.state.rates).toEqual([24000]);
    expect(status()).toHaveTextContent('Listening…');
    expect(screen.queryByTestId('consultant-headphones')).toBeNull();

    await act(async () => audio.speak(5000));
    expect(call.sentLevels()).toEqual([5000]);
    expect(status()).toHaveTextContent("You're speaking");

    await act(async () => {
      call.server().onTranscript({ speaker: 'user', text: 'And with the children?', final: true });
      call.server().onTranscript({ speaker: 'consultant', text: 'Try', final: false });
      call.server().onTranscript({ speaker: 'consultant', text: 'Try a family meeting.', final: true });
      call.server().onAudio(reply(3000));
      audio.setPlaying(true);
      call.server().onTurnEnd();
    });
    expect(screen.getByRole('header', { name: 'Captions' })).toBeOnTheScreen();
    expect(screen.getByText('And with the children?')).toBeOnTheScreen();
    expect(screen.getByText('Try a family meeting.')).toBeOnTheScreen();
    expect(screen.queryByText('Try')).toBeNull();
    expect(audio.state.played).toHaveLength(1);
    expect(audio.state.flushed).toBe(1);
    expect(status()).toHaveTextContent('Consultant speaking');

    // With echo cancellation the microphone stays open while the consultant speaks (full duplex).
    await act(async () => audio.speak(1500));
    expect(call.sentLevels()).toEqual([5000, 1500]);
    await act(async () => audio.setPlaying(false));

    await user.press(screen.getByRole('button', { name: 'End' }));
    expect(call.state.ended).toBe(1);
    expect(screen.getByText('Call ended')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Talk again' })).toBeOnTheScreen();
    expect(audio.state.stopped).toBe(1);
    // The call's lines join the chat, in order, so the assistant remembers them.
    await waitFor(async () => expect(await chat.list()).toHaveLength(4));
    expect((await chat.list()).slice(2).map(({ role, text, channel }) => ({ role, text, channel }))).toEqual([
      { role: 'user', text: 'And with the children?', channel: 'voice' },
      { role: 'assistant', text: 'Try a family meeting.', channel: 'voice' },
    ]);
  });

  it('keeps sending silence of the same length while muted, so the call stays open', async () => {
    const { user, call, audio } = await startCall();
    await goLive(call);

    await user.press(screen.getByRole('button', { name: 'Mute' }));
    expect(status()).toHaveTextContent('Muted');
    await act(async () => {
      audio.speak(5000);
      audio.speak(5000);
    });

    expect(call.sentLevels()).toEqual([0, 0]);
    expect(call.state.sent.map((frame) => frame.byteLength)).toEqual([4800, 4800]);
    await user.press(screen.getByRole('button', { name: 'Unmute' }));
    await act(async () => audio.speak(5000));
    expect(call.sentLevels()).toEqual([0, 0, 5000]);
  });

  it("shows the whole call's transcript, each growing line replaced in place", async () => {
    const { call } = await startCall();
    await goLive(call);

    await act(async () => {
      for (let index = 1; index <= 8; index += 1) {
        call.server().onTranscript({ speaker: index % 2 ? 'user' : 'consultant', text: `Line ${index}`, final: true });
      }
      // Both speak at once: each growing line stays where it started.
      call.server().onTranscript({ speaker: 'consultant', text: 'Well', final: false });
      call.server().onTranscript({ speaker: 'user', text: 'Wait', final: false });
      call.server().onTranscript({ speaker: 'consultant', text: 'Well, as I said', final: false });
      call.server().onTranscript({ speaker: 'user', text: 'Wait, one more thing', final: true });
    });

    const transcript = within(screen.getByTestId('consultant-transcript'));
    const texts = transcript
      .getAllByText(/^(Line \d|Well|Wait)/)
      .map((node) => node.props.children as string);
    expect(texts).toEqual([
      'Line 1',
      'Line 2',
      'Line 3',
      'Line 4',
      'Line 5',
      'Line 6',
      'Line 7',
      'Line 8',
      'Well, as I said',
      'Wait, one more thing',
    ]);
  });

  it('without echo cancellation sends silence instead of the echo, lets the person talk over the reply, and suggests headphones', async () => {
    const { call, audio } = await startCall({ capabilities: { echoCancellation: false, streaming: false } });
    await goLive(call);
    expect(screen.getByText('Headphones sound best.')).toBeOnTheScreen();

    // The reply plays and the microphone hears only its echo.
    await act(async () => {
      audio.setPlaying(true);
      for (let index = 0; index < 10; index += 1) {
        call.server().onAudio(reply(8000));
        audio.speak(3200);
      }
    });
    expect(new Set(call.sentLevels())).toEqual(new Set([0]));
    expect(status()).toHaveTextContent('Consultant speaking');

    // The person talks clearly over it: the reply stops and their voice is sent.
    await act(async () => {
      for (let index = 0; index < 3; index += 1) {
        call.server().onAudio(reply(8000));
        audio.speak(15000);
      }
    });
    expect(audio.state.cleared).toBe(1);
    expect(call.sentLevels().slice(-3)).toEqual([0, 0, 15000]);
    expect(status()).toHaveTextContent("You're speaking");

    // The rest of the interrupted reply is not played; the next one is.
    const played = audio.state.played.length;
    await act(async () => call.server().onAudio(reply(8000)));
    expect(audio.state.played).toHaveLength(played);
    await act(async () => {
      call.server().onTurnEnd();
      call.server().onAudio(reply(8000));
    });
    expect(audio.state.played).toHaveLength(played + 1);
  });

  it('shows the crisis support card over the call without ending it, until dismissed', async () => {
    const openURL = jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
    const { user, call } = await startCall();
    await goLive(call);
    expect(screen.queryByTestId('crisis-support-card')).toBeNull();

    await act(async () => {
      call.server().onSafety('crisis_support');
      call.server().onAction('sos');
    });

    expect(screen.getByTestId('crisis-support-card')).toBeOnTheScreen();
    expect(screen.getByText("You don't have to face this alone")).toBeOnTheScreen();
    expect(status()).toHaveTextContent('Listening…');
    expect(call.state.ended).toBe(0);
    expect(screen.getByRole('button', { name: 'End' })).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Dismiss' }));
    expect(screen.queryByTestId('crisis-support-card')).toBeNull();
    expect(call.state.ended).toBe(0);
    expect(openURL).not.toHaveBeenCalled();
  });

  it('ends the call before the card opens the dialler', async () => {
    const openURL = jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
    const { user, call } = await startCall();
    await goLive(call);
    await act(async () => call.server().onSafety('crisis_support'));

    await user.press(screen.getByRole('button', { name: 'Dial 999' }));

    expect(call.state.ended).toBe(1);
    expect(openURL).toHaveBeenCalledWith('tel:999');
  });

  it('ends the call before the card opens the emergency numbers', async () => {
    const { user, call } = await startCall();
    await goLive(call);
    await act(async () => call.server().onSafety('crisis_support'));

    await user.press(within(screen.getByTestId('crisis-support-card')).getByRole('button', { name: 'Emergency numbers' }));

    expect(call.state.ended).toBe(1);
    expect(router.push).toHaveBeenCalledWith('/emergency');
  });

  it('starts a second call without the crisis support card', async () => {
    const { user, call } = await startCall();
    await goLive(call);
    await act(async () => call.server().onSafety('crisis_support'));
    expect(screen.getByTestId('crisis-support-card')).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'End' }));
    await user.press(screen.getByRole('button', { name: 'Talk again' }));
    await goLive(call);

    expect(screen.queryByTestId('crisis-support-card')).toBeNull();
    expect(status()).toHaveTextContent('Listening…');
  });

  it('shows the screens the consultant suggests, SOS first, and opening one ends the call', async () => {
    const { user, call, audio, chat } = await startCall();
    await goLive(call);

    await act(async () => {
      call.server().onTranscript({ speaker: 'user', text: 'Where can I get help with debts?', final: true });
      call.server().onAction('services');
      call.server().onAction('sos');
      call.server().onAction('consultant');
      call.server().onAction('services');
    });

    const actions = within(screen.getByTestId('suggested-actions'));
    expect(actions.getAllByRole('button').map((button) => button.props.accessibilityLabel)).toEqual([
      'SOS',
      'All services',
    ]);
    await user.press(actions.getByRole('button', { name: 'All services' }));

    expect(router.push).toHaveBeenCalledWith('/services');
    expect(call.state.ended).toBe(1);
    expect(audio.state.stopped).toBe(1);
    await waitFor(async () => expect((await chat.list()).map((message) => message.text)).toEqual(['Where can I get help with debts?']));
  });

  it("adds the call's lines to the chat when the server ends it, and says why", async () => {
    const { call, chat } = await startCall();
    await goLive(call);

    await act(async () => {
      call.server().onTranscript({ speaker: 'consultant', text: 'Hello, how can I help?', final: false });
      call.server().onError('too_long');
      call.server().onClosed();
    });

    expect(screen.getByText('Something went wrong. Try again.')).toBeOnTheScreen();
    await waitFor(async () =>
      expect((await chat.list()).map(({ role, text, channel }) => ({ role, text, channel }))).toEqual([
        { role: 'assistant', text: 'Hello, how can I help?', channel: 'voice' },
      ]),
    );
  });

  it('says when no voice model is connected', async () => {
    const { user, call, audio } = await startCall();
    await act(async () => {
      call.server().onUnavailable();
      call.server().onClosed();
    });

    expect(screen.getByTestId('consultant-unavailable')).toHaveTextContent(/Help covers common questions/);
    await user.press(screen.getByRole('button', { name: 'Open Help' }));
    expect(router.push).toHaveBeenCalledWith('/help');
    expect(audio.state.rates).toEqual([]);
  });

  it('asks for the microphone in settings when it was refused, and connects nothing', async () => {
    const openSettings = jest.spyOn(Linking, 'openSettings').mockResolvedValue(undefined);
    audioModule.requestRecordingPermissionsAsync.mockResolvedValue({ granted: false });
    const { user, call } = await startCall();

    expect(await screen.findByText('Microphone needed')).toBeOnTheScreen();
    expect(call.state.callbacks).toBeNull();
    await user.press(screen.getByRole('button', { name: 'Open settings' }));
    expect(openSettings).toHaveBeenCalled();
    openSettings.mockRestore();
  });

  it('says when the microphone cannot start, and ends the call', async () => {
    const call = scripted();
    const user = userEvent.setup();
    const voice: VoiceAudio = { kind: 'native', start: () => Promise.reject(new Error('busy')) };
    await renderWithProviders(<ConsultantView voice={voice} />, { consultant: call.connector });
    await user.press(screen.getByRole('button', { name: 'Start talking' }));

    await goLive(call);

    expect(await screen.findByTestId('consultant-failed')).toBeOnTheScreen();
    expect(call.state.ended).toBe(1);
  });

  it('says when the connection fails, and can start again', async () => {
    const { call } = await startCall();
    await act(async () => {
      call.server().onError('connection');
      call.server().onClosed();
    });

    expect(screen.getByText("Couldn't connect. Check your connection.")).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Talk again' })).toBeOnTheScreen();
  });

  it('can be ended while it is still connecting, and a late answer starts nothing', async () => {
    const { user, call, audio } = await startCall();
    expect(screen.getByText('Connecting…')).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'End' }));

    expect(screen.getByText('Call ended')).toBeOnTheScreen();
    expect(call.state.ended).toBe(1);
    // The server answers after the person gave up: nothing goes live and the microphone stays off.
    await goLive(call);
    expect(screen.queryByTestId('consultant-status')).toBeNull();
    expect(audio.state.rates).toEqual([]);
    expect(screen.getByRole('button', { name: 'Talk again' })).toBeOnTheScreen();
  });

  it('ends a link that opens only after the person stopped waiting for it', async () => {
    const user = userEvent.setup();
    const ended = jest.fn();
    let open: () => void = () => undefined;
    const slow: ConsultantConnector = (_language, _start, callbacks) =>
      new Promise((resolve) => {
        open = () => resolve({ sendAudio: () => undefined, end: () => (ended(), callbacks.onClosed()) });
      });
    await renderWithProviders(<ConsultantView voice={fakeVoice().voice} />, { consultant: slow });
    await user.press(screen.getByRole('button', { name: 'Start talking' }));

    await user.press(await screen.findByRole('button', { name: 'End' }));
    await act(async () => open());

    expect(ended).toHaveBeenCalledTimes(1);
    expect(screen.getByText('Call ended')).toBeOnTheScreen();
  });

  it('gives up after 10 seconds without an answer and says the connection failed', async () => {
    jest.useFakeTimers();
    try {
      const user = userEvent.setup({ advanceTimers: jest.advanceTimersByTime });
      const call = scripted();
      const audio = fakeVoice();
      await renderWithProviders(<ConsultantView voice={audio.voice} />, { consultant: call.connector });
      await user.press(screen.getByRole('button', { name: 'Start talking' }));
      expect(await screen.findByText('Connecting…')).toBeOnTheScreen();

      await act(async () => jest.advanceTimersByTime(CONNECT_TIMEOUT_MS - 1000));
      expect(screen.getByText('Connecting…')).toBeOnTheScreen();
      await act(async () => jest.advanceTimersByTime(1000));

      expect(screen.getByTestId('consultant-failed')).toHaveTextContent(
        /Couldn't connect\. Check your connection\./,
      );
      expect(call.state.ended).toBe(1);
      expect(screen.getByRole('button', { name: 'Talk again' })).toBeOnTheScreen();
      expect(audio.state.rates).toEqual([]);
      // An answer after giving up starts nothing.
      await goLive(call);
      expect(screen.queryByTestId('consultant-status')).toBeNull();
      expect(audio.state.rates).toEqual([]);
    } finally {
      jest.useRealTimers();
    }
  });

  it('does not give up on a conversation that connected in time', async () => {
    jest.useFakeTimers();
    try {
      const user = userEvent.setup({ advanceTimers: jest.advanceTimersByTime });
      const call = scripted();
      await renderWithProviders(<ConsultantView voice={fakeVoice().voice} />, { consultant: call.connector });
      await user.press(screen.getByRole('button', { name: 'Start talking' }));
      await goLive(call);

      await act(async () => jest.advanceTimersByTime(CONNECT_TIMEOUT_MS * 2));

      expect(screen.getByTestId('consultant-status')).toHaveTextContent('Listening…');
      expect(call.state.ended).toBe(0);
    } finally {
      jest.useRealTimers();
    }
  });

  it('keeps the server\'s reason when a call that failed while connecting is not closed by the server', async () => {
    jest.useFakeTimers();
    try {
      const user = userEvent.setup({ advanceTimers: jest.advanceTimersByTime });
      const call = scripted();
      await renderWithProviders(<ConsultantView voice={fakeVoice().voice} />, { consultant: call.connector });
      await user.press(screen.getByRole('button', { name: 'Start talking' }));
      await act(async () => call.server().onUnavailable());

      await act(async () => jest.advanceTimersByTime(CONNECT_TIMEOUT_MS * 2));

      expect(screen.getByTestId('consultant-unavailable')).toBeOnTheScreen();
      expect(screen.queryByTestId('consultant-failed')).toBeNull();
    } finally {
      jest.useRealTimers();
    }
  });

  it('ends the call and keeps its lines when the screen goes away', async () => {
    const { call, audio, chat, rendering } = await startCall();
    await goLive(call);
    await act(async () => call.server().onTranscript({ speaker: 'user', text: 'Hello', final: true }));

    await act(async () => rendering.unmount());

    expect(call.state.ended).toBe(1);
    expect(audio.state.stopped).toBe(1);
    await waitFor(async () => expect(await chat.list()).toHaveLength(1));
  });

  it('reads in Arabic, with no simulated notice (D-142)', async () => {
    await renderWithProviders(<ConsultantView voice={fakeVoice().voice} />, { language: 'ar' });

    expect(screen.queryByText(/محاكاة|تطوير/)).toBeNull();
    expect(screen.getByRole('button', { name: 'ابدأ المحادثة' })).toBeOnTheScreen();
    expect(
      screen.getByText('لا تسجيل. تُضاف الترجمة النصية إلى محادثتك على هذا الهاتف.'),
    ).toBeOnTheScreen();
  });
});
