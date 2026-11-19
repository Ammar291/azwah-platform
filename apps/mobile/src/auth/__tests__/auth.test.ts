import { createSettingsRepository } from '@/storage/settings';
import { FakeSqlDatabase } from '@/storage/testing/FakeSqlDatabase';

import { createDevelopmentMockAuth, isDevelopmentMockAuth } from '../developmentMockAuth';
import { entryHref, isSignedIn, isStartupReady } from '../entryHref';
import { createOidcPlaceholderAuth } from '../oidcPlaceholderAuth';
import { SignInUnavailableError } from '../types';

const PERSONAS = [
  { code: 'household_lead', subject: 'dev-mock|household_lead' },
  { code: 'new_participant', subject: 'dev-mock|new_participant' },
];

const setup = () => {
  const db = new FakeSqlDatabase();
  const settings = createSettingsRepository(db);
  return { db, settings, auth: createDevelopmentMockAuth({ settings, personas: PERSONAS }) };
};

describe('development mock auth', () => {
  it('starts initializing and restores to signed out when nobody signed in', async () => {
    const { auth } = setup();
    expect(auth.getState()).toEqual({ status: 'initializing' });

    await auth.restore();

    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'none' });
  });

  it('signs in as a persona, remembering only its code, and never has a token', async () => {
    const { auth, db } = setup();
    await auth.restore();

    await auth.signIn({ method: 'development_persona', personaCode: 'household_lead' });

    expect(auth.getState()).toEqual({
      status: 'signed_in',
      session: { subject: 'dev-mock|household_lead', personaCode: 'household_lead', profile: null },
    });
    expect([...db.settings.entries()]).toEqual([['dev_mock_persona', 'household_lead']]);
    await expect(auth.getAccessToken()).resolves.toBeNull();
  });

  it('restores the remembered persona after a reload', async () => {
    const { settings } = setup();
    await settings.set('dev_mock_persona', 'new_participant');
    const auth = createDevelopmentMockAuth({ settings, personas: PERSONAS });

    await auth.restore();

    expect(isSignedIn(auth.getState())).toBe(true);
  });

  it('refuses an unknown persona and other sign-in methods', async () => {
    const { auth } = setup();

    await expect(auth.signIn({ method: 'development_persona', personaCode: 'nobody' })).rejects.toThrow(/Unknown/);
    await expect(auth.signIn({ method: 'oidc' })).rejects.toThrow(/development persona/);
  });

  it('notifies subscribers and forgets the persona on sign-out', async () => {
    const { auth, db } = setup();
    const listener = jest.fn();
    auth.subscribe(listener);
    await auth.signIn({ method: 'development_persona', personaCode: 'household_lead' });

    await auth.signOut();

    expect(listener).toHaveBeenCalledTimes(2);
    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'signed_out' });
    expect(db.settings.size).toBe(0);
  });

  it('ends the session as expired on a 401, once', async () => {
    const { auth } = setup();
    await auth.signIn({ method: 'development_persona', personaCode: 'household_lead' });
    const listener = jest.fn();
    auth.subscribe(listener);

    auth.reportUnauthorized();
    auth.reportUnauthorized();

    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'session_expired' });
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it('signs out after a demonstration reset and says so until someone signs in again', async () => {
    const { auth, db } = setup();
    await auth.signIn({ method: 'development_persona', personaCode: 'household_lead' });
    expect(auth.demonstrationReset).toBe(false);

    await auth.signOutAfterDemonstrationReset();

    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'signed_out' });
    expect(auth.demonstrationReset).toBe(true);
    expect(db.settings.size).toBe(0);

    await auth.signIn({ method: 'development_persona', personaCode: 'new_participant' });
    await auth.signOut();

    expect(auth.demonstrationReset).toBe(false);
  });

  it('is recognisable as the development mock', () => {
    const { auth } = setup();

    expect(isDevelopmentMockAuth(auth)).toBe(true);
    expect(isDevelopmentMockAuth(createOidcPlaceholderAuth())).toBe(false);
  });
});

describe('OIDC placeholder', () => {
  it('says sign-in is not configured and refuses to sign in', async () => {
    const auth = createOidcPlaceholderAuth();
    await auth.restore();

    expect(auth.getState()).toEqual({ status: 'unavailable', reason: 'not_configured' });
    await expect(auth.signIn({ method: 'oidc' })).rejects.toBeInstanceOf(SignInUnavailableError);
    await expect(auth.getAccessToken()).resolves.toBeNull();
  });
});

describe('entryHref', () => {
  it('waits while initializing', () => {
    expect(entryHref({ status: 'initializing' })).toBeNull();
  });

  const signedIn = { status: 'signed_in', session: { subject: 's', personaCode: null, profile: null } } as const;

  it('sends signed-in people home', () => {
    expect(entryHref(signedIn)).toBe('/home');
    expect(entryHref(signedIn, { active: false, finishing: false, registration: 'registered' })).toBe('/home');
  });

  it('sends a first sign-in to confirm details, then to account ready', () => {
    expect(entryHref(signedIn, { active: true, finishing: false, registration: 'required' })).toBe('/confirm-details');
    expect(entryHref(signedIn, { active: true, finishing: true, registration: 'registered' })).toBe('/account-ready');
  });

  it('lets the opening splash go once the sign-in state is known, without waiting for the registration check', () => {
    expect(isStartupReady({ status: 'initializing' })).toBe(false);
    expect(isStartupReady(signedIn)).toBe(true);
    expect(isStartupReady({ status: 'signed_out', reason: 'none' })).toBe(true);
    expect(isStartupReady({ status: 'unavailable', reason: 'not_configured' })).toBe(true);
  });

  it('waits for the registration check, but goes home when the server cannot be asked', () => {
    expect(entryHref(signedIn, { active: false, finishing: false, registration: 'checking' })).toBeNull();
    expect(entryHref(signedIn, { active: false, finishing: false, registration: 'unknown' })).toBe('/home');
  });

  it('sends an expired session to sign-in and everyone else to welcome', () => {
    expect(entryHref({ status: 'signed_out', reason: 'session_expired' })).toBe('/auth/sign-in');
    expect(entryHref({ status: 'signed_out', reason: 'signed_out' })).toBe('/welcome');
    expect(entryHref({ status: 'signed_out', reason: 'none' })).toBe('/welcome');
    expect(entryHref({ status: 'unavailable', reason: 'not_configured' })).toBe('/welcome');
  });
});
