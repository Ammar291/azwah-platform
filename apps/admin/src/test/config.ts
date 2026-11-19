import type { AdminConfig } from '@/lib/server/config';

/** A development configuration for tests; override any part. */
export function testConfig(overrides: Partial<AdminConfig> = {}): AdminConfig {
  return {
    environment: 'development',
    appOrigin: 'http://localhost:3000',
    secureCookies: false,
    sessionSecret: new Uint8Array(32).fill(9),
    sessionMaxAgeSeconds: 3600,
    auth: { mode: 'development', tokens: { researcher: 'dev-token-researcher' } },
    data: { source: 'api', apiUrl: 'http://api.test' },
    ...overrides,
  };
}

export function oidcTestConfig(overrides: Partial<AdminConfig> = {}): AdminConfig {
  return testConfig({
    environment: 'production',
    appOrigin: 'https://admin.example.org',
    secureCookies: true,
    auth: {
      mode: 'oidc',
      issuer: new URL('https://issuer.example.org'),
      clientId: 'azwah-admin',
      clientSecret: null,
      scope: 'openid offline_access',
      resource: null,
    },
    ...overrides,
  });
}
