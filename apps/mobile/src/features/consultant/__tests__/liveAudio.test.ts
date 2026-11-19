import { createSimulatedConsultantConnector } from '@/dev-fixtures/simulatedConsultant';

import {
  createWebSocketConsultantConnector,
  liveUrl,
  parseServerMessage,
  startMessage,
  type ConsultantCallbacks,
  type ConsultantStart,
  type SocketLike,
} from '../consultantConnector';
import { concatBytes, int16Samples, level, resample, wavFile } from '../pcm';
import { createReplyPlayback, type PlaybackPlayer } from '../replyPlayback';

type Event = readonly [string, ...unknown[]];

function recorder(): ConsultantCallbacks & { events: Event[] } {
  const events: Event[] = [];
  return {
    events,
    onReady: (format) => events.push(['ready', format]),
    onAudio: (pcm) => events.push(['audio', pcm.byteLength]),
    onTranscript: (line) => events.push(['transcript', line]),
    onTurnEnd: () => events.push(['turn_end']),
    onInterrupted: () => events.push(['interrupted']),
    onAction: (action) => events.push(['action', action]),
    onSafety: (notice) => events.push(['safety', notice]),
    onUnavailable: () => events.push(['unavailable']),
    onError: (code) => events.push(['error', code]),
    onClosed: () => events.push(['closed']),
  };
}

/** A tone at the given loudness, `seconds` long, at `rate` (24 kHz unless given). */
const tone = (amplitude: number, seconds: number, rate = 24000) => {
  const samples = new Int16Array(Math.round(rate * seconds));
  samples.forEach((_, index) => (samples[index] = index % 2 === 0 ? amplitude : -amplitude));
  return samples.buffer;
};

describe('PCM helpers', () => {
  it('resamples, measures loudness and wraps PCM in a WAV header', () => {
    const second = new Int16Array(48000).fill(1000);
    expect(resample(second, 48000, 24000)).toHaveLength(24000);
    expect(resample(second, 24000, 24000)).toBe(second);
    expect(level(new Int16Array([3, -3, 3, -3]))).toBe(3);
    expect(level(new Int16Array())).toBe(0);
    expect(int16Samples(new ArrayBuffer(5))).toHaveLength(2);

    const wav = wavFile(new Uint8Array([1, 2, 3, 4]), 24000);
    const view = new DataView(wav.buffer);
    expect(String.fromCharCode(...wav.slice(0, 4), ...wav.slice(8, 12))).toBe('RIFFWAVE');
    expect([view.getUint32(24, true), view.getUint16(34, true), view.getUint32(40, true)]).toEqual([24000, 16, 4]);
    expect(Array.from(concatBytes([new Uint8Array([1]), new Uint8Array([2, 3])]))).toEqual([1, 2, 3]);
  });
});

const START: ConsultantStart = {
  memory: 'The family asked about arguments.',
  messages: [
    { role: 'user', text: 'How can we argue less?', channel: 'chat' },
    { role: 'assistant', text: 'Try a weekly talk.' },
  ],
};

const READY = '{"type":"ready","protocol":2,"inputSampleRate":24000,"outputSampleRate":24000}';

function fakeSockets() {
  const sockets: { url: string; headers: Record<string, string>; socket: SocketLike; sent: unknown[]; closed: number[] }[] = [];
  const createSocket = (url: string, headers: Record<string, string>) => {
    const sent: unknown[] = [];
    const closed: number[] = [];
    const socket: SocketLike = {
      binaryType: 'blob',
      readyState: 1,
      onopen: null,
      onmessage: null,
      onerror: null,
      onclose: null,
      send: (data) => sent.push(data),
      close: (code) => closed.push(code ?? 0),
    };
    sockets.push({ url, headers, socket, sent, closed });
    return socket;
  };
  const last = () => {
    const entry = sockets.at(-1);
    if (!entry) throw new Error('No socket yet.');
    return entry;
  };
  return { createSocket, last };
}

