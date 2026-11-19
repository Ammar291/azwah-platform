import type { IdentityProfile } from './identityProfile';

export type SignedOutReason = 'none' | 'signed_out' | 'session_expired';

export interface AuthSession {
  /** The token subject (`sub`); the server maps it to a platform user. Never trusted for authorisation here. */
  subject: string;
  /** Development mock only: which fixture persona is signed in. */
  personaCode: string | null;
  /**
   * The details the identity provider released in the ID token, for the one-time confirmation (D-97); null when it
   * released none. In memory only: never stored on the phone or sent to the platform API.
   */
  profile: IdentityProfile | null;
}

export type AuthState =
  | { status: 'initializing' }
  | { status: 'signed_out'; reason: SignedOutReason }
  | { status: 'signed_in'; session: AuthSession }
  /** No sign-in method is configured in this build (API mode without EXPO_PUBLIC_OIDC_ISSUER and _CLIENT_ID). */
  | { status: 'unavailable'; reason: 'not_configured' };

export type SignInRequest =
  | { method: 'development_persona'; personaCode: string }
  | {
      method: 'oidc';
      /**
       * `uae_pass` asks the issuer to go straight to UAE PASS (`idp_hint`, see OidcConfig.idpHint); without it the
       * issuer shows its own choice, for example the development provider's test accounts. `google` asks it to go
       * straight to Google (`idp_hint=google`, D-147); the app offers it only when the issuer lists it.
       */
      identityProvider?: 'uae_pass' | 'google';
      /** The app's language, so the sign-in pages open in it (`ui_locales`). */
      language?: 'ar' | 'en';
    };

export type AuthProviderKind = 'development_mock' | 'oidc';

/**
 * How the app signs people in. Screens and the API client depend on this interface only, so the development mock
 * and the OpenID Connect provider are interchangeable.
 */
export interface AuthProvider {
  readonly kind: AuthProviderKind;
  getState(): AuthState;
  subscribe(listener: () => void): () => void;
  /** Reads any remembered session at start-up; moves out of `initializing`. */
  restore(): Promise<void>;
  signIn(request: SignInRequest): Promise<void>;
  signOut(): Promise<void>;
  /** The bearer token for API calls, or null when there is none (always null for the development mock). */
  getAccessToken(): Promise<string | null>;
  /** The API answered 401: the session is over. */
  reportUnauthorized(): void;
}

/** What the OpenID Connect provider says about its issuer before anyone signs in. */
export interface IssuerDescription {
  /**
   * The issuer declared itself a development provider (`azwah_development_only`); only development builds, and release
   * builds made as demonstration builds (D-148), accept it.
   */
  developmentOnly: boolean;
  /**
   * The issuer has the Google sign-in configured (its discovery lists `google` in `azwah_identity_providers`, D-147),
   * so the sign-in screen may offer "Continue with Google" (CONTRIBUTING.md rule 8).
   */
  offersGoogle: boolean;
}

/** The OpenID Connect provider (authorization code with PKCE through the system browser). */
export interface OidcAuthProvider extends AuthProvider {
  readonly kind: 'oidc';
  /**
   * The UAE PASS button sends the issuer straight to UAE PASS (`idp_hint`, `uae_pass` unless EXPO_PUBLIC_OIDC_IDP_HINT
   * says otherwise), so the sign-in may say UAE PASS. With EXPO_PUBLIC_OIDC_IDP_HINT=none it may not: the app says only
   * "Sign in" and the issuer's own page decides (CONTRIBUTING.md rule 8).
   */
  readonly offersUaePass: boolean;
  /** EXPO_PUBLIC_OIDC_IDP_HINT is set: UAE PASS is the only sign-in offered, never the provider's test accounts. */
  readonly uaePassOnly: boolean;
  /**
   * This build signs in with an issuer that declares itself development-only: a development build, or a release build
   * made as a demonstration build (EXPO_PUBLIC_DEMONSTRATION_BUILD, D-148). Such an issuer's own page may be offered.
   */
  readonly acceptsDevelopmentIssuer: boolean;
  /** Reads the issuer's discovery document (cached). Null when it cannot be read or is refused; never rejects. */
  describeIssuer(): Promise<IssuerDescription | null>;
}

export function isOidcAuthProvider(provider: AuthProvider): provider is OidcAuthProvider {
  return provider.kind === 'oidc' && 'describeIssuer' in provider;
}

/** The entries an OpenID Connect provider keeps in the platform keystore. Nothing else is ever stored. */
export type CredentialName = 'refresh_token' | 'subject';

/**
 * Where the OpenID Connect provider keeps the refresh token and the subject it belongs to: expo-secure-store (iOS
 * Keychain, Android Keystore). Access and ID tokens stay in memory. Nothing goes to SQLite, settings, logs or files.
 */
export interface CredentialStore {
  read(name: CredentialName): Promise<string | null>;
  write(name: CredentialName, value: string): Promise<void>;
  clear(): Promise<void>;
}

export class SignInUnavailableError extends Error {
  constructor() {
    super('No sign-in method is configured in this build.');
    this.name = 'SignInUnavailableError';
  }
}

/**
 * Why an OpenID Connect sign-in did not finish, in terms the sign-in screen translates:
 * - `cancelled`: the person closed the browser or chose cancel (shown as nothing);
 * - `unsafe_response`: something failed a check (state, issuer, nonce, an invalid or refused response);
 * - `provider_unreachable`: the identity provider could not be reached or had a fault;
 * - `refused`: the identity provider answered with an error.
 */
export type SignInErrorKind = 'cancelled' | 'unsafe_response' | 'provider_unreachable' | 'refused';

/** Carries only the kind: no token, code, address or provider text is ever kept in it. */
export class SignInError extends Error {
  constructor(readonly kind: SignInErrorKind) {
    super(`Sign-in did not finish: ${kind}`);
    this.name = 'SignInError';
  }
}

export function isSignInError(error: unknown): error is SignInError {
  return error instanceof SignInError;
}
