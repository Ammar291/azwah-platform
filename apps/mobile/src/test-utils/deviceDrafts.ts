import { createEncryptedSqliteDraftStore } from '@/drafts/encryptedSqliteDraftStore';
import type { DraftStore } from '@/drafts/types';
import { migrate } from '@/storage/migrations';
import { createFieldCipher } from '@/storage/protection/fieldCipher';
import type { SecretStore } from '@/storage/protection/types';
import { createMemorySecretStore } from '@/storage/testing/memorySecretStore';
import { createNodeAesGcm } from '@/storage/testing/nodeAesGcm';
import { NodeSqliteDatabase } from '@/storage/testing/NodeSqliteDatabase';

export interface DeviceDrafts {
  db: NodeSqliteDatabase;
  secrets: SecretStore;
  drafts: DraftStore;
  /** The same database and keystore opened again, as after the app was closed and reopened. */
  restart: () => DeviceDrafts;
  close: () => void;
}

function open(db: NodeSqliteDatabase, secrets: SecretStore): DeviceDrafts {
  const drafts = createEncryptedSqliteDraftStore(db, createFieldCipher({ aes: createNodeAesGcm(), secrets }));
  return { db, secrets, drafts, restart: () => open(db, secrets), close: () => db.close() };
}

/** The phone's real draft store: encrypted SQLite on node:sqlite, sealed with Node's AES-256-GCM. */
export async function createDeviceDrafts(): Promise<DeviceDrafts> {
  const db = new NodeSqliteDatabase();
  await migrate(db);
  return open(db, createMemorySecretStore());
}
