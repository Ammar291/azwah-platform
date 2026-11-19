import 'server-only';

import { createHash, hkdfSync } from 'node:crypto';

import { PERSONAS, type Persona } from '@/lib/personas';

/**
 * Server-only settings, read from the environment once. Nothing here has a NEXT_PUBLIC_ prefix, so none of it reaches
 * the browser bundle. The deployment fails closed: development sign-in and mock data exist only when
 * ADMIN_ENVIRONMENT is exactly `development`.
 */

export type AuthConfig =
  | { mode: 'oidc'; issuer: URL; clientId: string; clientSecret: string | null; scope: string; resource: string | null }
  | { mode: 'development'; tokens: Partial<Record<Persona, string>> };

export type MockScenario = 'no_approved_scores' | 'approved_scores';

export type DataConfig = { source: 'api'; apiUrl: string } | { source: 'mock'; scenario: MockScenario };

export interface AdminConfig {
  environment: 'development' | 'production';
  appOrigin: string;
  secureCookies: boolean;
  sessionSecret: Uint8Array;
  sessionMaxAgeSeconds: number;
  auth: AuthConfig;
  data: DataConfig;
}

export const CONFIG_PROBLEMS = [
  'session_secret_missing',
  'session_secret_weak',
  'session_max_age_invalid',
  'app_origin_missing',
  'app_origin_invalid',
  'auth_mode_invalid',
  'oidc_issuer_missing',
  'oidc_issuer_invalid',
  'oidc_client_missing',
  'development_mode_in_production',
  'data_source_invalid',
  'mock_needs_development_sign_in',
  'mock_scenario_invalid',
  'api_url_missing',
  'api_url_invalid',
] as const;

export type ConfigProblem = (typeof CONFIG_PROBLEMS)[number];

export type ConfigResult = { ok: true; config: AdminConfig } | { ok: false; problems: ConfigProblem[] };

const DEFAULT_SESSION_SECONDS = 8 * 60 * 60;
const DEVELOPMENT_ORIGIN = 'http://localhost:3000';
const DEFAULT_SCOPE = 'openid offline_access';

/**
 * Development sign-in without ADMIN_SESSION_SECRET gets a key derived from a fixed label and the app's origin. It must
 * be the same in every module instance: Next.js runs the proxy in its own bundle, so a random per-process key would
 * make the proxy refuse cookies the routes sealed. That is safe only because a development session holds a persona
 * name and nothing secret, and anyone can pick any persona anyway. OIDC sessions hold tokens and need a real secret.
 */
/** The 256-bit key `dir` + `A256GCM` needs, from a secret of any length of at least 32 bytes. */
function sessionKey(secret: Uint8Array): Uint8Array {
  return new Uint8Array(hkdfSync('sha256', secret, 'azwah-admin', 'session-cookie-v1', 32));
}

function developmentKey(origin: string): Uint8Array {
  return new Uint8Array(createHash('sha256').update(`azwah-admin-development-session:${origin}`).digest());
}

function text(value: string | undefined): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

function httpUrl(value: string): URL | null {
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:' ? url : null;
  } catch {
    return null;
  }
}

function tokenVariable(persona: Persona): string {
  return `ADMIN_DEV_TOKEN_${persona.toUpperCase()}`;
}

