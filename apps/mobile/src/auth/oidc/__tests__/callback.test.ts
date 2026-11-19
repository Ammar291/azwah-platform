import { SignInError } from '../../types';
import {
  buildAuthorizationUrl,
  buildEndSessionUrl,
  readAuthorizationCallback,
  readSignedOutCallback,
  type CallbackExpectations,
} from '../callback';
import { TEST_ISSUER, TEST_REDIRECT_URI, TEST_SIGNED_OUT_URI } from '../testing/fakeIdentityProvider';

const expected: CallbackExpectations = {
  redirectUri: TEST_REDIRECT_URI,
  state: 'state-123',
  issuer: TEST_ISSUER,
  issRequired: true,
};

const callback = (params: Record<string, string>, base = TEST_REDIRECT_URI) =>
  `${base}?${new URLSearchParams(params).toString()}`;

function kindOf(run: () => unknown): string {
  try {
    run();
  } catch (error) {
    if (error instanceof SignInError) return error.kind;
    throw error;
  }
  throw new Error('expected a SignInError');
}

describe('buildAuthorizationUrl', () => {
  it('asks for the code flow with PKCE S256, state, nonce and prompt', () => {
    const url = buildAuthorizationUrl(`${TEST_ISSUER}/authorize`, {
      clientId: 'azwah-mobile',
      redirectUri: TEST_REDIRECT_URI,
      scope: 'openid offline_access',
      state: 'state-123',
      nonce: 'nonce-456',
      codeChallenge: 'challenge-789',
      prompt: 'login',
      resource: null,
    });

    const [base, query] = url.split('?');
    expect(base).toBe(`${TEST_ISSUER}/authorize`);
    expect(Object.fromEntries(new URLSearchParams(query))).toEqual({
      response_type: 'code',
      client_id: 'azwah-mobile',
      redirect_uri: TEST_REDIRECT_URI,
      scope: 'openid offline_access',
      state: 'state-123',
      nonce: 'nonce-456',
      code_challenge: 'challenge-789',
      code_challenge_method: 'S256',
      prompt: 'login',
    });
    expect(query).toContain('scope=openid%20offline_access');
  });

  it('adds the resource indicator and keeps an endpoint query', () => {
    const url = buildAuthorizationUrl(`${TEST_ISSUER}/authorize?tenant=a`, {
      clientId: 'c',
      redirectUri: TEST_REDIRECT_URI,
      scope: 'openid',
      state: 's',
      nonce: 'n',
      codeChallenge: 'x',
      prompt: 'login',
      resource: 'https://api.example.test',
    });

    expect(url.startsWith(`${TEST_ISSUER}/authorize?tenant=a&`)).toBe(true);
    expect(new URLSearchParams(url.split('?')[1]).get('resource')).toBe('https://api.example.test');
  });
});

