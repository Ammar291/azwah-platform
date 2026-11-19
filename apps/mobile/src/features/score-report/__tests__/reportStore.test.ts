import type { ScoreReport } from '@/api/participant/schemas';
import { createEncryptedSqliteDraftStore } from '@/drafts/encryptedSqliteDraftStore';
import { migrate } from '@/storage/migrations';
import { createFieldCipher } from '@/storage/protection/fieldCipher';
import { createMemorySecretStore } from '@/storage/testing/memorySecretStore';
import { createNodeAesGcm } from '@/storage/testing/nodeAesGcm';
import { NodeSqliteDatabase } from '@/storage/testing/NodeSqliteDatabase';

import { createEncryptedScoreReportStore, createMemoryScoreReportStore } from '../reportStore';

const SESSION = '0199e0a1-0000-7000-8000-000000000001';

const report = (overrides: Partial<ScoreReport> = {}): ScoreReport => ({
  sessionId: SESSION,
  language: 'en',
  totalScore: 96,
  tierCode: 'transitional',
  scale: { minimum: 39, maximum: 156, bands: [{ code: 'transitional', min: 39, max: 156 }] },
  summary: 'Your family talks together often.',
  strengths: [{ title: 'Time together', detail: 'Shown by the answers.' }],
  weaknesses: [],
  nextSteps: [{ sectionCode: 'wealth_debt', area: 'Savings', reason: 'A reason.', steps: ['A step.'], standing: 0.5, authorities: [] }],
  modelName: 'synthetic-model',
  generatedAt: '2026-11-05T09:00:00.000Z',
  ...overrides,
});

async function phone() {
  const db = new NodeSqliteDatabase();
  await migrate(db);
  const secrets = createMemorySecretStore();
  const cipher = () => createFieldCipher({ aes: createNodeAesGcm(), secrets });
  // One cipher for both, as createServices wires them: clearing the drafts deletes the key for both.
  const shared = cipher();
  return { db, reports: createEncryptedScoreReportStore(db, shared), drafts: createEncryptedSqliteDraftStore(db, shared), cipher };
}

describe('the encrypted report store', () => {
  it('keeps a report across a restart, and never in clear text', async () => {
    const { db, reports, cipher } = await phone();
    await reports.save(report());

    const reopened = createEncryptedScoreReportStore(db, cipher());
    expect(await reopened.get(SESSION, 'en')).toEqual(report());
    const rows = await db.getAllAsync<{ report_cipher: string }>('SELECT report_cipher FROM score_reports');
    expect(rows.map((row) => row.report_cipher.startsWith('v1:'))).toEqual([true]);
    expect(JSON.stringify(rows)).not.toContain('family');
    db.close();
  });

  it('keeps one report for each session and language, the newest', async () => {
    const { db, reports } = await phone();
    await reports.save(report());
    await reports.save(report({ language: 'ar', summary: 'ملخص' }));
    await reports.save(report({ summary: 'A newer summary.' }));

    expect((await reports.get(SESSION, 'en'))?.summary).toBe('A newer summary.');
    expect((await reports.get(SESSION, 'ar'))?.summary).toBe('ملخص');
    expect(await reports.get('another-session', 'en')).toBeNull();
    db.close();
  });

  it('opens nothing once the key has gone with the drafts', async () => {
    const { db, reports, drafts } = await phone();
    await reports.save(report());

    await drafts.clearAll();

    expect(await reports.get(SESSION, 'en')).toBeNull();
    db.close();
  });

  it('deletes every report', async () => {
    const { db, reports } = await phone();
    await reports.save(report());

    await reports.clearAll();

    expect(await reports.get(SESSION, 'en')).toBeNull();
    expect(await db.getAllAsync('SELECT * FROM score_reports')).toEqual([]);
    db.close();
  });

  it('will not move a sealed report to another session', async () => {
    const { db, reports } = await phone();
    await reports.save(report());

    await db.runAsync(`UPDATE score_reports SET session_id = 'another-session'`);

    expect(await reports.get('another-session', 'en')).toBeNull();
    db.close();
  });
});

