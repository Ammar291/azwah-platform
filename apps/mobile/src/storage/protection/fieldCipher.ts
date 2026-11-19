import { fromBase64, toBase64 } from './base64';
import type { AesGcm, SecretStore } from './types';
import { utf8Decode, utf8Encode } from './utf8';

/** The keystore entry holding the key for answers kept on this phone. */
export const DRAFT_KEY_NAME = 'azwah.drafts.key.v1';

const VERSION = 'v1:';

/** A sealed value that cannot be opened: its key is gone, it was changed, or it belongs to another row. */
export class UnreadableValueError extends Error {
  constructor() {
    super('A protected value could not be read.');
    this.name = 'UnreadableValueError';
  }
}

/**
 * Encrypts single values for the local database (decision M-35). Each value is bound to its context (the table,
 * session and question it belongs to) through GCM's associated data, so it opens only where it was written.
 */
export interface FieldCipher {
  encrypt(plaintext: string, context: string): Promise<string>;
  /** Rejects with UnreadableValueError when the value cannot be opened. */
  decrypt(sealed: string, context: string): Promise<string>;
  /** Makes the key exist now, so a keystore that refuses writes is found before anything is saved. */
  ensureKey(): Promise<void>;
  /** Deletes the key: values sealed with it can never be read again. The next encrypt makes a new key. */
  destroyKey(): Promise<void>;
  /**
   * The same cipher (AES-GCM, the same keystore) with its own key, kept under `keyName`: destroying one key leaves
   * the other. For data that must outlive the drafts key, such as the development fixtures' worlds.
   */
  withKey(keyName: string): FieldCipher;
}

export function createFieldCipher({
  aes,
  secrets,
  keyName = DRAFT_KEY_NAME,
}: {
  aes: AesGcm;
  secrets: SecretStore;
  keyName?: string;
}): FieldCipher {
  // Shared promises, so values sealed at the same moment never make two keys (the second would orphan the first).
  let reading: Promise<string | null> | null = null;
  let writing: Promise<string> | null = null;

  const forgetOnFailure = <T>(promise: Promise<T>, forget: () => void): Promise<T> =>
    promise.catch((error: unknown) => {
      forget();
      throw error;
    });

  const readKey = (): Promise<string | null> => {
    reading ??= forgetOnFailure(secrets.get(keyName), () => (reading = null));
    return reading;
  };

  const keyForWriting = (): Promise<string> => {
    writing ??= forgetOnFailure(
      (async () => {
        const existing = await readKey();
        if (existing) {
          return existing;
        }
        const fresh = await aes.generateKey();
        await secrets.set(keyName, fresh);
        reading = Promise.resolve(fresh);
        return fresh;
      })(),
      () => (writing = null),
    );
    return writing;
  };

  const associated = (context: string) => utf8Encode(`azwah/${VERSION}${context}`);

  return {
    async encrypt(plaintext, context) {
      const sealed = await aes.seal(await keyForWriting(), utf8Encode(plaintext), associated(context));
      return VERSION + toBase64(sealed);
    },

    async decrypt(sealed, context) {
      if (!sealed.startsWith(VERSION)) {
        throw new UnreadableValueError();
      }
      const current = await readKey();
      if (!current) {
        throw new UnreadableValueError();
      }
      try {
        return utf8Decode(await aes.open(current, fromBase64(sealed.slice(VERSION.length)), associated(context)));
      } catch {
        throw new UnreadableValueError();
      }
    },

    async ensureKey() {
      await keyForWriting();
    },

    async destroyKey() {
      reading = null;
      writing = null;
      await secrets.remove(keyName);
    },

    withKey(otherKeyName) {
      return createFieldCipher({ aes, secrets, keyName: otherKeyName });
    },
  };
}
