// @vitest-environment node
import { describe, expect, it } from 'vitest';

import { sanitizeReturnTo } from './return-to';

describe('sanitizeReturnTo', () => {
  it.each([
    ['/overview', '/overview'],
    ['/overview?waveId=abc&page=2', '/overview?waveId=abc&page=2'],
    ['/configuration/versions/1', '/configuration/versions/1'],
  ])('keeps the same-site path %s', (value, expected) => {
    expect(sanitizeReturnTo(value)).toBe(expected);
  });

  it.each([
    ['//evil.example'],
    ['/\\evil.example'],
    ['https://evil.example/overview'],
    ['javascript:alert(1)'],
    ['overview'],
    ['/auth/callback'],
    ['/bff/me/permissions'],
    ['/sign-in'],
    [''],
    [null],
    [undefined],
  ])('sends %s home instead', (value) => {
    expect(sanitizeReturnTo(value)).toBe('/');
  });
});
