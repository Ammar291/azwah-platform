import { migrate } from '@/storage/migrations';
import { createFieldCipher, DRAFT_KEY_NAME, type FieldCipher } from '@/storage/protection/fieldCipher';
import { createSettingsRepository } from '@/storage/settings';
import { createMemorySecretStore } from '@/storage/testing/memorySecretStore';
import { createNodeAesGcm } from '@/storage/testing/nodeAesGcm';
import { NodeSqliteDatabase } from '@/storage/testing/NodeSqliteDatabase';
import { createFakeConnectivity } from '@/test-utils/fakeConnectivity';

import { createServices } from '../services';

/** A phone in development fixtures mode; `start` is one run of the app over the same database and keystore. */
async function fixturePhone() {
  const db = new NodeSqliteDatabase();
  await migrate(db);
  const settings = createSettingsRepository(db);
  const secrets = createMemorySecretStore();
  const start = (protection: FieldCipher | null = createFieldCipher({ aes: createNodeAesGcm(), secrets })) =>
    createServices('development_fixtures', { db, settings, protection, connectivity: createFakeConnectivity() });
  return { settings, secrets, start };
}

async function signedIn(services: ReturnType<Awaited<ReturnType<typeof fixturePhone>>['start']>) {
  await services.auth.restore();
  await services.auth.signIn({ method: 'development_persona', personaCode: 'new_participant' });
  return services;
}

describe('services in development fixtures mode', () => {
  it('seals the fixture worlds with a key of their own, so signing out and starting again keeps them', async () => {
    const phone = await fixturePhone();
    const first = await signedIn(phone.start());
    const { registeredAt } = await first.api.registerMe();

    const kept = (await phone.settings.get('dev_fixture_worlds')) ?? '';
    expect(kept).toMatch(/^v1:/);
    expect(kept).not.toContain('new_participant');

    // Signing out deletes the answers kept on the phone and their key; the fixture worlds keep theirs.
    await first.drafts.clearAll();
    await first.auth.signOut();
    expect(await phone.secrets.get(DRAFT_KEY_NAME)).toBeNull();

    const again = await signedIn(phone.start());
    expect((await again.api.getMe()).registeredAt).toBe(registeredAt);
  });

  it('keeps nothing on the phone when there is no keystore, as for drafts', async () => {
    const phone = await fixturePhone();
    const services = await signedIn(phone.start(null));

    await services.api.registerMe();

    expect(services.drafts.persistence).toBe('memory');
    expect(await phone.settings.get('dev_fixture_worlds')).toBeNull();
  });
});
