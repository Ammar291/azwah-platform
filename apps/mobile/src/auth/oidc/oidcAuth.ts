import { ApiError, fetchWithTimeout, joinUrl, type ApiErrorKind, type FetchLike } from '@/api/client';

import type { IdentityProfile } from '../identityProfile';
import { createAuthStateStore } from '../observable';
import {
  isSignInError,
  SignInError,
  type CredentialStore,
  type OidcAuthProvider,
  type SignInErrorKind,
  type SignInRequest,
} from '../types';
import { buildAuthorizationUrl, buildEndSessionUrl, readAuthorizationCallback, readSignedOutCallback } from './callback';
import { discoverProvider, GOOGLE_HINT, type ProviderMetadata } from './discovery';
import { acceptsDevelopmentIssuer, type OidcConfig } from './oidcConfig';
import { base64UrlEncode, createAuthorizationSecrets, type RandomSource, type Sha256 } from './pkce';
import {
  authorizationCodeBody,
  formEncode,
  refreshTokenBody,
  requestTokens,
  TokenRequestError,
  validateIdToken,
  type TokenFailure,
  type TokenSet,
} from './tokens';

export type AuthBrowserResult = { type: 'success'; url: string } | { type: 'cancel' };

/** The system browser (ASWebAuthenticationSession on iOS, Custom Tabs on Android), through expo-web-browser. */
export interface AuthBrowser {
  /** Where the provider sends the browser back: `azwah://auth/callback`, or `exp://…/--/auth/callback` in Expo Go. */
  readonly redirectUri: string;
  /** Where the provider sends the browser back after end-session: `azwah://auth/signed-out` (or the Expo Go address). */
  readonly signedOutUri: string;
  /**
   * Opens the URL and resolves with the URL the browser came back to (at `returnUri`, the sign-in redirect URI by
   * default), or `cancel`.
   */
  openAuthSession(url: string, returnUri?: string): Promise<AuthBrowserResult>;
}

export interface OidcAuthDependencies {
  config: OidcConfig;
  browser: AuthBrowser;
  fetchImpl: FetchLike;
  random: RandomSource;
  sha256: Sha256;
  /** The keystore: the refresh token and its subject only. */
  credentials: CredentialStore;
  /** Milliseconds, as Date.now. */
  clock: () => number;
  /** The platform API, for the best-effort sign-in and sign-out records. */
  apiBaseUrl?: string;
  /** How long start-up waits for the first refresh before carrying on with the stored session. */
  restoreWaitMs?: number;
}

/** An access token is renewed when it has less than this left. */
const REFRESH_MARGIN_MS = 60_000;
const BEST_EFFORT_TIMEOUT_MS = 5_000;
const DEFAULT_RESTORE_WAIT_MS = 5_000;

/** Lives in memory only; the keystore holds just `refreshToken` and `subject`. */
interface Session {
  subject: string;
  /** From the latest ID token; after a restart it returns with the first renewal. */
  profile: IdentityProfile | null;
  refreshToken: string | null;
  accessToken: string | null;
  /** Clock time at which the access token expires. */
  expiresAt: number;
  /**
   * The sign-in asked for UAE PASS (the UAE PASS button, with the build's hint). A session restored at start-up does
   * not know how it began, and counts as one whenever the build has a hint, so a shared phone is never left signed in.
   */
  viaUaePass: boolean;
}

const signInKindForToken: Record<TokenFailure, SignInErrorKind> = {
  network: 'provider_unreachable',
  server: 'provider_unreachable',
  invalid_grant: 'refused',
  rejected: 'refused',
  invalid_response: 'unsafe_response',
};

const apiKindForToken: Record<Exclude<TokenFailure, 'invalid_grant' | 'rejected'>, ApiErrorKind> = {
  network: 'network',
  server: 'server',
  invalid_response: 'invalid_response',
};

/** Resolves when the promise settles or the time is up, whichever is first. Never rejects. */
function settledOrAfter(promise: Promise<unknown>, ms: number): Promise<void> {
  return new Promise((resolve) => {
    const timer = setTimeout(resolve, ms);
    const finish = () => {
      clearTimeout(timer);
      resolve();
    };
    promise.then(finish, finish);
  });
}

