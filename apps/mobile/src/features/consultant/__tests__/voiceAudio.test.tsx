import { act, renderHook } from '@testing-library/react-native';

import { useExpoVoiceAudio, type ExpoVoiceParts } from '../expoVoiceAudio';
import { createNativeVoiceAudio, type AzwahVoiceLike } from '../nativeVoiceAudio';
import { useVoiceAudio } from '../useVoiceAudio';
import type { VoiceAudioCallbacks } from '../voiceAudio';

const mockMicrophone = { start: jest.fn(async () => undefined), stop: jest.fn() };
let mockOnBuffer: ((buffer: { data: ArrayBuffer; sampleRate: number; channels: number; timestamp: number }) => void) | null =
  null;
const mockStreamOptions: { sampleRate?: number }[] = [];
jest.mock('expo-audio', () => ({
  useAudioStream: (options: { sampleRate?: number; onBuffer: typeof mockOnBuffer }) => {
    mockOnBuffer = options.onBuffer;
    mockStreamOptions.push(options);
    return { stream: mockMicrophone, isStreaming: false };
  },
  setAudioModeAsync: jest.fn(async () => undefined),
}));
const { setAudioModeAsync } = jest.requireMock<{ setAudioModeAsync: jest.Mock }>('expo-audio');

beforeEach(() => {
  mockMicrophone.start.mockReset().mockResolvedValue(undefined);
  mockMicrophone.stop.mockReset();
  setAudioModeAsync.mockClear();
});

function recorder(): VoiceAudioCallbacks & { inputs: Int16Array[]; playing: boolean[]; errors: string[] } {
  const inputs: Int16Array[] = [];
  const playing: boolean[] = [];
  const errors: string[] = [];
  return {
    inputs,
    playing,
    errors,
    onInput: (pcm) => inputs.push(pcm),
    onPlayingChange: (now) => playing.push(now),
    onError: (code) => errors.push(code),
  };
}

/** Segment files and a player that never finishes on its own. */
function fakeParts(): ExpoVoiceParts & { written: string[]; played: string[]; released: number } {
  const parts = {
    written: [] as string[],
    played: [] as string[],
    released: 0,
    files: () => ({
      write: (name: string) => {
        parts.written.push(name);
        return `file:///${name}`;
      },
      remove: () => undefined,
      clear: () => undefined,
    }),
    player: () => ({
      play: (uri: string) => parts.played.push(uri),
      stop: () => undefined,
      release: () => {
        parts.released += 1;
      },
    }),
  };
  return parts;
}

describe('the expo-audio fallback (Expo Go, D-125)', () => {
  it('records at 24 kHz, delivers the microphone at the session rate, and plays the reply in segments', async () => {
    const parts = fakeParts();
    const { result } = await renderHook(() => useExpoVoiceAudio(parts));
    const callbacks = recorder();

    const session = await act(() => result.current.start({ sampleRate: 24000 }, callbacks));

    expect(result.current.kind).toBe('expo_audio');
    expect(session.capabilities).toEqual({ echoCancellation: false, streaming: false });
    expect(session.sampleRate).toBe(24000);
    expect(mockStreamOptions.at(-1)?.sampleRate).toBe(24000);
    expect(mockMicrophone.start).toHaveBeenCalledTimes(1);
    expect(setAudioModeAsync).toHaveBeenCalledWith({ allowsRecording: true, playsInSilentMode: true });

    // The phone delivered 48 kHz: it is resampled to the session's rate.
    await act(async () => mockOnBuffer?.({ data: new Int16Array(4800).fill(1000).buffer, sampleRate: 48000, channels: 1, timestamp: 0 }));
    expect(callbacks.inputs.map((pcm) => pcm.length)).toEqual([2400]);

    // The first segment of a reply is a second long.
    session.play(new Int16Array(12000).buffer);
    expect(parts.written).toHaveLength(0);
    session.play(new Int16Array(12000).buffer);
    expect(parts.played).toHaveLength(1);
    expect(session.isPlaying()).toBe(true);
    session.clear();
    expect(session.isPlaying()).toBe(false);
    expect(callbacks.playing).toEqual([true, false]);

    session.stop();
    session.stop();
    expect(mockMicrophone.stop).toHaveBeenCalledTimes(1);
    expect(parts.released).toBe(1);
    expect(setAudioModeAsync).toHaveBeenLastCalledWith({ allowsRecording: false });
    await act(async () => mockOnBuffer?.({ data: new Int16Array(2400).buffer, sampleRate: 24000, channels: 1, timestamp: 1 }));
    expect(callbacks.inputs).toHaveLength(1);
  });

  it('rejects and lets go of the player when the microphone cannot start', async () => {
    mockMicrophone.start.mockRejectedValueOnce(new Error('busy'));
    const parts = fakeParts();
    const { result } = await renderHook(() => useExpoVoiceAudio(parts));

    // start sets no React state, so it needs no act.
    await expect(result.current.start({ sampleRate: 24000 }, recorder())).rejects.toThrow('busy');
    expect(parts.released).toBe(1);
  });

  it('is what a call uses when this build has no native voice module', async () => {
    const { result } = await renderHook(() => useVoiceAudio(fakeParts()));

    expect(result.current.kind).toBe('expo_audio');
  });
});

