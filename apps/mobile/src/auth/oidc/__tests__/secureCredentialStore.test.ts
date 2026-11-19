import { createMemorySecretStore } from '@/storage/testing/memorySecretStore';

import { createExpoCredentialStore, createSecureCredentialStore } from '../secureCredentialStore';

jest.mock('expo-secure-store', () => ({
  WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'when-unlocked-this-device-only',
  isAvailableAsync: jest.fn(async () => true),
  getItemAsync: jest.fn(async () => null),
  setItemAsync: jest.fn(async () => undefined),
  deleteItemAsync: jest.fn(async () => undefined),
}));

describe('secure credential store', () => {
  it('keeps the refresh token and subject under their own keystore entries, and clears both', async () => {
    const secrets = createMemorySecretStore();
    const store = createSecureCredentialStore(secrets);

    await store.write('refresh_token', 'refresh-1');
    await store.write('subject', 'dev-participant-lead');

    await expect(store.read('refresh_token')).resolves.toBe('refresh-1');
    await expect(secrets.get('azwah.oidc.refresh_token.v1')).resolves.toBe('refresh-1');
    await expect(secrets.get('azwah.oidc.subject.v1')).resolves.toBe('dev-participant-lead');

    await store.clear();

    await expect(store.read('refresh_token')).resolves.toBeNull();
    await expect(store.read('subject')).resolves.toBeNull();
  });

  it('leaves the drafts key alone when it clears', async () => {
    const secrets = createMemorySecretStore();
    await secrets.set('azwah.drafts.key.v1', 'draft-key');

    await createSecureCredentialStore(secrets).clear();

    await expect(secrets.get('azwah.drafts.key.v1')).resolves.toBe('draft-key');
  });

  it('uses expo-secure-store readable only while unlocked and never moved off this device', async () => {
    const secureStore = jest.requireMock<{
      setItemAsync: jest.Mock;
      getItemAsync: jest.Mock;
      deleteItemAsync: jest.Mock;
    }>('expo-secure-store');
    const store = createExpoCredentialStore();

    await store.write('refresh_token', 'refresh-1');
    await store.read('subject');
    await store.clear();

    const options = { keychainAccessible: 'when-unlocked-this-device-only' };
    expect(secureStore.setItemAsync).toHaveBeenCalledWith('azwah.oidc.refresh_token.v1', 'refresh-1', options);
    expect(secureStore.getItemAsync).toHaveBeenCalledWith('azwah.oidc.subject.v1', options);
    expect(secureStore.deleteItemAsync).toHaveBeenCalledWith('azwah.oidc.refresh_token.v1', options);
    expect(secureStore.deleteItemAsync).toHaveBeenCalledWith('azwah.oidc.subject.v1', options);
  });
});
