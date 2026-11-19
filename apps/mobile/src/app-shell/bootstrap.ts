import type { i18n as I18n } from 'i18next';
import { I18nManager } from 'react-native';

import type { OidcPlatform } from '@/auth/oidc/nativeOidcPlatform';
import { appMode } from '@/config';
import { applyDirection } from '@/i18n/applyDirection';
import { createI18n } from '@/i18n/i18n';
import { isLanguage, resolveInitialLanguage, type Language } from '@/i18n/languages';
import type { AppActivity } from '@/features/sessions/assessmentEngine';
import type { Connectivity } from '@/network/connectivity';
import { LOCK_TIMES } from '@/security/lockTimes';
import type { FieldCipher } from '@/storage/protection/fieldCipher';
import { createSettingsRepository, type SettingsRepository } from '@/storage/settings';
import type { SqlDatabase } from '@/storage/sql';

import { createServices, type AppServices } from './services';

/** What start-up needs from the device. The app passes `nativeBootDependencies`; tests pass fakes. */
export interface BootDependencies {
  openDatabase: () => Promise<SqlDatabase>;
  deviceLanguageCodes: () => readonly (string | null)[];
  /** The cipher for answers kept on the phone; null keeps them in memory. Never rejects. */
  protection: () => Promise<FieldCipher | null>;
  connectivity: () => Connectivity;
  activity?: () => AppActivity;
  /** The phone's sign-in parts for OpenID Connect; called only when sign-in is configured (services.ts). */
  oidcPlatform?: () => OidcPlatform;
  now?: () => number;
}

export interface BootResult {
  settings: SettingsRepository;
  language: Language;
  i18n: I18n;
  services: AppServices;
  /** This start is the reload that applies a language chosen moments ago, so a restored session is not locked. */
  startedByLanguageChange: boolean;
}

/** Reads and removes the language switch's mark: it counts once, and only within `languageReloadMs` of the choice. */
async function readLanguageChange(settings: SettingsRepository, now: number): Promise<boolean> {
  const mark = await settings.get('language_changed_at');
  if (mark === null) {
    return false;
  }
  await settings.remove('language_changed_at');
  const elapsed = now - Number(mark);
  return Number.isFinite(elapsed) && elapsed >= 0 && elapsed <= LOCK_TIMES.languageReloadMs;
}

/**
 * Opens local storage, settles the language and layout direction, prepares translations, wires the services for
 * the app mode and restores any remembered sign-in.
 */
export async function bootstrap(dependencies: BootDependencies): Promise<BootResult> {
  I18nManager.allowRTL(true);
  const db = await dependencies.openDatabase();
  const settings = createSettingsRepository(db);
  const startedByLanguageChange = await readLanguageChange(settings, (dependencies.now ?? Date.now)());
  const language = resolveInitialLanguage(await settings.get('language'), dependencies.deviceLanguageCodes());
  const attempted = await settings.get('direction_reload_attempted_for');
  // May reload the app (and not return) when the language needs the other layout direction.
  await applyDirection(language, settings, isLanguage(attempted) ? attempted : null);
  const services = createServices(appMode, {
    db,
    settings,
    protection: await dependencies.protection(),
    connectivity: dependencies.connectivity(),
    activity: dependencies.activity?.(),
    oidcPlatform: dependencies.oidcPlatform,
  });
  await services.auth.restore();
  return { settings, language, i18n: await createI18n(language), services, startedByLanguageChange };
}
