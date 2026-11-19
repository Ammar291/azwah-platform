import { ApiError, type FetchLike } from '@/api/client';
import { utf8Decode } from '@/storage/protection/utf8';

import { SignInError, type CredentialStore, type OidcAuthProvider } from '../../types';
import { createOidcAuth } from '../oidcAuth';
import { base64UrlDecode, s256Challenge } from '../pkce';
import {
  createFakeIdentityProvider,
  createRecordingCredentials,
  TEST_API,
  TEST_CLIENT_ID,
  TEST_ISSUER,
  TEST_NOW,
  TEST_REDIRECT_URI,
  TEST_SIGNED_OUT_URI,
  testConfig,
  testJwt,
  type FakeIdentityProvider,
} from '../testing/fakeIdentityProvider';
import { nodeRandom, nodeSha256 } from '../testing/nodeCrypto';

const LEAD = 'dev-participant-lead';
const CONSOLE_METHODS = ['log', 'info', 'warn', 'error', 'debug'] as const;

interface Setup {
  clock: { now: number };
  idp: FakeIdentityProvider;
  recording: ReturnType<typeof createRecordingCredentials>;
  auth: OidcAuthProvider;
}

interface Options {
  isDevelopmentBuild?: boolean;
  developmentOnly?: boolean;
  /** The build's UAE PASS hint (EXPO_PUBLIC_OIDC_IDP_HINT). */
  idpHint?: string | null;
  /** Share a keystore (and identity provider) with an earlier phone session, as after an app restart. */
  restartOf?: Setup;
  credentials?: CredentialStore;
  fetchImpl?: FetchLike;
  restoreWaitMs?: number;
}

function setup(options: Options = {}): Setup {
  const clock = options.restartOf?.clock ?? { now: TEST_NOW };
  const idp: FakeIdentityProvider =
    options.restartOf?.idp ?? createFakeIdentityProvider({ clock: () => clock.now, developmentOnly: options.developmentOnly });
  const recording = options.restartOf?.recording ?? createRecordingCredentials();
  const auth: OidcAuthProvider = createOidcAuth({
    config: testConfig({ isDevelopmentBuild: options.isDevelopmentBuild ?? false, idpHint: options.idpHint ?? null }),
    browser: idp.browser,
    fetchImpl: options.fetchImpl ?? idp.fetchImpl,
    random: nodeRandom,
    sha256: nodeSha256,
    credentials: options.credentials ?? recording.credentials,
    clock: () => clock.now,
    apiBaseUrl: TEST_API,
    restoreWaitMs: options.restoreWaitMs,
  });
  return { clock, idp, recording, auth };
}

async function signedIn(options: Options = {}): Promise<Setup> {
  const world = setup(options);
  await world.auth.restore();
  await world.auth.signIn({ method: 'oidc' });
  return world;
}

async function signInFailure(auth: OidcAuthProvider): Promise<string> {
  const error = await auth.signIn({ method: 'oidc' }).then(
    () => null,
    (caught: unknown) => caught,
  );
  if (!(error instanceof SignInError)) throw new Error(`expected a SignInError, got ${String(error)}`);
  return error.kind;
}

const minutes = (count: number) => count * 60_000;
const tokenGrants = (idp: FakeIdentityProvider) =>
  idp.requestsTo('token').map((request) => new URLSearchParams(request.body ?? '').get('grant_type'));
const keystoreEntries = async ({ recording }: Setup) => ({
  refreshToken: await recording.credentials.read('refresh_token'),
  subject: await recording.credentials.read('subject'),
});

let consoleSpies: jest.SpyInstance[] = [];

beforeEach(() => {
  consoleSpies = CONSOLE_METHODS.map((method) => jest.spyOn(console, method));
});

afterEach(() => {
  // Nothing about tokens, codes, addresses or responses is ever logged, on success or failure.
  for (const spy of consoleSpies) {
    expect(spy).not.toHaveBeenCalled();
    spy.mockRestore();
  }
});

