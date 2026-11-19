import type { AesGcm } from '../protection/types';

interface NodeCipher {
  setAAD(data: Uint8Array): void;
  update(data: Uint8Array): Uint8Array;
  final(): Uint8Array;
  getAuthTag(): Uint8Array;
}
interface NodeDecipher {
  setAAD(data: Uint8Array): void;
  setAuthTag(tag: Uint8Array): void;
  update(data: Uint8Array): Uint8Array;
  final(): Uint8Array;
}
interface NodeCrypto {
  randomBytes(size: number): Uint8Array;
  createCipheriv(algorithm: 'aes-256-gcm', key: Uint8Array, iv: Uint8Array): NodeCipher;
  createDecipheriv(algorithm: 'aes-256-gcm', key: Uint8Array, iv: Uint8Array): NodeDecipher;
}
interface NodeBuffer {
  from(value: string, encoding: 'base64'): Uint8Array;
  from(value: Uint8Array): { toString(encoding: 'base64'): string };
  concat(parts: Uint8Array[]): Uint8Array;
}
declare const Buffer: NodeBuffer;

/**
 * Real AES-256-GCM from Node's crypto, with the same sealed layout as expo-crypto (nonce, ciphertext, tag), so the
 * envelope and store code run against actual encryption in tests.
 */
export function createNodeAesGcm(): AesGcm {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- Node built-in, tests only (no Node types in the app)
  const crypto = require('node:crypto') as NodeCrypto;
  const keyBytes = (key: string) => Buffer.from(key, 'base64');
  return {
    async generateKey() {
      return Buffer.from(crypto.randomBytes(32)).toString('base64');
    },
    async seal(key, plaintext, associatedData) {
      const iv = crypto.randomBytes(12);
      const cipher = crypto.createCipheriv('aes-256-gcm', keyBytes(key), iv);
      cipher.setAAD(associatedData);
      const body = Buffer.concat([cipher.update(plaintext), cipher.final()]);
      return new Uint8Array(Buffer.concat([iv, body, cipher.getAuthTag()]));
    },
    async open(key, sealed, associatedData) {
      if (sealed.length < 28) {
        throw new Error('Too short to be sealed.');
      }
      const decipher = crypto.createDecipheriv('aes-256-gcm', keyBytes(key), sealed.subarray(0, 12));
      decipher.setAAD(associatedData);
      decipher.setAuthTag(sealed.subarray(sealed.length - 16));
      return new Uint8Array(Buffer.concat([decipher.update(sealed.subarray(12, sealed.length - 16)), decipher.final()]));
    },
  };
}
