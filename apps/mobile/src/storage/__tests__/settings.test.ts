import { createSettingsRepository } from '../settings';
import { FakeSqlDatabase } from '../testing/FakeSqlDatabase';

describe('settings repository', () => {
  it('returns null for a setting that was never stored', async () => {
    const settings = createSettingsRepository(new FakeSqlDatabase());

    await expect(settings.get('language')).resolves.toBeNull();
  });

  it('stores, overwrites and removes a value', async () => {
    const settings = createSettingsRepository(new FakeSqlDatabase());

    await settings.set('language', 'ar');
    await expect(settings.get('language')).resolves.toBe('ar');

    await settings.set('language', 'en');
    await expect(settings.get('language')).resolves.toBe('en');

    await settings.remove('language');
    await expect(settings.get('language')).resolves.toBeNull();
  });
});
