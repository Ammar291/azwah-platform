package ae.azwah.voice

import android.media.AudioFormat
import android.media.AudioTrack
import android.os.Build
import android.os.Process
import android.os.SystemClock
import android.util.Log
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

/** Playback of a new stretch of speech starts once this much is queued (a small jitter buffer)... */
private const val START_BUFFER_MS = 120

/** ...or once this long has passed since its first piece arrived, whichever comes first. */
private const val START_WAIT_MS = 200L

/** The player counts as stopped this long after it ran out of audio, so short network gaps do not toggle it. */
private const val IDLE_AFTER_MS = 200L

/** The track's own buffer. Its start threshold is its size before Android 12, so the jitter buffer is larger. */
private const val TRACK_BUFFER_MS = 100

/** From Android 12 the track starts once this much is written. */
private const val START_THRESHOLD_MS = 20

/** The track's head standing still this long with nothing more to write means what it holds will not play. */
private const val STALL_MS = 60L

/** Audio the track keeps back (less than one mixer period) is not "left to play". */
private const val DRAIN_TOLERANCE_MS = 25

/** Waits of the playback thread: while the track is full, while it drains, and with nothing to do. */
private const val FULL_WAIT_MS = 10L
private const val DRAIN_WAIT_MS = 20L
private const val IDLE_WAIT_MS = 500L

/** A runaway producer cannot queue more than this; the oldest audio goes first. */
private const val MAX_QUEUED_SECONDS = 60

private const val JOIN_MS = 500L

/**
 * Gapless streaming playback on the voice-communication path (USAGE_VOICE_COMMUNICATION, so the echo canceller has
 * the reference it needs). Pieces from JavaScript are queued; one thread owns the AudioTrack and feeds it without
 * blocking, so a clear or a stop is never stuck behind a write.
 */
internal class VoicePlayback private constructor(private val track: AudioTrack, sampleRate: Int) {
  private val bytesPerMs = sampleRate * 2 / 1000
  private val startBytes = bytesPerMs * START_BUFFER_MS
  private val maxQueuedBytes = sampleRate * 2 * MAX_QUEUED_SECONDS
  private val drainToleranceFrames = (sampleRate.toLong() * DRAIN_TOLERANCE_MS) / 1000
  private val trackFrames = track.bufferSizeInFrames.coerceAtLeast(1)

  private val lock = ReentrantLock()
  private val wake = lock.newCondition()

  // Guarded by lock.
  private val queue = ArrayDeque<ByteArray>()
  private var queuedBytes = 0
  private var primed = false
  private var firstPieceAt = 0L
  private var clearRequested = false
  private var running = false

  private val released = AtomicBoolean(false)
  private var thread: Thread? = null

  // Counts for the stop log only.
  @Volatile private var pieces = 0L

  /** Starts the playback thread; [onFailure] runs once, on that thread, if the track fails by itself. */
  fun start(onPlaying: (Boolean) -> Unit, onFailure: () -> Unit) {
    try {
      track.play()
    } catch (error: IllegalStateException) {
      throw PlaybackUnavailableException(error)
    }
    lock.withLock { running = true }
    thread = Thread({ feed(onPlaying, onFailure) }, "AzwahVoicePlayback").also { it.start() }
  }

  /** Queues 16-bit PCM. Called on the JavaScript thread; returns at once. */
  fun play(pcm: ByteArray) {
    val length = pcm.size - pcm.size % 2
    if (length <= 0) return
    val piece = if (length == pcm.size) pcm else pcm.copyOf(length)
    lock.withLock {
      if (!running) return
      if (!primed && firstPieceAt == 0L) firstPieceAt = SystemClock.elapsedRealtime()
      queue.addLast(piece)
      queuedBytes += piece.size
      while (queuedBytes > maxQueuedBytes && queue.size > 1) {
        queuedBytes -= queue.removeFirst().size
      }
      wake.signal()
    }
  }

  /** Drops everything queued and silences the track as soon as the playback thread wakes (at once). */
  fun clear() {
    lock.withLock {
      queue.clear()
      queuedBytes = 0
      primed = false
      firstPieceAt = 0L
      clearRequested = true
      wake.signal()
    }
  }

  /** Stops the thread and releases the track. Safe to call more than once and from any thread. */
  fun stop() {
    lock.withLock {
      running = false
      queue.clear()
      queuedBytes = 0
      wake.signalAll()
    }
    val current = thread
    if (current != null && current !== Thread.currentThread()) {
      runQuietly { current.join(JOIN_MS) }
    }
    if (current == null || !current.isAlive) release()
    thread = null
    Log.d(TAG, "Player stopped after $pieces pieces")
  }

  private fun release() {
    if (!released.compareAndSet(false, true)) return
    runQuietly { track.pause() }
    runQuietly { track.flush() }
    runQuietly { track.stop() }
    runQuietly { track.release() }
  }

  /** What the playback thread does next, decided under the lock. */
  private enum class Step { STOP, CLEAR, WORK }

