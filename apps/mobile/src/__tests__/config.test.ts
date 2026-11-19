import { resolveApiBaseUrl, resolveAppMode, resolveDemonstrationBuild, resolveDemoTools } from '@/config';

describe('resolveApiBaseUrl', () => {
  it('keeps an https address in any build', () => {
    expect(resolveApiBaseUrl(' https://api.example.org ', false)).toBe('https://api.example.org');
    expect(resolveApiBaseUrl('https://api.example.org', true)).toBe('https://api.example.org');
  });

  it('allows plain http only in a development build, so tokens never travel in clear from a release build', () => {
    expect(resolveApiBaseUrl('http://10.0.2.2:8080', true)).toBe('http://10.0.2.2:8080');
    expect(resolveApiBaseUrl('http://10.0.2.2:8080', false)).toBeUndefined();
  });

  it('treats a missing or unusable address as not configured', () => {
    expect(resolveApiBaseUrl(undefined, true)).toBeUndefined();
    expect(resolveApiBaseUrl('   ', true)).toBeUndefined();
    expect(resolveApiBaseUrl('ftp://api.example.org', true)).toBeUndefined();
    expect(resolveApiBaseUrl('not a url', true)).toBeUndefined();
  });
});

describe('resolveDemoTools (D-130)', () => {
  it('shows the demo buttons in fixtures mode, or when EXPO_PUBLIC_DEMO_TOOLS is true', () => {
    expect(resolveDemoTools(undefined, 'development_fixtures')).toBe(true);
    expect(resolveDemoTools(' TRUE ', 'api')).toBe(true);
  });

  it('hides them against the API unless asked for', () => {
    expect(resolveDemoTools(undefined, 'api')).toBe(false);
    expect(resolveDemoTools('false', 'api')).toBe(false);
    expect(resolveDemoTools('yes', 'api')).toBe(false);
  });
});
describe('resolveDemonstrationBuild (D-148)', () => {
  it('is on only when EXPO_PUBLIC_DEMONSTRATION_BUILD is true, in any case and with spaces around it', () => {
    expect(resolveDemonstrationBuild('true')).toBe(true);
    expect(resolveDemonstrationBuild('TRUE')).toBe(true);
    expect(resolveDemonstrationBuild('  True\n')).toBe(true);
  });

  it('is off when absent, empty or anything else', () => {
    expect(resolveDemonstrationBuild(undefined)).toBe(false);
    expect(resolveDemonstrationBuild('')).toBe(false);
    expect(resolveDemonstrationBuild('   ')).toBe(false);
    expect(resolveDemonstrationBuild('false')).toBe(false);
    expect(resolveDemonstrationBuild('1')).toBe(false);
    expect(resolveDemonstrationBuild('yes')).toBe(false);
  });

  it('changes nothing else a release build refuses: plain http and the fixtures stay development-only', () => {
    // The flag is not an input to these: a release build passes isDevelopmentBuild=false whatever it says.
    expect(resolveApiBaseUrl('http://192.168.1.10:8080', false)).toBeUndefined();
    expect(resolveAppMode('true', false)).toBe('api');
  });
});
