/** The build's OpenID Connect settings (EXPO_PUBLIC_ variables, inlined by Expo; see apps/mobile/.env.example). */
export interface OidcEnvironment {
  EXPO_PUBLIC_OIDC_ISSUER?: string;
  EXPO_PUBLIC_OIDC_CLIENT_ID?: string;
  EXPO_PUBLIC_OIDC_SCOPE?: string;
  EXPO_PUBLIC_OIDC_RESOURCE?: string;
  EXPO_PUBLIC_OIDC_PROMPT?: string;
  EXPO_PUBLIC_OIDC_IDP_HINT?: string;
}

/**
 * How the app signs in. The client is public: there is no client secret, in configuration or anywhere else (a secret
 * shipped inside an app is not secret).
 */
export interface OidcConfig {
  /** Exactly as configured; discovery, the callback's `iss` and the ID token's `iss` must equal it. */
  issuer: string;
  clientId: string;
  /** Always includes `openid`. */
  scope: string;
  /** RFC 8707 resource indicator for the API, when the provider needs one. */
  resource: string | null;
  /** `login` by default, so a shared family phone asks again at every sign-in. */
  prompt: string;
  /**
   * Sent as `idp_hint` on a "Sign in with UAE PASS" request, so the Azwah identity provider goes straight to UAE PASS
   * staging instead of showing its own page (D-96, D-129). `uae_pass` unless EXPO_PUBLIC_OIDC_IDP_HINT names another;
   * null when it is `none`: the issuer's own page decides and the app names no UAE PASS.
   */
  idpHint: string | null;
  /**
   * EXPO_PUBLIC_OIDC_IDP_HINT names the hint: every sign-in goes to UAE PASS, so the sign-in screen offers no test
   * accounts (D-142). Otherwise a development build against a development provider also offers its test accounts.
   */
  uaePassOnly: boolean;
  /** Development builds accept `http` endpoints and an issuer that declares itself development-only. */
  isDevelopmentBuild: boolean;
  /**
   * EXPO_PUBLIC_DEMONSTRATION_BUILD (D-148): a release build for a Development stack accepts an issuer that declares
   * itself development-only and offers its own sign-in page. It never accepts `http`: that stays with
   * `isDevelopmentBuild`.
   */
  demonstrationBuild: boolean;
}

/** Whether this build signs in with an issuer that declares itself development-only (D-82, D-148). */
export function acceptsDevelopmentIssuer(config: Pick<OidcConfig, 'isDevelopmentBuild' | 'demonstrationBuild'>): boolean {
  return config.isDevelopmentBuild || config.demonstrationBuild;
}

const DEFAULT_SCOPE = 'openid offline_access';
const DEFAULT_PROMPT = 'login';
/** What the Azwah identity provider understands as "go straight to UAE PASS" (D-96). */
const DEFAULT_IDP_HINT = 'uae_pass';
/** EXPO_PUBLIC_OIDC_IDP_HINT=none: no hint, and the sign-in screen names no UAE PASS. */
const NO_HINT = 'none';
const PROMPT_VALUES = new Set(['login', 'consent', 'select_account']);
/** RFC 6749 section 3.3 scope-token characters. */
const SCOPE_TOKEN = /^[\x21\x23-\x5b\x5d-\x7e]+$/;
/** RFC 8707: an absolute URI without a fragment. It only names the API; nothing is fetched from it. */
const RESOURCE = /^[a-z][a-z0-9+.-]*:[^\s#]+$/i;

/** An absolute http(s) URL without credentials or fragment; `http` only in development builds. */
export function isAllowedUrl(value: string, isDevelopmentBuild: boolean): boolean {
  if (value.includes('#')) {
    return false;
  }
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    return false;
  }
  if (url.username !== '' || url.password !== '' || url.hostname === '') {
    return false;
  }
  return url.protocol === 'https:' || (isDevelopmentBuild && url.protocol === 'http:');
}

function resolveScope(raw: string | undefined): string | null {
  const tokens = (raw?.trim() || DEFAULT_SCOPE).split(/\s+/);
  if (!tokens.every((token) => SCOPE_TOKEN.test(token))) {
    return null;
  }
  return [...new Set(['openid', ...tokens])].join(' ');
}

/** Anything unexpected falls back to `login`, the safer choice. */
function resolvePrompt(raw: string | undefined): string {
  const values = raw?.trim().split(/\s+/).filter(Boolean) ?? [];
  return values.length > 0 && values.every((value) => PROMPT_VALUES.has(value)) ? values.join(' ') : DEFAULT_PROMPT;
}

/**
 * The OpenID Connect settings of this build, or null when sign-in is not configured: no issuer or client ID, an
 * issuer that is not an http(s) URL (or is `http` in a release build), or settings that cannot be used as they are.
 * Null keeps the honest "sign-in isn't available" state; nothing is guessed.
 */
export function resolveOidcConfig(
  env: OidcEnvironment,
  isDevelopmentBuild: boolean,
  demonstrationBuild = false,
): OidcConfig | null {
  const issuer = env.EXPO_PUBLIC_OIDC_ISSUER?.trim();
  const clientId = env.EXPO_PUBLIC_OIDC_CLIENT_ID?.trim();
  if (!issuer || !clientId || issuer.includes('?') || !isAllowedUrl(issuer, isDevelopmentBuild)) {
    return null;
  }
  const scope = resolveScope(env.EXPO_PUBLIC_OIDC_SCOPE);
  const resource = env.EXPO_PUBLIC_OIDC_RESOURCE?.trim() || null;
  const hintSetting = env.EXPO_PUBLIC_OIDC_IDP_HINT?.trim() || null;
  const noHint = hintSetting?.toLowerCase() === NO_HINT;
  const idpHint = noHint ? null : (hintSetting ?? DEFAULT_IDP_HINT);
  if (
    scope === null ||
    (resource !== null && !RESOURCE.test(resource)) ||
    (idpHint !== null && !SCOPE_TOKEN.test(idpHint))
  ) {
    return null;
  }
  return {
    issuer,
    clientId,
    scope,
    resource,
    prompt: resolvePrompt(env.EXPO_PUBLIC_OIDC_PROMPT),
    idpHint,
    uaePassOnly: hintSetting !== null && !noHint,
    isDevelopmentBuild,
    demonstrationBuild,
  };
}
