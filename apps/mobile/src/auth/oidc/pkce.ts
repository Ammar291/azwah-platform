import { fromBase64, toBase64 } from '@/storage/protection/base64';
import { utf8Encode } from '@/storage/protection/utf8';

/** Cryptographically secure random bytes (expo-crypto on the phone). */
export type RandomSource = (byteCount: number) => Uint8Array | Promise<Uint8Array>;

/** SHA-256 of the bytes (expo-crypto on the phone). */
export type Sha256 = (data: Uint8Array) => Promise<Uint8Array>;

/** 32 bytes: a 43-character verifier (RFC 7636 asks for 43 to 128) and 256-bit `state` and `nonce`. */
const RANDOM_BYTES = 32;

const VERIFIER = /^[A-Za-z0-9\-._~]{43,128}$/;

/** Base64url without padding (RFC 4648 section 5), as PKCE and JWTs use it. */
export function base64UrlEncode(bytes: Uint8Array): string {
  return toBase64(bytes).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** Throws on anything that is not unpadded base64url. */
export function base64UrlDecode(text: string): Uint8Array {
  if (!/^[A-Za-z0-9_-]*$/.test(text) || text.length % 4 === 1) {
    throw new Error('Not base64url.');
  }
  const base64 = text.replace(/-/g, '+').replace(/_/g, '/');
  return fromBase64(base64 + '='.repeat((4 - (base64.length % 4)) % 4));
}

/** A fresh random value of 256 bits as base64url (43 characters). */
export async function randomToken(random: RandomSource): Promise<string> {
  const bytes = await random(RANDOM_BYTES);
  if (bytes.length !== RANDOM_BYTES) {
    throw new Error('The random source returned the wrong number of bytes.');
  }
  return base64UrlEncode(bytes);
}

/** RFC 7636 S256: BASE64URL(SHA256(ASCII(code_verifier))). */
export async function s256Challenge(verifier: string, sha256: Sha256): Promise<string> {
  if (!VERIFIER.test(verifier)) {
    throw new Error('Not a PKCE code verifier.');
  }
  return base64UrlEncode(await sha256(utf8Encode(verifier)));
}

/** Everything one authorization request needs that must never be guessed or reused. */
export interface AuthorizationSecrets {
  verifier: string;
  challenge: string;
  state: string;
  nonce: string;
}

export async function createAuthorizationSecrets(random: RandomSource, sha256: Sha256): Promise<AuthorizationSecrets> {
  const verifier = await randomToken(random);
  return {
    verifier,
    challenge: await s256Challenge(verifier, sha256),
    state: await randomToken(random),
    nonce: await randomToken(random),
  };
}
