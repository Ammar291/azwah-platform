import { createFieldCipher, DRAFT_KEY_NAME } from '@/storage/protection/fieldCipher';
import { createSettingsRepository } from '@/storage/settings';
import { FakeSqlDatabase } from '@/storage/testing/FakeSqlDatabase';
import { createMemorySecretStore } from '@/storage/testing/memorySecretStore';
import { createNodeAesGcm } from '@/storage/testing/nodeAesGcm';

import { HOUSEHOLD_ASSESSMENT, itemsOf } from '../content';
import { createFixtureParticipantApi, FIXTURE_WORLDS_KEY_NAME } from '../fixtureParticipantApi';

const NOW = new Date('2026-10-14T09:00:00Z');
const LEAD = 'dev-mock|household_lead';
const NEWCOMER = 'dev-mock|new_participant';
const SETTING = 'dev_fixture_worlds';

/**
 * A phone: the settings in its database and its keystore. Each API made for it starts like the app after a reload:
 * a new answer cipher (decision M-35), from which the fixture worlds take a key of their own (app-shell/services.ts).
 */
function newPhone() {
  const secrets = createMemorySecretStore();
  const answerCipher = () => createFieldCipher({ aes: createNodeAesGcm(), secrets });
  return { settings: createSettingsRepository(new FakeSqlDatabase()), secrets, answerCipher };
}
type Phone = ReturnType<typeof newPhone>;

const apiFor = (subject: string, phone?: Phone) =>
  createFixtureParticipantApi({
    getSubject: () => subject,
    latencyMs: 0,
    now: () => NOW,
    settings: phone?.settings,
    cipher: phone?.answerCipher().withKey(FIXTURE_WORLDS_KEY_NAME),
  });

const self = {
  birthYear: 1990,
  birthMonth: 6,
  relationshipToHousehold: null,
  employmentStatus: 'employed' as const,
  isHeadOfHousehold: true,
  isPrimaryIncomeEarner: true,
  isSelf: true,
};

const HOUSEHOLD_ITEMS = itemsOf(HOUSEHOLD_ASSESSMENT.version, null);

/** A newcomer's whole demonstration: household, consent, the household assessment answered and submitted. */
async function runDemonstration(api: ReturnType<typeof apiFor>) {
  await api.registerMe();
  const household = await api.createHousehold({ members: [self] });
  await api.confirmRoster(household.id);
  const participation = (await api.getCurrentConsentPolicies()).find((p) => p.consentType === 'participation');
  const me = await api.getMe();
  await api.recordConsent({
    memberId: me.household?.memberId ?? '',
    policyVersionId: participation?.id ?? '',
    decision: 'grant',
  });
  const [assessment, moduleA] = await api.listAssignments();
  const finished = await api.startSession(assessment?.id ?? '');
  await api.saveAnswers(
    finished.id,
    HOUSEHOLD_ITEMS.map((item) => ({ itemId: item.id, optionId: item.options[3]?.id ?? '' })),
  );
  await api.submitSession(finished.id, 'key-1');
  const open = await api.startSession(moduleA?.id ?? '');
  return { householdId: household.id, finishedSessionId: finished.id, openSessionId: open.id };
}

/** Everything the screens read, so two APIs can be compared. */
async function everything(api: ReturnType<typeof apiFor>, ids: Awaited<ReturnType<typeof runDemonstration>>) {
  return {
    me: await api.getMe(),
    household: await api.getHousehold(ids.householdId),
    members: await api.listMembers(ids.householdId),
    consents: await api.listMyConsents(),
    assignments: await api.listAssignments(),
    finished: await api.getSession(ids.finishedSessionId),
    answers: await api.getAnswers(ids.finishedSessionId),
    result: await api.getResult(ids.finishedSessionId),
    open: await api.getSession(ids.openSessionId),
    history: await api.listHistory(),
    requests: await api.listPrivacyRequests(1),
  };
}

