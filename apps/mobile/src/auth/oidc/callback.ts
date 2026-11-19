import { SignInError } from '../types';
import { formEncode } from './tokens';

export interface AuthorizationRequest {
  clientId: string;
  redirectUri: string;
  scope: string;
  state: string;
  nonce: string;
  codeChallenge: string;
  prompt: string;
  resource: string | null;
  /** `idp_hint`: which upstream sign-in the issuer should go straight to. */
  idpHint?: string | null;
  /** `ui_locales` (OIDC Core 3.1.2.1). */
  uiLocales?: string | null;
}

/** The authorization code request (RFC 6749 4.1.1, RFC 7636 4.3, OIDC Core 3.1.2.1). */
export function buildAuthorizationUrl(endpoint: string, request: AuthorizationRequest): string {
  const query = formEncode({
    response_type: 'code',
    client_id: request.clientId,
    redirect_uri: request.redirectUri,
    scope: request.scope,
    state: request.state,
    nonce: request.nonce,
    code_challenge: request.codeChallenge,
    code_challenge_method: 'S256',
    prompt: request.prompt,
    resource: request.resource,
    idp_hint: request.idpHint,
    ui_locales: request.uiLocales,
  });
  return `${endpoint}${endpoint.includes('?') ? '&' : '?'}${query}`;
}

export interface CallbackExpectations {
  redirectUri: string;
  state: string;
  issuer: string;
  /** Discovery said `authorization_response_iss_parameter_supported` (RFC 9207). */
  issRequired: boolean;
}

/** OAuth error codes that mean the provider is having trouble, not that it refused. */
const PROVIDER_TROUBLE = new Set(['server_error', 'temporarily_unavailable']);

/**
 * Reads the URL the browser came back to and returns the authorization code. Refused (`unsafe_response`): a URL that
 * is not the redirect URI, a repeated parameter, an `iss` other than the issuer (or none when the provider promised
 * one), a `state` other than the one sent. An error answer becomes `cancelled` (`access_denied`), `provider_unreachable`
 * or `refused`. Nothing in the URL is kept beyond the code.
 */
export function readAuthorizationCallback(url: string, expected: CallbackExpectations): string {
  if (!url.startsWith(expected.redirectUri)) {
    throw new SignInError('unsafe_response');
  }
  const rest = url.slice(expected.redirectUri.length);
  if (rest !== '' && !rest.startsWith('?') && !rest.startsWith('#')) {
    throw new SignInError('unsafe_response');
  }
  const query = rest.startsWith('?') ? (rest.slice(1).split('#')[0] ?? '') : '';
  const params = new URLSearchParams(query);
  const single = (name: string): string | null => {
    const values = params.getAll(name);
    if (values.length > 1) {
      throw new SignInError('unsafe_response');
    }
    return values[0] ?? null;
  };
  const iss = single('iss');
  const state = single('state');
  const error = single('error');
  const code = single('code');

  if (iss === null ? expected.issRequired : iss !== expected.issuer) {
    throw new SignInError('unsafe_response');
  }
  if (state !== expected.state) {
    throw new SignInError('unsafe_response');
  }
  if (error !== null) {
    throw new SignInError(
      error === 'access_denied' ? 'cancelled' : PROVIDER_TROUBLE.has(error) ? 'provider_unreachable' : 'refused',
    );
  }
  if (!code) {
    throw new SignInError('unsafe_response');
  }
  return code;
}

export interface EndSessionRequest {
  clientId: string;
  /** Where the provider sends the browser back afterwards: the app's `auth/signed-out` link. */
  postLogoutRedirectUri: string;
  state: string;
  /** `idp_hint`: asks the issuer to end the upstream (UAE PASS) session too (D-129). */
  idpHint: string | null;
}

/** The RP-initiated logout request (OIDC RP-Initiated Logout 1.0 section 2). */
export function buildEndSessionUrl(endpoint: string, request: EndSessionRequest): string {
  const query = formEncode({
    client_id: request.clientId,
    post_logout_redirect_uri: request.postLogoutRedirectUri,
    state: request.state,
    idp_hint: request.idpHint,
  });
  return `${endpoint}${endpoint.includes('?') ? '&' : '?'}${query}`;
}

/**
 * True when the browser came back to the exact signed-out address with the state that was sent. Nothing else in the
 * URL is read; a mismatch only means the sign-out is not confirmed, which changes nothing locally.
 */
export function readSignedOutCallback(url: string, expected: { redirectUri: string; state: string }): boolean {
  if (!url.startsWith(expected.redirectUri)) {
    return false;
  }
  const rest = url.slice(expected.redirectUri.length);
  if (!rest.startsWith('?')) {
    return false;
  }
  const params = new URLSearchParams(rest.slice(1).split('#')[0] ?? '');
  const states = params.getAll('state');
  return states.length === 1 && states[0] === expected.state;
}
