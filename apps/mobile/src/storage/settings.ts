import type { SqlDatabase } from './sql';

/**
 * Ordinary preferences only. Access and refresh tokens never go here: they belong in the platform's secure storage
 * (see CredentialStore in src/auth/types.ts). `drafts_owner` is the token subject whose drafts are on the device.
 * `language_changed_at` is when the language switch asked for a reload (milliseconds), read once at the next start.
 * `dev_fixture_worlds` exists in development fixtures mode only: the fixture API's synthetic data, sealed with the
 * answers' cipher under a key of its own, so the reload that applies a language keeps a demonstration
 * (src/dev-fixtures/fixtureParticipantApi.ts).
 */
export type SettingKey =
  | 'language'
  | 'direction_reload_attempted_for'
  | 'dev_mock_persona'
  | 'dev_fixture_worlds'
  | 'drafts_owner'
  | 'language_changed_at';

export function createSettingsRepository(db: SqlDatabase) {
  return {
    async get(key: SettingKey): Promise<string | null> {
      const row = await db.getFirstAsync<{ value: string }>('SELECT value FROM settings WHERE key = ?', key);
      return row?.value ?? null;
    },
    async set(key: SettingKey, value: string): Promise<void> {
      await db.runAsync(
        'INSERT INTO settings (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value',
        key,
        value,
      );
    },
    async remove(key: SettingKey): Promise<void> {
      await db.runAsync('DELETE FROM settings WHERE key = ?', key);
    },
  };
}

export type SettingsRepository = ReturnType<typeof createSettingsRepository>;
