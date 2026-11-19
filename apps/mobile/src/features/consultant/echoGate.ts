import { level } from './pcm';

/*
 * The half-duplex fallback's microphone gate (D-125). Without echo cancellation (expo-audio in Expo Go, or a phone
 * whose platform canceller is missing) the microphone hears the consultant through the speaker. While the reply is
 * audible the app sends silence instead of that echo, and lets the person barge in only when the microphone is clearly
 * louder than the echo the reply is expected to make: louder than BARGE_IN_FLOOR, and more than BARGE_IN_MARGIN times
 * the reply's recent loudness scaled by how much of the speaker reaches this phone's microphone. That share (the
 * coupling) is learned while the person is quiet, so a loud speaker or a quiet room changes the bar.
 */

export type EchoDecision = 'microphone' | 'silence' | 'barge_in';

export interface EchoGateOptions {
  /** A barge-in is never quieter than this RMS. */
  floor?: number;
  /** How much louder than the expected echo the microphone must be. */
  margin?: number;
  /** For this many buffers in a row. */
  buffers?: number;
  /** The reply stays audible (echo tail, gaps between segments) this long after playback reports it stopped. */
  tailMs?: number;
  /** The reply's loudness halves this often. */
  halfLifeMs?: number;
  /** The share of the speaker the microphone is assumed to hear before any is learned (cautious). */
  initialCoupling?: number;
}

export interface EchoGate {
  /** The consultant audio handed to the player (PCM 16-bit), whose loudness is the echo to expect. */
  played(samples: Int16Array): void;
  /** One microphone buffer: send it, send silence instead, or stop the reply and send it (barge-in). */
  decide(input: { level: number; playing: boolean; durationMs: number }): EchoDecision;
  /** A new call: nothing learned, nothing playing. */
  reset(): void;
}

export const BARGE_IN_FLOOR = 2500;
export const BARGE_IN_MARGIN = 2.5;
export const BARGE_IN_BUFFERS = 3;
/** Below this reply loudness nothing is learned about the coupling (the ratio would be noise). */
const LEARN_FROM_LEVEL = 500;
const LEARN_RATE = 0.2;
const MIN_COUPLING = 0.05;
const MAX_COUPLING = 4;

export function createEchoGate({
  floor = BARGE_IN_FLOOR,
  margin = BARGE_IN_MARGIN,
  buffers = BARGE_IN_BUFFERS,
  tailMs = 400,
  halfLifeMs = 800,
  initialCoupling = 1,
}: EchoGateOptions = {}): EchoGate {
  let replyLevel = 0;
  let coupling = initialCoupling;
  let loudInARow = 0;
  let audibleForMs = 0;

  return {
    played(samples) {
      replyLevel = Math.max(replyLevel, level(samples));
    },

    decide({ level: microphone, playing, durationMs }) {
      audibleForMs = playing ? tailMs : Math.max(0, audibleForMs - durationMs);
      const decay = 0.5 ** (durationMs / halfLifeMs);
      if (!playing && audibleForMs === 0) {
        // Nothing audible: the person is heard as they are. Audio queued but not yet playing keeps its loudness.
        loudInARow = 0;
        replyLevel *= decay;
        return 'microphone';
      }
      const expectedEcho = replyLevel * coupling;
      const bar = Math.max(floor, margin * expectedEcho);
      let decision: EchoDecision = 'silence';
      if (microphone > bar) {
        loudInARow += 1;
        if (loudInARow >= buffers) {
          // The reply is being stopped: what the microphone hears next is the person.
          decision = 'barge_in';
          loudInARow = 0;
          audibleForMs = 0;
          replyLevel = 0;
        }
      } else {
        loudInARow = 0;
        if (replyLevel >= LEARN_FROM_LEVEL) {
          const observed = microphone / replyLevel;
          coupling = Math.min(MAX_COUPLING, Math.max(MIN_COUPLING, coupling + LEARN_RATE * (observed - coupling)));
        }
      }
      replyLevel *= decay;
      return decision;
    },

    reset() {
      replyLevel = 0;
      coupling = initialCoupling;
      loudInARow = 0;
      audibleForMs = 0;
    },
  };
}
