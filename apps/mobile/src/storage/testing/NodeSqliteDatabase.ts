import type { SqlDatabase, SqlValue } from '../sql';

interface StatementSync {
  run(...params: SqlValue[]): unknown;
  get(...params: SqlValue[]): unknown;
  all(...params: SqlValue[]): unknown[];
}

interface DatabaseSync {
  exec(source: string): void;
  prepare(source: string): StatementSync;
  close(): void;
}

/**
 * A real SQLite database in memory, from Node's built-in `node:sqlite`, behind the app's SqlDatabase interface.
 * Tests use it to run the app's actual SQL without Expo's native module.
 */
export class NodeSqliteDatabase implements SqlDatabase {
  private readonly db: DatabaseSync;

  constructor() {
    // Required lazily so the module loads only in tests that use it. An import would need Node's types, which the
    // app deliberately does not include.
    // eslint-disable-next-line @typescript-eslint/no-require-imports -- Node built-in, tests only (see above)
    const { DatabaseSync: Database } = require('node:sqlite') as { DatabaseSync: new (path: string) => DatabaseSync };
    this.db = new Database(':memory:');
    this.db.exec('PRAGMA foreign_keys = ON;');
  }

  async execAsync(source: string): Promise<void> {
    this.db.exec(source);
  }

  async runAsync(source: string, ...params: SqlValue[]): Promise<unknown> {
    return this.db.prepare(source).run(...params);
  }

  async getFirstAsync<T>(source: string, ...params: SqlValue[]): Promise<T | null> {
    const row = this.db.prepare(source).get(...params);
    return row === undefined ? null : ({ ...(row as object) } as T);
  }

  async getAllAsync<T>(source: string, ...params: SqlValue[]): Promise<T[]> {
    return this.db.prepare(source).all(...params).map((row) => ({ ...(row as object) }) as T);
  }

  async withTransactionAsync(task: () => Promise<void>): Promise<void> {
    this.db.exec('BEGIN');
    try {
      await task();
      this.db.exec('COMMIT');
    } catch (error) {
      this.db.exec('ROLLBACK');
      throw error;
    }
  }

  close(): void {
    this.db.close();
  }
}