describe('the live protocol, version 2 (D-124)', () => {
  it('reads the messages it knows and ignores the rest', () => {
    expect(parseServerMessage(READY)).toEqual({
      type: 'ready',
      protocol: 2,
      inputSampleRate: 24000,
      outputSampleRate: 24000,
    });
    expect(parseServerMessage('{"type":"action","action":"sos"}')).toEqual({ type: 'action', action: 'sos' });
    expect(parseServerMessage('{"type":"action","action":"teleport"}')).toBeNull();
    expect(parseServerMessage('{"type":"safety","notice":"crisis_support"}')).toEqual({
      type: 'safety',
      notice: 'crisis_support',
    });
    expect(parseServerMessage('{"type":"safety","notice":"something_new"}')).toEqual({ type: 'safety', notice: 'crisis_support' });
    expect(parseServerMessage('{"type":"safety","notice":null}')).toBeNull();
    expect(parseServerMessage('{"type":"error","code":"bad_start"}')).toEqual({ type: 'error', code: 'bad_start' });
    expect(parseServerMessage('{"type":"transcript","speaker":"consultant","text":"Hello","final":false}')).toMatchObject({
      speaker: 'consultant',
    });
    expect(parseServerMessage('{"type":"error","code":"too_long"}')).toEqual({ type: 'error', code: 'too_long' });
    expect(parseServerMessage('{"type":"something_new"}')).toBeNull();
    expect(parseServerMessage('{"type":"error","code":"<script>"}')).toBeNull();
    expect(parseServerMessage('not json')).toBeNull();
  });

  it('turns the API address into the WebSocket address', () => {
    expect(liveUrl('https://api.example.org/', 'ar')).toBe('wss://api.example.org/api/v1/consultant/live?language=ar');
    expect(liveUrl('http://10.0.2.2:5080', 'en')).toBe('ws://10.0.2.2:5080/api/v1/consultant/live?language=en');
    expect(liveUrl(undefined, 'en')).toBeNull();
  });

  it('connects with the bearer token in a header, starts with the memory and messages, relays both ways and ends cleanly', async () => {
    const sockets = fakeSockets();
    const connector = createWebSocketConsultantConnector({
      baseUrl: 'https://api.example.org',
      auth: { getAccessToken: async () => 'token-1' },
      createSocket: sockets.createSocket,
    });
    const callbacks = recorder();

    const link = await connector('en', START, callbacks);
    const { url, headers, socket, sent } = sockets.last();
    expect(sent).toEqual([]);
    socket.onopen?.();
    socket.onmessage?.({ data: READY });
    socket.onmessage?.({ data: new ArrayBuffer(8) });
    socket.onmessage?.({ data: '{"type":"transcript","speaker":"user","text":"Hi","final":true}' });
    socket.onmessage?.({ data: '{"type":"safety","notice":"crisis_support"}' });
    socket.onmessage?.({ data: '{"type":"action","action":"services"}' });
    socket.onmessage?.({ data: '{"type":"something_new"}' });
    socket.onmessage?.({ data: '{"type":"turn_end"}' });
    link.sendAudio(new ArrayBuffer(4));
    link.end();

    expect(url).toBe('wss://api.example.org/api/v1/consultant/live?language=en');
    expect(headers).toEqual({ Authorization: 'Bearer token-1' });
    expect(socket.binaryType).toBe('arraybuffer');
    expect(callbacks.events).toEqual([
      ['ready', { inputSampleRate: 24000, outputSampleRate: 24000 }],
      ['audio', 8],
      ['transcript', { speaker: 'user', text: 'Hi', final: true }],
      ['safety', 'crisis_support'],
      ['action', 'services'],
      ['turn_end'],
      ['closed'],
    ]);
    expect(JSON.parse(sent[0] as string)).toEqual({
      type: 'start',
      memory: 'The family asked about arguments.',
      messages: [
        { role: 'user', text: 'How can we argue less?', channel: 'chat' },
        { role: 'assistant', text: 'Try a weekly talk.', channel: 'chat' },
      ],
    });
    expect(sent.slice(1)).toEqual([expect.any(ArrayBuffer), '{"type":"end"}']);
  });

  it('sends an empty start when there is no chat yet', () => {
    expect(JSON.parse(startMessage({ memory: null, messages: [] }))).toEqual({ type: 'start', memory: null, messages: [] });
  });

  it('stops when the server speaks another protocol version, and reports a refused start', async () => {
    const sockets = fakeSockets();
    const connector = createWebSocketConsultantConnector({
      baseUrl: 'https://api.example.org',
      auth: { getAccessToken: async () => 'token-1' },
      createSocket: sockets.createSocket,
    });

    const older = recorder();
    await connector('en', START, older);
    sockets.last().socket.onopen?.();
    sockets.last().socket.onmessage?.({ data: '{"type":"ready","inputSampleRate":16000,"outputSampleRate":24000}' });
    expect(older.events).toEqual([['error', 'protocol'], ['closed']]);
    expect(sockets.last().closed).toEqual([1000]);

    const refused = recorder();
    await connector('ar', START, refused);
    sockets.last().socket.onopen?.();
    sockets.last().socket.onmessage?.({ data: '{"type":"error","code":"bad_start"}' });
    sockets.last().socket.onmessage?.({ data: new ArrayBuffer(8) });
    expect(refused.events).toEqual([['error', 'bad_start'], ['closed']]);
  });

  it('reports "not connected" and a lost link once, then closes', async () => {
    let socket: SocketLike | null = null;
    const connector = createWebSocketConsultantConnector({
      baseUrl: 'https://api.example.org',
      auth: { getAccessToken: async () => 'token-1' },
      createSocket: () =>
        (socket = {
          binaryType: '',
          readyState: 1,
          onopen: null,
          onmessage: null,
          onerror: null,
          onclose: null,
          send: () => undefined,
          close: () => undefined,
        }),
    });
    const unavailable = recorder();
    await connector('en', START, unavailable);
    socket!.onopen?.();
    socket!.onmessage?.({ data: '{"type":"unavailable"}' });
    socket!.onclose?.();
    expect(unavailable.events).toEqual([['unavailable'], ['closed']]);

    const refused = recorder();
    await connector('en', START, refused);
    socket!.onerror?.();
    socket!.onclose?.();
    expect(refused.events).toEqual([['error', 'connection'], ['closed']]);
  });

  it('cannot start without an API address', async () => {
    const connector = createWebSocketConsultantConnector({ baseUrl: undefined, auth: { getAccessToken: async () => null } });
    await expect(connector('en', START, recorder())).rejects.toThrow('not configured');
  });
});

