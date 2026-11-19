/**
 * AES-256-GCM. A sealed value is the 12-byte nonce, the ciphertext and the 16-byte tag, in that order. Keys travel
 * as base64 between the cipher and the keystore and nowhere else.
 */
export interface AesGcm {
  /** A new random 256-bit key, base64. */
  generateKey(): Promise<string>;
  seal(key: string, plaintext: Uint8Array, associatedData: Uint8Array): Promise<Uint8Array>;
  /** Rejects when the tag does not match: a wrong key, wrong associated data or changed bytes. */
  open(key: string, sealed: Uint8Array, associatedData: Uint8Array): Promise<Uint8Array>;
}

/**
 * The platform keystore (iOS Keychain, Android Keystore). Holds the draft key and, in development fixtures mode only,
 * the key of the fixture worlds.
 */
export interface SecretStore {
  isAvailable(): Promise<boolean>;
  get(name: string): Promise<string | null>;
  set(name: string, value: string): Promise<void>;
  remove(name: string): Promise<void>;
}
