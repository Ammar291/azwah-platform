import type { RandomSource, Sha256 } from '../pkce';

interface NodeHash {
  update(data: Uint8Array): NodeHash;
  digest(): Uint8Array;
}
interface NodeCrypto {
  randomBytes(size: number): Uint8Array;
  createHash(algorithm: 'sha256'): NodeHash;
}

// eslint-disable-next-line @typescript-eslint/no-require-imports -- Node built-in, tests only (no Node types in the app)
const crypto = require('node:crypto') as NodeCrypto;

/** Real SHA-256 and random bytes from Node, standing in for expo-crypto in tests. */
export const nodeSha256: Sha256 = async (data) => new Uint8Array(crypto.createHash('sha256').update(data).digest());

export const nodeRandom: RandomSource = (byteCount) => new Uint8Array(crypto.randomBytes(byteCount));
