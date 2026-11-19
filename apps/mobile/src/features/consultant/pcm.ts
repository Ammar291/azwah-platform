/*
 * 16-bit little-endian mono PCM, the format the live consultant protocol carries both ways (D-109, D-124). Phones are
 * little-endian, so typed arrays over the bytes read and write the samples as they are.
 */

/** The rate protocol version 2 uses both ways (24 kHz, D-124); the phone records at it, so nothing is resampled twice. */
export const VOICE_SAMPLE_RATE = 24000;

/** The samples in a buffer of 16-bit PCM (a trailing odd byte is ignored). */
export function int16Samples(data: ArrayBuffer): Int16Array {
  return new Int16Array(data, 0, Math.floor(data.byteLength / 2));
}

/** Linear resampling, enough for speech: the phone may deliver 44.1 or 48 kHz when 24 kHz is asked for. */
export function resample(input: Int16Array, fromRate: number, toRate: number): Int16Array {
  if (fromRate === toRate || input.length === 0) {
    return input;
  }
  const length = Math.max(1, Math.round((input.length * toRate) / fromRate));
  const output = new Int16Array(length);
  const step = fromRate / toRate;
  for (let index = 0; index < length; index += 1) {
    const position = index * step;
    const before = Math.min(Math.floor(position), input.length - 1);
    const after = Math.min(before + 1, input.length - 1);
    const weight = position - before;
    output[index] = Math.round((input[before] ?? 0) * (1 - weight) + (input[after] ?? 0) * weight);
  }
  return output;
}

/** Root-mean-square loudness, 0 (silence) to 32767. */
export function level(samples: Int16Array): number {
  if (samples.length === 0) {
    return 0;
  }
  let sum = 0;
  for (const sample of samples) {
    sum += sample * sample;
  }
  return Math.sqrt(sum / samples.length);
}

/** Joins byte chunks in order. */
export function concatBytes(chunks: readonly Uint8Array[]): Uint8Array {
  const total = chunks.reduce((length, chunk) => length + chunk.length, 0);
  const joined = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    joined.set(chunk, offset);
    offset += chunk.length;
  }
  return joined;
}

/** A playable WAV file: a 44-byte RIFF header in front of mono 16-bit PCM. */
export function wavFile(pcm: Uint8Array, sampleRate: number): Uint8Array {
  const file = new Uint8Array(44 + pcm.length);
  const view = new DataView(file.buffer);
  const ascii = (offset: number, text: string) => {
    for (let index = 0; index < text.length; index += 1) {
      file[offset + index] = text.charCodeAt(index);
    }
  };
  ascii(0, 'RIFF');
  view.setUint32(4, 36 + pcm.length, true);
  ascii(8, 'WAVE');
  ascii(12, 'fmt ');
  view.setUint32(16, 16, true); // fmt chunk size
  view.setUint16(20, 1, true); // PCM
  view.setUint16(22, 1, true); // mono
  view.setUint32(24, sampleRate, true);
  view.setUint32(28, sampleRate * 2, true); // byte rate
  view.setUint16(32, 2, true); // block align
  view.setUint16(34, 16, true); // bits per sample
  ascii(36, 'data');
  view.setUint32(40, pcm.length, true);
  file.set(pcm, 44);
  return file;
}