describe('OpenID Connect sign-in', () => {
  it('signs in through the browser with the code flow, PKCE S256, state, nonce and prompt=login', async () => {
    const { auth, idp } = await signedIn();

    expect(auth.getState()).toEqual({ status: 'signed_in', session: { subject: LEAD, personaCode: null, profile: null } });
    expect(idp.opened).toHaveLength(1);
    const [base, query] = (idp.opened[0] ?? '').split('?');
    const authorize = new URLSearchParams(query);
    expect(base).toBe(`${TEST_ISSUER}/authorize`);
    expect(Object.fromEntries(authorize)).toMatchObject({
      response_type: 'code',
      client_id: TEST_CLIENT_ID,
      redirect_uri: TEST_REDIRECT_URI,
      scope: 'openid offline_access',
      code_challenge_method: 'S256',
      prompt: 'login',
    });
    for (const name of ['state', 'nonce', 'code_challenge']) {
      expect(authorize.get(name)).toMatch(/^[A-Za-z0-9_-]{43}$/);
    }

    const exchange = new URLSearchParams(idp.requestsTo('token')[0]?.body ?? '');
    expect(exchange.get('grant_type')).toBe('authorization_code');
    expect(exchange.get('redirect_uri')).toBe(TEST_REDIRECT_URI);
    expect(exchange.get('client_id')).toBe(TEST_CLIENT_ID);
    expect(exchange.has('client_secret')).toBe(false);
    await expect(s256Challenge(exchange.get('code_verifier') ?? '', nodeSha256)).resolves.toBe(authorize.get('code_challenge'));
  });

  it('keeps only the refresh token and the subject in the keystore, and the access token in memory', async () => {
    const world = await signedIn();
    const { auth, idp, recording } = world;
    const [accessToken, refreshToken, idToken] = idp.issued.slice(1);

    expect(recording.writes).toEqual([
      ['refresh_token', refreshToken],
      ['subject', LEAD],
    ]);
    expect(JSON.stringify(recording.writes)).not.toContain(accessToken);
    expect(JSON.stringify(recording.writes)).not.toContain(idToken);
    await expect(auth.getAccessToken()).resolves.toBe(accessToken);
    await expect(keystoreEntries(world)).resolves.toEqual({ refreshToken, subject: LEAD });
  });

  it('records the sign-in with the new bearer token', async () => {
    const { idp } = await signedIn();
    const accessToken = idp.issued[1];

    const records = idp.requestsTo('sign-ins');
    expect(records).toHaveLength(1);
    expect(records[0]).toMatchObject({
      url: `${TEST_API}/api/v1/me/sign-ins`,
      method: 'POST',
      body: '{"client":"mobile"}',
      headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json' },
    });
  });

  it.each([
    ['cannot be sent', 'network' as const],
    ['is refused', { status: 429, body: { code: 'requests.rate_limited' } }],
  ])('still signs in when the sign-in record %s, without saying so', async (_name, fault) => {
    const world = setup();
    world.idp.fail('sign-ins', fault);
    await world.auth.restore();

    await expect(world.auth.signIn({ method: 'oidc' })).resolves.toBeUndefined();

    expect(world.auth.getState()).toMatchObject({ status: 'signed_in' });
  });

  it('treats a closed browser and a denied request as cancelled, quietly staying signed out', async () => {
    const { auth, idp } = setup();
    await auth.restore();

    idp.answerBrowser(() => ({ type: 'cancel' }));
    await expect(signInFailure(auth)).resolves.toBe('cancelled');

    idp.answerBrowser((authorize) => ({
      type: 'success',
      url: `${TEST_REDIRECT_URI}?error=access_denied&state=${authorize.get('state') ?? ''}&iss=${encodeURIComponent(TEST_ISSUER)}`,
    }));
    await expect(signInFailure(auth)).resolves.toBe('cancelled');

    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'none' });
    expect(idp.requestsTo('token')).toHaveLength(0);
  });

  it.each([
    ['another state', (url: string) => url.replace(/state=[^&]+/, 'state=forged')],
    ['another issuer', (url: string) => url.replace(/iss=[^&]+/, `iss=${encodeURIComponent('https://evil.example.test')}`)],
    ['no issuer although the provider promised one', (url: string) => url.replace(/&iss=[^&]+/, '')],
    ['a foreign redirect', (url: string) => url.replace(TEST_REDIRECT_URI, 'https://evil.example.test/callback')],
  ])('refuses a callback with %s before exchanging any code', async (_name, tamper) => {
    const { auth, idp, recording } = setup();
    await auth.restore();
    idp.answerBrowser((_authorize, approve) => ({ type: 'success', url: tamper(approve()) }));

    await expect(signInFailure(auth)).resolves.toBe('unsafe_response');

    expect(idp.requestsTo('token')).toHaveLength(0);
    expect(recording.writes).toEqual([]);
    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'none' });
  });

  it('turns an error answer from the provider into refused', async () => {
    const { auth, idp } = setup();
    await auth.restore();
    idp.answerBrowser((authorize) => ({
      type: 'success',
      url: `${TEST_REDIRECT_URI}?error=invalid_request&state=${authorize.get('state') ?? ''}&iss=${encodeURIComponent(TEST_ISSUER)}`,
    }));

    await expect(signInFailure(auth)).resolves.toBe('refused');
  });

  it.each([
    ['another issuer', { iss: 'https://evil.example.test' }],
    ['another audience', { aud: 'someone-else', azp: 'someone-else' }],
    ['another nonce', { nonce: 'replayed-nonce' }],
    ['an expiry in the past', { exp: TEST_NOW / 1000 - 3600 }],
    ['no subject', { sub: undefined }],
  ])('refuses an ID token with %s and keeps nothing', async (_name, patch) => {
    const { auth, idp, recording } = setup();
    await auth.restore();
    idp.editTokenResponses((body) => {
      const [, payload = ''] = String(body.id_token).split('.');
      const claims = JSON.parse(utf8Decode(base64UrlDecode(payload))) as Record<string, unknown>;
      return { ...body, id_token: testJwt({ ...claims, ...patch }) };
    });

    await expect(signInFailure(auth)).resolves.toBe('unsafe_response');

    expect(recording.writes).toEqual([]);
    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'none' });
    await expect(auth.getAccessToken()).resolves.toBeNull();
    expect(idp.requestsTo('sign-ins')).toHaveLength(0);
  });

  it('refuses a token response without an ID token', async () => {
    const { auth, idp } = setup();
    await auth.restore();
    idp.editTokenResponses((body) => ({ ...body, id_token: undefined }));

    await expect(signInFailure(auth)).resolves.toBe('unsafe_response');
  });

  it('says the provider is unreachable when discovery fails, and tries again next time', async () => {
    const { auth, idp } = setup();
    await auth.restore();
    idp.fail('discovery', 'network');

    await expect(signInFailure(auth)).resolves.toBe('provider_unreachable');
    expect(idp.opened).toHaveLength(0);

    await auth.signIn({ method: 'oidc' });
    expect(auth.getState()).toMatchObject({ status: 'signed_in' });
  });

  it('tells a refused code from an unreachable token endpoint', async () => {
    const { auth, idp } = setup();
    await auth.restore();

    idp.fail('token', { status: 400, body: { error: 'invalid_grant' } });
    await expect(signInFailure(auth)).resolves.toBe('refused');

    idp.fail('token', 'network');
    await expect(signInFailure(auth)).resolves.toBe('provider_unreachable');
  });

  it('refuses a development-only issuer in a release build without opening the browser', async () => {
    const { auth, idp } = setup({ developmentOnly: true, isDevelopmentBuild: false });
    await auth.restore();

    await expect(signInFailure(auth)).resolves.toBe('unsafe_response');
    expect(idp.opened).toHaveLength(0);
    await expect(auth.describeIssuer()).resolves.toBeNull();
  });

  it('describes a development-only issuer to development builds', async () => {
    await expect(setup({ developmentOnly: true, isDevelopmentBuild: true }).auth.describeIssuer()).resolves.toEqual({
      developmentOnly: true,
      offersGoogle: false,
    });
    await expect(setup().auth.describeIssuer()).resolves.toEqual({ developmentOnly: false, offersGoogle: false });

    const offline = setup();
    offline.idp.fail('discovery', 'network');
    await expect(offline.auth.describeIssuer()).resolves.toBeNull();
  });

  it.each([
    [['uae_pass', 'google'], true],
    [['uae_pass'], false],
    [[], false],
    ['google', false],
    [[1, 2], false],
  ])('describes whether the issuer offers Google (azwah_identity_providers %j, D-147)', async (listed, offersGoogle) => {
    const world = setup();
    world.idp.patchDiscovery({ azwah_identity_providers: listed });

    await expect(world.auth.describeIssuer()).resolves.toEqual({ developmentOnly: false, offersGoogle });
  });

  it('asks the issuer for Google with idp_hint=google, whatever the build sets for UAE PASS', async () => {
    const world = setup({ idpHint: null });
    await world.auth.restore();

    await world.auth.signIn({ method: 'oidc', identityProvider: 'google', language: 'en' });

    const query = new URLSearchParams(world.idp.opened[0]?.split('?')[1] ?? '');
    expect(query.get('idp_hint')).toBe('google');
    expect(query.get('ui_locales')).toBe('en');
    expect(world.auth.getState()).toMatchObject({ status: 'signed_in' });
  });

  it('runs one sign-in at a time', async () => {
    const { auth, idp } = setup();
    await auth.restore();

    await Promise.all([auth.signIn({ method: 'oidc' }), auth.signIn({ method: 'oidc' })]);

    expect(idp.opened).toHaveLength(1);
  });

  it('refuses the development persona method', async () => {
    const { auth } = setup();

    await expect(auth.signIn({ method: 'development_persona', personaCode: 'household_lead' })).rejects.toThrow(/oidc/);
  });
});