describe('fixture worlds kept across a reload', () => {
  it('keeps the household, consent, answers and results when the fixture API is created again', async () => {
    const phone = newPhone();
    const first = apiFor(NEWCOMER, phone);
    const ids = await runDemonstration(first);
    const before = await everything(first, ids);
    expect(before.result.state).toBe('complete');
    expect(before.me.household?.rosterConfirmedAt).not.toBeNull();

    // The reload after a language change: a new API over the same settings.
    const second = apiFor(NEWCOMER, phone);

    expect(await everything(second, ids)).toEqual(before);
  });

  it('carries on answering a session after the reload', async () => {
    const phone = newPhone();
    const ids = await runDemonstration(apiFor(NEWCOMER, phone));

    const second = apiFor(NEWCOMER, phone);
    const version = await second.getPublishedVersion('abu_dhabi_family_survey', 1);
    const item = version.sections.find((section) => section.code === 'A')?.items[0];
    const saved = await second.saveAnswers(ids.openSessionId, [
      { itemId: item?.id ?? '', optionId: item?.options[0]?.id ?? '' },
    ]);

    expect(saved.responses).toHaveLength(1);
    expect((await apiFor(NEWCOMER, phone).getAnswers(ids.openSessionId)).responses).toEqual(saved.responses);
  });

  it('never reuses an identifier after the reload', async () => {
    const phone = newPhone();
    const ids = await runDemonstration(apiFor(NEWCOMER, phone));
    const taken = new Set(
      (await apiFor(NEWCOMER, phone).listAssignments()).map((assignment) => assignment.id).concat(Object.values(ids)),
    );

    // A reload starts the module again, and its counter with it.
    let reloaded: typeof import('../fixtureParticipantApi') | undefined;
    jest.isolateModules(() => {
      // eslint-disable-next-line @typescript-eslint/no-require-imports -- a fresh copy of the module, as after a reload
      reloaded = require('../fixtureParticipantApi') as typeof import('../fixtureParticipantApi');
    });
    const api = reloaded?.createFixtureParticipantApi({
      getSubject: () => NEWCOMER,
      latencyMs: 0,
      now: () => NOW,
      settings: phone.settings,
      cipher: phone.answerCipher().withKey(FIXTURE_WORLDS_KEY_NAME),
    });
    const created = await api?.createPrivacyRequest({ type: 'access' });

    expect((await api?.getMe())?.household?.householdId).toBe(ids.householdId);
    // Identifiers count upwards, so a new one comes after every one handed out before the reload.
    const latest = [...taken].sort().at(-1) ?? '';
    expect((created?.id ?? '') > latest).toBe(true);
  });

  it('keeps each persona its own world', async () => {
    const phone = newPhone();
    await runDemonstration(apiFor(NEWCOMER, phone));
    const leadBefore = await apiFor(LEAD, phone).listAssignments();

    expect(await apiFor(LEAD, phone).listAssignments()).toEqual(leadBefore);
    expect((await apiFor(NEWCOMER, phone).getMe()).household).not.toBeNull();
  });

  it('starts from the seed when what was kept cannot be read', async () => {
    const phone = newPhone();
    await runDemonstration(apiFor(NEWCOMER, phone));
    await phone.settings.set(SETTING, '{"schema":1,"worlds":"not a list"}');

    const me = await apiFor(NEWCOMER, phone).getMe();

    expect(me.household).toBeNull();
    expect(me.registeredAt).toBeNull();
  });

  it('keeps nothing without settings', async () => {
    const api = apiFor(NEWCOMER);
    await api.registerMe();

    expect((await apiFor(NEWCOMER).getMe()).registeredAt).toBeNull();
  });
});

