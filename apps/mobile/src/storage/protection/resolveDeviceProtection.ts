import { createFieldCipher, type FieldCipher } from './fieldCipher';
import type { AesGcm, SecretStore } from './types';
import { utf8Decode, utf8Encode } from './utf8';

export interface ProtectionDependencies {
  /** May throw when the native module is missing. */
  aes: () => AesGcm;
  secrets: () => SecretStore;
}

/**
 * The cipher for answers kept on this phone, or null when they must stay in memory: no keystore, no working AES-GCM
 * (checked with a throwaway key before anything real is sealed), or a keystore that will not keep the key.
 */
export async function resolveDeviceProtection({ aes, secrets }: ProtectionDependencies): Promise<FieldCipher | null> {
  try {
    const store = secrets();
    if (!(await store.isAvailable())) {
      return null;
    }
    const primitive = aes();
    const probeKey = await primitive.generateKey();
    const probe = `probe-${Date.now()}`;
    const context = utf8Encode('azwah/probe');
    const sealed = await primitive.seal(probeKey, utf8Encode(probe), context);
    if (utf8Decode(await primitive.open(probeKey, sealed, context)) !== probe) {
      return null;
    }
    const cipher = createFieldCipher({ aes: primitive, secrets: store });
    await cipher.ensureKey();
    return cipher;
  } catch {
    return null;
  }
}