describe('readAuthorizationCallback', () => {
  it('returns the code when state and iss match', () => {
    expect(readAuthorizationCallback(callback({ code: 'code-1', state: 'state-123', iss: TEST_ISSUER }), expected)).toBe(
      'code-1',
    );
  });

  it('accepts a callback without iss when the provider did not promise one', () => {
    expect(
      readAuthorizationCallback(callback({ code: 'code-1', state: 'state-123' }), { ...expected, issRequired: false }),
    ).toBe('code-1');
  });

  it('refuses another state, a missing state and a repeated one', () => {
    expect(kindOf(() => readAuthorizationCallback(callback({ code: 'c', state: 'other', iss: TEST_ISSUER }), expected))).toBe(
      'unsafe_response',
    );
    expect(kindOf(() => readAuthorizationCallback(callback({ code: 'c', iss: TEST_ISSUER }), expected))).toBe('unsafe_response');
    expect(
      kindOf(() =>
        readAuthorizationCallback(`${TEST_REDIRECT_URI}?code=c&state=state-123&state=state-123&iss=${encodeURIComponent(TEST_ISSUER)}`, expected),
      ),
    ).toBe('unsafe_response');
  });

  it('refuses another issuer (RFC 9207 mix-up) and a missing iss the provider promised', () => {
    expect(
      kindOf(() => readAuthorizationCallback(callback({ code: 'c', state: 'state-123', iss: 'https://evil.example.test' }), expected)),
    ).toBe('unsafe_response');
    expect(kindOf(() => readAuthorizationCallback(callback({ code: 'c', state: 'state-123' }), expected))).toBe('unsafe_response');
  });

  it('refuses a foreign redirect, including one that only starts like ours', () => {
    expect(
      kindOf(() => readAuthorizationCallback(callback({ code: 'c', state: 'state-123', iss: TEST_ISSUER }, 'https://evil.example.test/cb'), expected)),
    ).toBe('unsafe_response');
    expect(
      kindOf(() => readAuthorizationCallback(callback({ code: 'c', state: 'state-123', iss: TEST_ISSUER }, `${TEST_REDIRECT_URI}-evil`), expected)),
    ).toBe('unsafe_response');
  });

  it('refuses a success without a code', () => {
    expect(kindOf(() => readAuthorizationCallback(callback({ state: 'state-123', iss: TEST_ISSUER }), expected))).toBe(
      'unsafe_response',
    );
  });

  it.each([
    ['access_denied', 'cancelled'],
    ['temporarily_unavailable', 'provider_unreachable'],
    ['server_error', 'provider_unreachable'],
    ['invalid_request', 'refused'],
    ['login_required', 'refused'],
  ])('turns the error %s into %s', (error, kind) => {
    expect(
      kindOf(() =>
        readAuthorizationCallback(
          callback({ error, error_description: '<b>Provider text</b>', state: 'state-123', iss: TEST_ISSUER }),
          expected,
        ),
      ),
    ).toBe(kind);
  });

  it('refuses an error answer for another request', () => {
    expect(
      kindOf(() => readAuthorizationCallback(callback({ error: 'access_denied', state: 'other', iss: TEST_ISSUER }), expected)),
    ).toBe('unsafe_response');
  });

  it('keeps no provider text in the error', () => {
    try {
      readAuthorizationCallback(
        callback({ error: 'invalid_request', error_description: 'secret detail', state: 'state-123', iss: TEST_ISSUER }),
        expected,
      );
    } catch (error) {
      expect(String(error)).not.toContain('secret detail');
    }
  });
});

describe('buildEndSessionUrl', () => {
  it('names the client, the post-logout return, the state and the UAE PASS hint', () => {
    const url = buildEndSessionUrl(`${TEST_ISSUER}/end-session`, {
      clientId: 'azwah-mobile',
      postLogoutRedirectUri: TEST_SIGNED_OUT_URI,
      state: 'bye-1',
      idpHint: 'uae_pass',
    });

    const [base, query] = url.split('?');
    expect(base).toBe(`${TEST_ISSUER}/end-session`);
    expect(Object.fromEntries(new URLSearchParams(query))).toEqual({
      client_id: 'azwah-mobile',
      post_logout_redirect_uri: TEST_SIGNED_OUT_URI,
      state: 'bye-1',
      idp_hint: 'uae_pass',
    });
  });

  it('leaves the hint out when the build has none', () => {
    const url = buildEndSessionUrl(`${TEST_ISSUER}/end-session`, {
      clientId: 'azwah-mobile',
      postLogoutRedirectUri: TEST_SIGNED_OUT_URI,
      state: 'bye-2',
      idpHint: null,
    });

    expect(new URLSearchParams(url.split('?')[1]).has('idp_hint')).toBe(false);
  });
});

describe('readSignedOutCallback', () => {
  it('is true only for the exact signed-out address with the same state', () => {
    const expected = { redirectUri: TEST_SIGNED_OUT_URI, state: 'bye-1' };

    expect(readSignedOutCallback(`${TEST_SIGNED_OUT_URI}?state=bye-1`, expected)).toBe(true);
    expect(readSignedOutCallback(`${TEST_SIGNED_OUT_URI}?state=other`, expected)).toBe(false);
    expect(readSignedOutCallback(TEST_SIGNED_OUT_URI, expected)).toBe(false);
    expect(readSignedOutCallback(`${TEST_SIGNED_OUT_URI}-evil?state=bye-1`, expected)).toBe(false);
    expect(readSignedOutCallback(`${TEST_REDIRECT_URI}?state=bye-1`, expected)).toBe(false);
  });
});
