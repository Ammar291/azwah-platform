/** What the phone said when asked to confirm the person: `unavailable` means it has no biometrics or passcode. */
export type DeviceAuthenticationOutcome = 'success' | 'cancelled' | 'failed' | 'unavailable';

/** The translated words of the system prompt. */
export interface AuthenticationPrompt {
  promptMessage: string;
  cancelLabel: string;
}

/**
 * The phone's own lock: biometrics, with the device passcode as fallback. The app never sees a fingerprint, face or
 * passcode, only whether the phone confirmed its owner. Neither method rejects.
 */
export interface DeviceAuthentication {
  /** Whether the phone has biometrics or a passcode the app can ask for. */
  isAvailable(): Promise<boolean>;
  authenticate(prompt: AuthenticationPrompt): Promise<DeviceAuthenticationOutcome>;
}
