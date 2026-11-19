import { assertContiguous, migrate, MIGRATIONS, pendingMigrations, type Migration } from '../migrations';
import { FakeSqlDatabase } from '../testing/FakeSqlDatabase';

const step = (version: number): Migration => ({ version, description: `step ${version}`, sql: `-- ${version}` });

describe('migrations', () => {
  it('are numbered 1..n with no gaps', () => {
    expect(() => assertContiguous(MIGRATIONS)).not.toThrow();
  });

  it('rejects gaps and duplicates', () => {
    expect(() => assertContiguous([step(1), step(3)])).toThrow(/Migration 2/);
    expect(() => assertContiguous([step(1), step(1)])).toThrow(/Migration 2/);
  });

  it('lists only the migrations after the current version', () => {
    expect(pendingMigrations(1, [step(1), step(2), step(3)]).map((m) => m.version)).toEqual([2, 3]);
    expect(pendingMigrations(3, [step(1), step(2), step(3)])).toEqual([]);
  });

  it('creates settings and draft answers on a fresh database', async () => {
    const db = new FakeSqlDatabase();

    await expect(migrate(db)).resolves.toBe(MIGRATIONS.length);

    expect(db.userVersion).toBe(MIGRATIONS.length);
    expect(db.executed.join('\n')).toMatch(/CREATE TABLE settings/);
    expect(db.executed.join('\n')).toMatch(/CREATE TABLE draft_answers/);
  });

  it('runs each migration in its own transaction and does nothing the second time', async () => {
    const db = new FakeSqlDatabase();
    await migrate(db, [step(1), step(2)]);
    expect(db.transactions).toBe(2);

    await migrate(db, [step(1), step(2)]);

    expect(db.transactions).toBe(2);
    expect(db.executed).toEqual(['-- 1', '-- 2']);
  });

  it('refuses a database written by a newer app version', async () => {
    const db = new FakeSqlDatabase();
    db.userVersion = 99;

    await expect(migrate(db, [step(1)])).rejects.toThrow(/newer than this app supports/);
  });
});
