import AVFoundation
import ExpoModulesCore

struct AzwahVoiceStartOptions: Record {
  @Field var sampleRate: Double = 24000
}

/**
 The live consultant's audio (D-125): a voice-chat audio session, the microphone through voice processing (echo
 cancellation) and a gapless streaming player, all PCM 16-bit little-endian mono at one rate. Audio never leaves this
 module except as events to JavaScript, and nothing here logs it.
 */
public class AzwahVoiceModule: Module, VoiceSessionDelegate {
  private let sessionLock = NSLock()
  private var session: VoiceSession?

  public func definition() -> ModuleDefinition {
    Name("AzwahVoice")

    Events("onInput", "onPlaybackState", "onError")

    AsyncFunction("start") { (options: AzwahVoiceStartOptions) -> [String: Any] in
      guard Self.hasRecordPermission() else {
        throw MicrophonePermissionException()
      }
      self.sessionLock.lock()
      defer { self.sessionLock.unlock() }
      self.session?.stop()
      let next = VoiceSession(requestedRate: options.sampleRate, delegate: self)
      self.session = next
      do {
        let started = try next.start()
        return ["sampleRate": started.sampleRate, "echoCancellation": started.echoCancellation]
      } catch {
        self.session = nil
        throw error
      }
    }

    Function("play") { (pcm: Data) in
      guard let session = self.currentSession() else {
        return
      }
      session.play(pcm)
    }

    Function("clearPlayback") {
      guard let session = self.currentSession() else {
        return
      }
      session.clearPlayback()
    }

    Function("stop") {
      self.stopSession()
    }

    OnDestroy {
      self.stopSession()
    }
  }

  private func currentSession() -> VoiceSession? {
    sessionLock.lock()
    defer { sessionLock.unlock() }
    return session
  }

  private func stopSession() {
    sessionLock.lock()
    let current = session
    session = nil
    sessionLock.unlock()
    current?.stop()
  }

  private static func hasRecordPermission() -> Bool {
    if #available(iOS 17.0, *) {
      return AVAudioApplication.shared.recordPermission == .granted
    }
    return AVAudioSession.sharedInstance().recordPermission == .granted
  }

  // MARK: - VoiceSessionDelegate (any thread; a session stops calling these once it is stopped)

  func voiceSession(_ session: VoiceSession, captured data: NativeArrayBuffer) {
    emit(event: "onInput", payload: ["data": data])
  }

  func voiceSession(_ session: VoiceSession, playing: Bool) {
    emit(event: "onPlaybackState", payload: ["playing": playing])
  }

  func voiceSession(_ session: VoiceSession, failed code: String) {
    emit(event: "onError", payload: ["code": code])
  }
}

// Fixed messages only: they reach JavaScript, which shows its own translated states.

internal final class MicrophonePermissionException: Exception {
  override var code: String { "ERR_AZWAH_VOICE_PERMISSION" }
  override var reason: String { "Microphone permission has not been granted" }
}

internal final class MicrophoneUnavailableException: Exception {
  override var code: String { "ERR_AZWAH_VOICE_MICROPHONE" }
  override var reason: String { "The microphone could not be started" }
}

internal final class AudioSessionUnavailableException: Exception {
  override var code: String { "ERR_AZWAH_VOICE_AUDIO_SESSION" }
  override var reason: String { "The audio session could not be set up" }
}
