import { reloadAppAsync } from 'expo';
import { I18nManager } from 'react-native';

import { createSettingsRepository } from '@/storage/settings';
import { FakeSqlDatabase } from '@/storage/testing/FakeSqlDatabase';

import { createI18n } from '../i18n';
import { createLanguageSwitcher, RELOAD_GRACE_MS } from '../languageSwitcher';

jest.mock('expo', () => ({ reloadAppAsync: jest.fn(() => Promise.resolve()) }));

async function setUp() {
  const settings = createSettingsRepository(new FakeSqlDatabase());
  const i18n = await createI18n('en');
  const changeLanguage = jest.spyOn(i18n, 'changeLanguage');
  const onChanged = jest.fn();
  const scheduled: { task: () => Promise<void>; ms: number }[] = [];
  const switchTo = createLanguageSwitcher({
    settings,
    i18n,
    onChanged,
    now: () => 1_000_000,
    later: (task, ms) => scheduled.push({ task, ms }),
  });
  /** What happens when the app is still running once the scheduled time has passed. */
  const runScheduled = () => Promise.all(scheduled.splice(0).map(({ task }) => task()));
  return { settings, changeLanguage, onChanged, switchTo, scheduled, runScheduled };
}

describe('createLanguageSwitcher (the test runtime lays out left to right)', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    jest.spyOn(I18nManager, 'forceRTL').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('stores the language, switches translations, reports it and reloads once for a direction change', async () => {
    const { settings, changeLanguage, onChanged, switchTo } = await setUp();

    await switchTo('ar');

    await expect(settings.get('language')).resolves.toBe('ar');
    expect(changeLanguage).toHaveBeenCalledWith('ar');
    expect(onChanged).toHaveBeenCalledWith('ar');
    expect(reloadAppAsync).toHaveBeenCalledTimes(1);
  });

  it('ignores a second tap while the first switch is still running', async () => {
    const { changeLanguage, switchTo } = await setUp();

    await Promise.all([switchTo('ar'), switchTo('en')]);

    expect(changeLanguage).toHaveBeenCalledTimes(1);
    expect(changeLanguage).toHaveBeenCalledWith('ar');
    expect(reloadAppAsync).toHaveBeenCalledTimes(1);
  });

  it('ignores further switches once a reload has been requested', async () => {
    const { changeLanguage, switchTo } = await setUp();

    await switchTo('ar');
    await switchTo('en');

    expect(changeLanguage).toHaveBeenCalledTimes(1);
    expect(reloadAppAsync).toHaveBeenCalledTimes(1);
  });

  it('accepts switches again when the app is still running after the reload should have happened', async () => {
    const { settings, changeLanguage, onChanged, switchTo, scheduled, runScheduled } = await setUp();

    await switchTo('ar');
    expect(scheduled.map(({ ms }) => ms)).toEqual([RELOAD_GRACE_MS]);
    await runScheduled();
    await switchTo('en');

    expect(changeLanguage).toHaveBeenLastCalledWith('en');
    expect(onChanged).toHaveBeenLastCalledWith('en');
    await expect(settings.get('language')).resolves.toBe('en');
  });

  it('drops the mark of a reload that did not happen, so the next start locks as usual', async () => {
    const { settings, switchTo, runScheduled } = await setUp();

    await switchTo('ar');
    await runScheduled();

    await expect(settings.get('language_changed_at')).resolves.toBeNull();
  });

  it('schedules nothing when no reload was needed', async () => {
    const { switchTo, scheduled } = await setUp();

    await switchTo('en');

    expect(scheduled).toEqual([]);
  });

  it('accepts the next switch when no reload was needed', async () => {
    const { changeLanguage, switchTo } = await setUp();

    await switchTo('en');
    await switchTo('en');

    expect(changeLanguage).toHaveBeenCalledTimes(2);
    expect(reloadAppAsync).not.toHaveBeenCalled();
  });

  it('marks the reload it asks for, so the next start knows it is not a new start (the app lock)', async () => {
    const { settings, switchTo } = await setUp();

    await switchTo('ar');

    await expect(settings.get('language_changed_at')).resolves.toBe('1000000');
  });

  it('leaves no mark when no reload is needed', async () => {
    const { settings, switchTo } = await setUp();

    await switchTo('en');

    await expect(settings.get('language_changed_at')).resolves.toBeNull();
  });
});
