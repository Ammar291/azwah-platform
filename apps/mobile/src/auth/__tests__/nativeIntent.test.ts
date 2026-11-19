import { redirectSystemPath as routedRedirect } from '../../../app/+native-intent';
import { isAuthReturnPath, redirectSystemPath } from '../nativeIntent';

describe('native intent', () => {
  it.each([
    'azwah://auth/callback?code=c&state=s&iss=https%3A%2F%2Fid.example.test',
    'azwah:///auth/callback?code=c',
    'azwah://auth/signed-out?state=s',
    'exp://192.168.1.5:8081/--/auth/callback?code=c&state=s',
    'exp://192.168.1.5:8081/--/auth/signed-out',
    '/auth/callback?code=c',
    'auth/callback',
    '/--/auth/callback?code=c',
    '--/auth/signed-out',
    'auth/callback/',
  ])('recognises %p as the identity provider handing back', (path) => {
    expect(isAuthReturnPath(path)).toBe(true);
  });

  it.each([
    'azwah://home',
    'azwah://household/abc/members',
    'azwah://household/auth/callback',
    'exp://192.168.1.5:8081/--/auth/sign-in',
    '/auth/sign-in',
    '/auth/callbacks',
    '/welcome',
    '/',
  ])('leaves %p alone', (path) => {
    expect(isAuthReturnPath(path)).toBe(false);
    expect(redirectSystemPath({ path, initial: false })).toBe(path);
    expect(redirectSystemPath({ path, initial: true })).toBe(path);
  });

  it('keeps the router where it is while the app runs, so it never shows "not found"', () => {
    expect(redirectSystemPath({ path: 'azwah://auth/callback?code=c&state=s', initial: false })).toBeNull();
    expect(redirectSystemPath({ path: 'exp://10.0.2.2:8081/--/auth/signed-out', initial: false })).toBeNull();
  });

  it('opens the anchor route when the callback starts the app', () => {
    expect(redirectSystemPath({ path: 'azwah://auth/callback?code=c&state=s', initial: true })).toBe('/');
    expect(redirectSystemPath({ path: '/--/auth/signed-out', initial: true })).toBe('/');
  });

  it('is what app/+native-intent.tsx exports to Expo Router', () => {
    expect(routedRedirect).toBe(redirectSystemPath);
  });
});
