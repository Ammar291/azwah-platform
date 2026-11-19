package ae.azwah.voice

import android.content.Context
import android.media.AudioManager
import android.util.Log
import expo.modules.kotlin.exception.CodedException
import expo.modules.kotlin.jni.NativeArrayBuffer
import java.util.concurrent.atomic.AtomicBoolean

/**
 * One call's audio: the voice-call route, the microphone and the player, at one sample rate. A part that fails after
 * [start] is released and reported once through [Listener.onError]; losing audio focus ends the whole session.
 */
internal class VoiceSession(
  context: Context,
  private val requestedRate: Int,
  private val listener: Listener
) {
  interface Listener {
    fun onInput(source: VoiceSession, data: NativeArrayBuffer)
    fun onPlaybackState(source: VoiceSession, playing: Boolean)
    fun onError(source: VoiceSession, code: String)
  }

  data class Started(val sampleRate: Int, val echoCancellation: Boolean)

  private val audioManager = context.getSystemService(Context.AUDIO_SERVICE) as AudioManager
  private val route = VoiceRoute(audioManager) { fail(VoiceErrorCode.AUDIO_SESSION, endSession = true) }

  @Volatile private var capture: VoiceCapture? = null
  @Volatile private var playback: VoicePlayback? = null
  @Volatile private var stopped = false
  private val failed = AtomicBoolean(false)

  fun start(): Started {
    try {
      try {
        route.acquire()
      } catch (error: Exception) {
        throw AudioSessionUnavailableException(error)
      }
      val microphone = VoiceCapture.open(requestedRate)
      capture = microphone
      val player = VoicePlayback.open(microphone.sampleRate)
      playback = player
      player.start(
        onPlaying = { playing -> if (!stopped) listener.onPlaybackState(this, playing) },
        onFailure = { fail(VoiceErrorCode.PLAYBACK, endSession = false) }
      )
      microphone.start(
        onData = { data -> if (!stopped) listener.onInput(this, data) },
        onFailure = { fail(VoiceErrorCode.MICROPHONE, endSession = false) }
      )
      Log.i(TAG, "Voice session started at ${microphone.sampleRate} Hz, echo cancellation ${microphone.echoCancellation}")
      return Started(microphone.sampleRate, microphone.echoCancellation)
    } catch (error: Throwable) {
      stop()
      throw error as? CodedException ?: MicrophoneUnavailableException(error)
    }
  }

  fun play(pcm: ByteArray) {
    if (!stopped) playback?.play(pcm)
  }

  fun clearPlayback() {
    if (!stopped) playback?.clear()
  }

  /** Releases everything and gives the audio route back. Safe to call more than once and from any thread. */
  @Synchronized
  fun stop() {
    if (stopped) return
    stopped = true
    capture?.stop()
    capture = null
    playback?.stop()
    playback = null
    route.release()
    Log.i(TAG, "Voice session stopped")
  }

  /** Reports the first failure only. The part that failed has released itself; focus loss ends the session. */
  private fun fail(code: String, endSession: Boolean) {
    if (stopped || !failed.compareAndSet(false, true)) return
    listener.onError(this, code)
    if (endSession) stop()
  }
}
