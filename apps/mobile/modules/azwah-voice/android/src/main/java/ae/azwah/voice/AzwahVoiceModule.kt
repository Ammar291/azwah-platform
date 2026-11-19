package ae.azwah.voice

import android.Manifest
import android.content.pm.PackageManager
import expo.modules.kotlin.exception.Exceptions
import expo.modules.kotlin.jni.NativeArrayBuffer
import expo.modules.kotlin.modules.Module
import expo.modules.kotlin.modules.ModuleDefinition
import expo.modules.kotlin.records.Field
import expo.modules.kotlin.records.Record

internal const val TAG = "AzwahVoice"

private const val EVENT_INPUT = "onInput"
private const val EVENT_PLAYBACK_STATE = "onPlaybackState"
private const val EVENT_ERROR = "onError"

class AzwahVoiceStartOptions : Record {
  @Field var sampleRate: Int = 24000
}

/**
 * The live consultant's audio (D-125): a voice-call audio session, an echo-cancelled microphone and a gapless
 * streaming player, all PCM 16-bit little-endian mono at one rate. Audio never leaves this module except as events to
 * JavaScript, and nothing here logs it.
 */
class AzwahVoiceModule : Module() {
  private val sessionLock = Any()

  @Volatile private var session: VoiceSession? = null

  private val listener = object : VoiceSession.Listener {
    override fun onInput(source: VoiceSession, data: NativeArrayBuffer) {
      if (source === session) sendEvent(EVENT_INPUT, mapOf("data" to data))
    }

    override fun onPlaybackState(source: VoiceSession, playing: Boolean) {
      if (source === session) sendEvent(EVENT_PLAYBACK_STATE, mapOf("playing" to playing))
    }

    override fun onError(source: VoiceSession, code: String) {
      if (source === session) sendEvent(EVENT_ERROR, mapOf("code" to code))
    }
  }

  override fun definition() = ModuleDefinition {
    Name("AzwahVoice")

    Events(EVENT_INPUT, EVENT_PLAYBACK_STATE, EVENT_ERROR)

    AsyncFunction("start") { options: AzwahVoiceStartOptions ->
      val context = appContext.reactContext?.applicationContext ?: throw Exceptions.ReactContextLost()
      if (context.checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
        throw MicrophonePermissionException()
      }
      synchronized(sessionLock) {
        session?.stop()
        session = null
        val next = VoiceSession(context, options.sampleRate, listener)
        // Events from the new session are let through only once it is the current one.
        session = next
        val started = try {
          next.start()
        } catch (error: Throwable) {
          session = null
          throw error
        }
        mapOf("sampleRate" to started.sampleRate, "echoCancellation" to started.echoCancellation)
      }
    }

    Function("play") { pcm: ByteArray ->
      session?.play(pcm)
      Unit
    }

    Function("clearPlayback") {
      session?.clearPlayback()
      Unit
    }

    Function("stop") {
      stopSession()
    }

    OnDestroy {
      stopSession()
    }
  }

  private fun stopSession() {
    synchronized(sessionLock) {
      val current = session ?: return
      session = null
      current.stop()
    }
  }
}
