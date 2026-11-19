/**
 * The API's address, or undefined (the app then says the server is not configured). Plain `http` is accepted only in a
 * development build, for the emulator and the local network: a release build sends bearer tokens over HTTPS only.
 */
export function resolveApiBaseUrl(value: string | undefined, isDevelopmentBuild: boolean): string | undefined {
  const trimmed = value?.trim();
  if (!trimmed) return undefined;
  let protocol: string;
  try {
    ({ protocol } = new URL(trimmed));
  } catch {
    return undefined;
  }
  return protocol === 'https:' || (protocol === 'http:' && isDevelopmentBuild) ? trimmed : undefined;
}

/** From apps/mobile/.env (see .env.example). Expo inlines EXPO_PUBLIC_ variables when it bundles the app. */
export const apiBaseUrl: string | undefined = resolveApiBaseUrl(process.env.EXPO_PUBLIC_API_URL, __DEV__);

/**
 * The OpenID Connect settings (src/auth/oidc/oidcConfig.ts). Each variable is read by name so Expo can inline it;
 * there is no client-secret variable: the app is a public client.
 */
export const oidcEnvironment = {
  EXPO_PUBLIC_OIDC_ISSUER: process.env.EXPO_PUBLIC_OIDC_ISSUER,
  EXPO_PUBLIC_OIDC_CLIENT_ID: process.env.EXPO_PUBLIC_OIDC_CLIENT_ID,
  EXPO_PUBLIC_OIDC_SCOPE: process.env.EXPO_PUBLIC_OIDC_SCOPE,
  EXPO_PUBLIC_OIDC_RESOURCE: process.env.EXPO_PUBLIC_OIDC_RESOURCE,
  EXPO_PUBLIC_OIDC_PROMPT: process.env.EXPO_PUBLIC_OIDC_PROMPT,
  EXPO_PUBLIC_OIDC_IDP_HINT: process.env.EXPO_PUBLIC_OIDC_IDP_HINT,
};

/**
 * `development_fixtures`: the simulated sign-in and the in-memory fixture API from src/dev-fixtures, with synthetic
 * data only. `api`: the real API, with OpenID Connect sign-in when EXPO_PUBLIC_OIDC_ISSUER and _CLIENT_ID are set
 * (otherwise sign-in says it is not available).
 */
export type AppMode = 'development_fixtures' | 'api';

/** Fixtures only when asked for, and never in a release build. */
export function resolveAppMode(flag: string | undefined, isDevelopmentBuild: boolean): AppMode {
  return isDevelopmentBuild && flag?.trim().toLowerCase() === 'true' ? 'development_fixtures' : 'api';
}

export const appMode: AppMode = resolveAppMode(process.env.EXPO_PUBLIC_DEV_FIXTURES, __DEV__);

/**
 * The questionnaire's "fill for a demo" buttons (D-130): shown in fixtures mode, or when EXPO_PUBLIC_DEMO_TOOLS is
 * true. The server decides the answers and refuses unless its own DemoTools switch is on, which no deployed API allows.
 */
export function resolveDemoTools(flag: string | undefined, mode: AppMode): boolean {
  return mode === 'development_fixtures' || flag?.trim().toLowerCase() === 'true';
}

export const demoToolsEnabled: boolean = resolveDemoTools(process.env.EXPO_PUBLIC_DEMO_TOOLS, appMode);

/**
 * A demonstration build (D-148): a sideloaded release APK for a Development stack with synthetic data only, never a
 * build real families use. In a release build it lets the app accept an issuer that declares itself development-only,
 * offer that issuer's own sign-in page, and offer the demo family. It is not a development build: plain http, the
 * fixtures and every other development-only part stay refused (resolveApiBaseUrl, resolveAppMode, isAllowedUrl).
 */
export function resolveDemonstrationBuild(flag: string | undefined): boolean {
  return flag?.trim().toLowerCase() === 'true';
}

export const demonstrationBuild: boolean = resolveDemonstrationBuild(process.env.EXPO_PUBLIC_DEMONSTRATION_BUILD);
