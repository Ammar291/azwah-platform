import { NativeModule, requireOptionalNativeModule } from 'expo-modules-core';

/*
 * AzwahVoice: the live consultant's audio on the phone (D-125). The microphone is recorded for voice communication
 * (the platform's echo cancellation, noise suppression and gain control where the device has them) and the consultant's
 * voice plays through a gapless stream on the same voice path, so the phone does not hear itself. Everything is PCM
 * 16-bit little-endian mono at the rate `start` returns. Not in Expo Go or Jest: `AzwahVoice` is null there.
 */

export type AzwahVoiceErrorCode = 'microphone' | 'playback' | 'audio_session';

export type AzwahVoiceEvents = {
  /** About 40 ms of microphone audio. */
  onInput(event: { data: ArrayBuffer }): void;
  /** The player started or stopped producing sound (queued audio ran out). */
  onPlaybackState(event: { playing: boolean }): void;
  /** Something failed after `start`; the module has already stopped what failed. */
  onError(event: { code: AzwahVoiceErrorCode }): void;
};

export interface AzwahVoiceStartResult {
  /** The rate actually used both ways. */
  sampleRate: number;
  /** Whether the platform's echo canceller is on for the microphone. */
  echoCancellation: boolean;
}

declare class AzwahVoiceModule extends NativeModule<AzwahVoiceEvents> {
  /** Sets the audio session for a voice call and starts the microphone and the player. Rejects without permission. */
  start(options: { sampleRate: number }): Promise<AzwahVoiceStartResult>;
  /** Queues consultant audio for gapless playback. */
  play(pcm: Uint8Array): void;
  /** Stops playback at once and drops what is queued. */
  clearPlayback(): void;
  /** Stops the microphone and the player and gives the audio session back. Safe to call twice. */
  stop(): void;
}

export const AzwahVoice = requireOptionalNativeModule<AzwahVoiceModule>('AzwahVoice');
