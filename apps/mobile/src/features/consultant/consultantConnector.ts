import { z } from 'zod';

import {
  assistantActionSchema,
  safetyNoticeSchema,
  type AssistantAction,
  type AssistantMessage,
  type SafetyNotice,
} from '@/api/participant/schemas';
import type { AuthProvider } from '@/auth/types';

export type Speaker = 'user' | 'consultant';

export interface TranscriptLine {
  speaker: Speaker;
  text: string;
  final: boolean;
}

export interface AudioFormat {
  inputSampleRate: number;
  outputSampleRate: number;
}

/** The live protocol version this app speaks (D-124). */
export const CONSULTANT_PROTOCOL = 2;

/**
 * What a call starts with (D-123): the rolling memory and the latest chat messages, so the consultant knows what was
 * said before. 0 to 40 messages within the assistant's limits; the last may be either role.
 */
export interface ConsultantStart {
  memory: string | null;
  messages: readonly AssistantMessage[];
}

/** What the live consultant tells the app (protocol version 2, D-124). */
export interface ConsultantCallbacks {
  onReady(format: AudioFormat): void;
  /** The consultant's voice: 16-bit mono PCM at `outputSampleRate`, silence left out. */
  onAudio(pcm: ArrayBuffer): void;
  onTranscript(line: TranscriptLine): void;
  /** The consultant stopped speaking. */
  onTurnEnd(): void;
  /** The person spoke over the reply: stop playing it. */
  onInterrupted(): void;
  /** The consultant suggests a screen: show a button for it (codes as the assistant's, D-122). */
  onAction(action: AssistantAction): void;
  /** The server's safety screen matched (at most once per call): show the crisis support card. The call goes on. */
  onSafety(notice: SafetyNotice): void;
  /** No model is connected: nothing was started. */
  onUnavailable(): void;
  /**
   * A server code (`model_failed`, `too_long`, `idle`, `bad_start`), `protocol` when the server speaks another
   * version, or `connection` when the link failed.
   */
  onError(code: string): void;
  /** The link is closed, after any of the above or after `end`. */
  onClosed(): void;
}

export interface ConsultantLink {
  /** The microphone: 16-bit mono PCM at the `inputSampleRate` from `onReady`, continuous (zeros while muted). */
  sendAudio(pcm: ArrayBuffer): void;
  /** Ends the conversation. */
  end(): void;
}

/** Opens a live conversation in the given language. Rejects only when it cannot even try (not configured). */
export type ConsultantConnector = (
  language: 'en' | 'ar',
  start: ConsultantStart,
  callbacks: ConsultantCallbacks,
) => Promise<ConsultantLink>;

/** Raised when the app has no API address to connect to. */
export class ConsultantNotConfiguredError extends Error {
  constructor() {
    super('The API address is not configured.');
    this.name = 'ConsultantNotConfiguredError';
  }
}

const rate = z.number().int().min(8000).max(48000);

/**
 * Server messages are validated like any API response; unknown types (and actions this app does not know) are
 * ignored, so the server can add some.
 */
const serverMessageSchema = z.discriminatedUnion('type', [
  z.object({ type: z.literal('ready'), protocol: z.number().int().optional(), inputSampleRate: rate, outputSampleRate: rate }),
  z.object({
    type: z.literal('transcript'),
    speaker: z.enum(['user', 'consultant']),
    text: z.string().max(4000),
    final: z.boolean(),
  }),
  z.object({ type: z.literal('turn_end') }),
  z.object({ type: z.literal('interrupted') }),
  z.object({ type: z.literal('action'), action: assistantActionSchema }),
  z.object({ type: z.literal('safety'), notice: safetyNoticeSchema }),
  z.object({ type: z.literal('unavailable') }),
  z.object({ type: z.literal('error'), code: z.string().regex(/^[a-z_]{1,40}$/) }),
]);
export type ServerMessage = z.infer<typeof serverMessageSchema>;

/** One text frame from the server, or null when it is not a message this version understands. */
export function parseServerMessage(text: string): ServerMessage | null {
  try {
    const parsed = serverMessageSchema.safeParse(JSON.parse(text));
    return parsed.success ? parsed.data : null;
  } catch {
    return null;
  }
}

