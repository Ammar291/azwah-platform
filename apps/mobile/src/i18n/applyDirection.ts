import { reloadAppAsync } from 'expo';
import Constants, { ExecutionEnvironment } from 'expo-constants';
import { I18nManager } from 'react-native';

import type { SettingsRepository } from '@/storage/settings';

import { planDirection, type DirectionPlan } from './direction';
import type { Language } from './languages';

/**
 * Expo Go lays out every project left to right and ignores a reload asked for from JavaScript (`reloadAppAsync`
 * resolves and nothing happens), so there the direction cannot change (D-15).
 */
function directionCanChange(): boolean {
  return Constants.executionEnvironment !== ExecutionEnvironment.StoreClient;
}

/**
 * Makes the layout direction match the language. A change needs an app reload; the language we reload for is
 * stored first, so a reload that does not take effect is not repeated (see planDirection).
 */
export async function applyDirection(
  language: Language,
  settings: SettingsRepository,
  attemptedFor: Language | null,
): Promise<DirectionPlan> {
  const plan = planDirection(language, I18nManager.isRTL, attemptedFor, directionCanChange());
  if (plan.kind === 'reload') {
    await settings.set('direction_reload_attempted_for', language);
    I18nManager.forceRTL(plan.rtl);
    await reloadAppAsync('Apply the layout direction for the chosen language');
  } else if (plan.kind === 'ok' && attemptedFor !== null) {
    await settings.remove('direction_reload_attempted_for');
  }
  return plan;
}