  private fun feed(onPlaying: (Boolean) -> Unit, onFailure: () -> Unit) {
    Process.setThreadPriority(Process.THREAD_PRIORITY_URGENT_AUDIO)
    var current: ByteArray? = null
    var offset = 0
    var playing = false
    var framesWritten = 0L
    var padded = false
    var lastHead = 0L
    var headMovedAt = 0L
    var drainedSince = 0L
    var failed = false

    fun setPlaying(next: Boolean) {
      if (playing == next) return
      playing = next
      Log.d(TAG, if (next) "Player started" else "Player idle")
      onPlaying(next)
    }

    try {
      while (true) {
        val now = SystemClock.elapsedRealtime()
        var queueEmpty = true
        val step = lock.withLock {
          when {
            !running -> Step.STOP
            clearRequested -> {
              clearRequested = false
              Step.CLEAR
            }
            else -> {
              if (current == null && isReadyToPlay(now)) {
                val next = queue.removeFirstOrNull()
                if (next != null) {
                  current = next
                  offset = 0
                  queuedBytes -= next.size
                  primed = true
                  firstPieceAt = 0L
                }
              }
              queueEmpty = queue.isEmpty()
              Step.WORK
            }
          }
        }
        if (step == Step.STOP) break
        if (step == Step.CLEAR) {
          current = null
          track.pause()
          track.flush()
          track.play()
          framesWritten = 0L
          lastHead = 0L
          padded = false
          drainedSince = 0L
          Log.d(TAG, "Player cleared")
          setPlaying(false)
          continue
        }

        val piece = current
        if (piece != null) {
          val written = track.write(piece, offset, piece.size - offset, AudioTrack.WRITE_NON_BLOCKING)
          if (written < 0) {
            failed = true
            break
          }
          if (written > 0) {
            if (!playing) headMovedAt = now
            offset += written
            framesWritten += written / 2
            padded = false
            drainedSince = 0L
            setPlaying(true)
          }
          if (offset >= piece.size) {
            current = null
            pieces += 1
          } else {
            await(FULL_WAIT_MS) { true }
          }
          continue
        }

        if (playing) {
          val head = track.playbackHeadPosition.toLong() and 0xFFFFFFFFL
          if (head != lastHead) {
            lastHead = head
            headMovedAt = now
          }
          val left = framesWritten - head
          val stalled = now - headMovedAt >= STALL_MS
          if (queueEmpty && left > 0 && stalled && !padded) {
            // A short last piece can sit below the track's start threshold (or less than a mixer period): push it
            // out with silence. This only happens once the queue is empty, so it never adds a gap inside speech.
            val silence = ByteArray(trackFrames * 2)
            val written = track.write(silence, 0, silence.size, AudioTrack.WRITE_NON_BLOCKING)
            if (written < 0) {
              failed = true
              break
            }
            framesWritten += written / 2
            padded = true
          }
          val drained = queueEmpty && (left <= drainToleranceFrames || stalled)
          if (!drained) {
            drainedSince = 0L
          } else if (drainedSince == 0L) {
            drainedSince = now
          } else if (now - drainedSince >= IDLE_AFTER_MS) {
            val stillEmpty = lock.withLock {
              if (queue.isEmpty()) primed = false
              queue.isEmpty()
            }
            drainedSince = 0L
            if (stillEmpty) setPlaying(false)
            continue
          }
          await(DRAIN_WAIT_MS) { queue.isEmpty() }
          continue
        }

        // Nothing playing: wait for audio, or for the moment the jitter buffer gives up waiting for more.
        val wait = lock.withLock {
          if (firstPieceAt != 0L) (START_WAIT_MS - (now - firstPieceAt)).coerceAtLeast(1L) else IDLE_WAIT_MS
        }
        await(wait) { !isReadyToPlay(SystemClock.elapsedRealtime()) || queue.isEmpty() }
      }
    } catch (error: Throwable) {
      failed = lock.withLock { running }
    } finally {
      release()
    }
    if (failed) {
      Log.w(TAG, "Player stopped unexpectedly")
      lock.withLock { running = false }
      onFailure()
    }
  }

  /** Under the lock: a stretch of speech may start once the jitter buffer is full or has waited long enough. */
  private fun isReadyToPlay(now: Long): Boolean =
    primed || queuedBytes >= startBytes || (firstPieceAt != 0L && now - firstPieceAt >= START_WAIT_MS)

  /** Waits up to [ms] for a signal, unless stopping or clearing, or [stillIdle] (checked under the lock) is false. */
  private inline fun await(ms: Long, stillIdle: () -> Boolean) {
    lock.withLock {
      if (running && !clearRequested && stillIdle()) wake.await(ms, TimeUnit.MILLISECONDS)
    }
  }

  companion object {
    fun open(sampleRate: Int): VoicePlayback {
      val minimum = AudioTrack.getMinBufferSize(sampleRate, AudioFormat.CHANNEL_OUT_MONO, AudioFormat.ENCODING_PCM_16BIT)
      if (minimum <= 0) throw PlaybackUnavailableException()
      val track = try {
        AudioTrack.Builder()
          .setAudioAttributes(VOICE_ATTRIBUTES)
          .setAudioFormat(
            AudioFormat.Builder()
              .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
              .setSampleRate(sampleRate)
              .setChannelMask(AudioFormat.CHANNEL_OUT_MONO)
              .build()
          )
          .setTransferMode(AudioTrack.MODE_STREAM)
          .setBufferSizeInBytes(maxOf(minimum, sampleRate * 2 * TRACK_BUFFER_MS / 1000))
          .build()
      } catch (error: Exception) {
        throw PlaybackUnavailableException(error)
      }
      if (track.state != AudioTrack.STATE_INITIALIZED) {
        runQuietly { track.release() }
        throw PlaybackUnavailableException()
      }
      if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
        // Start as soon as a little is written instead of when the whole buffer is full (the default).
        runQuietly { track.setStartThresholdInFrames(sampleRate * START_THRESHOLD_MS / 1000) }
      }
      Log.i(TAG, "Player opened at $sampleRate Hz")
      return VoicePlayback(track, sampleRate)
    }
  }
}
