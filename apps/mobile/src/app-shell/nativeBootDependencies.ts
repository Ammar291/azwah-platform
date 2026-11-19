import { getLocales } from 'expo-localization';

import { createNativeOidcPlatform } from '@/auth/oidc/nativeOidcPlatform';
import { createAppStateActivity } from '@/features/sessions/assessmentEngine';
import { createExpoConnectivity, createUnknownConnectivity } from '@/network/connectivity';
import { openAppDatabase } from '@/storage/database';
import { createExpoAesGcm, createExpoSecretStore } from '@/storage/protection/expoProtection';
import { resolveDeviceProtection } from '@/storage/protection/resolveDeviceProtection';

import type { BootDependencies } from './bootstrap';

/**
 * The real device: Expo SQLite, the device's languages, the keystore and AES-GCM, the network state, and the system
 * browser for sign-in.
 */
export const nativeBootDependencies: BootDependencies = {
  openDatabase: openAppDatabase,
  deviceLanguageCodes: () => getLocales().map((locale) => locale.languageCode),
  protection: () => resolveDeviceProtection({ aes: createExpoAesGcm, secrets: createExpoSecretStore }),
  connectivity: () => {
    try {
      return createExpoConnectivity();
    } catch {
      return createUnknownConnectivity();
    }
  },
  activity: createAppStateActivity,
  oidcPlatform: createNativeOidcPlatform,
};