/**
 * OpenID Connect sign-in: authorization code with PKCE (S256), `state` and `nonce`, through the system browser.
 * The refresh token and the subject live in the keystore; access and ID tokens only in memory. Access tokens are
 * renewed 60 s before they expire, one renewal at a time. A refused renewal or a 401 from the API ends the session
 * as expired; a network failure does not, so the app keeps working offline. Nothing about tokens, codes or responses
 * is ever logged.
 */
export function createOidcAuth(deps: OidcAuthDependencies): OidcAuthProvider {
  const { config, browser, fetchImpl, random, sha256, credentials, clock, apiBaseUrl } = deps;
  const restoreWaitMs = deps.restoreWaitMs ?? DEFAULT_RESTORE_WAIT_MS;
  const store = createAuthStateStore({ status: 'initializing' });

  let session: Session | null = null;
  let metadata: Promise<ProviderMetadata> | null = null;
  let refreshing: { session: Session; promise: Promise<string | null> } | null = null;
  let signingIn: Promise<void> | null = null;
  // Keystore writes and deletes run one after another, so a late delete never removes a newer sign-in's entries.
  let keystoreQueue: Promise<void> = Promise.resolve();

  const keystore = (task: () => Promise<void>): Promise<void> => {
    keystoreQueue = keystoreQueue.then(task).catch(() => {
      // Without a working keystore the session lasts until the app closes.
    });
    return keystoreQueue;
  };

  const discover = (): Promise<ProviderMetadata> => {
    metadata ??= discoverProvider(config, fetchImpl).catch((error: unknown) => {
      metadata = null;
      throw error;
    });
    return metadata;
  };

  const signedIn = (current: Session) =>
    store.set({
      status: 'signed_in',
      session: { subject: current.subject, personaCode: null, profile: current.profile },
    });

  /** The session is over without asking the provider: a refused renewal, another subject, or a 401 (D-64). */
  const expire = () => {
    session = null;
    void keystore(() => credentials.clear());
    store.set({ status: 'signed_out', reason: 'session_expired' });
  };

  async function postBestEffort(url: string, headers: Record<string, string>, body: string): Promise<void> {
    try {
      const { done } = await fetchWithTimeout(url, { method: 'POST', headers, body }, BEST_EFFORT_TIMEOUT_MS, fetchImpl);
      done();
    } catch {
      // Best effort: the identity provider's own log is the authoritative record.
    }
  }

  /** `POST /api/v1/me/sign-ins` or `sign-outs` (204). Failures are ignored and never shown. */
  async function record(kind: 'sign-ins' | 'sign-outs', accessToken: string | null): Promise<void> {
    if (!accessToken || !apiBaseUrl || !/^https?:\/\//i.test(apiBaseUrl)) {
      return;
    }
    await postBestEffort(
      joinUrl(apiBaseUrl, `/api/v1/me/${kind}`),
      { Accept: 'application/json', 'Content-Type': 'application/json', Authorization: `Bearer ${accessToken}` },
      JSON.stringify({ client: 'mobile' }),
    );
  }

  /**
   * A sign-in that came through UAE PASS (the released profile says so, or it asked for UAE PASS and the provider
   * released no profile) also ends the UAE PASS browser session at sign-out, so a shared phone asks again. A test
   * account chosen on the provider's own page does not.
   */
  function endsUpstreamSession(current: Session): boolean {
    const source = current.profile?.source;
    return source === 'uae_pass_staging' || source === 'uae_pass' || (current.profile === null && current.viaUaePass);
  }

  /**
   * OIDC RP-Initiated Logout through the browser, when the provider has an end-session endpoint, asking it to end the
   * UAE PASS session too (`idp_hint=uae_pass`, D-129). Runs after the local session is already over, and is best
   * effort: a closed browser, a failure or a return without the state changes nothing here.
   */
  async function endUpstreamSessionInBrowser(): Promise<void> {
    try {
      const provider = await discover();
      if (!provider.endSessionEndpoint) {
        return;
      }
      const state = base64UrlEncode(await random(16));
      const url = buildEndSessionUrl(provider.endSessionEndpoint, {
        clientId: config.clientId,
        postLogoutRedirectUri: browser.signedOutUri,
        state,
        idpHint: 'uae_pass',
      });
      const result = await browser.openAuthSession(url, browser.signedOutUri);
      if (result.type === 'success') {
        readSignedOutCallback(result.url, { redirectUri: browser.signedOutUri, state });
      }
    } catch {
      // Discovery or the browser failed, or it was closed: the UAE PASS session may remain in the browser.
    }
  }

  /** RFC 7009, when the provider has a revocation endpoint. */
  async function revoke(refreshToken: string): Promise<void> {
    let provider: ProviderMetadata;
    try {
      provider = await discover();
    } catch {
      return;
    }
    if (provider.revocationEndpoint) {
      await postBestEffort(
        provider.revocationEndpoint,
        { 'Content-Type': 'application/x-www-form-urlencoded' },
        formEncode({ token: refreshToken, token_type_hint: 'refresh_token', client_id: config.clientId }),
      );
    }
  }

  async function refresh(current: Session): Promise<string | null> {
    const refreshToken = current.refreshToken;
    if (!refreshToken) {
      if (session === current) expire();
      return null;
    }
    let provider: ProviderMetadata;
    try {
      provider = await discover();
    } catch (error) {
      throw new ApiError(isSignInError(error) && error.kind === 'unsafe_response' ? 'invalid_response' : 'network');
    }
    const requestedAt = clock();
    let tokens: TokenSet;
    try {
      tokens = await requestTokens(
        provider.tokenEndpoint,
        refreshTokenBody({ refreshToken, clientId: config.clientId, resource: config.resource }),
        fetchImpl,
      );
    } catch (error) {
      if (session !== current) return null;
      const reason: TokenFailure = error instanceof TokenRequestError ? error.reason : 'network';
      if (reason === 'invalid_grant' || reason === 'rejected') {
        expire();
        return null;
      }
      throw new ApiError(apiKindForToken[reason]);
    }
    let renewedProfile: IdentityProfile | null | undefined;
    if (tokens.idToken !== null) {
      try {
        renewedProfile = validateIdToken(tokens.idToken, {
          issuer: config.issuer,
          clientId: config.clientId,
          now: clock(),
          nonce: null,
          subject: current.subject,
        }).profile;
      } catch {
        // Another subject's tokens, or claims that fail the checks: never kept.
        if (session === current) expire();
        return null;
      }
    }
    current.accessToken = tokens.accessToken;
    current.expiresAt = requestedAt + tokens.expiresIn * 1000;
    const rotated = tokens.refreshToken !== null && tokens.refreshToken !== refreshToken ? tokens.refreshToken : null;
    if (rotated) {
      current.refreshToken = rotated;
    }
    if (session !== current) return null;
    // A renewal that brings the profile back (after a restart) or changes it is shown; one without an ID token keeps it.
    if (renewedProfile !== undefined && JSON.stringify(renewedProfile) !== JSON.stringify(current.profile)) {
      current.profile = renewedProfile;
      if (store.get().status === 'signed_in') signedIn(current);
    }
    if (rotated) {
      await keystore(() => credentials.write('refresh_token', rotated));
    }
    return tokens.accessToken;
  }

  /** One renewal at a time for a session; everyone asking meanwhile shares it. */
  function refreshOnce(current: Session): Promise<string | null> {
    const running = refreshing;
    if (running && running.session === current) {
      return running.promise;
    }
    const promise: Promise<string | null> = refresh(current).finally(() => {
      if (refreshing?.promise === promise) refreshing = null;
    });
    refreshing = { session: current, promise };
    return promise;
  }

  async function getAccessToken(): Promise<string | null> {
    const current = session;
    if (!current) {
      return null;
    }
    if (current.accessToken && current.expiresAt - clock() > REFRESH_MARGIN_MS) {
      return current.accessToken;
    }
    return refreshOnce(current);
  }

  async function runSignIn(request: Extract<SignInRequest, { method: 'oidc' }>): Promise<void> {
    const provider = await discover();
    const secrets = await createAuthorizationSecrets(random, sha256);
    const idpHint =
      request.identityProvider === 'uae_pass'
        ? config.idpHint
        : request.identityProvider === 'google'
          ? GOOGLE_HINT
          : null;
    const authorizationUrl = buildAuthorizationUrl(provider.authorizationEndpoint, {
      clientId: config.clientId,
      redirectUri: browser.redirectUri,
      scope: config.scope,
      state: secrets.state,
      nonce: secrets.nonce,
      codeChallenge: secrets.challenge,
      prompt: config.prompt,
      resource: config.resource,
      idpHint,
      uiLocales: request.language ?? null,
    });
    let result: Awaited<ReturnType<AuthBrowser['openAuthSession']>>;
    try {
      result = await browser.openAuthSession(authorizationUrl);
    } catch {
      throw new SignInError('provider_unreachable');
    }
    if (result.type !== 'success') {
      throw new SignInError('cancelled');
    }
    const code = readAuthorizationCallback(result.url, {
      redirectUri: browser.redirectUri,
      state: secrets.state,
      issuer: config.issuer,
      issRequired: provider.issParameterSupported,
    });
    const requestedAt = clock();
    let tokens: TokenSet;
    try {
      tokens = await requestTokens(
        provider.tokenEndpoint,
        authorizationCodeBody({
          code,
          codeVerifier: secrets.verifier,
          redirectUri: browser.redirectUri,
          clientId: config.clientId,
          resource: config.resource,
        }),
        fetchImpl,
      );
    } catch (error) {
      throw new SignInError(error instanceof TokenRequestError ? signInKindForToken[error.reason] : 'provider_unreachable');
    }
    if (tokens.idToken === null) {
      throw new SignInError('unsafe_response');
    }
    const { subject, profile } = validateIdToken(tokens.idToken, {
      issuer: config.issuer,
      clientId: config.clientId,
      now: clock(),
      nonce: secrets.nonce,
    });

    const current: Session = {
      subject,
      profile,
      refreshToken: tokens.refreshToken,
      accessToken: tokens.accessToken,
      expiresAt: requestedAt + tokens.expiresIn * 1000,
      viaUaePass: request.identityProvider === 'uae_pass' && idpHint !== null,
    };
    session = current;
    const refreshToken = tokens.refreshToken;
    await keystore(async () => {
      await credentials.clear();
      if (refreshToken) {
        await credentials.write('refresh_token', refreshToken);
        await credentials.write('subject', subject);
      }
    });
    if (session !== current) {
      return;
    }
    signedIn(current);
    await record('sign-ins', tokens.accessToken);
  }

  return {
    kind: 'oidc',
    offersUaePass: config.idpHint !== null,
    uaePassOnly: config.uaePassOnly,
    acceptsDevelopmentIssuer: acceptsDevelopmentIssuer(config),
    getState: store.get,
    subscribe: store.subscribe,

    async restore() {
      let stored: [string | null, string | null];
      try {
        stored = await Promise.all([credentials.read('refresh_token'), credentials.read('subject')]);
      } catch {
        // The keystore cannot be read now (for example a locked phone): nothing is deleted.
        store.set({ status: 'signed_out', reason: 'none' });
        return;
      }
      const [refreshToken, subject] = stored;
      if (!refreshToken || !subject) {
        if (refreshToken || subject) {
          await keystore(() => credentials.clear());
        }
        store.set({ status: 'signed_out', reason: 'none' });
        return;
      }
      const current: Session = {
        subject,
        profile: null,
        refreshToken,
        accessToken: null,
        expiresAt: 0,
        viaUaePass: config.idpHint !== null,
      };
      session = current;
      // A slow or absent network never holds start-up: the stored session carries on and renews later.
      await settledOrAfter(refreshOnce(current), restoreWaitMs);
      if (session === current && store.get().status === 'initializing') {
        signedIn(current);
      }
    },

    signIn(request) {
      if (request.method !== 'oidc') {
        return Promise.reject(new Error('The OpenID Connect provider signs in with the "oidc" method only.'));
      }
      signingIn ??= runSignIn(request).finally(() => {
        signingIn = null;
      });
      return signingIn;
    },

    async signOut() {
      const current = session;
      if (current) {
        await record('sign-outs', await getAccessToken().catch(() => null));
        session = null;
        if (refreshing?.session === current) {
          await refreshing.promise.catch(() => null);
        }
        if (current.refreshToken) {
          await revoke(current.refreshToken);
        }
      }
      session = null;
      await keystore(() => credentials.clear());
      store.set({ status: 'signed_out', reason: 'signed_out' });
      // Only now, with nothing left to lose if the app is killed while the browser is open.
      if (current && endsUpstreamSession(current)) {
        await endUpstreamSessionInBrowser();
      }
    },

    getAccessToken,

    reportUnauthorized() {
      if (store.get().status === 'signed_in') {
        expire();
      }
    },

    async describeIssuer() {
      try {
        const provider = await discover();
        return { developmentOnly: provider.developmentOnly, offersGoogle: provider.offersGoogle };
      } catch {
        return null;
      }
    },
  };
}
