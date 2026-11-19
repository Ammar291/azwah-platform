import AVFoundation
import ExpoModulesCore

protocol VoiceSessionDelegate: AnyObject {
  func voiceSession(_ session: VoiceSession, captured data: NativeArrayBuffer)
  func voiceSession(_ session: VoiceSession, playing: Bool)
  func voiceSession(_ session: VoiceSession, failed code: String)
}

/// Each microphone event carries this much audio.
private let chunkSeconds = 0.04
/// Playback of a new stretch of speech starts once this much is queued (a small jitter buffer)...
private let startBufferSeconds = 0.12
/// ...or once this long has passed since its first piece arrived, whichever comes first.
private let startWait: DispatchTimeInterval = .milliseconds(200)
/// The player counts as stopped this long after it ran out of audio, so short network gaps do not toggle it.
private let idleAfter: DispatchTimeInterval = .milliseconds(200)

/// Microphone audio converted on the tap thread and gathered until a 40 ms piece is complete. Only the tap uses it.
private final class CaptureState {
  let converter: AVAudioConverter
  let format: AVAudioFormat
  let chunkSamples: Int
  var samples: [Int16] = []

  init(converter: AVAudioConverter, format: AVAudioFormat, chunkSamples: Int) {
    self.converter = converter
    self.format = format
    self.chunkSamples = chunkSamples
    samples.reserveCapacity(chunkSamples * 4)
  }
}

/**
 One call's audio. The session is `.playAndRecord` in `.voiceChat` mode (loudspeaker unless a headset is connected);
 `AVAudioEngine` runs with voice processing on its input (echo cancellation against what the player plays), an
 `AVAudioPlayerNode` plays scheduled buffers back to back, and an input tap delivers 16-bit mono at the requested
 rate. Engine work happens on one serial queue; the tap converts on its own thread.
 */
final class VoiceSession {
  struct Started {
    let sampleRate: Double
    let echoCancellation: Bool
  }

  private let rate: Double
  private let playFormat: AVAudioFormat?
  private let micFormat: AVAudioFormat?
  private weak var delegate: VoiceSessionDelegate?
  private let queue = DispatchQueue(label: "ae.azwah.voice.session")
  private let audioSession = AVAudioSession.sharedInstance()

  // Guarded by stateLock: read on the tap thread and the JavaScript thread.
  private let stateLock = NSLock()
  private var active = false
  private var failed = false

  // On the queue only.
  private var engine: AVAudioEngine?
  private var player: AVAudioPlayerNode?
  private var previousCategory: AVAudioSession.Category?
  private var previousMode: AVAudioSession.Mode = .default
  private var previousOptions: AVAudioSession.CategoryOptions = []
  private var sessionActivated = false
  private var observers: [NSObjectProtocol] = []
  private var tornDown = false
  private var held: [AVAudioPCMBuffer] = []
  private var heldFrames: AVAudioFrameCount = 0
  private var primed = false
  private var startTimer: DispatchWorkItem?
  private var idleTimer: DispatchWorkItem?
  private var scheduled = 0
  private var generation = 0
  private var playing = false

