/*
 * The phone's side of a live call (D-125): the microphone in and the consultant's voice out, both PCM 16-bit
 * little-endian mono. Two implementations exist: the native AzwahVoice module (echo cancellation, gapless streaming
 * playback, off the JavaScript thread) in the Azwah app build, and an expo-audio fallback for Expo Go and builds without
 * the module (no echo cancellation, half-duplex, half-second WAV segments).
 */

export interface VoiceAudioCapabilities {
  /** The platform cancels the phone's own speaker in the microphone: the microphone may stay open while it speaks. */
  echoCancellation: boolean;
  /** Playback is a gapless stream (native module), not WAV segments. */
  streaming: boolean;
}

export interface VoiceAudioCallbacks {
  /** Microphone audio: PCM 16-bit mono at the session's sample rate, 20 to 100 ms at a time. */
  onInput(pcm: Int16Array): void;
  onPlayingChange(playing: boolean): void;
  onError(code: 'microphone' | 'playback'): void;
}

export interface VoiceAudioSession {
  readonly capabilities: VoiceAudioCapabilities;
  /** The rate both directions use, in samples a second. */
  readonly sampleRate: number;
  /** Queues the consultant's audio (PCM 16-bit mono at sampleRate). */
  play(pcm: ArrayBuffer): void;
  /** The consultant stopped speaking: play whatever is still held back. */
  flush(): void;
  /** Stops at once and drops what is queued. */
  clear(): void;
  isPlaying(): boolean;
  /** Releases the microphone and the player. Safe to call twice. */
  stop(): void;
}

export interface VoiceAudio {
  readonly kind: 'native' | 'expo_audio';
  /** Starts the microphone and the player. Rejects when the microphone cannot start. */
  start(options: { sampleRate: number }, callbacks: VoiceAudioCallbacks): Promise<VoiceAudioSession>;
}
