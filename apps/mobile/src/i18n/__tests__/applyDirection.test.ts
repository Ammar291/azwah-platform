import { reloadAppAsync } from 'expo';
import { I18nManager } from 'react-native';

import { createSettingsRepository } from '@/storage/settings';
import { FakeSqlDatabase } from '@/storage/testing/FakeSqlDatabase';

import { applyDirection } from '../applyDirection';

jest.mock('expo', () => ({ reloadAppAsync: jest.fn(() => Promise.resolve()) }));

jest.mock('expo-constants', () => ({
  __esModule: true,
  default: { executionEnvironment: 'bare' },
  ExecutionEnvironment: { Bare: 'bare', Standalone: 'standalone', StoreClient: 'storeClient' },
}));
const mockConstants = jest.requireMock<{ default: { executionEnvironment: string } }>('expo-constants').default;

describe('applyDirection (the test runtime lays out left to right)', () => {
  let forceRTL: jest.SpyInstance;

  beforeEach(() => {
    jest.clearAllMocks();
    mockConstants.executionEnvironment = 'bare';
    forceRTL = jest.spyOn(I18nManager, 'forceRTL').mockImplementation(() => undefined);
  });

  it('remembers the attempt, switches to RTL and reloads for Arabic', async () => {
    const settings = createSettingsRepository(new FakeSqlDatabase());

    const plan = await applyDirection('ar', settings, null);

    expect(plan).toEqual({ kind: 'reload', rtl: true });
    expect(forceRTL).toHaveBeenCalledWith(true);
    expect(reloadAppAsync).toHaveBeenCalledTimes(1);
    await expect(settings.get('direction_reload_attempted_for')).resolves.toBe('ar');
  });

  it('does not reload again when the last reload for Arabic did not take effect', async () => {
    const settings = createSettingsRepository(new FakeSqlDatabase());

    const plan = await applyDirection('ar', settings, 'ar');

    expect(plan).toEqual({ kind: 'give_up', rtl: true });
    expect(forceRTL).not.toHaveBeenCalled();
    expect(reloadAppAsync).not.toHaveBeenCalled();
  });

  it('neither forces the direction nor asks for a reload in Expo Go, which ignores both', async () => {
    mockConstants.executionEnvironment = 'storeClient';
    const settings = createSettingsRepository(new FakeSqlDatabase());

    const plan = await applyDirection('ar', settings, null);

    expect(plan).toEqual({ kind: 'give_up', rtl: true });
    expect(forceRTL).not.toHaveBeenCalled();
    expect(reloadAppAsync).not.toHaveBeenCalled();
    await expect(settings.get('direction_reload_attempted_for')).resolves.toBeNull();
  });

  it('clears the marker once the direction is right', async () => {
    const settings = createSettingsRepository(new FakeSqlDatabase());
    await settings.set('direction_reload_attempted_for', 'en');

    const plan = await applyDirection('en', settings, 'en');

    expect(plan).toEqual({ kind: 'ok' });
    await expect(settings.get('direction_reload_attempted_for')).resolves.toBeNull();
  });
});
