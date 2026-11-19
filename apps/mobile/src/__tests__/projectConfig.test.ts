import appJson from '../../app.json';
import pkg from '../../package.json';

type PluginEntry = string | [string, Record<string, unknown>];

describe('native project configuration', () => {
  it('lets the app own the layout direction on iOS and Android', () => {
    // With supportsRTL or forcesRTL set, expo-localization's iOS module calls forceRTL(device language is RTL) every
    // time it starts, which undoes the language chosen in the app (docs/decisions.md D-15).
    const plugins = appJson.expo.plugins as unknown as PluginEntry[];
    const localization = plugins.find((plugin) => (Array.isArray(plugin) ? plugin[0] : plugin) === 'expo-localization');

    expect(localization).toBeDefined();
    const options = Array.isArray(localization) ? localization[1] : {};
    expect(options).not.toHaveProperty('supportsRTL');
    expect(options).not.toHaveProperty('forcesRTL');
  });

  it('keeps the local database out of Android cloud backups', () => {
    // azwah.db will hold draft answers, which are sensitive personal information (docs/security-model.md).
    expect(appJson.expo.android.allowBackup).toBe(false);
  });

  it('asks for Face ID only to unlock the app, and never offers to receive shared content', () => {
    const plugins = appJson.expo.plugins as unknown as PluginEntry[];
    const options = (name: string) => {
      const entry = plugins.find((plugin) => (Array.isArray(plugin) ? plugin[0] : plugin) === name);
      expect([name, entry]).not.toEqual([name, undefined]);
      return Array.isArray(entry) ? entry[1] : {};
    };

    expect(options('expo-local-authentication').faceIDPermission).toMatch(/unlock/);
    // The data download sends a file out through the share sheet; the share extension and intent filters stay off.
    const sharing = options('expo-sharing') as { ios?: { enabled?: boolean }; android?: { enabled?: boolean } };
    expect(sharing.ios?.enabled ?? false).toBe(false);
    expect(sharing.android?.enabled ?? false).toBe(false);
  });

  it('asks Android 13 and later to keep no Recents screenshot of the app', () => {
    // The JavaScript privacy cover comes too late for Android's Recents snapshot (plugins/withRecentsScreenshotDisabled).
    const plugins = appJson.expo.plugins as unknown as PluginEntry[];
    const names = plugins.map((plugin) => (Array.isArray(plugin) ? plugin[0] : plugin));

    expect(names).toContain('./plugins/withRecentsScreenshotDisabled');
  });

  it('keeps npm run android and ios on Expo Go, with native builds on their own scripts', () => {
    // `expo prebuild` and `expo run` rewrite these two scripts; keep them as documented in the README.
    const scripts = pkg.scripts as Record<string, string>;

    expect(scripts.android).toBe('expo start --android');
    expect(scripts.ios).toBe('expo start --ios');
    expect(scripts['android:dev']).toBe('expo run:android');
    expect(scripts['ios:dev']).toBe('expo run:ios');
  });
});