  init(requestedRate: Double, delegate: VoiceSessionDelegate) {
    let rate = requestedRate > 0 ? requestedRate : 24000
    self.rate = rate
    self.delegate = delegate
    self.playFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: rate, channels: 1, interleaved: false)
    self.micFormat = AVAudioFormat(commonFormat: .pcmFormatInt16, sampleRate: rate, channels: 1, interleaved: true)
  }

  private var isActive: Bool {
    stateLock.lock()
    defer { stateLock.unlock() }
    return active
  }

  private func setActive(_ value: Bool) {
    stateLock.lock()
    active = value
    stateLock.unlock()
  }

  // MARK: - Start and stop

  func start() throws -> Started {
    return try queue.sync {
      try startOnQueue()
    }
  }

  /// Releases everything and gives the audio session back. Safe to call more than once; never call it on the queue.
  func stop() {
    setActive(false)
    queue.sync {
      teardown()
    }
  }

  private func startOnQueue() throws -> Started {
    guard let playFormat, micFormat != nil else {
      throw MicrophoneUnavailableException()
    }
    previousCategory = audioSession.category
    previousMode = audioSession.mode
    previousOptions = audioSession.categoryOptions
    do {
      try audioSession.setCategory(.playAndRecord, mode: .voiceChat, options: Self.categoryOptions)
      try? audioSession.setPreferredIOBufferDuration(0.02)
      try audioSession.setActive(true)
      sessionActivated = true
    } catch {
      teardown()
      throw AudioSessionUnavailableException()
    }

    let engine = AVAudioEngine()
    self.engine = engine
    let input = engine.inputNode
    var echoCancellation = false
    do {
      // Voice processing must be switched on while the engine is stopped; it applies to the output node too.
      try input.setVoiceProcessingEnabled(true)
      echoCancellation = input.isVoiceProcessingEnabled
    } catch {
      echoCancellation = false
    }

    let player = AVAudioPlayerNode()
    engine.attach(player)
    engine.connect(player, to: engine.mainMixerNode, format: playFormat)
    self.player = player

    do {
      try installTap(on: input)
      setActive(true)
      engine.prepare()
      try engine.start()
    } catch {
      setActive(false)
      teardown()
      throw (error as? Exception) ?? MicrophoneUnavailableException()
    }
    player.play()
    observe(engine)
    return Started(sampleRate: rate, echoCancellation: echoCancellation)
  }

  private func teardown() {
    guard !tornDown else {
      return
    }
    tornDown = true
    setActive(false)
    observers.forEach { NotificationCenter.default.removeObserver($0) }
    observers.removeAll()
    startTimer?.cancel()
    startTimer = nil
    idleTimer?.cancel()
    idleTimer = nil
    held.removeAll()
    heldFrames = 0
    scheduled = 0
    generation &+= 1
    playing = false
    if let engine {
      engine.inputNode.removeTap(onBus: 0)
      player?.stop()
      engine.stop()
      if let player {
        engine.detach(player)
      }
      try? engine.inputNode.setVoiceProcessingEnabled(false)
    }
    player = nil
    engine = nil
    if sessionActivated {
      try? audioSession.setActive(false, options: .notifyOthersOnDeactivation)
      sessionActivated = false
    }
    if let previousCategory {
      try? audioSession.setCategory(previousCategory, mode: previousMode, options: previousOptions)
    }
  }

  private static var categoryOptions: AVAudioSession.CategoryOptions {
    var options: AVAudioSession.CategoryOptions = [.defaultToSpeaker]
#if compiler(>=6.2) // Xcode 26
    options.insert(.allowBluetoothHFP)
#else
    options.insert(.allowBluetooth)
#endif
    return options
  }

  // MARK: - Failures and session changes

  /// Reports the first failure only. A microphone failure removes the tap; anything else ends the session.
  private func fail(code: String) {
    stateLock.lock()
    let first = active && !failed
    if first {
      failed = true
    }
    stateLock.unlock()
    guard first else {
      return
    }
    delegate?.voiceSession(self, failed: code)
    queue.async { [weak self] in
      guard let self else {
        return
      }
      if code == "microphone" {
        self.engine?.inputNode.removeTap(onBus: 0)
      } else {
        self.teardown()
      }
    }
  }

  private func observe(_ engine: AVAudioEngine) {
    let center = NotificationCenter.default
    observers.append(center.addObserver(
      forName: AVAudioSession.interruptionNotification,
      object: audioSession,
      queue: nil
    ) { [weak self] notification in
      // A phone call, Siri or an alarm took the audio: the engine has stopped, so the call cannot go on.
      guard let raw = notification.userInfo?[AVAudioSessionInterruptionTypeKey] as? UInt,
        AVAudioSession.InterruptionType(rawValue: raw) == .began else {
        return
      }
      self?.fail(code: "audio_session")
    })
    observers.append(center.addObserver(
      forName: AVAudioSession.mediaServicesWereResetNotification,
      object: audioSession,
      queue: nil
    ) { [weak self] _ in
      self?.fail(code: "audio_session")
    })
    observers.append(center.addObserver(
      forName: .AVAudioEngineConfigurationChange,
      object: engine,
      queue: nil
    ) { [weak self] _ in
      self?.queue.async { self?.recoverIfStopped() }
    })
    observers.append(center.addObserver(
      forName: AVAudioSession.routeChangeNotification,
      object: audioSession,
      queue: nil
    ) { [weak self] _ in
      self?.queue.async { self?.recoverIfStopped() }
    })
  }

  /// A headset or a new hardware format stops the engine: drop what was queued, rebuild the tap and start again.
  private func recoverIfStopped() {
    guard !tornDown, isActive, let engine, let player, !engine.isRunning else {
      return
    }
    clearOnQueue()
    engine.inputNode.removeTap(onBus: 0)
    do {
      try installTap(on: engine.inputNode)
      engine.prepare()
      try engine.start()
      player.play()
    } catch {
      fail(code: "audio_session")
    }
  }

  // MARK: - Microphone

  private func installTap(on input: AVAudioInputNode) throws {
    guard let micFormat else {
      throw MicrophoneUnavailableException()
    }
    let inputFormat = input.outputFormat(forBus: 0)
    guard inputFormat.sampleRate > 0, inputFormat.channelCount > 0,
      let converter = AVAudioConverter(from: inputFormat, to: micFormat) else {
      throw MicrophoneUnavailableException()
    }
    let capture = CaptureState(converter: converter, format: micFormat, chunkSamples: Int(rate * chunkSeconds))
    let frames = AVAudioFrameCount(max(256, inputFormat.sampleRate * chunkSeconds))
    input.installTap(onBus: 0, bufferSize: frames, format: inputFormat) { [weak self] buffer, _ in
      self?.captured(buffer, into: capture)
    }
  }

  private func captured(_ buffer: AVAudioPCMBuffer, into capture: CaptureState) {
    guard isActive, buffer.frameLength > 0 else {
      return
    }
    let ratio = capture.format.sampleRate / buffer.format.sampleRate
    let capacity = AVAudioFrameCount(Double(buffer.frameLength) * ratio) + 64
    guard let output = AVAudioPCMBuffer(pcmFormat: capture.format, frameCapacity: capacity) else {
      return
    }
    var supplied = false
    var conversionError: NSError?
    let status = capture.converter.convert(to: output, error: &conversionError) { _, inputStatus in
      if supplied {
        inputStatus.pointee = .noDataNow
        return nil
      }
      supplied = true
      inputStatus.pointee = .haveData
      return buffer
    }
    if status == .error || conversionError != nil {
      fail(code: "microphone")
      return
    }
    guard output.frameLength > 0, let channel = output.int16ChannelData else {
      return
    }
    capture.samples.append(contentsOf: UnsafeBufferPointer(start: channel[0], count: Int(output.frameLength)))
    let pieceBytes = capture.chunkSamples * MemoryLayout<Int16>.size
    while capture.samples.count >= capture.chunkSamples {
      let piece = capture.samples.withUnsafeBytes { (raw: UnsafeRawBufferPointer) -> NativeArrayBuffer in
        NativeArrayBuffer.copy(of: raw.baseAddress!, count: pieceBytes)
      }
      capture.samples.removeFirst(capture.chunkSamples)
      guard isActive else {
        return
      }
      delegate?.voiceSession(self, captured: piece)
    }
  }

  // MARK: - Playback

  /// Converts 16-bit PCM to the player's format on the calling (JavaScript) thread and queues it.
  func play(_ data: Data) {
    let count = data.count / MemoryLayout<Int16>.size
    guard count > 0, isActive, let playFormat,
      let buffer = AVAudioPCMBuffer(pcmFormat: playFormat, frameCapacity: AVAudioFrameCount(count)),
      let channel = buffer.floatChannelData?[0] else {
      return
    }
    buffer.frameLength = AVAudioFrameCount(count)
    data.withUnsafeBytes { (raw: UnsafeRawBufferPointer) in
      for index in 0..<count {
        let sample = Int16(littleEndian: raw.loadUnaligned(fromByteOffset: index * 2, as: Int16.self))
        channel[index] = Float(sample) / 32768
      }
    }
    queue.async { [weak self] in
      self?.enqueue(buffer)
    }
  }

  func clearPlayback() {
    queue.async { [weak self] in
      self?.clearOnQueue()
    }
  }

  private func enqueue(_ buffer: AVAudioPCMBuffer) {
    guard !tornDown, isActive else {
      return
    }
    idleTimer?.cancel()
    idleTimer = nil
    if primed {
      schedule(buffer)
      return
    }
    held.append(buffer)
    heldFrames += buffer.frameLength
    if Double(heldFrames) >= rate * startBufferSeconds {
      releaseHeld()
    } else if startTimer == nil {
      let work = DispatchWorkItem { [weak self] in
        self?.releaseHeld()
      }
      startTimer = work
      queue.asyncAfter(deadline: .now() + startWait, execute: work)
    }
  }

  private func releaseHeld() {
    startTimer?.cancel()
    startTimer = nil
    guard !tornDown, !held.isEmpty else {
      return
    }
    primed = true
    let buffers = held
    held.removeAll()
    heldFrames = 0
    buffers.forEach { schedule($0) }
  }

  private func schedule(_ buffer: AVAudioPCMBuffer) {
    guard let player, let engine else {
      return
    }
    let current = generation
    scheduled += 1
    player.scheduleBuffer(buffer, completionCallbackType: .dataPlayedBack) { [weak self] _ in
      self?.queue.async {
        self?.played(generation: current)
      }
    }
    // `play()` on a player whose engine is not running raises an exception, so check first.
    if engine.isRunning, !player.isPlaying {
      player.play()
    }
    setPlaying(true)
  }

  private func played(generation completed: Int) {
    guard !tornDown, completed == generation else {
      return
    }
    scheduled = max(0, scheduled - 1)
    if scheduled == 0, held.isEmpty {
      scheduleIdle()
    }
  }

  private func scheduleIdle() {
    idleTimer?.cancel()
    let work = DispatchWorkItem { [weak self] in
      guard let self, !self.tornDown, self.scheduled == 0, self.held.isEmpty else {
        return
      }
      self.primed = false
      self.setPlaying(false)
    }
    idleTimer = work
    queue.asyncAfter(deadline: .now() + idleAfter, execute: work)
  }

  private func clearOnQueue() {
    guard !tornDown else {
      return
    }
    generation &+= 1
    startTimer?.cancel()
    startTimer = nil
    idleTimer?.cancel()
    idleTimer = nil
    held.removeAll()
    heldFrames = 0
    primed = false
    scheduled = 0
    if let player {
      player.stop()
      if engine?.isRunning == true {
        player.play()
      }
    }
    setPlaying(false)
  }

  private func setPlaying(_ next: Bool) {
    guard playing != next else {
      return
    }
    playing = next
    guard isActive else {
      return
    }
    delegate?.voiceSession(self, playing: next)
  }
}
