// @vitest-environment node
import { describe, expect, it, vi } from 'vitest';

import { parseAdminConfig } from './config';

const secret = Buffer.alloc(32, 7).toString('base64');

const production = {
  ADMIN_APP_ORIGIN: 'https://admin.example.org',
  ADMIN_SESSION_SECRET: secret,
  ADMIN_AUTH_MODE: 'oidc',
  OIDC_ISSUER: 'https://issuer.example.org/realms/azwah',
  OIDC_CLIENT_ID: 'azwah-admin',
  ADMIN_API_URL: 'https://api.example.org',
};

function problemsOf(env: Record<string, string | undefined>) {
  const result = parseAdminConfig(env);
  return result.ok ? [] : result.problems;
}

describe('parseAdminConfig', () => {
  it('reads a complete production configuration', () => {
    const result = parseAdminConfig(production);

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.config.environment).toBe('production');
    expect(result.config.appOrigin).toBe('https://admin.example.org');
    expect(result.config.secureCookies).toBe(true);
    expect(result.config.sessionMaxAgeSeconds).toBe(8 * 60 * 60);
    expect(result.config.auth).toEqual({
      mode: 'oidc',
      issuer: new URL('https://issuer.example.org/realms/azwah'),
      clientId: 'azwah-admin',
      clientSecret: null,
      scope: 'openid offline_access',
      resource: null,
    });
    expect(result.config.data).toEqual({ source: 'api', apiUrl: 'https://api.example.org' });
  });

  it('treats a missing or unknown environment as production', () => {
    expect(problemsOf({ ...production, ADMIN_AUTH_MODE: 'development' })).toContain('development_mode_in_production');
    expect(problemsOf({ ...production, ADMIN_ENVIRONMENT: 'staging', ADMIN_AUTH_MODE: 'development' })).toContain(
      'development_mode_in_production',
    );
  });

  it('refuses mock data outside development', () => {
    expect(problemsOf({ ...production, ADMIN_DATA_SOURCE: 'mock' })).toContain('development_mode_in_production');
  });

  it('refuses mock data with a real identity provider', () => {
    expect(
      problemsOf({ ...production, ADMIN_ENVIRONMENT: 'development', ADMIN_DATA_SOURCE: 'mock', ADMIN_AUTH_MODE: 'oidc' }),
    ).toContain('mock_needs_development_sign_in');
  });

  it('requires a strong session secret in production', () => {
    expect(problemsOf({ ...production, ADMIN_SESSION_SECRET: undefined })).toContain('session_secret_missing');
    expect(problemsOf({ ...production, ADMIN_SESSION_SECRET: Buffer.alloc(16).toString('base64') })).toContain(
      'session_secret_weak',
    );
  });

  it('accepts a secret longer than 32 bytes and turns it into a key that seals sessions', async () => {
    const result = parseAdminConfig({ ...production, ADMIN_SESSION_SECRET: Buffer.alloc(64, 3).toString('base64') });

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.config.sessionSecret.byteLength).toBe(32);
    const { sealSession, unsealSession } = await import('./session');
    const session = { kind: 'development' as const, persona: 'researcher' as const, issuedAt: Date.now() };
    expect(await unsealSession(await sealSession(session, result.config.sessionSecret, 60), result.config.sessionSecret)).toEqual(session);
  });

  it('requires the issuer and client in OIDC mode', () => {
    const problems = problemsOf({ ...production, OIDC_ISSUER: undefined, OIDC_CLIENT_ID: '' });

    expect(problems).toContain('oidc_issuer_missing');
    expect(problems).toContain('oidc_client_missing');
  });

  it('requires an absolute origin and API address in production', () => {
    expect(problemsOf({ ...production, ADMIN_APP_ORIGIN: undefined })).toContain('app_origin_missing');
    expect(problemsOf({ ...production, ADMIN_APP_ORIGIN: 'admin.example.org' })).toContain('app_origin_invalid');
    expect(problemsOf({ ...production, ADMIN_API_URL: undefined })).toContain('api_url_missing');
    expect(problemsOf({ ...production, ADMIN_API_URL: 'ftp://api' })).toContain('api_url_invalid');
  });

  it('refuses an unknown sign-in mode or data source', () => {
    expect(problemsOf({ ...production, ADMIN_AUTH_MODE: 'password' })).toContain('auth_mode_invalid');
    expect(problemsOf({ ...production, ADMIN_DATA_SOURCE: 'csv' })).toContain('data_source_invalid');
  });

  it('gives development sensible defaults', () => {
    const result = parseAdminConfig({ ADMIN_ENVIRONMENT: 'development', ADMIN_DATA_SOURCE: 'mock' });

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.config.appOrigin).toBe('http://localhost:3000');
    expect(result.config.secureCookies).toBe(false);
    expect(result.config.auth).toEqual({ mode: 'development', tokens: {} });
    expect(result.config.data).toEqual({ source: 'mock', scenario: 'no_approved_scores' });
  });

  it('derives the same development key in every module instance, so the proxy and the routes agree', async () => {
    const env = { ADMIN_ENVIRONMENT: 'development', ADMIN_DATA_SOURCE: 'mock' };
    const first = parseAdminConfig(env);
    vi.resetModules();
    const fresh = await import('./config');
    const second = fresh.parseAdminConfig({ ...env });

    expect(first.ok && second.ok).toBe(true);
    if (!first.ok || !second.ok) return;
    expect(first.config.sessionSecret.byteLength).toBe(32);
    expect(Buffer.from(second.config.sessionSecret).equals(Buffer.from(first.config.sessionSecret))).toBe(true);
  });

  it('still requires a real secret for OIDC sign-in in development, because those sessions hold tokens', () => {
    expect(
      problemsOf({
        ADMIN_ENVIRONMENT: 'development',
        ADMIN_AUTH_MODE: 'oidc',
        OIDC_ISSUER: 'http://localhost:8180/realms/azwah',
        OIDC_CLIENT_ID: 'azwah-admin',
        ADMIN_API_URL: 'http://localhost:5080',
      }),
    ).toContain('session_secret_missing');
  });

  it('reads development tokens per persona and the mock scenario', () => {
    const result = parseAdminConfig({
      ADMIN_ENVIRONMENT: 'development',
      ADMIN_API_URL: 'http://localhost:5080',
      ADMIN_DEV_TOKEN_RESEARCHER: 'token-r',
      ADMIN_DEV_TOKEN_SYSTEM_ADMINISTRATOR: 'token-s',
    });

    expect(result.ok && result.config.auth).toEqual({
      mode: 'development',
      tokens: { researcher: 'token-r', system_administrator: 'token-s' },
    });
    expect(
      problemsOf({ ADMIN_ENVIRONMENT: 'development', ADMIN_DATA_SOURCE: 'mock', ADMIN_MOCK_SCENARIO: 'everything' }),
    ).toContain('mock_scenario_invalid');
  });

  it('bounds the session lifetime', () => {
    expect(problemsOf({ ...production, ADMIN_SESSION_MAX_AGE_SECONDS: '60' })).toContain('session_max_age_invalid');
    expect(problemsOf({ ...production, ADMIN_SESSION_MAX_AGE_SECONDS: 'forever' })).toContain(
      'session_max_age_invalid',
    );
  });
});
