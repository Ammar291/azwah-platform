import type { SqlDatabase } from './sql';

export interface Migration {
  version: number;
  description: string;
  sql: string;
}

/**
 * Append only. Never edit a migration that has shipped; add the next version instead.
 * Answers are sensitive: they are written here only encrypted (src/storage/protection), and only when the platform
 * keystore works; otherwise they stay in memory (src/drafts/selectDraftStore.ts).
 */
export const MIGRATIONS: readonly Migration[] = [
  {
    version: 1,
    description: 'Settings and an empty drafts table',
    sql: `
      CREATE TABLE settings (
        key TEXT PRIMARY KEY NOT NULL,
        value TEXT NOT NULL
      );
      CREATE TABLE drafts (
        id TEXT PRIMARY KEY NOT NULL,
        kind TEXT NOT NULL,
        payload TEXT NOT NULL,
        updated_at TEXT NOT NULL
      );
    `,
  },
  {
    version: 2,
    description: 'Draft answers keyed by session and item, replacing the unused drafts placeholder',
    // `drafts` was never written (D-16), so dropping it loses nothing.
    sql: `
      DROP TABLE drafts;
      CREATE TABLE draft_answers (
        session_id TEXT NOT NULL,
        item_id TEXT NOT NULL,
        option_id TEXT NOT NULL,
        revision INTEGER NOT NULL CHECK (revision >= 1),
        updated_at TEXT NOT NULL,
        sync_state TEXT NOT NULL CHECK (sync_state IN ('pending', 'synced')),
        synced_at TEXT,
        PRIMARY KEY (session_id, item_id)
      ) WITHOUT ROWID;
      CREATE INDEX ix_draft_answers_pending ON draft_answers (session_id) WHERE sync_state = 'pending';
    `,
  },
  {
    version: 3,
    description:
      'Encrypted draft answers with the server change they were based on, encrypted session copies, queued submissions',
    // Version 2 rows held only synthetic fixture data (D-65) in clear text; they are dropped, never carried over.
    // Answers and copies are sealed by src/storage/protection (decision M-35): *_cipher columns hold "v1:" envelopes.
    sql: `
      DROP TABLE draft_answers;
      CREATE TABLE draft_answers (
        session_id TEXT NOT NULL,
        item_id TEXT NOT NULL,
        answer_cipher TEXT NOT NULL,
        revision INTEGER NOT NULL CHECK (revision >= 1),
        base_modified_at TEXT,
        updated_at TEXT NOT NULL,
        sync_state TEXT NOT NULL CHECK (sync_state IN ('pending', 'synced')),
        synced_at TEXT,
        PRIMARY KEY (session_id, item_id)
      ) WITHOUT ROWID;
      CREATE INDEX ix_draft_answers_pending ON draft_answers (session_id) WHERE sync_state = 'pending';
      CREATE TABLE session_copies (
        session_id TEXT PRIMARY KEY NOT NULL,
        instrument_version_id TEXT NOT NULL,
        content_cipher TEXT NOT NULL,
        answers_cipher TEXT NOT NULL,
        saved_at TEXT NOT NULL,
        last_synced_at TEXT
      ) WITHOUT ROWID;
      CREATE TABLE pending_submissions (
        session_id TEXT PRIMARY KEY NOT NULL,
        idempotency_key TEXT NOT NULL,
        requested_at TEXT NOT NULL,
        attempts INTEGER NOT NULL DEFAULT 0 CHECK (attempts >= 0),
        last_attempt_at TEXT
      ) WITHOUT ROWID;
    `,
  },
  {
    version: 4,
    description: 'The Azwah assistant conversation, encrypted (D-108)',
    // Each message is sealed by src/storage/protection like answers; the server keeps no copy (the owner's choice).
    sql: `
      CREATE TABLE assistant_messages (
        id TEXT PRIMARY KEY NOT NULL,
        position INTEGER NOT NULL UNIQUE,
        role TEXT NOT NULL CHECK (role IN ('user', 'assistant')),
        text_cipher TEXT NOT NULL,
        created_at TEXT NOT NULL
      );
    `,
  },
  {
    version: 5,
    description: "The assistant's call lines, suggested screens and sources, and its rolling memory (D-122, D-123)",
    // Existing messages were typed in the chat. extras_cipher seals the reply's actions and sources as JSON; the one
    // memory row is sealed the same way. Nothing here is ever in clear text.
    sql: `
      ALTER TABLE assistant_messages ADD COLUMN channel TEXT NOT NULL DEFAULT 'chat' CHECK (channel IN ('chat', 'voice'));
      ALTER TABLE assistant_messages ADD COLUMN extras_cipher TEXT;
      CREATE TABLE assistant_memory (
        id INTEGER PRIMARY KEY NOT NULL CHECK (id = 1),
        text_cipher TEXT NOT NULL,
        updated_at TEXT NOT NULL
      );
    `,
  },
  {
    // The score report's tables were versions 5 and 6 on their own branch; they follow the assistant's 5 here.
    version: 6,
    description: 'Written score reports, encrypted (D-112)',
    // A report describes a family from its answers, so it is sealed like them; the server keeps no copy.
    sql: `
      CREATE TABLE score_reports (
        session_id TEXT NOT NULL,
        language TEXT NOT NULL CHECK (language IN ('en', 'ar')),
        report_cipher TEXT NOT NULL,
        created_at TEXT NOT NULL,
        PRIMARY KEY (session_id, language)
      ) WITHOUT ROWID;
    `,
  },
  {
    version: 7,
    description: 'A report written ahead as the person answers: its sealed parts and their choice (D-113)',
    // Parts describe a family from its answers, so they are sealed like them. The choice itself says nothing about anyone.
    sql: `
      CREATE TABLE score_report_parts (
        session_id TEXT NOT NULL,
        language TEXT NOT NULL CHECK (language IN ('en', 'ar')),
        part_key TEXT NOT NULL,
        part_cipher TEXT NOT NULL,
        PRIMARY KEY (session_id, language, part_key)
      ) WITHOUT ROWID;
      CREATE TABLE score_report_choices (
        session_id TEXT PRIMARY KEY NOT NULL,
        choice TEXT NOT NULL CHECK (choice IN ('on', 'off'))
      ) WITHOUT ROWID;
    `,
  },
];

export function assertContiguous(migrations: readonly Migration[]): void {
  migrations.forEach((migration, index) => {
    if (migration.version !== index + 1) {
      throw new Error(`Migration ${index + 1} is missing or out of order (found version ${migration.version}).`);
    }
  });
}

export function pendingMigrations(currentVersion: number, migrations: readonly Migration[] = MIGRATIONS): Migration[] {
  assertContiguous(migrations);
  return migrations.filter((migration) => migration.version > currentVersion);
}

/** Brings the database up to the latest version and returns that version. */
export async function migrate(db: SqlDatabase, migrations: readonly Migration[] = MIGRATIONS): Promise<number> {
  const row = await db.getFirstAsync<{ user_version: number }>('PRAGMA user_version');
  const current = row?.user_version ?? 0;
  const latest = migrations.length;
  if (current > latest) {
    throw new Error(`The local database is version ${current}, newer than this app supports (${latest}).`);
  }
  for (const migration of pendingMigrations(current, migrations)) {
    await db.withTransactionAsync(async () => {
      await db.execAsync(migration.sql);
      await db.execAsync(`PRAGMA user_version = ${migration.version}`);
    });
  }
  return latest;
}
