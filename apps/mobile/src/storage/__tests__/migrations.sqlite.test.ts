import { migrate, MIGRATIONS } from '../migrations';
import { createSettingsRepository } from '../settings';
import { NodeSqliteDatabase } from '../testing/NodeSqliteDatabase';

const tables = async (db: NodeSqliteDatabase) =>
  (await db.getAllAsync<{ name: string }>("SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name")).map(
    (row) => row.name,
  );

describe('migrations on real SQLite', () => {
  let db: NodeSqliteDatabase;

  beforeEach(() => {
    db = new NodeSqliteDatabase();
  });

  afterEach(() => db.close());

  const LATEST_TABLES = [
    'assistant_memory',
    'assistant_messages',
    'draft_answers',
    'pending_submissions',
    'score_report_choices',
    'score_report_parts',
    'score_reports',
    'session_copies',
    'settings',
  ];

  it('bring a fresh database to the latest version', async () => {
    await migrate(db);

    expect(await db.getFirstAsync('PRAGMA user_version')).toEqual({ user_version: MIGRATIONS.length });
    expect(await tables(db)).toEqual(LATEST_TABLES);
  });

  it('upgrade a version 1 database without losing settings', async () => {
    await migrate(db, MIGRATIONS.slice(0, 1));
    await createSettingsRepository(db).set('language', 'ar');

    await migrate(db);

    await expect(createSettingsRepository(db).get('language')).resolves.toBe('ar');
    expect(await tables(db)).toEqual(LATEST_TABLES);
  });

  it('upgrade a version 4 conversation: its messages were typed in the chat and have no extras', async () => {
    await migrate(db, MIGRATIONS.slice(0, 4));
    await db.runAsync(
      "INSERT INTO assistant_messages VALUES ('m1', 1, 'user', 'v1:sealed', '2026-11-09T09:00:00.000Z')",
    );

    await migrate(db);

    await expect(db.getAllAsync('SELECT id, channel, extras_cipher FROM assistant_messages')).resolves.toEqual([
      { id: 'm1', channel: 'chat', extras_cipher: null },
    ]);
    await expect(
      db.runAsync(
        "INSERT INTO assistant_messages (id, position, role, text_cipher, created_at, channel) VALUES ('m2', 2, 'user', 'v1:s', 'now', 'fax')",
      ),
    ).rejects.toThrow(/CHECK constraint failed/);
  });

  it('keep at most one memory row', async () => {
    await migrate(db);
    const memory = (id: number) =>
      db.runAsync('INSERT INTO assistant_memory VALUES (?, ?, ?)', id, 'v1:sealed', '2026-11-09T09:00:00.000Z');

    await memory(1);

    await expect(memory(1)).rejects.toThrow(/UNIQUE constraint failed|PRIMARY KEY/);
    await expect(memory(2)).rejects.toThrow(/CHECK constraint failed/);
  });

  it('upgrade a version 2 database without carrying clear-text drafts over', async () => {
    await migrate(db, MIGRATIONS.slice(0, 2));
    await db.runAsync(
      "INSERT INTO draft_answers VALUES ('s1', 'i1', 'o1', 1, '2026-10-14T09:00:00.000Z', 'pending', NULL)",
    );

    await migrate(db);

    await expect(db.getAllAsync('SELECT * FROM draft_answers')).resolves.toEqual([]);
    const columns = (await db.getAllAsync<{ name: string }>("SELECT name FROM pragma_table_info('draft_answers')")).map(
      (column) => column.name,
    );
    expect(columns).toContain('answer_cipher');
    expect(columns).not.toContain('option_id');
  });

  const draft = (state = 'pending') =>
    db.runAsync(
      `INSERT INTO draft_answers VALUES ('s1', 'i1', 'v1:sealed', 1, NULL, '2026-10-14T09:00:00.000Z', '${state}', NULL)`,
    );

  it('allow one draft answer per session and item', async () => {
    await migrate(db);

    await draft();

    await expect(draft()).rejects.toThrow(/UNIQUE constraint failed/);
  });

  it('refuse an unknown sync state', async () => {
    await migrate(db);

    await expect(draft('sent')).rejects.toThrow(/CHECK constraint failed/);
  });

  it('keep one session copy and one queued submission per session', async () => {
    await migrate(db);
    const copy = () =>
      db.runAsync(
        "INSERT INTO session_copies VALUES ('s1', 'v', 'v1:c', 'v1:a', '2026-10-14T09:00:00.000Z', NULL)",
      );
    const queued = () =>
      db.runAsync(
        "INSERT INTO pending_submissions VALUES ('s1', 'k1', '2026-10-14T09:00:00.000Z', 0, NULL)",
      );

    await copy();
    await queued();

    await expect(copy()).rejects.toThrow(/UNIQUE constraint failed/);
    await expect(queued()).rejects.toThrow(/UNIQUE constraint failed/);
  });
});