describe('reply playback', () => {
  function fakes() {
    const written: string[] = [];
    const removed: string[] = [];
    const played: string[] = [];
    let finish: (() => void) | null = null;
    const player: PlaybackPlayer = {
      play: (uri, onFinished) => {
        played.push(uri);
        finish = onFinished;
      },
      stop: () => {
        finish = null;
      },
      release: () => undefined,
    };
    const files = {
      write: (name: string) => {
        written.push(name);
        return `file:///${name}`;
      },
      remove: (uri: string) => removed.push(uri),
      clear: () => undefined,
    };
    return { written, removed, played, player, files, finishCurrent: () => finish?.() };
  }

  it('plays the reply in order, segment by segment, as it arrives', () => {
    const { written, played, removed, player, files, finishCurrent } = fakes();
    const playing: boolean[] = [];
    const playback = createReplyPlayback({ sampleRate: 16000, files, player, onPlayingChange: (now) => playing.push(now) });

    playback.push(tone(1000, 0.3, 16000));
    expect(written).toHaveLength(0);
    playback.push(tone(1000, 0.3, 16000));
    expect(written).toHaveLength(1);
    playback.push(tone(1000, 0.1, 16000));
    playback.finish();
    expect(written).toHaveLength(2);
    expect(played).toHaveLength(1);

    finishCurrent();
    expect(played).toHaveLength(2);
    finishCurrent();
    expect(removed).toHaveLength(2);
    expect(playing).toEqual([true, false]);
    expect(playback.isPlaying()).toBe(false);
  });

  it('stops at once and drops what is queued when interrupted', () => {
    const { removed, played, player, files } = fakes();
    const playback = createReplyPlayback({ sampleRate: 16000, files, player, segmentSeconds: 0.1 });
    playback.push(tone(1000, 0.1, 16000));
    playback.push(tone(1000, 0.1, 16000));
    playback.push(tone(1000, 0.1, 16000));

    playback.stop();

    expect(played).toHaveLength(1);
    expect(removed).toHaveLength(3);
    expect(playback.isPlaying()).toBe(false);
  });

  it('makes the first segment of a reply longer, so the next is ready before it ends', () => {
    const { written, player, files } = fakes();
    const playback = createReplyPlayback({ sampleRate: 24000, files, player, segmentSeconds: 0.5, firstSegmentSeconds: 1 });

    playback.push(tone(1000, 0.6));
    expect(written).toHaveLength(0);
    playback.push(tone(1000, 0.4));
    expect(written).toHaveLength(1);
    playback.push(tone(1000, 0.5));
    expect(written).toHaveLength(2);
  });
});

describe('the development consultant (protocol version 2)', () => {
  beforeEach(() => jest.useFakeTimers());
  afterEach(() => jest.useRealTimers());

  it('greets with what it was started with, waits for speech and a pause, then says it is simulated and plays the speech back', async () => {
    const callbacks = recorder();
    const link = await createSimulatedConsultantConnector({ connectMs: 10, replyDelayMs: 10 })('en', START, callbacks);
    // Audio before it is ready is not a turn.
    link.sendAudio(tone(5000, 1));
    jest.advanceTimersByTime(10);

    // Silence (muted, or held back while a reply plays) is part of the stream but not speech.
    link.sendAudio(tone(0, 0.5));
    link.sendAudio(tone(5000, 1));
    link.sendAudio(tone(0, 1));
    jest.advanceTimersByTime(10);

    expect(callbacks.events.slice(0, 4)).toEqual([
      ['ready', { inputSampleRate: 24000, outputSampleRate: 24000 }],
      [
        'transcript',
        { speaker: 'consultant', text: '[Simulated] The call started with 2 earlier messages and a summary from your chat.', final: true },
      ],
      ['transcript', { speaker: 'user', text: '[Simulated] You spoke for 2.0 seconds.', final: true }],
      [
        'transcript',
        {
          speaker: 'consultant',
          text: '[Simulated] No voice model is connected, so the consultant repeats what you said. [Simulated] A button to All services is shown as an example.',
          final: true,
        },
      ],
    ]);
    expect(callbacks.events[4]).toEqual(['action', 'services']);
    expect(callbacks.events.filter(([kind]) => kind === 'audio')).toHaveLength(20);
    expect(callbacks.events.at(-1)).toEqual(['turn_end']);

    link.end();
    expect(callbacks.events.at(-1)).toEqual(['closed']);
  });
});
