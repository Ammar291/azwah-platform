import * as SQLite from 'expo-sqlite';

import { migrate } from './migrations';

export const DATABASE_NAME = 'azwah.db';

export async function openAppDatabase(): Promise<SQLite.SQLiteDatabase> {
  const db = await SQLite.openDatabaseAsync(DATABASE_NAME);
  // secure_delete overwrites deleted rows, so removed answers do not linger in free pages (decision M-36).
  await db.execAsync('PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; PRAGMA secure_delete = ON;');
  await migrate(db);
  return db;
}
