import { useState } from 'react';

import { useExpoVoiceAudio, type ExpoVoiceParts } from './expoVoiceAudio';
import { createNativeVoiceAudio } from './nativeVoiceAudio';
import type { VoiceAudio } from './voiceAudio';

/**
 * The phone's audio for a call (D-125): the native AzwahVoice module (echo cancellation, gapless streaming, full
 * duplex) when this build has it, otherwise the expo-audio fallback (Expo Go, Jest). The fallback's hook runs either
 * way, as hooks must; it records nothing until it is started.
 */
export function useVoiceAudio(parts?: ExpoVoiceParts): VoiceAudio {
  const fallback = useExpoVoiceAudio(parts);
  const [native] = useState(() => createNativeVoiceAudio());
  return native ?? fallback;
}
