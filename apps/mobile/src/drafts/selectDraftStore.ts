import type { FieldCipher } from '@/storage/protection/fieldCipher';
import type { SqlDatabase } from '@/storage/sql';

import { createEncryptedSqliteDraftStore } from './encryptedSqliteDraftStore';
import { createMemoryDraftStore } from './memoryDraftStore';
import type { DraftStore } from './types';

/**
 * Answers are sensitive. They are kept on the phone only sealed with a key from the platform keystore (decision M-35);
 * without one (`protection` null: no keystore, no working cipher) they stay in memory and the app says so. The same
 * rule applies in both app modes, so development exercises the protected path.
 */
export function createDraftStore(db: SqlDatabase, protection: FieldCipher | null): DraftStore {
  return protection ? createEncryptedSqliteDraftStore(db, protection) : createMemoryDraftStore();
}
