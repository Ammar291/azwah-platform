import { reloadAppAsync } from 'expo';
import { I18nManager } from 'react-native';

import { createFieldCipher } from '@/storage/protection/fieldCipher';
import { createSettingsRepository } from '@/storage/settings';
import { FakeSqlDatabase } from '@/storage/testing/FakeSqlDatabase';
import { createMemorySecretStore } from '@/storage/testing/memorySecretStore';
import { createNodeAesGcm } from '@/storage/testing/nodeAesGcm';
import { createFakeConnectivity } from '@/test-utils/fakeConnectivity';

import { bootstrap, type BootDependencies } from '../bootstrap';

jest.mock('expo', () => ({ reloadAppAsync: jest.fn(() => Promise.resolve()) }));

const NOW = 5_000_000;

function dependencies(
  db: FakeSqlDatabase,
  deviceLanguages: (string | null)[],
  protection: BootDependencies['protection'] = async () => null,
): BootDependencies {
  return {
    openDatabase: async () => db,
    deviceLanguageCodes: () => deviceLanguages,
    protection,
    connectivity: () => createFakeConnectivity(),
    now: () => NOW,
  };
}

describe('bootstrap', () => {
  let forceRTL: jest.SpyInstance;

  beforeEach(() => {
    jest.clearAllMocks();
    forceRTL = jest.spyOn(I18nManager, 'forceRTL').mockImplementation(() => undefined);
    jest.spyOn(I18nManager, 'allowRTL').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('starts in English on an English device without reloading', async () => {
    jest.replaceProperty(I18nManager, 'isRTL', false);

    const boot = await bootstrap(dependencies(new FakeSqlDatabase(), ['en']));

    expect(boot.language).toBe('en');
    expect(boot.i18n.t('app.name')).toBe('Azwah');
    expect(reloadAppAsync).not.toHaveBeenCalled();
  });

  it('never reloads in a loop when the direction change does not take effect', async () => {
    const db = new FakeSqlDatabase();
    jest.replaceProperty(I18nManager, 'isRTL', false);

    // First boot on an Arabic device: the layout is left to right, so it asks for one reload.
    await bootstrap(dependencies(db, ['ar']));
    expect(forceRTL).toHaveBeenCalledWith(true);
    expect(reloadAppAsync).toHaveBeenCalledTimes(1);

    // Second boot: the reload did not change the direction. It must carry on in Arabic without reloading again.
    const second = await bootstrap(dependencies(db, ['ar']));
    expect(second.language).toBe('ar');
    expect(reloadAppAsync).toHaveBeenCalledTimes(1);
  });

  it('clears the reload marker once the direction is right', async () => {
    const db = new FakeSqlDatabase();
    jest.replaceProperty(I18nManager, 'isRTL', false);
    await bootstrap(dependencies(db, ['ar']));

    jest.replaceProperty(I18nManager, 'isRTL', true);
    await bootstrap(dependencies(db, ['ar']));

    await expect(createSettingsRepository(db).get('direction_reload_attempted_for')).resolves.toBeNull();
  });

  it('prefers the stored language over the device language', async () => {
    const db = new FakeSqlDatabase();
    await createSettingsRepository(db).set('language', 'en');
    jest.replaceProperty(I18nManager, 'isRTL', false);

    const boot = await bootstrap(dependencies(db, ['ar']));

    expect(boot.language).toBe('en');
    expect(reloadAppAsync).not.toHaveBeenCalled();
  });

  it('keeps answers on the phone, encrypted, only when the keystore and cipher work', async () => {
    jest.replaceProperty(I18nManager, 'isRTL', false);
    const cipher = createFieldCipher({ aes: createNodeAesGcm(), secrets: createMemorySecretStore() });

    const protectedBoot = await bootstrap(dependencies(new FakeSqlDatabase(), ['en'], async () => cipher));
    const unprotectedBoot = await bootstrap(dependencies(new FakeSqlDatabase(), ['en'], async () => null));

    expect(protectedBoot.services.drafts).toMatchObject({ persistence: 'device', encryptedAtRest: true });
    expect(unprotectedBoot.services.drafts).toMatchObject({ persistence: 'memory', encryptedAtRest: false });
  });

  it('knows the start that applies a language chosen moments ago, and uses the mark once', async () => {
    const db = new FakeSqlDatabase();
    jest.replaceProperty(I18nManager, 'isRTL', true);
    await createSettingsRepository(db).set('language', 'ar');
    await createSettingsRepository(db).set('language_changed_at', String(NOW - 5_000));

    const reloaded = await bootstrap(dependencies(db, ['en']));
    const next = await bootstrap(dependencies(db, ['en']));

    expect(reloaded.startedByLanguageChange).toBe(true);
    expect(next.startedByLanguageChange).toBe(false);
    await expect(createSettingsRepository(db).get('language_changed_at')).resolves.toBeNull();
  });

  it('treats an old, future or unreadable mark as an ordinary start', async () => {
    jest.replaceProperty(I18nManager, 'isRTL', false);
    const marks = [String(NOW - 31_000), String(NOW + 60_000), 'soon'];

    for (const mark of marks) {
      const db = new FakeSqlDatabase();
      await createSettingsRepository(db).set('language_changed_at', mark);
      expect([mark, (await bootstrap(dependencies(db, ['en']))).startedByLanguageChange]).toEqual([mark, false]);
    }
  });
});
