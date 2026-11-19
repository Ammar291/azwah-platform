import type * as LocalAuthentication from 'expo-local-authentication';

import type { DeviceAuthentication, DeviceAuthenticationOutcome } from './deviceAuthentication';

/** Required when first used, not imported: a build without the module then offers only "Sign in again". */
function load(): typeof LocalAuthentication {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- loaded lazily on purpose (see above)
  return require('expo-local-authentication') as typeof LocalAuthentication;
}

const CANCELLED: readonly LocalAuthentication.LocalAuthenticationError[] = ['user_cancel', 'system_cancel', 'app_cancel'];
const UNAVAILABLE: readonly LocalAuthentication.LocalAuthenticationError[] = [
  'not_enrolled',
  'not_available',
  'passcode_not_set',
];

/**
 * expo-local-authentication: Face ID, Touch ID or the Android biometric prompt, falling back to the device passcode
 * (`disableDeviceFallback: false`). Available when biometrics are enrolled or the phone has a passcode at all.
 */
export function createExpoDeviceAuthentication(): DeviceAuthentication {
  return {
    async isAvailable() {
      try {
        const auth = load();
        if ((await auth.hasHardwareAsync()) && (await auth.isEnrolledAsync())) {
          return true;
        }
        return (await auth.getEnrolledLevelAsync()) !== auth.SecurityLevel.NONE;
      } catch {
        return false;
      }
    },

    async authenticate({ promptMessage, cancelLabel }): Promise<DeviceAuthenticationOutcome> {
      try {
        const result = await load().authenticateAsync({ promptMessage, cancelLabel, disableDeviceFallback: false });
        if (result.success) return 'success';
        if (CANCELLED.includes(result.error)) return 'cancelled';
        if (UNAVAILABLE.includes(result.error)) return 'unavailable';
        return 'failed';
      } catch {
        return 'failed';
      }
    },
  };
}
