import type { FetchLike } from '@/api/client';
import { createMemorySecretStore } from '@/storage/testing/memorySecretStore';
import { utf8Encode } from '@/storage/protection/utf8';

import type { CredentialName, CredentialStore } from '../../types';
import type { AuthBrowser, AuthBrowserResult } from '../oidcAuth';
import type { OidcConfig } from '../oidcConfig';
import { base64UrlEncode, s256Challenge } from '../pkce';
import { createSecureCredentialStore } from '../secureCredentialStore';
import { nodeSha256 } from './nodeCrypto';

export const TEST_ISSUER = 'https://id.example.test';
export const TEST_CLIENT_ID = 'azwah-mobile';
export const TEST_REDIRECT_URI = 'azwah://auth/callback';
export const TEST_SIGNED_OUT_URI = 'azwah://auth/signed-out';
export const TEST_API = 'https://api.example.test';
/** 2027-01-15T08:00:00Z, in milliseconds. */
export const TEST_NOW = 1_800_000_000_000;

export const testConfig = (overrides: Partial<OidcConfig> = {}): OidcConfig => ({
  issuer: TEST_ISSUER,
  clientId: TEST_CLIENT_ID,
  scope: 'openid offline_access',
  resource: null,
  prompt: 'login',
  idpHint: null,
  uaePassOnly: false,
  isDevelopmentBuild: false,
  demonstrationBuild: false,
  ...overrides,
});

/** A JWT whose signature is a placeholder: the app checks ID token claims, not signatures (OIDC Core 3.1.3.7). */
export function testJwt(
  claims: Record<string, unknown>,
  header: Record<string, unknown> = { alg: 'RS256', typ: 'JWT', kid: 'test-key' },
): string {
  const encode = (value: unknown) => base64UrlEncode(utf8Encode(JSON.stringify(value)));
  return `${encode(header)}.${encode(claims)}.c2lnbmF0dXJl`;
}

/** A minimal fetch Response; the app reads only `status` and `json()`. */
export function testResponse(status: number, body?: unknown): Response {
  return {
    status,
    ok: status >= 200 && status < 300,
    json: async () => {
      if (body === undefined) {
        throw new SyntaxError('Unexpected end of JSON input');
      }
      return body;
    },
  } as unknown as Response;
}

export interface RecordedRequest {
  url: string;
  method: string;
  headers: Record<string, string>;
  body: string | null;
}

export type FakeEndpoint = 'discovery' | 'token' | 'revoke' | 'sign-ins' | 'sign-outs';
/** One programmed failure for the next request to an endpoint. */
export type Fault = 'network' | 'hang' | { status: number; body?: unknown };

type TokenBody = Record<string, unknown>;

export interface FakeIdentityProviderOptions {
  /** Shared with the provider under test, so tokens expire in the same time. */
  clock: () => number;
  issuer?: string;
  apiBaseUrl?: string;
  redirectUri?: string;
  subject?: string;
  developmentOnly?: boolean;
  accessTokenSeconds?: number;
}

/**
 * An identity provider and the platform API's sign-in records, in memory, behaving like the development identity
 * provider (spec 5.12): discovery, authorization code with PKCE S256 checked for real, rotating refresh tokens where
 * reusing a rotated one revokes the family, revocation, and the `/api/v1/me/sign-ins|sign-outs` records (204).
 */
