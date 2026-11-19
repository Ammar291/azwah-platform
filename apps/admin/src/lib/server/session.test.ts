// @vitest-environment node
import { describe, expect, it } from 'vitest';

import { sealSession, sessionCookieName, unsealSession, type Session } from './session';

const secret = new Uint8Array(32).fill(1);
const otherSecret = new Uint8Array(32).fill(2);
const now = Date.UTC(2026, 9, 16, 9, 0, 0);

const oidc: Session = {
  kind: 'oidc',
  accessToken: 'access',
  refreshToken: 'refresh',
  accessTokenExpiresAt: now + 300_000,
  issuedAt: now,
};

describe('session sealing', () => {
  it('round-trips a session', async () => {
    const sealed = await sealSession(oidc, secret, 3600);

    expect(sealed).not.toContain('access');
    expect(await unsealSession(sealed, secret, now + 1000)).toEqual(oidc);
  });

  it('round-trips a development session', async () => {
    const session: Session = { kind: 'development', persona: 'researcher', issuedAt: now };

    expect(await unsealSession(await sealSession(session, secret, 3600), secret, now)).toEqual(session);
  });

  it('refuses a session sealed with another key', async () => {
    expect(await unsealSession(await sealSession(oidc, otherSecret, 3600), secret, now)).toBeNull();
  });

  it('refuses a tampered session', async () => {
    const sealed = await sealSession(oidc, secret, 3600);
    const tampered = `${sealed.slice(0, -4)}${sealed.slice(-4) === 'AAAA' ? 'BBBB' : 'AAAA'}`;

    expect(await unsealSession(tampered, secret, now)).toBeNull();
  });

  it('refuses a session past its lifetime, counted from when it was issued', async () => {
    const sealed = await sealSession(oidc, secret, 3600);

    expect(await unsealSession(sealed, secret, now + 3_601_000)).toBeNull();
  });

  it('keeps the original lifetime when a session is sealed again', async () => {
    const resealed = await sealSession({ ...oidc, accessToken: 'new' }, secret, 3600);

    expect(await unsealSession(resealed, secret, now + 3_601_000)).toBeNull();
  });

  it('refuses garbage and empty values', async () => {
    expect(await unsealSession(undefined, secret, now)).toBeNull();
    expect(await unsealSession('', secret, now)).toBeNull();
    expect(await unsealSession('not-a-jwe', secret, now)).toBeNull();
  });

  it('refuses a payload that is not a session', async () => {
    const sealed = await sealSession({ kind: 'development', persona: 'root', issuedAt: now } as unknown as Session, secret, 3600);

    expect(await unsealSession(sealed, secret, now)).toBeNull();
  });

  it('names the cookie with the __Host- prefix only over HTTPS', () => {
    expect(sessionCookieName(true)).toBe('__Host-azwah_session');
    expect(sessionCookieName(false)).toBe('azwah_session');
  });
});
