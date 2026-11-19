import { base64UrlDecode, base64UrlEncode, createAuthorizationSecrets, randomToken, s256Challenge } from '../pkce';
import { nodeRandom, nodeSha256 } from '../testing/nodeCrypto';

describe('PKCE', () => {
  it('derives the S256 challenge of RFC 7636 appendix B', async () => {
    await expect(s256Challenge('dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk', nodeSha256)).resolves.toBe(
      'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM',
    );
  });

  it('encodes the octets of RFC 7636 appendix B as its verifier', () => {
    const octets = [
      116, 24, 223, 180, 151, 153, 224, 37, 79, 250, 96, 125, 216, 173, 187, 186, 22, 212, 37, 77, 105, 214, 191, 240,
      91, 88, 5, 88, 83, 132, 141, 121,
    ];

    expect(base64UrlEncode(new Uint8Array(octets))).toBe('dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk');
    expect(Array.from(base64UrlDecode('dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk'))).toEqual(octets);
  });

  it('makes a 43-character verifier, state and nonce from 256 random bits, all different', async () => {
    const secrets = await createAuthorizationSecrets(nodeRandom, nodeSha256);

    for (const value of [secrets.verifier, secrets.state, secrets.nonce]) {
      expect(value).toMatch(/^[A-Za-z0-9_-]{43}$/);
    }
    expect(new Set([secrets.verifier, secrets.state, secrets.nonce]).size).toBe(3);
    await expect(s256Challenge(secrets.verifier, nodeSha256)).resolves.toBe(secrets.challenge);
  });

  it('asks the random source for 32 bytes and refuses a short answer', async () => {
    const random = jest.fn((count: number) => new Uint8Array(count));
    await randomToken(random);
    expect(random).toHaveBeenCalledWith(32);

    await expect(randomToken(() => new Uint8Array(16))).rejects.toThrow(/wrong number of bytes/);
  });

  it('refuses a verifier outside RFC 7636 (too short or with other characters)', async () => {
    await expect(s256Challenge('short', nodeSha256)).rejects.toThrow();
    await expect(s256Challenge(`${'a'.repeat(42)}+`, nodeSha256)).rejects.toThrow();
  });

  it('refuses text that is not base64url', () => {
    expect(() => base64UrlDecode('a+b/')).toThrow();
    expect(() => base64UrlDecode('abcde')).toThrow();
  });
});
