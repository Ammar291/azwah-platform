import { AzwahVoice } from '../../../modules/azwah-voice';

import type { VoiceAudio, VoiceAudioCallbacks, VoiceAudioSession } from './voiceAudio';

/** The structural part of the native module this wrapper uses (a fake in tests). */
export interface AzwahVoiceLike {
  start(options: { sampleRate: number }): Promise<{ sampleRate: number; echoCancellation: boolean }>;
  play(pcm: Uint8Array): void;
  clearPlayback(): void;
  stop(): void;
  addListener(event: 'onInput', listener: (event: { data: ArrayBuffer }) => void): { remove(): void };
  addListener(event: 'onPlaybackState', listener: (event: { playing: boolean }) => void): { remove(): void };
  addListener(event: 'onError', listener: (event: { code: string }) => void): { remove(): void };
}

/**
 * The native AzwahVoice module as a VoiceAudio (D-125): echo-cancelled microphone, gapless playback, full duplex. Null
 * when the module is not in this build (Expo Go, Jest).
 */
export function createNativeVoiceAudio(module: AzwahVoiceLike | null = AzwahVoice as AzwahVoiceLike | null): VoiceAudio | null {
  if (!module) return null;
  return {
    kind: 'native',
    async start({ sampleRate }, callbacks: VoiceAudioCallbacks): Promise<VoiceAudioSession> {
      let playing = false;
      let stopped = false;
      const subscriptions = [
        module.addListener('onInput', ({ data }) => {
          if (!stopped) callbacks.onInput(new Int16Array(data, 0, Math.floor(data.byteLength / 2)));
        }),
        module.addListener('onPlaybackState', ({ playing: next }) => {
          if (stopped || next === playing) return;
          playing = next;
          callbacks.onPlayingChange(next);
        }),
        module.addListener('onError', ({ code }) => {
          if (!stopped) callbacks.onError(code === 'microphone' ? 'microphone' : 'playback');
        }),
      ];
      const release = () => subscriptions.forEach((subscription) => subscription.remove());
      let started: { sampleRate: number; echoCancellation: boolean };
      try {
        started = await module.start({ sampleRate });
      } catch (error) {
        release();
        throw error;
      }
      return {
        capabilities: { echoCancellation: started.echoCancellation, streaming: true },
        sampleRate: started.sampleRate,
        play(pcm) {
          if (!stopped && pcm.byteLength > 0) module.play(new Uint8Array(pcm));
        },
        flush() {
          // A stream plays everything it is given; nothing is held back.
        },
        clear() {
          if (!stopped) module.clearPlayback();
        },
        isPlaying: () => playing,
        stop() {
          if (stopped) return;
          stopped = true;
          release();
          module.stop();
          if (playing) {
            playing = false;
            callbacks.onPlayingChange(false);
          }
        },
      };
    },
  };
}
