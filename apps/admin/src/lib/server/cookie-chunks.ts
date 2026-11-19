import 'server-only';

/** Browsers keep about 4 KB per cookie including its attributes; chunks stay well under that. */
export const COOKIE_CHUNK_SIZE = 3800;

type Read = (name: string) => string | undefined;

/**
 * One cookie when the value fits, otherwise `name.0`, `name.1`, … where the first chunk starts with the chunk count
 * (`3.`), so a lost chunk is detected instead of joining a partial value.
 */
export function splitCookie(name: string, value: string, chunkSize = COOKIE_CHUNK_SIZE): { name: string; value: string }[] {
  if (value.length <= chunkSize) return [{ name, value }];
  const parts: string[] = [];
  for (let start = 0; start < value.length; start += chunkSize - 8) {
    parts.push(value.slice(start, start + chunkSize - 8));
  }
  return parts.map((part, index) => ({ name: `${name}.${index}`, value: index === 0 ? `${parts.length}.${part}` : part }));
}

export function joinCookie(name: string, read: Read): string | undefined {
  const whole = read(name);
  if (whole) return whole;
  const first = read(`${name}.0`);
  const header = first?.match(/^(\d{1,2})\.([\s\S]*)$/);
  if (!header) return undefined;
  const count = Number(header[1]);
  const parts = [header[2]];
  for (let index = 1; index < count; index++) {
    const part = read(`${name}.${index}`);
    if (part === undefined) return undefined;
    parts.push(part);
  }
  return parts.join('');
}

/** Every cookie name that may hold part of the value: the single name and any chunk that is present. */
export function cookieNamesFor(name: string, read: Read): string[] {
  const names = read(name) !== undefined ? [name] : [];
  for (let index = 0; read(`${name}.${index}`) !== undefined; index++) {
    names.push(`${name}.${index}`);
  }
  return names;
}