/** The app's first message: it must reach the server within 5 seconds of the socket opening. */
export function startMessage({ memory, messages }: ConsultantStart): string {
  return JSON.stringify({
    type: 'start',
    memory,
    messages: messages.map(({ role, text, channel }) => ({ role, text, channel: channel ?? 'chat' })),
  });
}

/** The WebSocket address of the live consultant for an `http(s)` API address, or null for anything else. */
export function liveUrl(baseUrl: string | undefined, language: 'en' | 'ar'): string | null {
  if (!baseUrl || !/^https?:\/\//i.test(baseUrl)) {
    return null;
  }
  return `${baseUrl.replace(/\/+$/, '').replace(/^http/i, 'ws')}/api/v1/consultant/live?language=${language}`;
}

/** The parts of React Native's WebSocket the connector uses (a fake in tests). */
export interface SocketLike {
  binaryType: string;
  readyState: number;
  onopen: (() => void) | null;
  onmessage: ((event: { data: unknown }) => void) | null;
  onerror: (() => void) | null;
  onclose: (() => void) | null;
  send(data: string | ArrayBuffer): void;
  close(code?: number): void;
}

export type SocketFactory = (url: string, headers: Record<string, string>) => SocketLike;

const OPEN = 1;

const nativeSocket: SocketFactory = (url, headers) =>
  // React Native's WebSocket takes headers as a third argument; the bearer token goes there, never in the URL.
  new (WebSocket as unknown as new (u: string, p: null, o: { headers: Record<string, string> }) => SocketLike)(
    url,
    null,
    { headers },
  );

export interface WebSocketConsultantOptions {
  baseUrl: string | undefined;
  auth: Pick<AuthProvider, 'getAccessToken'>;
  createSocket?: SocketFactory;
}

/**
 * The live consultant on the platform API (protocol version 2, D-124): one WebSocket per conversation, with the
 * signed-in person's token. The start message goes as soon as the socket opens.
 */
export function createWebSocketConsultantConnector({
  baseUrl,
  auth,
  createSocket = nativeSocket,
}: WebSocketConsultantOptions): ConsultantConnector {
  return async (language, start, callbacks) => {
    const url = liveUrl(baseUrl, language);
    if (!url) {
      throw new ConsultantNotConfiguredError();
    }
    const token = await auth.getAccessToken();
    const socket = createSocket(url, token ? { Authorization: `Bearer ${token}` } : {});
    socket.binaryType = 'arraybuffer';
    let settled = false;
    let opened = false;
    const finish = (report?: () => void) => {
      if (settled) return;
      settled = true;
      report?.();
      callbacks.onClosed();
    };
    const fail = (code: string) => {
      socket.close(1000);
      finish(() => callbacks.onError(code));
    };

    socket.onopen = () => {
      opened = true;
      socket.send(startMessage(start));
    };
    socket.onmessage = ({ data }) => {
      if (settled) return;
      if (data instanceof ArrayBuffer) {
        callbacks.onAudio(data);
        return;
      }
      if (typeof data !== 'string') return;
      const message = parseServerMessage(data);
      switch (message?.type) {
        case 'ready':
          // An older or newer server would expect other messages: stop rather than talk past each other.
          if (message.protocol !== CONSULTANT_PROTOCOL) return fail('protocol');
          return callbacks.onReady({ inputSampleRate: message.inputSampleRate, outputSampleRate: message.outputSampleRate });
        case 'transcript':
          return callbacks.onTranscript({ speaker: message.speaker, text: message.text, final: message.final });
        case 'turn_end':
          return callbacks.onTurnEnd();
        case 'interrupted':
          return callbacks.onInterrupted();
        case 'action':
          return callbacks.onAction(message.action);
        case 'safety':
          return callbacks.onSafety(message.notice);
        case 'unavailable':
          return finish(() => callbacks.onUnavailable());
        case 'error':
          return finish(() => callbacks.onError(message.code));
        default:
          return undefined;
      }
    };
    socket.onerror = () => finish(() => callbacks.onError('connection'));
    // A refused handshake looks the same as a lost network here, so neither signs the person out.
    socket.onclose = () => finish(opened ? undefined : () => callbacks.onError('connection'));

    return {
      sendAudio(pcm) {
        if (!settled && socket.readyState === OPEN) socket.send(pcm);
      },
      end() {
        if (!settled && socket.readyState === OPEN) socket.send(JSON.stringify({ type: 'end' }));
        socket.close(1000);
        finish();
      },
    };
  };
}
