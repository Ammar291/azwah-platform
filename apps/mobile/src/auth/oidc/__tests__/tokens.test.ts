import type { FetchLike } from '@/api/client';

import { SignInError } from '../../types';
import { TEST_CLIENT_ID, TEST_ISSUER, TEST_NOW, testJwt, testResponse } from '../testing/fakeIdentityProvider';
import {
  authorizationCodeBody,
  refreshTokenBody,
  requestTokens,
  TokenRequestError,
  validateIdToken,
  type IdTokenExpectations,
} from '../tokens';

const nowSeconds = TEST_NOW / 1000;
const claims = (patch: Record<string, unknown> = {}) => ({
  iss: TEST_ISSUER,
  sub: 'dev-participant-lead',
  aud: TEST_CLIENT_ID,
  azp: TEST_CLIENT_ID,
  exp: nowSeconds + 600,
  iat: nowSeconds,
  nonce: 'nonce-1',
  ...patch,
});
const expected: IdTokenExpectations = { issuer: TEST_ISSUER, clientId: TEST_CLIENT_ID, now: TEST_NOW, nonce: 'nonce-1' };

function idTokenKind(token: string, expectations: IdTokenExpectations = expected): string | null {
  try {
    validateIdToken(token, expectations);
    return null;
  } catch (error) {
    if (error instanceof SignInError) return error.kind;
    throw error;
  }
}

const tokenEndpoint = `${TEST_ISSUER}/token`;
const answering =
  (status: number, body?: unknown): FetchLike =>
  async () =>
    testResponse(status, body);

async function tokenFailure(fetchImpl: FetchLike, timeoutMs?: number): Promise<string> {
  const error = await requestTokens(tokenEndpoint, 'grant_type=x', fetchImpl, timeoutMs).then(
    () => null,
    (caught: unknown) => caught,
  );
  if (!(error instanceof TokenRequestError)) throw new Error('expected a TokenRequestError');
  return error.reason;
}

describe('token request bodies', () => {
  it('exchanges the code with the verifier, redirect URI and client, and no secret', () => {
    const body = authorizationCodeBody({
      code: 'code 1',
      codeVerifier: 'verifier-1',
      redirectUri: 'azwah://auth/callback',
      clientId: TEST_CLIENT_ID,
      resource: null,
    });

    expect(Object.fromEntries(new URLSearchParams(body))).toEqual({
      grant_type: 'authorization_code',
      code: 'code 1',
      code_verifier: 'verifier-1',
      redirect_uri: 'azwah://auth/callback',
      client_id: TEST_CLIENT_ID,
    });
    expect(body).toContain('redirect_uri=azwah%3A%2F%2Fauth%2Fcallback');
    expect(body).not.toContain('secret');
  });

  it('refreshes with the refresh token and the client, adding the resource when configured', () => {
    expect(
      Object.fromEntries(
        new URLSearchParams(refreshTokenBody({ refreshToken: 'refresh-1', clientId: TEST_CLIENT_ID, resource: 'https://api.example.test' })),
      ),
    ).toEqual({
      grant_type: 'refresh_token',
      refresh_token: 'refresh-1',
      client_id: TEST_CLIENT_ID,
      resource: 'https://api.example.test',
    });
  });
});