describe('fixture worlds sealed on the phone', () => {
  it('keeps them encrypted: the setting holds no readable data', async () => {
    const phone = newPhone();
    const ids = await runDemonstration(apiFor(NEWCOMER, phone));

    const kept = (await phone.settings.get(SETTING)) ?? '';

    expect(kept).toMatch(/^v1:[A-Za-z0-9+/]+=*$/);
    expect(() => JSON.parse(kept)).toThrow();
    for (const readable of [NEWCOMER, ids.householdId, ids.finishedSessionId, 'answers', 'household', 'schema']) {
      expect(kept).not.toContain(readable);
    }
    expect(await phone.secrets.get(FIXTURE_WORLDS_KEY_NAME)).not.toBeNull();
  });

  it('keeps them when sign-out deletes the drafts key: they have a key of their own', async () => {
    const phone = newPhone();
    await phone.answerCipher().ensureKey();
    const first = apiFor(NEWCOMER, phone);
    const ids = await runDemonstration(first);
    const before = await everything(first, ids);

    // What every sign-out does to the key of the answers kept on the phone (the encrypted draft store's clearAll).
    await phone.answerCipher().destroyKey();

    expect(await phone.secrets.get(DRAFT_KEY_NAME)).toBeNull();
    expect(await everything(apiFor(NEWCOMER, phone), ids)).toEqual(before);
  });

  it('keeps nothing on a phone without a keystore, and removes what an earlier build left readable', async () => {
    const { settings } = newPhone();
    await settings.set(SETTING, '{"schema":1,"worlds":[]}');
    const api = createFixtureParticipantApi({ getSubject: () => NEWCOMER, latencyMs: 0, now: () => NOW, settings, cipher: null });

    await api.registerMe();

    expect(await settings.get(SETTING)).toBeNull();
    // Still kept in memory while the app runs.
    expect((await api.getMe()).registeredAt).not.toBeNull();
  });
});

describe('resetting the demonstration data', () => {
  it('returns a changed world to its first state, now and after a reload', async () => {
    const phone = newPhone();
    const api = apiFor(NEWCOMER, phone);
    await runDemonstration(api);

    await api.resetDemonstration();

    const me = await api.getMe();
    expect(me.household).toBeNull();
    expect(me.registeredAt).toBeNull();
    expect(await api.listAssignments()).toEqual([]);
    expect((await apiFor(NEWCOMER, phone).getMe()).household).toBeNull();
  });

  it('removes what was kept on the phone, and its key', async () => {
    const phone = newPhone();
    const api = apiFor(NEWCOMER, phone);
    await runDemonstration(api);
    expect(await phone.settings.get(SETTING)).not.toBeNull();

    await api.resetDemonstration();

    expect(await phone.settings.get(SETTING)).toBeNull();
    expect(await phone.secrets.get(FIXTURE_WORLDS_KEY_NAME)).toBeNull();
    expect((await apiFor(NEWCOMER, phone).getMe()).registeredAt).toBeNull();
  });

  it('gives the household lead the seeded household again', async () => {
    const phone = newPhone();
    const api = apiFor(LEAD, phone);
    const seeded = {
      members: (await api.listMembers((await api.getMe()).household?.householdId ?? '')).length,
      assignments: (await api.listAssignments()).map((assignment) => assignment.status),
    };
    const me = await api.getMe();
    const consent = (await api.listMyConsents())[0];
    await api.recordConsent({
      memberId: me.household?.memberId ?? '',
      policyVersionId: consent?.policyVersionId ?? '',
      decision: 'withdraw',
    });
    await api.addMember(me.household?.householdId ?? '', {
      birthYear: 2001,
      birthMonth: 2,
      relationshipToHousehold: 'child',
      employmentStatus: 'student',
      isHeadOfHousehold: false,
      isPrimaryIncomeEarner: false,
    });

    await api.resetDemonstration();

    for (const fresh of [api, apiFor(LEAD, phone)]) {
      const again = await fresh.getMe();
      expect(again.household?.rosterConfirmedAt).not.toBeNull();
      expect(await fresh.listMembers(again.household?.householdId ?? '')).toHaveLength(seeded.members);
      expect((await fresh.listAssignments()).map((assignment) => assignment.status)).toEqual(seeded.assignments);
      expect((await fresh.listMyConsents()).filter((record) => record.isActive)).toHaveLength(1);
    }
  });
});
