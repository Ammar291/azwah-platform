import type { ConsultantCallbacks, ConsultantConnector, ConsultantStart } from '@/features/consultant/consultantConnector';
import { concatBytes, int16Samples, level, VOICE_SAMPLE_RATE } from '@/features/consultant/pcm';

/*
 * DEVELOPMENT FIXTURES ONLY. No voice model is connected in development: this stand-in speaks protocol version 2
 * (D-124) the way the server does (24 kHz both ways, the start message first, `ready`), then listens for a stretch of
 * speech followed by a pause and plays it back, with captions saying so, so the microphone, the turn-taking, the
 * playback and the suggested-screen buttons can be tried end to end. It understands nothing that is said: the button it
 * suggests after the first turn is always the same, and its captions say it is simulated.
 */

const RATE = VOICE_SAMPLE_RATE;
/** Louder than this counts as speech. */
const SPEECH_LEVEL = 700;
/** This much quiet after speech ends the person's turn. */
const PAUSE_SECONDS = 0.9;
/** Turns longer than this are cut. */
const MAX_TURN_SECONDS = 12;
/** Played back in chunks of this many bytes (0.1 s), the way the server relays the model. */
const CHUNK_BYTES = (RATE / 10) * 2;

const CAPTIONS = {
  en: {
    greeting: (messages: number, memory: boolean) =>
      `[Simulated] The call started with ${messages} earlier messages${memory ? ' and a summary' : ''} from your chat.`,
    heard: (seconds: string) => `[Simulated] You spoke for ${seconds} seconds.`,
    reply: '[Simulated] No voice model is connected, so the consultant repeats what you said.',
    action: '[Simulated] A button to All services is shown as an example.',
  },
  ar: {
    greeting: (messages: number, memory: boolean) =>
      `[محاكاة] بدأت المكالمة ومعها ${messages} رسالة سابقة${memory ? ' وملخص' : ''} من محادثتك.`,
    heard: (seconds: string) => `[محاكاة] تحدّثت لمدة ${seconds} ثانية.`,
    reply: '[محاكاة] لا يوجد نموذج صوتي متصل، لذلك يكرر المستشار ما قلته.',
    action: '[محاكاة] يظهر زر إلى جميع الخدمات كمثال.',
  },
} as const;

export interface SimulatedConsultantOptions {
  /** Delays in tests. */
  connectMs?: number;
  replyDelayMs?: number;
}

export function createSimulatedConsultantConnector({
  connectMs = 400,
  replyDelayMs = 500,
}: SimulatedConsultantOptions = {}): ConsultantConnector {
  return async (language, start: ConsultantStart, callbacks: ConsultantCallbacks) => {
    const timers = new Set<ReturnType<typeof setTimeout>>();
    const later = (task: () => void, ms: number) => {
      const timer = setTimeout(() => {
        timers.delete(timer);
        task();
      }, ms);
      timers.add(timer);
    };
    const captions = CAPTIONS[language];
    let closed = false;
    let ready = false;
    let replying = false;
    let turns = 0;
    let turn: Uint8Array[] = [];
    let spokenSeconds = 0;
    let quietSeconds = 0;

    const reply = () => {
      const audio = concatBytes(turn);
      const seconds = spokenSeconds;
      turn = [];
      spokenSeconds = 0;
      quietSeconds = 0;
      replying = true;
      turns += 1;
      const first = turns === 1;
      callbacks.onTranscript({ speaker: 'user', text: captions.heard(seconds.toFixed(1)), final: true });
      later(() => {
        callbacks.onTranscript({ speaker: 'consultant', text: first ? `${captions.reply} ${captions.action}` : captions.reply, final: true });
        if (first) callbacks.onAction('services');
        for (let offset = 0; offset < audio.length; offset += CHUNK_BYTES) {
          callbacks.onAudio(audio.slice(offset, offset + CHUNK_BYTES).buffer);
        }
        callbacks.onTurnEnd();
        // Its own voice coming back through the microphone is not a new turn.
        later(() => {
          replying = false;
        }, (audio.length / 2 / RATE) * 1000 + 600);
      }, replyDelayMs);
    };

    later(() => {
      if (closed) return;
      ready = true;
      callbacks.onReady({ inputSampleRate: RATE, outputSampleRate: RATE });
      callbacks.onTranscript({
        speaker: 'consultant',
        text: captions.greeting(start.messages.length, start.memory !== null),
        final: true,
      });
    }, connectMs);

    return {
      sendAudio(pcm) {
        if (closed || !ready || replying) return;
        const seconds = pcm.byteLength / 2 / RATE;
        const speaking = level(int16Samples(pcm)) > SPEECH_LEVEL;
        if (turn.length === 0 && !speaking) return;
        turn.push(new Uint8Array(pcm.slice(0)));
        spokenSeconds += seconds;
        quietSeconds = speaking ? 0 : quietSeconds + seconds;
        if (quietSeconds >= PAUSE_SECONDS || spokenSeconds >= MAX_TURN_SECONDS) reply();
      },
      end() {
        if (closed) return;
        closed = true;
        timers.forEach(clearTimeout);
        timers.clear();
        callbacks.onClosed();
      },
    };
  };
}
