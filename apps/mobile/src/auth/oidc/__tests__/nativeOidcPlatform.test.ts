import { createNativeOidcPlatform } from '../nativeOidcPlatform';

jest.mock('expo-web-browser', () => ({ openAuthSessionAsync: jest.fn() }));
jest.mock('expo-linking', () => ({ createURL: jest.fn((path: string) => `exp://192.168.1.5:8081/--/${path}`) }));
jest.mock('expo-crypto', () => ({
  CryptoDigestAlgorithm: { SHA256: 'SHA-256' },
  getRandomBytes: jest.fn((count: number) => new Uint8Array(count).fill(7)),
  digest: jest.fn(async () => new Uint8Array([1, 2, 3]).buffer),
}));
jest.mock('expo-secure-store', () => ({
  WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'when-unlocked-this-device-only',
  getItemAsync: jest.fn(),
  setItemAsync: jest.fn(),
  deleteItemAsync: jest.fn(),
}));

const webBrowser = jest.requireMock<{ openAuthSessionAsync: jest.Mock }>('expo-web-browser');
const linking = jest.requireMock<{ createURL: jest.Mock }>('expo-linking');
const crypto = jest.requireMock<{ digest: jest.Mock; getRandomBytes: jest.Mock }>('expo-crypto');

describe('native OpenID Connect platform', () => {
  it('comes back to the app on the auth/callback deep link of this build', () => {
    expect(createNativeOidcPlatform().browser.redirectUri).toBe('exp://192.168.1.5:8081/--/auth/callback');
    expect(linking.createURL).toHaveBeenCalledWith('auth/callback');
  });

  it('comes back from sign-out on the auth/signed-out deep link, and opens the end-session page expecting it', async () => {
    webBrowser.openAuthSessionAsync.mockResolvedValueOnce({ type: 'success', url: 'exp://x/--/auth/signed-out?state=1' });
    const { browser } = createNativeOidcPlatform();

    expect(browser.signedOutUri).toBe('exp://192.168.1.5:8081/--/auth/signed-out');
    await expect(browser.openAuthSession('https://id.example.test/end-session?x=1', browser.signedOutUri)).resolves.toEqual({
      type: 'success',
      url: 'exp://x/--/auth/signed-out?state=1',
    });
    expect(webBrowser.openAuthSessionAsync).toHaveBeenCalledWith(
      'https://id.example.test/end-session?x=1',
      'exp://192.168.1.5:8081/--/auth/signed-out',
      { preferEphemeralSession: true },
    );
  });

  it('opens an ephemeral system-browser session and returns the callback URL', async () => {
    webBrowser.openAuthSessionAsync.mockResolvedValueOnce({ type: 'success', url: 'exp://x/--/auth/callback?code=1' });
    const { browser } = createNativeOidcPlatform();

    await expect(browser.openAuthSession('https://id.example.test/authorize?x=1')).resolves.toEqual({
      type: 'success',
      url: 'exp://x/--/auth/callback?code=1',
    });
    expect(webBrowser.openAuthSessionAsync).toHaveBeenCalledWith(
      'https://id.example.test/authorize?x=1',
      'exp://192.168.1.5:8081/--/auth/callback',
      { preferEphemeralSession: true },
    );
  });

  it.each(['cancel', 'dismiss', 'opened', 'locked'])('treats a %s result as cancelled', async (type) => {
    webBrowser.openAuthSessionAsync.mockResolvedValueOnce({ type });

    await expect(createNativeOidcPlatform().browser.openAuthSession('https://id.example.test/authorize')).resolves.toEqual({
      type: 'cancel',
    });
  });

  it('takes random bytes and SHA-256 from expo-crypto', async () => {
    const platform = createNativeOidcPlatform();

    expect(Array.from(await platform.random(4))).toEqual([7, 7, 7, 7]);
    expect(crypto.getRandomBytes).toHaveBeenCalledWith(4);
    expect(Array.from(await platform.sha256(new Uint8Array([9])))).toEqual([1, 2, 3]);
    expect(crypto.digest).toHaveBeenCalledWith('SHA-256', new Uint8Array([9]));
  });
});