describe('OpenID Connect access tokens', () => {
  it('returns the token in memory while it has more than 60 s left', async () => {
    const { auth, idp, clock } = await signedIn();
    const accessToken = idp.issued[1];

    clock.now += minutes(10) - 61_000;

    await expect(auth.getAccessToken()).resolves.toBe(accessToken);
    expect(tokenGrants(idp)).toEqual(['authorization_code']);
  });

  it('refreshes 60 s before expiry, once for everyone asking, and keeps the rotated refresh token', async () => {
    const world = await signedIn();
    const { auth, idp, clock, recording } = world;
    clock.now += minutes(10) - 59_000;

    const tokens = await Promise.all([auth.getAccessToken(), auth.getAccessToken(), auth.getAccessToken()]);

    expect(tokenGrants(idp)).toEqual(['authorization_code', 'refresh_token']);
    const refreshed = idp.issued.at(-3);
    expect(tokens).toEqual([refreshed, refreshed, refreshed]);
    expect(new URLSearchParams(idp.requestsTo('token')[1]?.body ?? '').get('refresh_token')).toBe(idp.issued[2]);
    expect(recording.writes.at(-1)).toEqual(['refresh_token', idp.currentRefreshToken]);
    await expect(keystoreEntries(world)).resolves.toEqual({ refreshToken: idp.currentRefreshToken, subject: LEAD });

    // The rotated token works for the next renewal; the first one was used up.
    clock.now += minutes(10);
    expect(await auth.getAccessToken()).toBe(idp.issued.at(-3));
    expect(tokenGrants(idp)).toEqual(['authorization_code', 'refresh_token', 'refresh_token']);
  });

  it('ends the session as expired when the provider refuses the refresh token (invalid_grant)', async () => {
    const world = await signedIn();
    const { auth, idp, clock, recording } = world;
    idp.fail('token', { status: 400, body: { error: 'invalid_grant' } });
    clock.now += minutes(10);

    await expect(auth.getAccessToken()).resolves.toBeNull();

    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'session_expired' });
    expect(recording.credentials.clear).toHaveBeenCalled();
    await expect(keystoreEntries(world)).resolves.toEqual({ refreshToken: null, subject: null });
  });

  it('keeps the session and throws a network error when the refresh cannot reach the provider', async () => {
    const world = await signedIn();
    const { auth, idp, clock } = world;
    idp.fail('token', 'network');
    clock.now += minutes(10);

    const error = await auth.getAccessToken().catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ kind: 'network' });
    expect(auth.getState()).toEqual({ status: 'signed_in', session: { subject: LEAD, personaCode: null, profile: null } });
    await expect(keystoreEntries(world)).resolves.toMatchObject({ subject: LEAD });

    // Back online: the same refresh token still works.
    expect(await auth.getAccessToken()).toBe(idp.issued.at(-3));
  });

  it('keeps the session through a provider fault, as a retryable server error', async () => {
    const { auth, idp, clock } = await signedIn();
    idp.fail('token', { status: 503 });
    clock.now += minutes(10);

    await expect(auth.getAccessToken()).rejects.toMatchObject({ kind: 'server' });
    expect(auth.getState()).toMatchObject({ status: 'signed_in' });
  });

  it('ends the session when a refreshed ID token names another subject', async () => {
    const { auth, idp, clock } = await signedIn();
    idp.setSubject('dev-participant-new');
    clock.now += minutes(10);

    await expect(auth.getAccessToken()).resolves.toBeNull();

    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'session_expired' });
  });

  it('ends the session as expired on a 401 from the API, once, forgetting every token', async () => {
    const world = await signedIn();
    const { auth } = world;
    const listener = jest.fn();
    auth.subscribe(listener);

    auth.reportUnauthorized();
    auth.reportUnauthorized();

    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'session_expired' });
    expect(listener).toHaveBeenCalledTimes(1);
    await expect(auth.getAccessToken()).resolves.toBeNull();
    await expect(keystoreEntries(world)).resolves.toEqual({ refreshToken: null, subject: null });
  });
});

