import type * as ExpoCrypto from 'expo-crypto';
import type * as ExpoSecureStore from 'expo-secure-store';

import type { AesGcm, SecretStore } from './types';

/*
 * The native modules are required when first used, not imported: a build without them (or a test runner) then
 * gets an error that resolveDeviceProtection turns into "keep drafts in memory", instead of failing at start-up.
 */

/** AES-256-GCM from expo-crypto (native on iOS and Android). */
export function createExpoAesGcm(): AesGcm {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- loaded lazily on purpose (see above)
  const crypto = require('expo-crypto') as typeof ExpoCrypto;
  const importKey = (key: string) => crypto.AESEncryptionKey.import(key, 'base64');
  return {
    async generateKey() {
      const key = await crypto.AESEncryptionKey.generate(crypto.AESKeySize.AES256);
      return key.encoded('base64');
    },
    async seal(key, plaintext, associatedData) {
      const sealed = await crypto.aesEncryptAsync(plaintext, await importKey(key), {
        nonce: { length: 12 },
        tagLength: 16,
        additionalData: associatedData,
      });
      return sealed.combined('bytes');
    },
    async open(key, sealed, associatedData) {
      const data = crypto.AESSealedData.fromCombined(sealed, { ivLength: 12, tagLength: 16 });
      return crypto.aesDecryptAsync(data, await importKey(key), { additionalData: associatedData, output: 'bytes' });
    },
  };
}

/**
 * The iOS Keychain or Android Keystore, through expo-secure-store. The key is readable only while the phone is
 * unlocked and never moves to another device or a backup.
 */
export function createExpoSecretStore(): SecretStore {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- loaded lazily on purpose (see above)
  const store = require('expo-secure-store') as typeof ExpoSecureStore;
  const options = { keychainAccessible: store.WHEN_UNLOCKED_THIS_DEVICE_ONLY };
  return {
    isAvailable: () => store.isAvailableAsync(),
    get: (name) => store.getItemAsync(name, options),
    set: (name, value) => store.setItemAsync(name, value, options),
    remove: (name) => store.deleteItemAsync(name, options),
  };
}
