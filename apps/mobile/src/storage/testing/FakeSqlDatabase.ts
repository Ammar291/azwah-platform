import type { SqlDatabase, SqlValue } from '../sql';

/** Records schema SQL and keeps the user_version and the settings table in memory. */
export class FakeSqlDatabase implements SqlDatabase {
  userVersion = 0;
  readonly executed: string[] = [];
  readonly settings = new Map<string, string>();
  transactions = 0;

  async execAsync(source: string): Promise<void> {
    const pragma = /PRAGMA user_version = (\d+)/.exec(source);
    if (pragma) {
      this.userVersion = Number(pragma[1]);
      return;
    }
    this.executed.push(source);
  }

  async runAsync(source: string, ...params: SqlValue[]): Promise<unknown> {
    if (source.startsWith('INSERT INTO settings')) {
      this.settings.set(String(params[0]), String(params[1]));
    } else if (source.startsWith('DELETE FROM settings')) {
      this.settings.delete(String(params[0]));
    } else {
      throw new Error(`Unexpected statement: ${source}`);
    }
    return { changes: 1 };
  }

  async getFirstAsync<T>(source: string, ...params: SqlValue[]): Promise<T | null> {
    if (source.startsWith('PRAGMA user_version')) {
      return { user_version: this.userVersion } as T;
    }
    if (source.startsWith('SELECT value FROM settings')) {
      const value = this.settings.get(String(params[0]));
      return (value === undefined ? null : { value }) as T | null;
    }
    throw new Error(`Unexpected query: ${source}`);
  }

  async getAllAsync<T>(source: string): Promise<T[]> {
    throw new Error(`Unexpected query: ${source}`);
  }

  async withTransactionAsync(task: () => Promise<void>): Promise<void> {
    this.transactions += 1;
    await task();
  }
}