export function createFakeIdentityProvider(options: FakeIdentityProviderOptions) {
  const issuer = options.issuer ?? TEST_ISSUER;
  const apiBaseUrl = options.apiBaseUrl ?? TEST_API;
  const redirectUri = options.redirectUri ?? TEST_REDIRECT_URI;
  const accessTokenSeconds = options.accessTokenSeconds ?? 600;
  let subject = options.subject ?? 'dev-participant-lead';

  const requests: RecordedRequest[] = [];
  /** Every secret it handed out (codes, access, refresh and ID tokens), to prove none is kept where it must not be. */
  const issued: string[] = [];
  const revoked: string[] = [];
  const faults = new Map<FakeEndpoint, Fault[]>();
  const codes = new Map<string, { challenge: string; nonce: string; redirectUri: string; used: boolean }>();
  let currentRefreshToken: string | null = null;
  let counter = 0;
  let browserAnswer: ((authorize: URLSearchParams, approve: () => string) => AuthBrowserResult) | null = null;
  let editTokens: ((body: TokenBody, grant: string) => TokenBody) | null = null;
  let profileClaims: Record<string, unknown> = {};
  let discovery: Record<string, unknown> = {
    issuer,
    authorization_endpoint: `${issuer}/authorize`,
    token_endpoint: `${issuer}/token`,
    revocation_endpoint: `${issuer}/revoke`,
    end_session_endpoint: `${issuer}/end-session`,
    response_types_supported: ['code'],
    code_challenge_methods_supported: ['S256'],
    authorization_response_iss_parameter_supported: true,
    ...(options.developmentOnly ? { azwah_development_only: true } : {}),
  };

  const secret = (prefix: string) => {
    counter += 1;
    const value = `${prefix}-${counter}-${'x'.repeat(8)}`;
    issued.push(value);
    return value;
  };

  const endpointOf = (url: string): FakeEndpoint | null => {
    switch (url) {
      case `${issuer.replace(/\/+$/, '')}/.well-known/openid-configuration`:
        return 'discovery';
      case `${issuer}/token`:
        return 'token';
      case `${issuer}/revoke`:
        return 'revoke';
      case `${apiBaseUrl}/api/v1/me/sign-ins`:
        return 'sign-ins';
      case `${apiBaseUrl}/api/v1/me/sign-outs`:
        return 'sign-outs';
      default:
        return null;
    }
  };

  function issueTokens(grant: string, nonce: string | null): Response {
    const nowSeconds = Math.floor(options.clock() / 1000);
    const accessToken = secret('access');
    const refreshToken = secret('refresh');
    currentRefreshToken = refreshToken;
    const idToken = testJwt({
      iss: issuer,
      sub: subject,
      aud: TEST_CLIENT_ID,
      azp: TEST_CLIENT_ID,
      exp: nowSeconds + 600,
      iat: nowSeconds,
      auth_time: nowSeconds,
      ...(nonce ? { nonce } : {}),
      ...profileClaims,
    });
    issued.push(idToken);
    let body: TokenBody = {
      token_type: 'Bearer',
      access_token: accessToken,
      expires_in: accessTokenSeconds,
      refresh_token: refreshToken,
      id_token: idToken,
      scope: 'openid offline_access',
    };
    if (editTokens) {
      body = editTokens(body, grant);
    }
    return testResponse(200, body);
  }

  async function token(form: URLSearchParams): Promise<Response> {
    if (form.get('client_id') !== TEST_CLIENT_ID) {
      return testResponse(401, { error: 'invalid_client' });
    }
    if (form.get('grant_type') === 'authorization_code') {
      const code = codes.get(form.get('code') ?? '');
      const challenge = await s256Challenge(form.get('code_verifier') ?? '', nodeSha256).catch(() => null);
      if (!code || code.used || code.redirectUri !== form.get('redirect_uri') || challenge !== code.challenge) {
        return testResponse(400, { error: 'invalid_grant' });
      }
      code.used = true;
      return issueTokens('authorization_code', code.nonce);
    }
    if (form.get('grant_type') === 'refresh_token') {
      const presented = form.get('refresh_token');
      if (!presented || presented !== currentRefreshToken) {
        // Reuse of a rotated (or unknown) token revokes the family.
        currentRefreshToken = null;
        return testResponse(400, { error: 'invalid_grant' });
      }
      return issueTokens('refresh_token', null);
    }
    return testResponse(400, { error: 'unsupported_grant_type' });
  }

  const fetchImpl: FetchLike = async (input, init) => {
    const body = typeof init?.body === 'string' ? init.body : null;
    requests.push({
      url: input,
      method: init?.method ?? 'GET',
      headers: { ...(init?.headers as Record<string, string> | undefined) },
      body,
    });
    const endpoint = endpointOf(input);
    const fault = endpoint ? faults.get(endpoint)?.shift() : undefined;
    if (fault === 'network') {
      throw new TypeError('Network request failed');
    }
    if (fault === 'hang') {
      return new Promise<Response>((_resolve, reject) => {
        init?.signal?.addEventListener('abort', () => reject(new Error('aborted')));
      });
    }
    if (fault) {
      return testResponse(fault.status, fault.body);
    }
    switch (endpoint) {
      case 'discovery':
        return testResponse(200, discovery);
      case 'token':
        return token(new URLSearchParams(body ?? ''));
      case 'revoke': {
        const revokedToken = new URLSearchParams(body ?? '').get('token');
        if (revokedToken) {
          revoked.push(revokedToken);
          if (revokedToken === currentRefreshToken) currentRefreshToken = null;
        }
        return testResponse(200);
      }
      case 'sign-ins':
      case 'sign-outs':
        return testResponse(204);
      default:
        return testResponse(404, { error: 'not_found' });
    }
  };

  function approve(authorize: URLSearchParams): string {
    const code = secret('code');
    codes.set(code, {
      challenge: authorize.get('code_challenge') ?? '',
      nonce: authorize.get('nonce') ?? '',
      redirectUri: authorize.get('redirect_uri') ?? '',
      used: false,
    });
    const answer = new URLSearchParams({ code, state: authorize.get('state') ?? '', iss: issuer });
    return `${authorize.get('redirect_uri') ?? ''}?${answer.toString()}`;
  }

  const opened: string[] = [];
  let endSessionAnswer: (() => AuthBrowserResult) | null = null;
  const browser: AuthBrowser = {
    redirectUri,
    signedOutUri: TEST_SIGNED_OUT_URI,
    async openAuthSession(url) {
      opened.push(url);
      const query = new URLSearchParams(url.split('?')[1] ?? '');
      if (url.startsWith(`${issuer}/end-session`)) {
        if (endSessionAnswer) return endSessionAnswer();
        const back = new URLSearchParams();
        const state = query.get('state');
        if (state) back.set('state', state);
        return { type: 'success', url: `${query.get('post_logout_redirect_uri') ?? ''}?${back.toString()}` };
      }
      return browserAnswer ? browserAnswer(query, () => approve(query)) : { type: 'success', url: approve(query) };
    },
  };

  return {
    issuer,
    apiBaseUrl,
    redirectUri,
    fetchImpl,
    browser,
    requests,
    issued,
    revoked,
    /** The authorization URLs the browser was asked to open. */
    opened,
    get currentRefreshToken() {
      return currentRefreshToken;
    },
    /** The next requests to `endpoint` fail in these ways, in order. */
    fail(endpoint: FakeEndpoint, ...next: Fault[]) {
      faults.set(endpoint, [...(faults.get(endpoint) ?? []), ...next]);
    },
    /** Decides what the browser returns; `approve()` signs in as the provider normally would. */
    answerBrowser(answer: (authorize: URLSearchParams, approve: () => string) => AuthBrowserResult) {
      browserAnswer = answer;
    },
    /** Decides what the browser returns from the end-session page (default: back to the post-logout URI with the state). */
    answerEndSession(answer: () => AuthBrowserResult) {
      endSessionAnswer = answer;
    },
    /** The end-session URLs the browser was asked to open. */
    get endSessions() {
      return opened.filter((url) => url.startsWith(`${issuer}/end-session`));
    },
    editTokenResponses(edit: (body: TokenBody, grant: string) => TokenBody) {
      editTokens = edit;
    },
    patchDiscovery(patch: Record<string, unknown>) {
      discovery = { ...discovery, ...patch };
    },
    setSubject(value: string) {
      subject = value;
    },
    /** Profile claims (identityProfile.ts) put in every ID token from now on, as the development provider does for UAE PASS. */
    setProfileClaims(claims: Record<string, unknown>) {
      profileClaims = claims;
    },
    requestsTo(endpoint: FakeEndpoint): RecordedRequest[] {
      return requests.filter((request) => endpointOf(request.url) === endpoint);
    },
  };
}

export type FakeIdentityProvider = ReturnType<typeof createFakeIdentityProvider>;

/** The real keystore-backed credential store on an in-memory keystore, recording every write. */
export function createRecordingCredentials() {
  const secrets = createMemorySecretStore();
  const inner = createSecureCredentialStore(secrets);
  const writes: [CredentialName, string][] = [];
  const credentials: CredentialStore = {
    read: (name) => inner.read(name),
    async write(name, value) {
      writes.push([name, value]);
      await inner.write(name, value);
    },
    clear: jest.fn(() => inner.clear()),
  };
  return { secrets, credentials, writes };
}