export function parseAdminConfig(env: Record<string, string | undefined>): ConfigResult {
  const problems: ConfigProblem[] = [];
  const environment = env.ADMIN_ENVIRONMENT === 'development' ? 'development' : 'production';
  const development = environment === 'development';

  const originText = text(env.ADMIN_APP_ORIGIN) ?? (development ? DEVELOPMENT_ORIGIN : null);
  const origin = originText ? httpUrl(originText) : null;
  if (!originText) problems.push('app_origin_missing');
  else if (!origin || origin.pathname !== '/' || origin.search || origin.hash) problems.push('app_origin_invalid');

  const authModeText = text(env.ADMIN_AUTH_MODE) ?? (development ? 'development' : 'oidc');
  let sessionSecret: Uint8Array | null = null;
  const secretText = text(env.ADMIN_SESSION_SECRET);
  if (secretText) {
    const bytes = Buffer.from(secretText, 'base64');
    if (bytes.byteLength < 32) problems.push('session_secret_weak');
    else sessionSecret = sessionKey(new Uint8Array(bytes));
  } else if (development && authModeText === 'development') {
    sessionSecret = developmentKey(originText ?? DEVELOPMENT_ORIGIN);
  } else {
    problems.push('session_secret_missing');
  }

  const maxAgeText = text(env.ADMIN_SESSION_MAX_AGE_SECONDS);
  const sessionMaxAgeSeconds = maxAgeText === null ? DEFAULT_SESSION_SECONDS : Number(maxAgeText);
  if (!Number.isInteger(sessionMaxAgeSeconds) || sessionMaxAgeSeconds < 300 || sessionMaxAgeSeconds > 86_400) {
    problems.push('session_max_age_invalid');
  }

  const authMode = authModeText;
  let auth: AuthConfig | null = null;
  if (authMode === 'oidc') {
    const issuerText = text(env.OIDC_ISSUER);
    const issuer = issuerText ? httpUrl(issuerText) : null;
    const clientId = text(env.OIDC_CLIENT_ID);
    if (!issuerText) problems.push('oidc_issuer_missing');
    else if (!issuer || (!development && issuer.protocol !== 'https:')) problems.push('oidc_issuer_invalid');
    if (!clientId) problems.push('oidc_client_missing');
    if (issuer && clientId) {
      auth = {
        mode: 'oidc',
        issuer,
        clientId,
        clientSecret: text(env.OIDC_CLIENT_SECRET),
        scope: text(env.OIDC_SCOPE) ?? DEFAULT_SCOPE,
        resource: text(env.OIDC_RESOURCE),
      };
    }
  } else if (authMode === 'development') {
    if (!development) problems.push('development_mode_in_production');
    const tokens: Partial<Record<Persona, string>> = {};
    for (const persona of PERSONAS) {
      const token = text(env[tokenVariable(persona)]);
      if (token) tokens[persona] = token;
    }
    auth = { mode: 'development', tokens };
  } else {
    problems.push('auth_mode_invalid');
  }

  const source = text(env.ADMIN_DATA_SOURCE) ?? 'api';
  let data: DataConfig | null = null;
  if (source === 'mock') {
    if (!development) problems.push('development_mode_in_production');
    else if (authMode !== 'development') problems.push('mock_needs_development_sign_in');
    const scenario = text(env.ADMIN_MOCK_SCENARIO) ?? 'no_approved_scores';
    if (scenario === 'no_approved_scores' || scenario === 'approved_scores') data = { source: 'mock', scenario };
    else problems.push('mock_scenario_invalid');
  } else if (source === 'api') {
    const apiText = text(env.ADMIN_API_URL);
    const apiUrl = apiText ? httpUrl(apiText) : null;
    if (!apiText) problems.push('api_url_missing');
    else if (!apiUrl) problems.push('api_url_invalid');
    else data = { source: 'api', apiUrl: apiUrl.href.replace(/\/+$/, '') };
  } else {
    problems.push('data_source_invalid');
  }

  if (problems.length > 0 || !origin || !sessionSecret || !auth || !data) {
    return { ok: false, problems: [...new Set(problems)] };
  }

  return {
    ok: true,
    config: {
      environment,
      appOrigin: origin.origin,
      secureCookies: origin.protocol === 'https:',
      sessionSecret,
      sessionMaxAgeSeconds,
      auth,
      data,
    },
  };
}

let cached: ConfigResult | null = null;

/** The deployment's configuration, parsed once per server process. */
export function getAdminConfig(): ConfigResult {
  cached ??= parseAdminConfig(process.env);
  return cached;
}
