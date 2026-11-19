import { requestRecordingPermissionsAsync } from 'expo-audio';
import { useFocusEffect } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';

import type { AssistantAction } from '@/api/participant/schemas';
import type { ChatMessage, ChatStore } from '@/features/assistant/chatStore';
import { useChatStore } from '@/features/assistant/ChatStoreContext';
import { conversationForRequest, memoryForRequest } from '@/features/assistant/conversation';
import { orderedActions } from '@/features/assistant/suggestedScreens';
import { createIdempotencyKey } from '@/features/sessions/idempotencyKey';
import { useLocalization } from '@/i18n/useLocalization';

import { useConsultantConnector } from './ConsultantConnectorContext';
import type {
  AudioFormat,
  ConsultantCallbacks,
  ConsultantLink,
  ConsultantStart,
  TranscriptLine,
} from './consultantConnector';
import { createEchoGate, type EchoGate } from './echoGate';
import { int16Samples, level, resample } from './pcm';
import { useVoiceAudio } from './useVoiceAudio';
import type { VoiceAudio, VoiceAudioSession } from './voiceAudio';

export type CallStatus =
  | 'idle'
  | 'starting'
  | 'connecting'
  | 'live'
  | 'ended'
  | 'unavailable'
  | 'permission_denied'
  | 'failed';

export interface Caption extends TranscriptLine {
  id: number;
  /** When the line was first heard; it becomes the chat message's time. */
  startedAt: string;
}

export interface CallState {
  status: CallStatus;
  /** The whole call so far (at most CALL_LINE_LIMIT lines), in memory; saved to the chat when the call ends. */
  captions: Caption[];
  /** Who is talking now. */
  speaking: 'none' | 'user' | 'consultant';
  muted: boolean;
  /** For `failed`: a server code, or `connection`, `not_configured`, `protocol`, `microphone`, `playback`. */
  failure: string | null;
  /** The screens the consultant suggested during the call, SOS first. Kept after the call until the next starts. */
  actions: AssistantAction[];
  /** The server's safety screen matched during this call: the crisis support card shows until dismissed. */
  crisisSupport: boolean;
  /** Whether this phone cancels its own speaker in the microphone; null until the call is live. */
  echoCancellation: boolean | null;
}

/** A call keeps at most this many caption lines. */
export const CALL_LINE_LIMIT = 200;
/** Louder than this (RMS), the person is speaking. */
const VOICE_LEVEL = 900;
/** Consultant audio that arrives before the microphone and player are ready is held, up to this many chunks. */
const HELD_AUDIO_LIMIT = 100;
/** How long "connecting" may last before the server says it is ready. After it the call fails as a lost connection. */
export const CONNECT_TIMEOUT_MS = 10_000;

const INITIAL: CallState = {
  status: 'idle',
  captions: [],
  speaking: 'none',
  muted: false,
  failure: null,
  actions: [],
  crisisSupport: false,
  echoCancellation: null,
};

/** What a call starts with (D-123): the chat's memory and its latest messages, never failing the call. */
async function openingFor(chat: ChatStore): Promise<ConsultantStart> {
  const [memory, history] = await Promise.all([
    chat.getMemory().catch(() => null),
    chat.list().catch((): ChatMessage[] => []),
  ]);
  return { memory: memoryForRequest(memory), messages: conversationForRequest(history) };
}

/**
 * A growing line replaces the latest unfinished line of the same speaker, wherever it is (both may talk at once);
 * otherwise it starts a new line. The oldest lines go beyond CALL_LINE_LIMIT.
 */
export function placeLine(lines: readonly Caption[], line: TranscriptLine, nextId: () => number): Caption[] {
  for (let index = lines.length - 1; index >= 0; index -= 1) {
    const existing = lines[index];
    if (existing && existing.speaker === line.speaker && !existing.final) {
      const updated = [...lines];
      updated[index] = { ...line, id: existing.id, startedAt: existing.startedAt };
      return updated;
    }
  }
  return [...lines, { ...line, id: nextId(), startedAt: new Date().toISOString() }].slice(-CALL_LINE_LIMIT);
}

/** A call's lines as chat messages (D-123), in order: the person's as `user`, the consultant's as `assistant`. */
export function callLinesForChat(lines: readonly Caption[]): ChatMessage[] {
  return lines
    .filter((line) => line.text.trim().length > 0)
    .map((line) => ({
      id: createIdempotencyKey(),
      role: line.speaker === 'user' ? 'user' : 'assistant',
      text: line.text.trim(),
      createdAt: line.startedAt,
      channel: 'voice',
    }));
}

/**
 * A live conversation with the Azwah consultant (protocol version 2, D-123 to D-125). The call starts with the chat's
 * memory and latest messages; the microphone streams continuously (silence while muted, so the server never sees an
 * idle call); the consultant's voice plays as it comes. With echo cancellation (the native module) the microphone
 * stays open while the consultant speaks (full duplex); without it the app sends silence instead of the reply's echo
 * unless the person clearly talks over it (echoGate). Captions cover the whole call and are added to the chat when it
 * ends, however it ends. Leaving the screen ends the call. Connecting can be ended by the person and gives up by
 * itself after CONNECT_TIMEOUT_MS; whatever an abandoned attempt says later is ignored.
 */
