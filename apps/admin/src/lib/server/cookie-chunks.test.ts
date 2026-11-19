// @vitest-environment node
import { describe, expect, it } from 'vitest';

import { cookieNamesFor, joinCookie, splitCookie } from './cookie-chunks';

function jar(cookies: { name: string; value: string }[]) {
  const map = new Map(cookies.map((cookie) => [cookie.name, cookie.value]));
  return (name: string) => map.get(name);
}

describe('cookie chunks', () => {
  it('keeps a short value in one cookie', () => {
    expect(splitCookie('s', 'abc')).toEqual([{ name: 's', value: 'abc' }]);
    expect(joinCookie('s', jar([{ name: 's', value: 'abc' }]))).toBe('abc');
  });

  it('splits a long value into chunks of at most the chunk size and joins them back', () => {
    const value = 'x'.repeat(9000);
    const chunks = splitCookie('s', value, 3800);

    expect(chunks.map((chunk) => chunk.name)).toEqual(['s.0', 's.1', 's.2']);
    expect(Math.max(...chunks.map((chunk) => chunk.value.length))).toBeLessThanOrEqual(3800);
    expect(joinCookie('s', jar(chunks))).toBe(value);
  });

  it('treats a missing chunk as no cookie at all', () => {
    const chunks = splitCookie('s', 'y'.repeat(9000), 3800).filter((chunk) => chunk.name !== 's.1');

    expect(joinCookie('s', jar(chunks))).toBeUndefined();
  });

  it('treats a malformed first chunk as no cookie', () => {
    expect(joinCookie('s', jar([{ name: 's.0', value: 'nonsense' }]))).toBeUndefined();
  });

  it('returns nothing when there is no cookie', () => {
    expect(joinCookie('s', jar([]))).toBeUndefined();
  });

  it('lists every cookie name that belongs to the value, for clearing', () => {
    const read = jar([
      { name: 's', value: 'old' },
      { name: 's.0', value: '2.ab' },
      { name: 's.1', value: 'cd' },
      { name: 'other', value: 'z' },
    ]);

    expect(cookieNamesFor('s', read)).toEqual(['s', 's.0', 's.1']);
  });
});
