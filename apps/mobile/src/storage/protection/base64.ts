const ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
const VALUES = new Map([...ALPHABET].map((char, index) => [char, index]));

/** Standard base64 with padding, without depending on the engine's btoa. */
export function toBase64(bytes: Uint8Array): string {
  let text = '';
  for (let index = 0; index < bytes.length; index += 3) {
    const a = bytes[index] ?? 0;
    const b = bytes[index + 1];
    const c = bytes[index + 2];
    const triple = (a << 16) | ((b ?? 0) << 8) | (c ?? 0);
    text += ALPHABET[(triple >> 18) & 0x3f];
    text += ALPHABET[(triple >> 12) & 0x3f];
    text += b === undefined ? '=' : ALPHABET[(triple >> 6) & 0x3f];
    text += c === undefined ? '=' : ALPHABET[triple & 0x3f];
  }
  return text;
}

/** Throws on anything that is not padded standard base64. */
export function fromBase64(text: string): Uint8Array {
  if (text.length % 4 !== 0 || !/^[A-Za-z0-9+/]*={0,2}$/.test(text)) {
    throw new Error('Not base64.');
  }
  const padding = text.endsWith('==') ? 2 : text.endsWith('=') ? 1 : 0;
  const bytes = new Uint8Array((text.length / 4) * 3 - padding);
  let out = 0;
  for (let index = 0; index < text.length; index += 4) {
    const quad = [0, 1, 2, 3].map((offset) => VALUES.get(text[index + offset] ?? '') ?? 0);
    const triple = ((quad[0] ?? 0) << 18) | ((quad[1] ?? 0) << 12) | ((quad[2] ?? 0) << 6) | (quad[3] ?? 0);
    for (const shift of [16, 8, 0]) {
      if (out < bytes.length) {
        bytes[out++] = (triple >> shift) & 0xff;
      }
    }
  }
  return bytes;
}