export function useConsultantCall(voice?: VoiceAudio) {
  const { language } = useLocalization();
  const connect = useConsultantConnector();
  const chat = useChatStore();
  const fallback = useVoiceAudio();
  const audio = voice ?? fallback;
  const [state, setState] = useState<CallState>(INITIAL);
  const [gate] = useState<EchoGate>(() => createEchoGate());

  const link = useRef<ConsultantLink | null>(null);
  const format = useRef<AudioFormat | null>(null);
  const session = useRef<VoiceAudioSession | null>(null);
  /** The current call's number: callbacks of an earlier call are ignored. */
  const call = useRef(0);
  /** The current call has not ended. */
  const open = useRef(false);
  const starting = useRef(false);
  const mounted = useRef(true);
  const muted = useRef(false);
  const playing = useRef(false);
  const speaking = useRef<CallState['speaking']>('none');
  const lines = useRef<Caption[]>([]);
  const lineId = useRef(0);
  const held = useRef<{ audio: ArrayBuffer[]; flush: boolean }>({ audio: [], flush: false });
  /** After a barge-in the rest of the interrupted reply is not played. */
  const dropReply = useRef(false);
  /** Gives up on a call the server has not answered within CONNECT_TIMEOUT_MS: "connecting" never lasts for ever. */
  const connectTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const patch = useCallback((next: Partial<CallState>) => setState((current) => ({ ...current, ...next })), []);

  const stopWaiting = useCallback(() => {
    if (connectTimer.current !== null) {
      clearTimeout(connectTimer.current);
      connectTimer.current = null;
    }
  }, []);

  const setSpeaking = useCallback((next: CallState['speaking']) => {
    if (speaking.current === next) return;
    speaking.current = next;
    setState((current) => ({ ...current, speaking: next }));
  }, []);

  /** Ends the current call once: releases the audio and adds its lines to the chat. */
  const finish = useCallback(() => {
    stopWaiting();
    if (!open.current) return;
    open.current = false;
    session.current?.stop();
    session.current = null;
    link.current = null;
    format.current = null;
    held.current = { audio: [], flush: false };
    playing.current = false;
    dropReply.current = false;
    speaking.current = 'none';
    const said = callLinesForChat(lines.current);
    if (said.length > 0) {
      // The call is over either way; a chat that cannot keep its lines only misses them.
      void chat.appendMany(said).catch(() => undefined);
    }
  }, [chat, stopWaiting]);

  const playReply = useCallback(
    (pcm: ArrayBuffer) => {
      if (dropReply.current) return;
      const current = session.current;
      const target = format.current;
      if (!current || !target) {
        if (held.current.audio.length < HELD_AUDIO_LIMIT) held.current.audio.push(pcm);
        return;
      }
      const out =
        target.outputSampleRate === current.sampleRate
          ? pcm
          : resample(int16Samples(pcm), target.outputSampleRate, current.sampleRate).slice().buffer;
      if (!current.capabilities.echoCancellation) gate.played(int16Samples(out));
      current.play(out);
    },
    [gate],
  );

  const onInput = useCallback(
    (pcm: Int16Array) => {
      const current = session.current;
      const target = format.current;
      const out = link.current;
      if (!open.current || !current || !target || !out) return;
      const samples =
        current.sampleRate === target.inputSampleRate ? pcm : resample(pcm, current.sampleRate, target.inputSampleRate);
      const loudness = level(samples);
      let send: Int16Array | null = samples;
      let heard = false;
      if (muted.current) {
        send = null;
      } else if (current.capabilities.echoCancellation) {
        heard = loudness > VOICE_LEVEL;
      } else {
        const decision = gate.decide({
          level: loudness,
          playing: current.isPlaying(),
          durationMs: (samples.length / target.inputSampleRate) * 1000,
        });
        if (decision === 'barge_in') {
          current.clear();
          dropReply.current = true;
        }
        if (decision === 'silence') send = null;
        else heard = loudness > VOICE_LEVEL;
      }
      // Silence of the same length keeps the stream continuous: muted or while the echo is held back (D-124).
      out.sendAudio(send ? send.slice().buffer : new Int16Array(samples.length).buffer);
      setSpeaking(playing.current ? 'consultant' : heard ? 'user' : 'none');
    },
    [gate, setSpeaking],
  );

  const callbacksFor = useCallback(
    (id: number): ConsultantCallbacks => {
      const mine = () => id === call.current && open.current;
      const stopWith = (failure: string) => {
        if (!mine()) return;
        patch({ status: 'failed', failure });
        link.current?.end();
      };
      return {
        onReady(ready) {
          if (!mine() || format.current) return;
          stopWaiting();
          format.current = ready;
          audio
            .start(
              { sampleRate: ready.inputSampleRate },
              {
                onInput,
                onPlayingChange(now) {
                  if (!mine()) return;
                  playing.current = now;
                  setSpeaking(now ? 'consultant' : 'none');
                },
                onError: (code) => stopWith(code),
              },
            )
            .then(
              (started) => {
                if (!mine()) {
                  started.stop();
                  return;
                }
                session.current = started;
                patch({ status: 'live', echoCancellation: started.capabilities.echoCancellation });
                const pending = held.current;
                held.current = { audio: [], flush: false };
                pending.audio.forEach(playReply);
                if (pending.flush) started.flush();
              },
              () => stopWith('microphone'),
            );
        },
        onAudio(pcm) {
          if (mine()) playReply(pcm);
        },
        onTranscript(line) {
          if (!mine()) return;
          lines.current = placeLine(lines.current, line, () => (lineId.current += 1));
          setState((current) => ({ ...current, captions: lines.current }));
        },
        onTurnEnd() {
          if (!mine()) return;
          dropReply.current = false;
          if (session.current) session.current.flush();
          else held.current.flush = true;
        },
        onInterrupted() {
          if (!mine()) return;
          held.current = { audio: [], flush: false };
          session.current?.clear();
        },
        onAction(action) {
          if (!mine()) return;
          setState((current) =>
            current.actions.includes(action) ? current : { ...current, actions: orderedActions([...current.actions, action]) },
          );
        },
        onSafety() {
          if (mine()) patch({ crisisSupport: true });
        },
        // The server answered, so the wait is over; a link it does not close stays open until the person leaves.
        onUnavailable() {
          if (!mine()) return;
          stopWaiting();
          patch({ status: 'unavailable' });
        },
        onError(code) {
          if (!mine()) return;
          stopWaiting();
          patch({ status: 'failed', failure: code });
        },
        onClosed() {
          if (id !== call.current || !open.current) return;
          finish();
          setState((current) =>
            current.status === 'live' || current.status === 'connecting'
              ? { ...current, status: 'ended', speaking: 'none' }
              : { ...current, speaking: 'none' },
          );
        },
      };
    },
    [audio, finish, onInput, patch, playReply, setSpeaking, stopWaiting],
  );

  const start = useCallback(async () => {
    // A second tap before the screen shows "Connecting…" starts nothing.
    if (starting.current) return;
    starting.current = true;
    try {
      if (open.current) {
        link.current?.end();
        finish();
      }
      const id = (call.current += 1);
      lines.current = [];
      muted.current = false;
      speaking.current = 'none';
      gate.reset();
      setState({ ...INITIAL, status: 'starting' });
      const permission = await requestRecordingPermissionsAsync().catch(() => ({ granted: false }));
      if (id !== call.current || !mounted.current) return;
      if (!permission.granted) {
        patch({ status: 'permission_denied' });
        return;
      }
      const opening = await openingFor(chat);
      if (id !== call.current || !mounted.current) return;
      patch({ status: 'connecting' });
      open.current = true;
      // No answer in time: the call fails as a lost connection, and whatever this attempt says later is ignored.
      connectTimer.current = setTimeout(() => {
        connectTimer.current = null;
        if (id !== call.current || !open.current) return;
        const current = link.current;
        call.current += 1;
        current?.end();
        finish();
        patch({ status: 'failed', failure: 'connection', speaking: 'none' });
      }, CONNECT_TIMEOUT_MS);
      let opened: ConsultantLink;
      try {
        opened = await connect(language, opening, callbacksFor(id));
      } catch {
        if (id !== call.current) return;
        finish();
        patch({ status: 'failed', failure: 'not_configured' });
        return;
      }
      if (id !== call.current || !open.current || !mounted.current) {
        // Ended (or left) while connecting.
        opened.end();
        return;
      }
      link.current = opened;
    } finally {
      starting.current = false;
    }
  }, [callbacksFor, chat, connect, finish, gate, language, patch]);

  /** Ends the call the person is in (End, an action button, leaving the screen); a failure stays on screen. */
  const end = useCallback(() => {
    const current = link.current;
    if (current) current.end();
    finish();
    call.current += 1;
    setState((now) =>
      now.status === 'live' || now.status === 'connecting' || now.status === 'starting'
        ? { ...now, status: 'ended', speaking: 'none' }
        : { ...now, speaking: 'none' },
    );
  }, [finish]);

  const toggleMute = useCallback(() => {
    muted.current = !muted.current;
    const now = playing.current ? 'consultant' : 'none';
    speaking.current = now;
    patch({ muted: muted.current, speaking: now });
  }, [patch]);

  // Leaving the screen ends the call: another place in the side menu (the screen stays mounted), or going away.
  useFocusEffect(useCallback(() => () => end(), [end]));
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      link.current?.end();
      finish();
      call.current += 1;
    };
  }, [finish]);

  const dismissCrisisSupport = useCallback(() => patch({ crisisSupport: false }), [patch]);

  return { state, start, end, toggleMute, dismissCrisisSupport };
}