describe('requestTokens', () => {
  it('posts a form and reads a Bearer token response, in any letter case', async () => {
    const fetchImpl = jest.fn<ReturnType<FetchLike>, Parameters<FetchLike>>(
      answering(200, { token_type: 'bearer', access_token: 'a', expires_in: 600, refresh_token: 'r', id_token: 'i' }),
    );

    await expect(requestTokens(tokenEndpoint, 'grant_type=x', fetchImpl)).resolves.toEqual({
      accessToken: 'a',
      expiresIn: 600,
      refreshToken: 'r',
      idToken: 'i',
    });
    const [url, init] = fetchImpl.mock.calls[0] ?? [];
    expect(url).toBe(tokenEndpoint);
    expect(init).toMatchObject({
      method: 'POST',
      body: 'grant_type=x',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded', Accept: 'application/json' },
    });
  });

  it('accepts expires_in as a numeric string and no refresh or ID token', async () => {
    await expect(
      requestTokens(tokenEndpoint, '', answering(200, { token_type: 'Bearer', access_token: 'a', expires_in: '300' })),
    ).resolves.toEqual({ accessToken: 'a', expiresIn: 300, refreshToken: null, idToken: null });
  });

  it.each([
    ['another token type', { token_type: 'DPoP', access_token: 'a', expires_in: 600 }],
    ['no access token', { token_type: 'Bearer', expires_in: 600 }],
    ['no lifetime', { token_type: 'Bearer', access_token: 'a' }],
    ['a lifetime of zero', { token_type: 'Bearer', access_token: 'a', expires_in: 0 }],
    ['an empty refresh token', { token_type: 'Bearer', access_token: 'a', expires_in: 600, refresh_token: '' }],
  ])('refuses a response with %s', async (_name, body) => {
    await expect(tokenFailure(answering(200, body))).resolves.toBe('invalid_response');
  });

  it('tells invalid_grant from other refusals, faults and silence', async () => {
    await expect(tokenFailure(answering(400, { error: 'invalid_grant' }))).resolves.toBe('invalid_grant');
    await expect(tokenFailure(answering(401, { error: 'invalid_client' }))).resolves.toBe('rejected');
    await expect(tokenFailure(answering(400, 'not json'))).resolves.toBe('rejected');
    await expect(tokenFailure(answering(503, {}))).resolves.toBe('server');
    await expect(tokenFailure(answering(429, {}))).resolves.toBe('server');
    await expect(tokenFailure(answering(200, undefined))).resolves.toBe('invalid_response');
    await expect(
      tokenFailure(async () => {
        throw new TypeError('Network request failed');
      }),
    ).resolves.toBe('network');
    await expect(
      tokenFailure(
        (_input, init) =>
          new Promise((_resolve, reject) => init?.signal?.addEventListener('abort', () => reject(new Error('aborted')))),
        20,
      ),
    ).resolves.toBe('network');
  });
});

describe('validateIdToken', () => {
  it('returns the subject of a token that passes every check', () => {
    expect(validateIdToken(testJwt(claims()), expected)).toEqual({ subject: 'dev-participant-lead', profile: null });
  });

  it('accepts an audience list that names the client when azp is the client', () => {
    expect(idTokenKind(testJwt(claims({ aud: [TEST_CLIENT_ID, 'azwah-api'] })))).toBeNull();
  });

  it.each([
    ['another issuer', claims({ iss: 'https://evil.example.test' })],
    ['another audience', claims({ aud: 'someone-else', azp: undefined })],
    ['several audiences without azp', claims({ aud: [TEST_CLIENT_ID, 'azwah-api'], azp: undefined })],
    ['azp of another client', claims({ azp: 'someone-else' })],
    ['another nonce', claims({ nonce: 'nonce-2' })],
    ['no nonce', claims({ nonce: undefined })],
    ['an expiry more than 60 s ago', claims({ exp: nowSeconds - 61 })],
    ['an issue time more than 60 s ahead', claims({ iat: nowSeconds + 61 })],
    ['no subject', claims({ sub: undefined })],
    ['an empty subject', claims({ sub: '' })],
  ])('refuses a token with %s', (_name, payload) => {
    expect(idTokenKind(testJwt(payload))).toBe('unsafe_response');
  });

  it('allows 60 s of clock skew', () => {
    expect(idTokenKind(testJwt(claims({ exp: nowSeconds - 59, iat: nowSeconds + 59 })))).toBeNull();
  });

  it('refuses alg none and tokens that are not JWTs', () => {
    expect(idTokenKind(testJwt(claims(), { alg: 'none' }))).toBe('unsafe_response');
    expect(idTokenKind('not-a-jwt')).toBe('unsafe_response');
    expect(idTokenKind('a.b.c')).toBe('unsafe_response');
    expect(idTokenKind(`${testJwt(claims()).split('.').slice(0, 2).join('.')}.`)).toBe('unsafe_response');
  });

  it('on refresh, needs no nonce but the same subject', () => {
    const refresh = { ...expected, nonce: null, subject: 'dev-participant-lead' };

    expect(idTokenKind(testJwt(claims({ nonce: undefined })), refresh)).toBeNull();
    expect(idTokenKind(testJwt(claims({ nonce: undefined, sub: 'dev-participant-new' })), refresh)).toBe('unsafe_response');
  });
});
