import * as LocalAuthentication from 'expo-local-authentication';

import { createExpoDeviceAuthentication } from '../expoDeviceAuthentication';

jest.mock('expo-local-authentication', () => ({
  SecurityLevel: { NONE: 0, SECRET: 1, BIOMETRIC_WEAK: 2, BIOMETRIC_STRONG: 3 },
  hasHardwareAsync: jest.fn(),
  isEnrolledAsync: jest.fn(),
  getEnrolledLevelAsync: jest.fn(),
  authenticateAsync: jest.fn(),
}));

const native = jest.mocked(LocalAuthentication);
const PROMPT = { promptMessage: 'Unlock Azwah', cancelLabel: 'Cancel' };

beforeEach(() => jest.resetAllMocks());

describe('the phone lock through expo-local-authentication', () => {
  it('is available with enrolled biometrics', async () => {
    native.hasHardwareAsync.mockResolvedValue(true);
    native.isEnrolledAsync.mockResolvedValue(true);

    await expect(createExpoDeviceAuthentication().isAvailable()).resolves.toBe(true);
  });

  it('is available with only a passcode', async () => {
    native.hasHardwareAsync.mockResolvedValue(false);
    native.isEnrolledAsync.mockResolvedValue(false);
    native.getEnrolledLevelAsync.mockResolvedValue(LocalAuthentication.SecurityLevel.SECRET);

    await expect(createExpoDeviceAuthentication().isAvailable()).resolves.toBe(true);
  });

  it('is not available on a phone with no screen lock, or when the module fails', async () => {
    native.hasHardwareAsync.mockResolvedValue(true);
    native.isEnrolledAsync.mockResolvedValue(false);
    native.getEnrolledLevelAsync.mockResolvedValue(LocalAuthentication.SecurityLevel.NONE);
    await expect(createExpoDeviceAuthentication().isAvailable()).resolves.toBe(false);

    native.hasHardwareAsync.mockRejectedValue(new Error('no native module'));
    await expect(createExpoDeviceAuthentication().isAvailable()).resolves.toBe(false);
  });

  it('asks with the translated prompt and lets the passcode stand in for biometrics', async () => {
    native.authenticateAsync.mockResolvedValue({ success: true });

    await expect(createExpoDeviceAuthentication().authenticate(PROMPT)).resolves.toBe('success');

    expect(native.authenticateAsync).toHaveBeenCalledWith({ ...PROMPT, disableDeviceFallback: false });
  });

  it.each([
    ['user_cancel', 'cancelled'],
    ['system_cancel', 'cancelled'],
    ['app_cancel', 'cancelled'],
    ['not_enrolled', 'unavailable'],
    ['passcode_not_set', 'unavailable'],
    ['lockout', 'failed'],
    ['authentication_failed', 'failed'],
  ] as const)('reads %s as %s', async (error, outcome) => {
    native.authenticateAsync.mockResolvedValue({ success: false, error });

    await expect(createExpoDeviceAuthentication().authenticate(PROMPT)).resolves.toBe(outcome);
  });

  it('reads a thrown error as a failure', async () => {
    native.authenticateAsync.mockRejectedValue(new Error('no activity'));

    await expect(createExpoDeviceAuthentication().authenticate(PROMPT)).resolves.toBe('failed');
  });
});