describe('OpenID Connect restore', () => {
  it('is signed out when nothing is stored, without calling anyone', async () => {
    const { auth, idp } = setup();

    await auth.restore();

    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'none' });
    expect(idp.requests).toEqual([]);
  });

  it('restores a stored session after a restart by refreshing it', async () => {
    const before = await signedIn();
    const after = setup({ restartOf: before });

    await after.auth.restore();

    expect(after.auth.getState()).toEqual({ status: 'signed_in', session: { subject: LEAD, personaCode: null, profile: null } });
    expect(tokenGrants(after.idp)).toEqual(['authorization_code', 'refresh_token']);
    expect(await after.auth.getAccessToken()).toBe(after.idp.issued.at(-3));
    await expect(keystoreEntries(after)).resolves.toEqual({ refreshToken: after.idp.currentRefreshToken, subject: LEAD });
  });

  it('ends a stored session the provider refuses as expired and clears it', async () => {
    const before = await signedIn();
    const after = setup({ restartOf: before });
    after.idp.fail('token', { status: 400, body: { error: 'invalid_grant' } });

    await after.auth.restore();

    expect(after.auth.getState()).toEqual({ status: 'signed_out', reason: 'session_expired' });
    await expect(keystoreEntries(after)).resolves.toEqual({ refreshToken: null, subject: null });
  });

  it('never restores a refresh token that now belongs to another subject', async () => {
    const before = await signedIn();
    const after = setup({ restartOf: before });
    after.idp.setSubject('dev-participant-new');

    await after.auth.restore();

    expect(after.auth.getState()).toEqual({ status: 'signed_out', reason: 'session_expired' });
    await expect(keystoreEntries(after)).resolves.toEqual({ refreshToken: null, subject: null });
  });

  it('stays signed in offline with the stored subject and refreshes once the network is back', async () => {
    const before = await signedIn();
    const after = setup({ restartOf: before });
    after.idp.fail('discovery', 'network');

    await after.auth.restore();

    expect(after.auth.getState()).toEqual({ status: 'signed_in', session: { subject: LEAD, personaCode: null, profile: null } });
    after.idp.fail('discovery', 'network');
    await expect(after.auth.getAccessToken()).rejects.toMatchObject({ kind: 'network' });

    expect(await after.auth.getAccessToken()).toBe(after.idp.issued.at(-3));
    expect(after.auth.getState()).toMatchObject({ status: 'signed_in' });
  });

  it('does not hold start-up for a slow network', async () => {
    const before = await signedIn();
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const slowFetch: FetchLike = async (input, init) => {
      await gate;
      return before.idp.fetchImpl(input, init);
    };
    const after = setup({ restartOf: before, fetchImpl: slowFetch, restoreWaitMs: 20 });

    await after.auth.restore();
    expect(after.auth.getState()).toMatchObject({ status: 'signed_in' });

    release();
    expect(await after.auth.getAccessToken()).toBe(after.idp.issued.at(-3));
  });

  it('clears a refresh token stored without its subject, and the reverse', async () => {
    const world = setup();
    await world.recording.credentials.write('refresh_token', 'orphan');

    await world.auth.restore();

    expect(world.auth.getState()).toEqual({ status: 'signed_out', reason: 'none' });
    await expect(keystoreEntries(world)).resolves.toEqual({ refreshToken: null, subject: null });
    expect(world.idp.requests).toEqual([]);
  });

  it('is signed out without deleting anything when the keystore cannot be read', async () => {
    const clear = jest.fn(async () => undefined);
    const unreadable: CredentialStore = {
      read: async () => {
        throw new Error('User interaction is not allowed.');
      },
      write: async () => undefined,
      clear,
    };
    const { auth } = setup({ credentials: unreadable });

    await auth.restore();

    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'none' });
    expect(clear).not.toHaveBeenCalled();
  });
});

