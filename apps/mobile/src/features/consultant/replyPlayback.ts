import { concatBytes, wavFile } from './pcm';

/** Where reply segments are written while they wait to be played. */
export interface PlaybackFiles {
  /** Writes a file and returns its URI. */
  write(name: string, bytes: Uint8Array): string;
  remove(uri: string): void;
  /** Deletes every segment, for example one left by a call that ended abruptly. */
  clear(): void;
}

/** One audio player, reused for each segment. */
export interface PlaybackPlayer {
  /** Plays the file; `onFinished` is called once when it has played to the end. */
  play(uri: string, onFinished: () => void): void;
  stop(): void;
  release(): void;
}

export interface ReplyPlayback {
  /** Adds the consultant's voice (16-bit mono PCM); played in short segments as it arrives. */
  push(pcm: ArrayBuffer): void;
  /** The reply is complete: plays what is left. */
  finish(): void;
  /** Stops at once and drops everything queued (the person spoke over it, or the call ended). */
  stop(): void;
  isPlaying(): boolean;
  release(): void;
}

export interface ReplyPlaybackOptions {
  sampleRate: number;
  files: PlaybackFiles;
  player: PlaybackPlayer;
  onPlayingChange?: (playing: boolean) => void;
  /** About how long each played segment is. */
  segmentSeconds?: number;
  /**
   * How long the first segment of a reply is (default: as long as the others). Longer, the next segment is written
   * before the first ends, so the player is never left waiting between them.
   */
  firstSegmentSeconds?: number;
}

/**
 * expo-audio plays files, not a stream, so the reply is cut into WAV segments of about half a second and played in
 * order: the first words are heard while the rest is still arriving (D-109).
 */
export function createReplyPlayback({
  sampleRate,
  files,
  player,
  onPlayingChange,
  segmentSeconds = 0.5,
  firstSegmentSeconds = segmentSeconds,
}: ReplyPlaybackOptions): ReplyPlayback {
  const segmentBytes = Math.max(2, Math.round(sampleRate * segmentSeconds) * 2);
  const firstSegmentBytes = Math.max(2, Math.round(sampleRate * firstSegmentSeconds) * 2);
  let pending: Uint8Array[] = [];
  let pendingBytes = 0;
  const queue: string[] = [];
  let current: string | null = null;
  let counter = 0;
  let playing = false;

  const setPlaying = (next: boolean) => {
    if (playing !== next) {
      playing = next;
      onPlayingChange?.(next);
    }
  };

  const playNext = () => {
    const next = queue.shift();
    current = next ?? null;
    if (!next) {
      setPlaying(false);
      return;
    }
    setPlaying(true);
    player.play(next, () => {
      if (current !== next) return;
      files.remove(next);
      playNext();
    });
  };

  const cut = () => {
    if (pendingBytes === 0) return;
    const pcm = concatBytes(pending);
    pending = [];
    pendingBytes = 0;
    counter += 1;
    queue.push(files.write(`reply-${Date.now()}-${counter}.wav`, wavFile(pcm, sampleRate)));
    if (!current) playNext();
  };

  return {
    push(pcm) {
      const bytes = new Uint8Array(pcm);
      pending.push(bytes);
      pendingBytes += bytes.length;
      // Nothing playing or queued: this segment starts a reply.
      const starting = !current && queue.length === 0;
      if (pendingBytes >= (starting ? firstSegmentBytes : segmentBytes)) cut();
    },
    finish: cut,
    stop() {
      player.stop();
      if (current) files.remove(current);
      queue.splice(0).forEach((uri) => files.remove(uri));
      current = null;
      pending = [];
      pendingBytes = 0;
      setPlaying(false);
    },
    isPlaying: () => playing,
    release() {
      this.stop();
      player.release();
      files.clear();
    },
  };
}
