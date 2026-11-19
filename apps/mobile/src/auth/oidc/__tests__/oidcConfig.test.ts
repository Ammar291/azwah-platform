import { resolveOidcConfig } from '../oidcConfig';

const configured = {
  EXPO_PUBLIC_OIDC_ISSUER: 'https://id.example.test',
  EXPO_PUBLIC_OIDC_CLIENT_ID: 'azwah-mobile',
};

describe('resolveOidcConfig', () => {
  it('uses the defaults: openid offline_access, prompt login, no resource', () => {
    expect(resolveOidcConfig(configured, false)).toEqual({
      issuer: 'https://id.example.test',
      clientId: 'azwah-mobile',
      scope: 'openid offline_access',
      resource: null,
      prompt: 'login',
      idpHint: 'uae_pass',
      uaePassOnly: false,
      isDevelopmentBuild: false,
      demonstrationBuild: false,
    });
  });

  it('carries a demonstration build through without making it a development build (D-148)', () => {
    expect(resolveOidcConfig(configured, false, true)).toMatchObject({ isDevelopmentBuild: false, demonstrationBuild: true });
  });

  it('still refuses an http issuer in a demonstration release build (D-148)', () => {
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_ISSUER: 'http://192.168.1.10:5090' }, false, true)).toBeNull();
  });

  it('sends the UAE PASS button to UAE PASS by default, leaving the test accounts possible', () => {
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_IDP_HINT: '  ' }, false)).toMatchObject({
      idpHint: 'uae_pass',
      uaePassOnly: false,
    });
  });

  it('takes a hint from the build as an override: UAE PASS only, no test accounts', () => {
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_IDP_HINT: ' uae_pass ' }, false)).toMatchObject({
      idpHint: 'uae_pass',
      uaePassOnly: true,
    });
  });

  it('sends no hint and names no UAE PASS when the build says none', () => {
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_IDP_HINT: 'NONE' }, false)).toMatchObject({
      idpHint: null,
      uaePassOnly: false,
    });
  });

  it('refuses a hint that is not a plain token', () => {
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_IDP_HINT: 'uae pass' }, false)).toBeNull();
  });

  it('is not configured without an issuer or a client ID', () => {
    expect(resolveOidcConfig({}, true)).toBeNull();
    expect(resolveOidcConfig({ EXPO_PUBLIC_OIDC_ISSUER: 'https://id.example.test' }, true)).toBeNull();
    expect(resolveOidcConfig({ EXPO_PUBLIC_OIDC_CLIENT_ID: 'azwah-mobile' }, true)).toBeNull();
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_ISSUER: '   ' }, true)).toBeNull();
  });

  it.each(['ftp://id.example.test', 'id.example.test', 'https://', 'https://id.example.test/?tenant=a', 'https://id.example.test/#x', 'https://user:pw@id.example.test'])(
    'refuses the issuer %p',
    (issuer) => {
      expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_ISSUER: issuer }, true)).toBeNull();
    },
  );

  it('accepts an http issuer in development builds only', () => {
    const env = { ...configured, EXPO_PUBLIC_OIDC_ISSUER: 'http://localhost:5090' };

    expect(resolveOidcConfig(env, true)?.issuer).toBe('http://localhost:5090');
    expect(resolveOidcConfig(env, false)).toBeNull();
  });

  it('keeps the issuer exactly as configured (trimmed), trailing slash included', () => {
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_ISSUER: ' https://id.example.test/ ' }, false)?.issuer).toBe(
      'https://id.example.test/',
    );
  });

  it('always asks for openid and keeps the other scopes once each', () => {
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_SCOPE: 'offline_access  azwah-api offline_access' }, false)?.scope).toBe(
      'openid offline_access azwah-api',
    );
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_SCOPE: 'openid "quoted"' }, false)).toBeNull();
  });

  it('passes a resource indicator through and refuses one with a fragment', () => {
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_RESOURCE: 'https://api.example.test' }, false)?.resource).toBe(
      'https://api.example.test',
    );
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_RESOURCE: 'urn:azwah:api' }, false)?.resource).toBe('urn:azwah:api');
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_RESOURCE: 'https://api.example.test#x' }, false)).toBeNull();
  });

  it('accepts known prompt values and falls back to login for anything else', () => {
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_PROMPT: 'select_account' }, false)?.prompt).toBe('select_account');
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_PROMPT: 'none' }, false)?.prompt).toBe('login');
    expect(resolveOidcConfig({ ...configured, EXPO_PUBLIC_OIDC_PROMPT: '' }, false)?.prompt).toBe('login');
  });

  it('has no setting for a client secret', () => {
    const env = { ...configured, EXPO_PUBLIC_OIDC_CLIENT_SECRET: 'must-be-ignored' };

    expect(JSON.stringify(resolveOidcConfig(env, true))).not.toContain('must-be-ignored');
  });
});
