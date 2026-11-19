package ae.azwah.voice

import android.annotation.SuppressLint
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.media.audiofx.AcousticEchoCanceler
import android.media.audiofx.AudioEffect
import android.media.audiofx.AutomaticGainControl
import android.media.audiofx.NoiseSuppressor
import android.os.Process
import android.util.Log
import expo.modules.kotlin.jni.NativeArrayBuffer
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.atomic.AtomicBoolean

/** Each microphone event carries this much audio. */
private const val CHUNK_MS = 40

/** The recorder's own buffer holds this many chunks, so a slow JavaScript thread does not lose audio. */
private const val RECORD_BUFFER_CHUNKS = 5

/** Rates tried after the requested one; JavaScript resamples if the rate differs from what it asked for. */
private val FALLBACK_RATES = intArrayOf(48000, 16000)

private const val JOIN_MS = 500L

/**
 * The microphone, recorded for voice communication (VOICE_COMMUNICATION: the platform's echo canceller, noise
 * suppressor and gain control where the device has them) and delivered in 40 ms pieces from a thread of its own.
 */
internal class VoiceCapture private constructor(
  private val record: AudioRecord,
  val sampleRate: Int,
  private val effects: List<AudioEffect>,
  val echoCancellation: Boolean
) {
  private val chunkBytes = sampleRate * CHUNK_MS / 1000 * 2
  private val released = AtomicBoolean(false)

  @Volatile private var running = false
  private var thread: Thread? = null
  private var chunks = 0L

  /** Starts recording; [onFailure] runs once, on the capture thread, if the microphone stops by itself. */
  fun start(onData: (NativeArrayBuffer) -> Unit, onFailure: () -> Unit) {
    try {
      record.startRecording()
    } catch (error: IllegalStateException) {
      throw MicrophoneUnavailableException(error)
    }
    if (record.recordingState != AudioRecord.RECORDSTATE_RECORDING) {
      throw MicrophoneUnavailableException()
    }
    running = true
    thread = Thread({ capture(onData, onFailure) }, "AzwahVoiceCapture").also { it.start() }
  }

  private fun capture(onData: (NativeArrayBuffer) -> Unit, onFailure: () -> Unit) {
    Process.setThreadPriority(Process.THREAD_PRIORITY_URGENT_AUDIO)
    var failed = false
    try {
      while (running) {
        val buffer = ByteBuffer.allocateDirect(chunkBytes).order(ByteOrder.LITTLE_ENDIAN)
        val read = record.read(buffer, chunkBytes, AudioRecord.READ_BLOCKING)
        if (!running) break
        if (read < 0) {
          failed = true
          break
        }
        if (read == 0) continue
        // The JavaScript ArrayBuffer is as long as the direct buffer's capacity, so a short read gets its own buffer.
        val data = if (read == chunkBytes) buffer else exactCopy(buffer, read)
        if (chunks == 0L) Log.d(TAG, "Microphone delivering audio")
        chunks += 1
        onData(NativeArrayBuffer(data))
      }
    } catch (error: Throwable) {
      failed = running
    } finally {
      release()
    }
    if (failed) {
      Log.w(TAG, "Microphone stopped unexpectedly")
      running = false
      onFailure()
    }
  }

  /** Stops and releases the recorder. Safe to call more than once and from any thread. */
  fun stop() {
    running = false
    runQuietly { record.stop() }
    val current = thread
    if (current != null && current !== Thread.currentThread()) {
      runQuietly { current.join(JOIN_MS) }
    }
    if (current == null || !current.isAlive) release()
    thread = null
    Log.d(TAG, "Microphone stopped after $chunks chunks")
  }

  private fun release() {
    if (!released.compareAndSet(false, true)) return
    effects.forEach { effect -> runQuietly { effect.release() } }
    runQuietly { record.stop() }
    runQuietly { record.release() }
  }

  companion object {
    /** Opens the microphone at [requestedRate], or at a fallback rate the device supports. */
    @SuppressLint("MissingPermission") // The module checks RECORD_AUDIO before it starts a session.
    fun open(requestedRate: Int): VoiceCapture {
      val rates = (intArrayOf(requestedRate) + FALLBACK_RATES).filter { it > 0 }.distinct()
      for (rate in rates) {
        val minimum = AudioRecord.getMinBufferSize(rate, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT)
        if (minimum <= 0) continue
        val chunkBytes = rate * CHUNK_MS / 1000 * 2
        val record = try {
          AudioRecord(
            MediaRecorder.AudioSource.VOICE_COMMUNICATION,
            rate,
            AudioFormat.CHANNEL_IN_MONO,
            AudioFormat.ENCODING_PCM_16BIT,
            maxOf(minimum, chunkBytes * RECORD_BUFFER_CHUNKS)
          )
        } catch (error: Exception) {
          // IllegalArgumentException for an unsupported rate, SecurityException without the permission.
          continue
        }
        if (record.state != AudioRecord.STATE_INITIALIZED) {
          record.release()
          continue
        }
        val session = record.audioSessionId
        val echoCanceller = attach(AcousticEchoCanceler.isAvailable()) { AcousticEchoCanceler.create(session) }
        val noiseSuppressor = attach(NoiseSuppressor.isAvailable()) { NoiseSuppressor.create(session) }
        val gainControl = attach(AutomaticGainControl.isAvailable()) { AutomaticGainControl.create(session) }
        val echoCancellation = echoCanceller?.enabled == true
        Log.i(
          TAG,
          "Microphone opened at $rate Hz (echo canceller ${echoCanceller != null}, " +
            "noise suppressor ${noiseSuppressor != null}, gain control ${gainControl != null})"
        )
        return VoiceCapture(record, rate, listOfNotNull(echoCanceller, noiseSuppressor, gainControl), echoCancellation)
      }
      throw MicrophoneUnavailableException()
    }

    /** Creates and enables an effect on the recorder's session, or returns null if the device cannot. */
    private fun <T : AudioEffect> attach(available: Boolean, create: () -> T?): T? {
      if (!available) return null
      val effect = try {
        create()
      } catch (error: Throwable) {
        null
      } ?: return null
      val enabled = try {
        effect.setEnabled(true) == AudioEffect.SUCCESS || effect.enabled
      } catch (error: Throwable) {
        false
      }
      if (!enabled) {
        runQuietly { effect.release() }
        return null
      }
      return effect
    }

    private fun exactCopy(buffer: ByteBuffer, length: Int): ByteBuffer {
      val copy = ByteBuffer.allocateDirect(length).order(ByteOrder.LITTLE_ENDIAN)
      val source = buffer.duplicate()
      source.position(0)
      source.limit(length)
      copy.put(source)
      copy.rewind()
      return copy
    }
  }
}
