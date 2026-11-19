import { createExpoSecretStore } from '@/storage/protection/expoProtection';
import type { SecretStore } from '@/storage/protection/types';

import type { CredentialName, CredentialStore } from '../types';

/** Keystore entry names (letters, digits, `.`, `-` and `_` only, as expo-secure-store requires). */
const ENTRY: Record<CredentialName, string> = {
  refresh_token: 'azwah.oidc.refresh_token.v1',
  subject: 'azwah.oidc.subject.v1',
};

/**
 * The refresh token and the subject it belongs to, in the platform keystore. Access and ID tokens are never passed
 * here: they stay in the provider's memory.
 */
export function createSecureCredentialStore(secrets: SecretStore): CredentialStore {
  return {
    read: (name) => secrets.get(ENTRY[name]),
    write: (name, value) => secrets.set(ENTRY[name], value),
    async clear() {
      await Promise.all(Object.values(ENTRY).map((entry) => secrets.remove(entry)));
    },
  };
}

/**
 * The iOS Keychain or Android Keystore through expo-secure-store, readable only while the phone is unlocked and never
 * moved to another device or a backup (`WHEN_UNLOCKED_THIS_DEVICE_ONLY`, set in createExpoSecretStore).
 */
export function createExpoCredentialStore(): CredentialStore {
  return createSecureCredentialStore(createExpoSecretStore());
}