describe('OpenID Connect sign-out', () => {
  it('records the sign-out, revokes the refresh token, clears the keystore and signs out', async () => {
    const world = await signedIn();
    const { auth, idp } = world;
    const [accessToken, refreshToken] = idp.issued.slice(1);

    await auth.signOut();

    const signOut = idp.requestsTo('sign-outs');
    expect(signOut).toHaveLength(1);
    expect(signOut[0]).toMatchObject({
      method: 'POST',
      body: '{"client":"mobile"}',
      headers: { Authorization: `Bearer ${accessToken}` },
    });
    const revocation = idp.requestsTo('revoke');
    expect(revocation).toHaveLength(1);
    expect(Object.fromEntries(new URLSearchParams(revocation[0]?.body ?? ''))).toEqual({
      token: refreshToken,
      token_type_hint: 'refresh_token',
      client_id: TEST_CLIENT_ID,
    });
    const order = idp.requests.map((request) => request.url);
    expect(order.indexOf(`${TEST_API}/api/v1/me/sign-outs`)).toBeLessThan(order.indexOf(`${TEST_ISSUER}/revoke`));
    expect(idp.revoked).toEqual([refreshToken]);
    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'signed_out' });
    await expect(auth.getAccessToken()).resolves.toBeNull();
    await expect(keystoreEntries(world)).resolves.toEqual({ refreshToken: null, subject: null });
  });

  it('still signs out and clears the keystore when the records and revocation cannot be sent', async () => {
    const world = await signedIn();
    world.idp.fail('sign-outs', 'network');
    world.idp.fail('revoke', 'network');

    await world.auth.signOut();

    expect(world.auth.getState()).toEqual({ status: 'signed_out', reason: 'signed_out' });
    await expect(keystoreEntries(world)).resolves.toEqual({ refreshToken: null, subject: null });
  });

  it('skips revocation when the provider has no revocation endpoint', async () => {
    const world = setup();
    world.idp.patchDiscovery({ revocation_endpoint: undefined });
    await world.auth.restore();
    await world.auth.signIn({ method: 'oidc' });

    await world.auth.signOut();

    expect(world.idp.requestsTo('revoke')).toHaveLength(0);
    expect(world.auth.getState()).toEqual({ status: 'signed_out', reason: 'signed_out' });
  });

  it('ends the UAE PASS session in the browser after revoking, when the sign-in came from UAE PASS', async () => {
    const world = setup({ idpHint: 'uae_pass' });
    world.idp.setProfileClaims({ given_name: 'Mariam', azwah_profile: { source: 'uae_pass_staging' } });
    await world.auth.restore();
    await world.auth.signIn({ method: 'oidc', identityProvider: 'uae_pass' });

    await world.auth.signOut();

    const [endSession] = world.idp.endSessions;
    expect(world.idp.opened).toHaveLength(2);
    const query = new URLSearchParams(endSession?.split('?')[1] ?? '');
    expect(query.get('client_id')).toBe(TEST_CLIENT_ID);
    expect(query.get('post_logout_redirect_uri')).toBe(TEST_SIGNED_OUT_URI);
    expect(query.get('idp_hint')).toBe('uae_pass');
    expect(query.get('state')).toMatch(/^[A-Za-z0-9_-]{16,}$/);
    // Revocation first, then the browser: the tokens are already dead while UAE PASS signs out.
    const order = world.idp.requests.map((request) => request.url);
    expect(order.indexOf(`${TEST_ISSUER}/revoke`)).toBeGreaterThanOrEqual(0);
    expect(world.auth.getState()).toEqual({ status: 'signed_out', reason: 'signed_out' });
    await expect(keystoreEntries(world)).resolves.toEqual({ refreshToken: null, subject: null });
  });

  it('sends the UAE PASS hint at sign-out even when the build sets none, because the session came from UAE PASS', async () => {
    const world = setup({ idpHint: null });
    world.idp.setProfileClaims({ azwah_profile: { source: 'uae_pass_staging' } });
    await world.auth.restore();
    await world.auth.signIn({ method: 'oidc' });

    await world.auth.signOut();

    const [endSession] = world.idp.endSessions;
    expect(new URLSearchParams(endSession?.split('?')[1] ?? '').get('idp_hint')).toBe('uae_pass');
  });

  it('clears the keystore and is signed out before the end-session browser opens', async () => {
    const world = setup({ idpHint: 'uae_pass' });
    world.idp.setProfileClaims({ azwah_profile: { source: 'uae_pass_staging' } });
    await world.auth.restore();
    await world.auth.signIn({ method: 'oidc', identityProvider: 'uae_pass' });
    let statusWhenBrowserOpened: string | null = null;
    let keystoreWhenBrowserOpened: Promise<string | null> | null = null;
    world.idp.answerEndSession(() => {
      statusWhenBrowserOpened = world.auth.getState().status;
      keystoreWhenBrowserOpened = world.recording.credentials.read('refresh_token');
      return { type: 'cancel' };
    });

    await world.auth.signOut();

    expect(statusWhenBrowserOpened).toBe('signed_out');
    await expect(keystoreWhenBrowserOpened).resolves.toBeNull();
  });

  it('does not open the browser at sign-out for a persona sign-in', async () => {
    const { auth, idp } = await signedIn();

    await auth.signOut();

    expect(idp.opened).toHaveLength(1);
    expect(idp.endSessions).toHaveLength(0);
  });

  it('does not end a UAE PASS session for a test-account sign-in, though the build has the UAE PASS hint', async () => {
    const { auth, idp } = await signedIn({ idpHint: 'uae_pass' });

    await auth.signOut();

    expect(idp.endSessions).toHaveLength(0);
  });

  it('ends the UAE PASS session for a UAE PASS sign-in that released no profile', async () => {
    const world = setup({ idpHint: 'uae_pass' });
    await world.auth.restore();
    await world.auth.signIn({ method: 'oidc', identityProvider: 'uae_pass' });

    await world.auth.signOut();

    expect(world.idp.endSessions).toHaveLength(1);
  });

  it.each([
    ['with its profile', { given_name: 'Test', azwah_profile: { source: 'google' } }],
    ['without a profile', {}],
  ])('does not end a UAE PASS session for a Google sign-in %s (D-147)', async (_name, claims) => {
    const world = setup({ idpHint: 'uae_pass' });
    world.idp.setProfileClaims(claims);
    await world.auth.restore();
    await world.auth.signIn({ method: 'oidc', identityProvider: 'google' });

    await world.auth.signOut();

    expect(world.idp.endSessions).toHaveLength(0);
    expect(world.auth.getState()).toEqual({ status: 'signed_out', reason: 'signed_out' });
  });

  it.each(['cancel', 'throw'] as const)('still signs out when the end-session browser answers %s', async (outcome) => {
    const world = setup({ idpHint: 'uae_pass' });
    world.idp.setProfileClaims({ azwah_profile: { source: 'uae_pass_staging' } });
    await world.auth.restore();
    await world.auth.signIn({ method: 'oidc', identityProvider: 'uae_pass' });
    world.idp.answerEndSession(() => {
      if (outcome === 'throw') throw new Error('browser unavailable');
      return { type: 'cancel' };
    });

    await world.auth.signOut();

    expect(world.idp.endSessions).toHaveLength(1);
    expect(world.auth.getState()).toEqual({ status: 'signed_out', reason: 'signed_out' });
    await expect(keystoreEntries(world)).resolves.toEqual({ refreshToken: null, subject: null });
  });

  it('skips end-session when the provider has none', async () => {
    const world = setup({ idpHint: 'uae_pass' });
    world.idp.patchDiscovery({ end_session_endpoint: undefined });
    world.idp.setProfileClaims({ azwah_profile: { source: 'uae_pass_staging' } });
    await world.auth.restore();
    await world.auth.signIn({ method: 'oidc', identityProvider: 'uae_pass' });

    await world.auth.signOut();

    expect(world.idp.endSessions).toHaveLength(0);
    expect(world.auth.getState()).toEqual({ status: 'signed_out', reason: 'signed_out' });
  });

  it('records the sign-out with a renewed token when the current one has expired', async () => {
    const { auth, idp, clock } = await signedIn();
    clock.now += minutes(30);

    await auth.signOut();

    expect(tokenGrants(idp)).toEqual(['authorization_code', 'refresh_token']);
    const renewed = idp.issued.at(-3);
    expect(idp.requestsTo('sign-outs')[0]?.headers.Authorization).toBe(`Bearer ${renewed}`);
    // The rotated refresh token is the one revoked.
    expect(idp.revoked).toEqual([idp.issued.at(-2)]);
  });
});

