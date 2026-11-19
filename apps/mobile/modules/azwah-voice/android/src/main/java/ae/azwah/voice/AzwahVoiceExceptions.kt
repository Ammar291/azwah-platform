package ae.azwah.voice

import expo.modules.kotlin.exception.CodedException

// Fixed messages only: they reach JavaScript, which shows its own translated states.

internal class MicrophonePermissionException :
  CodedException("ERR_AZWAH_VOICE_PERMISSION", "Microphone permission has not been granted", null)

internal class MicrophoneUnavailableException(cause: Throwable? = null) :
  CodedException("ERR_AZWAH_VOICE_MICROPHONE", "The microphone could not be started", cause)

internal class PlaybackUnavailableException(cause: Throwable? = null) :
  CodedException("ERR_AZWAH_VOICE_PLAYBACK", "The player could not be started", cause)

internal class AudioSessionUnavailableException(cause: Throwable? = null) :
  CodedException("ERR_AZWAH_VOICE_AUDIO_SESSION", "The audio session could not be set up", cause)

internal object VoiceErrorCode {
  const val MICROPHONE = "microphone"
  const val PLAYBACK = "playback"
  const val AUDIO_SESSION = "audio_session"
}
