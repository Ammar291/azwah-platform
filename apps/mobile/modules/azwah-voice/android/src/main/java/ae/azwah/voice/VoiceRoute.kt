package ae.azwah.voice

import android.media.AudioAttributes
import android.media.AudioDeviceCallback
import android.media.AudioDeviceInfo
import android.media.AudioFocusRequest
import android.media.AudioManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.util.Log

/** Headsets in the order they are preferred over the loudspeaker: wired first, then Bluetooth, then hearing aids. */
private val HEADSET_TYPES = intArrayOf(
  AudioDeviceInfo.TYPE_WIRED_HEADSET,
  AudioDeviceInfo.TYPE_WIRED_HEADPHONES,
  AudioDeviceInfo.TYPE_USB_HEADSET,
  AudioDeviceInfo.TYPE_BLE_HEADSET,
  AudioDeviceInfo.TYPE_BLUETOOTH_SCO,
  AudioDeviceInfo.TYPE_HEARING_AID
)

private val WIRED_TYPES = intArrayOf(
  AudioDeviceInfo.TYPE_WIRED_HEADSET,
  AudioDeviceInfo.TYPE_WIRED_HEADPHONES,
  AudioDeviceInfo.TYPE_USB_HEADSET
)

internal val VOICE_ATTRIBUTES: AudioAttributes = AudioAttributes.Builder()
  .setUsage(AudioAttributes.USAGE_VOICE_COMMUNICATION)
  .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
  .build()

/**
 * The phone's audio set up for a voice call: audio focus, MODE_IN_COMMUNICATION (which engages the platform's voice
 * processing) and output on the loudspeaker unless a wired or Bluetooth headset is connected. Everything changed is
 * given back on [release]. Routing follows headsets being connected or removed during the call.
 */
internal class VoiceRoute(
  private val audioManager: AudioManager,
  private val onFocusLost: () -> Unit
) {
  private val lock = Any()
  private val handler = Handler(Looper.getMainLooper())
  private var active = false
  private var previousMode = AudioManager.MODE_NORMAL
  private var previousSpeakerphone = false
  private var scoStarted = false
  private var focusRequest: AudioFocusRequest? = null
  private var focusHeld = false

  private val focusListener = AudioManager.OnAudioFocusChangeListener { change ->
    // A transient loss (a navigation prompt) leaves the call running; a permanent one means another app took audio.
    if (change == AudioManager.AUDIOFOCUS_LOSS) {
      Log.i(TAG, "Audio focus lost")
      onFocusLost()
    }
  }

  private val deviceCallback = object : AudioDeviceCallback() {
    override fun onAudioDevicesAdded(addedDevices: Array<out AudioDeviceInfo>?) = reroute()
    override fun onAudioDevicesRemoved(removedDevices: Array<out AudioDeviceInfo>?) = reroute()
  }

  fun acquire() {
    synchronized(lock) {
      if (active) return
      active = true
      previousMode = audioManager.mode
      @Suppress("DEPRECATION")
      previousSpeakerphone = audioManager.isSpeakerphoneOn
      requestFocus()
      audioManager.mode = AudioManager.MODE_IN_COMMUNICATION
      applyRoute()
      audioManager.registerAudioDeviceCallback(deviceCallback, handler)
      Log.d(TAG, "Audio mode set for a voice call")
    }
  }

  fun release() {
    synchronized(lock) {
      if (!active) return
      active = false
      runQuietly { audioManager.unregisterAudioDeviceCallback(deviceCallback) }
      if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
        runQuietly { audioManager.clearCommunicationDevice() }
      } else {
        runQuietly { stopSco() }
        @Suppress("DEPRECATION")
        runQuietly { audioManager.isSpeakerphoneOn = previousSpeakerphone }
      }
      runQuietly { audioManager.mode = previousMode }
      abandonFocus()
      Log.d(TAG, "Audio mode restored")
    }
  }

  private fun reroute() {
    synchronized(lock) {
      if (active) runQuietly { applyRoute() }
    }
  }

  private fun applyRoute() {
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
      val devices = audioManager.availableCommunicationDevices
      val headset = HEADSET_TYPES.asSequence().mapNotNull { type -> devices.firstOrNull { it.type == type } }.firstOrNull()
      val choice = headset
        ?: devices.firstOrNull { it.type == AudioDeviceInfo.TYPE_BUILTIN_SPEAKER }
        ?: return
      if (audioManager.communicationDevice?.id != choice.id) {
        val routed = audioManager.setCommunicationDevice(choice)
        Log.d(TAG, "Voice routed to device type ${choice.type}: $routed")
      }
      return
    }
    val outputs = audioManager.getDevices(AudioManager.GET_DEVICES_OUTPUTS)
    val wired = outputs.any { it.type in WIRED_TYPES }
    val bluetooth = outputs.any { it.type == AudioDeviceInfo.TYPE_BLUETOOTH_SCO }
    @Suppress("DEPRECATION")
    when {
      wired -> {
        stopSco()
        audioManager.isSpeakerphoneOn = false
      }
      bluetooth && audioManager.isBluetoothScoAvailableOffCall -> {
        audioManager.isSpeakerphoneOn = false
        if (!scoStarted) {
          audioManager.startBluetoothSco()
          audioManager.isBluetoothScoOn = true
          scoStarted = true
        }
      }
      else -> {
        stopSco()
        audioManager.isSpeakerphoneOn = true
      }
    }
  }

  @Suppress("DEPRECATION")
  private fun stopSco() {
    if (!scoStarted) return
    scoStarted = false
    audioManager.isBluetoothScoOn = false
    audioManager.stopBluetoothSco()
  }

  private fun requestFocus() {
    val result = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
      val request = AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN_TRANSIENT)
        .setAudioAttributes(VOICE_ATTRIBUTES)
        .setOnAudioFocusChangeListener(focusListener, handler)
        .build()
      focusRequest = request
      audioManager.requestAudioFocus(request)
    } else {
      @Suppress("DEPRECATION")
      audioManager.requestAudioFocus(focusListener, AudioManager.STREAM_VOICE_CALL, AudioManager.AUDIOFOCUS_GAIN_TRANSIENT)
    }
    focusHeld = result == AudioManager.AUDIOFOCUS_REQUEST_GRANTED
    if (!focusHeld) {
      // Not fatal: some devices refuse focus while another app holds it, and the call can still run.
      Log.w(TAG, "Audio focus was not granted")
    }
  }

  private fun abandonFocus() {
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
      focusRequest?.let { request -> runQuietly { audioManager.abandonAudioFocusRequest(request) } }
      focusRequest = null
    } else {
      @Suppress("DEPRECATION")
      runQuietly { audioManager.abandonAudioFocus(focusListener) }
    }
    focusHeld = false
  }
}

/** Runs a clean-up step whose failure must not stop the remaining steps. */
internal inline fun runQuietly(block: () -> Unit) {
  try {
    block()
  } catch (_: Throwable) {
    // Nothing to do: the step is best effort and its failure carries no participant data.
  }
}
