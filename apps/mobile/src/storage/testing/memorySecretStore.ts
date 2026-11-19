import type { SecretStore } from '../protection/types';

/** A keystore stand-in for tests. Share one instance between two ciphers to simulate an app restart. */
export function createMemorySecretStore(): SecretStore {
  const values = new Map<string, string>();
  return {
    async isAvailable() {
      return true;
    },
    async get(name) {
      return values.get(name) ?? null;
    },
    async set(name, value) {
      values.set(name, value);
    },
    async remove(name) {
      values.delete(name);
    },
  };
}