/** The native module as the wrapper sees it, driven by the test. */
function fakeModule(started = { sampleRate: 24000, echoCancellation: true }) {
  const listeners = new Map<string, Set<(event: never) => void>>();
  const calls = { played: [] as number[], cleared: 0, stopped: 0 };
  const module = {
    start: jest.fn(async () => started),
    play: (pcm: Uint8Array) => calls.played.push(pcm.byteLength),
    clearPlayback: () => {
      calls.cleared += 1;
    },
    stop: () => {
      calls.stopped += 1;
    },
    addListener: (event: string, listener: (event: never) => void) => {
      const set = listeners.get(event) ?? new Set();
      set.add(listener);
      listeners.set(event, set);
      return { remove: () => set.delete(listener) };
    },
  };
  const emit = (event: string, payload: unknown) => listeners.get(event)?.forEach((listener) => listener(payload as never));
  const listening = () => [...listeners.values()].reduce((sum, set) => sum + set.size, 0);
  return { module: module as unknown as AzwahVoiceLike & { start: jest.Mock }, calls, emit, listening };
}

/** The wrapper around a module that is there. */
function nativeVoice(module: AzwahVoiceLike) {
  const voice = createNativeVoiceAudio(module);
  if (!voice) throw new Error('The wrapper should exist when the module does.');
  return voice;
}

describe('the native voice module wrapper (D-125)', () => {
  it('is absent without the module (Expo Go, Jest)', () => {
    expect(createNativeVoiceAudio(null)).toBeNull();
  });

  it('streams the echo-cancelled microphone and plays the reply gaplessly, full duplex', async () => {
    const native = fakeModule();
    const voice = nativeVoice(native.module);
    const callbacks = recorder();

    const session = await voice.start({ sampleRate: 24000 }, callbacks);
    native.emit('onInput', { data: new Int16Array(960).buffer });
    native.emit('onPlaybackState', { playing: true });
    native.emit('onPlaybackState', { playing: true });
    session.play(new ArrayBuffer(4800));
    session.play(new ArrayBuffer(0));
    session.clear();
    native.emit('onError', { code: 'audio_session' });

    expect(voice.kind).toBe('native');
    expect(native.module.start).toHaveBeenCalledWith({ sampleRate: 24000 });
    expect(session.capabilities).toEqual({ echoCancellation: true, streaming: true });
    expect(callbacks.inputs.map((pcm) => pcm.length)).toEqual([960]);
    expect(callbacks.playing).toEqual([true]);
    expect(session.isPlaying()).toBe(true);
    expect(native.calls.played).toEqual([4800]);
    expect(native.calls.cleared).toBe(1);
    expect(callbacks.errors).toEqual(['playback']);

    session.stop();
    session.stop();
    expect(native.calls.stopped).toBe(1);
    expect(native.listening()).toBe(0);
    expect(callbacks.playing).toEqual([true, false]);
  });

  it('reports a device without echo cancellation, and lets go of its listeners when it cannot start', async () => {
    const quiet = fakeModule({ sampleRate: 16000, echoCancellation: false });
    const session = await nativeVoice(quiet.module).start({ sampleRate: 24000 }, recorder());
    expect(session.capabilities.echoCancellation).toBe(false);
    expect(session.sampleRate).toBe(16000);

    const refused = fakeModule();
    refused.module.start.mockRejectedValueOnce(new Error('permission'));
    await expect(nativeVoice(refused.module).start({ sampleRate: 24000 }, recorder())).rejects.toThrow('permission');
    expect(refused.listening()).toBe(0);
  });
});