describe('OpenID Connect profile (UAE PASS details)', () => {
  const PROFILE_CLAIMS = {
    name: 'Mariam Ahmed Al Mansoori',
    given_name: 'Mariam',
    azwah_profile: { source: 'uae_pass_staging', given_name_ar: 'مريم', emirates_id_masked: '784-••••-•••••42-1' },
  };

  it('asks the issuer for UAE PASS with the hint and the language, only when asked to', async () => {
    const clock = { now: TEST_NOW };
    const idp = createFakeIdentityProvider({ clock: () => clock.now });
    const auth = createOidcAuth({
      config: testConfig({ idpHint: 'uae_pass' }),
      browser: idp.browser,
      fetchImpl: idp.fetchImpl,
      random: nodeRandom,
      sha256: nodeSha256,
      credentials: createRecordingCredentials().credentials,
      clock: () => clock.now,
    });
    await auth.restore();

    await auth.signIn({ method: 'oidc', identityProvider: 'uae_pass', language: 'ar' });
    await auth.signOut();
    await auth.signIn({ method: 'oidc' });

    // The sign-out in between opened the end-session page (D-129); only the authorization requests matter here.
    const [withHint, without] = idp.opened
      .filter((url) => !url.startsWith(`${TEST_ISSUER}/end-session`))
      .map((url) => new URLSearchParams(url.split('?')[1] ?? ''));
    expect(withHint?.get('idp_hint')).toBe('uae_pass');
    expect(withHint?.get('ui_locales')).toBe('ar');
    expect(without?.has('idp_hint')).toBe(false);
    expect(without?.has('ui_locales')).toBe(false);
  });

  it('keeps the released details with the session, in memory only', async () => {
    const world = setup();
    world.idp.setProfileClaims(PROFILE_CLAIMS);
    await world.auth.restore();

    await world.auth.signIn({ method: 'oidc' });

    expect(world.auth.getState()).toMatchObject({
      status: 'signed_in',
      session: { subject: LEAD, profile: { nameEn: 'Mariam Ahmed Al Mansoori', givenNameAr: 'مريم' } },
    });
    expect(JSON.stringify(world.recording.writes)).not.toContain('Mariam');
    expect(JSON.stringify(world.recording.writes)).not.toContain('784');
  });

  it('brings the details back with the first renewal after a restart', async () => {
    const before = setup();
    before.idp.setProfileClaims(PROFILE_CLAIMS);
    await before.auth.restore();
    await before.auth.signIn({ method: 'oidc' });

    const after = setup({ restartOf: before });
    await after.auth.restore();

    expect(after.auth.getState()).toMatchObject({
      status: 'signed_in',
      session: { subject: LEAD, profile: { givenNameEn: 'Mariam' } },
    });
  });

  it('has no profile when the provider releases none', async () => {
    const { auth } = await signedIn();

    expect(auth.getState()).toMatchObject({ status: 'signed_in', session: { profile: null } });
  });
});
