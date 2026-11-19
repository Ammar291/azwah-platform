/*
 * UTF-8 by hand: Hermes' TextEncoder and TextDecoder support has changed between React Native releases, and the
 * cipher must read back exactly what it wrote on every engine the app runs on.
 */

export function utf8Encode(text: string): Uint8Array {
  const bytes: number[] = [];
  for (const char of text) {
    const point = char.codePointAt(0) ?? 0;
    if (point < 0x80) {
      bytes.push(point);
    } else if (point < 0x800) {
      bytes.push(0xc0 | (point >> 6), 0x80 | (point & 0x3f));
    } else if (point < 0x10000) {
      bytes.push(0xe0 | (point >> 12), 0x80 | ((point >> 6) & 0x3f), 0x80 | (point & 0x3f));
    } else {
      bytes.push(
        0xf0 | (point >> 18),
        0x80 | ((point >> 12) & 0x3f),
        0x80 | ((point >> 6) & 0x3f),
        0x80 | (point & 0x3f),
      );
    }
  }
  return Uint8Array.from(bytes);
}

/** Throws on bytes that are not well-formed UTF-8. */
export function utf8Decode(bytes: Uint8Array): string {
  let text = '';
  let index = 0;
  const continuation = () => {
    const byte = bytes[index++];
    if (byte === undefined || (byte & 0xc0) !== 0x80) {
      throw new Error('Not UTF-8.');
    }
    return byte & 0x3f;
  };
  while (index < bytes.length) {
    const lead = bytes[index++] ?? 0;
    let point: number;
    if (lead < 0x80) {
      point = lead;
    } else if (lead >= 0xc2 && lead < 0xe0) {
      point = ((lead & 0x1f) << 6) | continuation();
    } else if (lead >= 0xe0 && lead < 0xf0) {
      point = ((lead & 0x0f) << 12) | (continuation() << 6) | continuation();
      if (point < 0x800 || (point >= 0xd800 && point < 0xe000)) throw new Error('Not UTF-8.');
    } else if (lead >= 0xf0 && lead < 0xf5) {
      point = ((lead & 0x07) << 18) | (continuation() << 12) | (continuation() << 6) | continuation();
      if (point < 0x10000 || point > 0x10ffff) throw new Error('Not UTF-8.');
    } else {
      throw new Error('Not UTF-8.');
    }
    text += String.fromCodePoint(point);
  }
  return text;
}
