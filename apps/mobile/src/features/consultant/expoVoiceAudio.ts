import { setAudioModeAsync, useAudioStream, type AudioStreamBuffer } from 'expo-audio';
import { useCallback, useMemo, useRef } from 'react';

import { expoPlaybackFiles, expoPlaybackPlayer } from './expoPlayback';
import { int16Samples, resample, VOICE_SAMPLE_RATE } from './pcm';
import { createReplyPlayback, type PlaybackFiles, type PlaybackPlayer } from './replyPlayback';
import type { VoiceAudio, VoiceAudioCallbacks, VoiceAudioSession } from './voiceAudio';

/** The phone's file and player parts; tests pass fakes. */
export interface ExpoVoiceParts {
  files: () => PlaybackFiles;
  player: () => PlaybackPlayer;
}

const EXPO_PARTS: ExpoVoiceParts = { files: expoPlaybackFiles, player: expoPlaybackPlayer };

/** The first segment of a reply is longer, so the next is ready before it ends (D-125). */
const FIRST_SEGMENT_SECONDS = 1;

/**
 * The fallback VoiceAudio for Expo Go and builds without the AzwahVoice module (D-125): expo-audio's microphone stream
 * and the reply played as WAV segments. It cancels no echo and its playback is not a stream, so the call runs half
 * duplex (useConsultantCall sends silence while the reply is audible, unless the person clearly talks over it).
 * useAudioStream is a hook, so this is one too, called whether or not the native module is there.
 */
export function useExpoVoiceAudio(parts: ExpoVoiceParts = EXPO_PARTS): VoiceAudio {
  const active = useRef<{ sampleRate: number; callbacks: VoiceAudioCallbacks } | null>(null);

  // Stable: it reads only the ref, so the stream never needs a new listener.
  const onBuffer = useCallback((buffer: AudioStreamBuffer) => {
    const current = active.current;
    if (!current) return;
    current.callbacks.onInput(resample(int16Samples(buffer.data), buffer.sampleRate, current.sampleRate));
  }, []);

  const { stream } = useAudioStream({ sampleRate: VOICE_SAMPLE_RATE, channels: 1, encoding: 'int16', onBuffer });

  return useMemo<VoiceAudio>(
    () => ({
      kind: 'expo_audio',
      async start({ sampleRate }, callbacks): Promise<VoiceAudioSession> {
        await setAudioModeAsync({ allowsRecording: true, playsInSilentMode: true }).catch(() => undefined);
        const playback = createReplyPlayback({
          sampleRate,
          files: parts.files(),
          player: parts.player(),
          onPlayingChange: (playing) => callbacks.onPlayingChange(playing),
          firstSegmentSeconds: FIRST_SEGMENT_SECONDS,
        });
        const owner = { sampleRate, callbacks };
        let stopped = false;
        const guarded = (task: () => void) => {
          if (stopped) return;
          try {
            task();
          } catch {
            callbacks.onError('playback');
          }
        };
        const session: VoiceAudioSession = {
          capabilities: { echoCancellation: false, streaming: false },
          sampleRate,
          play: (pcm) => guarded(() => playback.push(pcm)),
          flush: () => guarded(() => playback.finish()),
          clear: () => guarded(() => playback.stop()),
          isPlaying: () => !stopped && playback.isPlaying(),
          stop() {
            if (stopped) return;
            stopped = true;
            if (active.current === owner) active.current = null;
            try {
              stream.stop();
            } catch {
              // Not started.
            }
            playback.release();
            void setAudioModeAsync({ allowsRecording: false }).catch(() => undefined);
          },
        };
        active.current = owner;
        try {
          await stream.start();
        } catch (error) {
          session.stop();
          throw error;
        }
        return session;
      },
    }),
    [parts, stream],
  );
}