describe('a report written ahead on the phone', () => {
  const part = (key: string, fingerprint = 'one') => ({
    key,
    fingerprint,
    kind: 'section' as const,
    part: {
      sectionCode: 'A',
      language: 'en' as const,
      strengths: [{ title: 'Your family talks together', detail: 'Shown by the answers.' }],
      weaknesses: [],
      nextSteps: [],
      modelName: 'synthetic-model',
      generatedAt: '2026-11-05T09:00:00.000Z',
      standing: 0.5,
    },
  });

  it('keeps parts across a restart, and never in clear text', async () => {
    const { db, reports, cipher } = await phone();
    await reports.savePart(SESSION, 'en', part('section:A'));

    const reopened = createEncryptedScoreReportStore(db, cipher());
    expect(await reopened.listParts(SESSION, 'en')).toEqual([part('section:A')]);
    const rows = await db.getAllAsync<{ part_cipher: string }>('SELECT part_cipher FROM score_report_parts');
    expect(rows.map((row) => row.part_cipher.startsWith('v1:'))).toEqual([true]);
    expect(JSON.stringify(rows)).not.toContain('family');
    db.close();
  });

  it('keeps one part for each key, the newest, and apart by language', async () => {
    const { db, reports } = await phone();
    await reports.savePart(SESSION, 'en', part('section:A', 'old'));
    await reports.savePart(SESSION, 'en', part('section:A', 'new'));

    expect((await reports.listParts(SESSION, 'en')).map((stored) => stored.fingerprint)).toEqual(['new']);
    expect(await reports.listParts(SESSION, 'ar')).toEqual([]);
    db.close();
  });

  it('opens no part once the key has gone with the drafts', async () => {
    const { db, reports, drafts } = await phone();
    await reports.savePart(SESSION, 'en', part('section:A'));

    await drafts.clearAll();

    expect(await reports.listParts(SESSION, 'en')).toEqual([]);
    db.close();
  });

  it('deletes a session’s parts and leaves its report', async () => {
    const { db, reports } = await phone();
    await reports.save(report());
    await reports.savePart(SESSION, 'en', part('section:A'));

    await reports.deleteParts(SESSION);

    expect(await reports.listParts(SESSION, 'en')).toEqual([]);
    expect(await reports.get(SESSION, 'en')).not.toBeNull();
    db.close();
  });

  it('remembers the person’s choice, and clearing everything forgets it', async () => {
    const { db, reports } = await phone();
    expect(await reports.getChoice(SESSION)).toBeNull();

    await reports.setChoice(SESSION, 'on');
    expect(await reports.getChoice(SESSION)).toBe('on');

    await reports.savePart(SESSION, 'en', part('section:A'));
    await reports.clearAll();
    expect(await reports.getChoice(SESSION)).toBeNull();
    expect(await reports.listParts(SESSION, 'en')).toEqual([]);
    db.close();
  });

  it('works the same in memory', async () => {
    const reports = createMemoryScoreReportStore();
    await reports.setChoice(SESSION, 'off');
    await reports.savePart(SESSION, 'en', part('section:A'));

    expect(await reports.getChoice(SESSION)).toBe('off');
    expect(await reports.listParts(SESSION, 'en')).toEqual([part('section:A')]);

    await reports.deleteParts(SESSION);
    expect(await reports.listParts(SESSION, 'en')).toEqual([]);
  });
});

describe('the report store without a keystore', () => {
  it('keeps a report in memory only, for the session and language', async () => {
    const reports = createMemoryScoreReportStore();
    expect(reports.persistence).toBe('memory');
    await reports.save(report());

    expect(await reports.get(SESSION, 'en')).toEqual(report());
    expect(await reports.get(SESSION, 'ar')).toBeNull();

    await reports.clearAll();
    expect(await reports.get(SESSION, 'en')).toBeNull();
  });
});
