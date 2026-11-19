import {
  assignmentSchema,
  consentRecordSchema,
  historyItemSchema,
  householdSchema,
  memberSchema,
  meSchema,
  myDataExportSchema,
  privacyRequestPageSchema,
  publishedVersionSchema,
  resilienceTrendSchema,
  sessionResultSchema,
  sessionSchema,
} from '@/api/participant/schemas';
import { PLACEHOLDER_CATALOG, PLACEHOLDER_FIXTURE } from '@/test-utils/placeholderContent';

import { FAMILY_VOICES, HOUSEHOLD_ASSESSMENT, itemsOf } from '../content';
import { createFixtureParticipantApi } from '../fixtureParticipantApi';

const NOW = new Date('2026-10-14T09:00:00Z');

const apiFor = (subject: string | null) =>
  createFixtureParticipantApi({ getSubject: () => subject, latencyMs: 0, now: () => NOW });

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
const MODULE_A_ITEMS = itemsOf(FAMILY_VOICES.version, 'A');

/** Every item answered with its option at `index` in display order. */
const answersAt = (items: typeof HOUSEHOLD_ITEMS, index: number) =>
  items.map((item) => ({ itemId: item.id, optionId: item.options[index]?.id ?? '' }));

async function grantParticipation(api: ReturnType<typeof apiFor>) {
  const me = await api.getMe();
  const participation = (await api.getCurrentConsentPolicies()).find((p) => p.consentType === 'participation');
  await api.recordConsent({
    memberId: me.household?.memberId ?? '',
    policyVersionId: participation?.id ?? '',
    decision: 'grant',
  });
}

describe('fixture participant API', () => {
  it('refuses requests when nobody is signed in', async () => {
    await expect(apiFor(null).getMe()).rejects.toMatchObject({ kind: 'unauthorized' });
  });

  it('serves the household lead a world that matches the API contract', async () => {
    const api = apiFor('dev-mock|household_lead');

    const me = meSchema.parse(await api.getMe());
    const householdId = me.household?.householdId ?? '';
    householdSchema.parse(await api.getHousehold(householdId));
    (await api.listMembers(householdId)).forEach((member) => memberSchema.parse(member));
    const assignments = await api.listAssignments();
    assignments.forEach((assignment) => assignmentSchema.parse(assignment));
    (await api.listHistory()).forEach((item) => historyItemSchema.parse(item));
    (await api.listMyConsents()).forEach((record) => consentRecordSchema.parse(record));
    publishedVersionSchema.parse(await api.getPublishedVersion('household_assessment', 1));
    publishedVersionSchema.parse(await api.getPublishedVersion('abu_dhabi_family_survey', 1));
    await expect(api.getPublishedVersion('dev_placeholder', 1)).rejects.toMatchObject({ kind: 'not_found' });

    expect(me.household?.actableMemberIds).toHaveLength(3);
    expect(assignments.map((a) => [a.instrumentCode, a.sectionCode, a.status]).sort()).toEqual([
      ['abu_dhabi_family_survey', 'A', 'assigned'],
      ['household_assessment', null, 'completed'],
      ['household_assessment', null, 'completed'],
      ['household_assessment', null, 'in_progress'],
    ]);
    resilienceTrendSchema.parse(await api.getResilienceTrend());
  });

  it("charts the household lead's two scored rounds on the proposed scale, for the head only (D-132)", async () => {
    const api = apiFor('dev-mock|household_lead');

    const trend = await api.getResilienceTrend();

    expect(trend.scoreVisibility).toBe('visible');
    expect(trend.scale).toEqual({
      minimum: 39,
      maximum: 156,
      bands: [
        { code: 'vulnerable', min: 39, max: 78 },
        { code: 'transitional', min: 79, max: 117 },
        { code: 'flourishing', min: 118, max: 156 },
      ],
    });
    expect(trend.points.map((point) => [point.waveCode, point.totalScore, point.tierCode, point.segment])).toEqual([
      ['dev-wave-0', 69, 'vulnerable', 0],
      ['dev-wave-1', 88, 'transitional', 0],
    ]);
    expect(trend.notShownCount).toBe(0);

    const household = (await api.getMe()).household;
    const members = await api.listMembers(household?.householdId ?? '');
    const lead = members.find((member) => member.isCaller);
    const spouse = members.find((member) => member.relationshipToHousehold === 'spouse');
    await api.updateMember(household?.householdId ?? '', lead?.id ?? '', { isHeadOfHousehold: false, relationshipToHousehold: 'spouse' });
    await api.updateMember(household?.householdId ?? '', spouse?.id ?? '', { isHeadOfHousehold: true, relationshipToHousehold: null });

    expect(await api.getResilienceTrend()).toEqual({
      scoreVisibility: 'head_of_household_only',
      scale: null,
      points: [],
      notShownCount: 0,
    });
  });

  it('charts nothing from a family-voices module: the two instruments are never combined', async () => {
    const api = createFixtureParticipantApi({
      getSubject: () => 'dev-mock|household_lead',
      latencyMs: 0,
      now: () => NOW,
      catalog: PLACEHOLDER_CATALOG,
    });

    expect(await api.getResilienceTrend()).toEqual({ scoreVisibility: 'visible', scale: null, points: [], notShownCount: 0 });
  });

  it("serves the platform's draft content verbatim, never as approved", async () => {
    const api = apiFor('dev-mock|household_lead');

    const assessment = await api.getPublishedVersion('household_assessment', 1);
    expect(assessment).toEqual(HOUSEHOLD_ASSESSMENT.version);
    expect(assessment.status).toBe('draft');
    expect(assessment.name.en).toBe('Family Resilience Assessment (39 items)');
    expect(await api.getPublishedVersion('abu_dhabi_family_survey', 1)).toEqual(FAMILY_VOICES.version);
  });

  it('shows the household lead an earlier assessment scored by the simulated proposal', async () => {
    const api = apiFor('dev-mock|household_lead');

    const completed = (await api.listAssignments()).find((a) => a.status === 'completed');
    const scored = (await api.listHistory()).find((item) => item.assignmentId === completed?.id);
    const result = sessionResultSchema.parse(await api.getResult(scored?.sessionId ?? ''));

    expect(scored).toMatchObject({ instrumentCode: 'household_assessment', scoreStatus: 'complete', tierCode: 'transitional' });
    expect(result).toMatchObject({ state: 'complete', sessionStatus: 'scored', totalScore: 88, tierCode: 'transitional' });
    expect(result.categoryScores).toHaveLength(7);
  });

  it('walks a new participant from household to a simulated scored result', async () => {
    const api = apiFor('dev-mock|new_participant');
    expect((await api.getMe()).household).toBeNull();

    const household = await api.createHousehold({ members: [self] });
    expect((await api.confirmRoster(household.id)).assignments).toHaveLength(2);
    expect((await api.confirmRoster(household.id)).assignments).toHaveLength(2);

    const assessment = (await api.listAssignments()).find((a) => a.instrumentCode === 'household_assessment');
    await expect(api.startSession(assessment?.id ?? '')).rejects.toMatchObject({
      kind: 'forbidden',
      code: 'sessions.participation_consent_required',
    });
    await grantParticipation(api);

    const session = sessionSchema.parse(await api.startSession(assessment?.id ?? ''));
    expect(session.sectionCode).toBeNull();
    expect(session.requiredCount).toBe(39);

    await api.saveAnswers(session.id, answersAt(HOUSEHOLD_ITEMS, 0).slice(0, 1));
    await expect(api.submitSession(session.id, 'key-1')).rejects.toMatchObject({
      code: 'sessions.required_items_unanswered',
    });

    await api.saveAnswers(session.id, answersAt(HOUSEHOLD_ITEMS, 2));
    expect((await api.getSession(session.id)).answeredCount).toBe(39);

    await expect(api.submitSession(session.id, 'key-2')).resolves.toMatchObject({ outcome: 'submitted', status: 'scored' });
    await expect(api.submitSession(session.id, 'key-2')).resolves.toMatchObject({ outcome: 'submitted' });
    await expect(api.submitSession(session.id, 'key-3')).resolves.toMatchObject({ outcome: 'already_submitted' });

    const result = sessionResultSchema.parse(await api.getResult(session.id));
    expect(result).toMatchObject({ state: 'complete', sessionStatus: 'scored', totalScore: 107, tierCode: 'transitional' });
    expect(result.categoryScores.map((category) => category.score)).toEqual([19, 14, 14, 14, 14, 16, 16]);
    expect((await api.getAssignment(assessment?.id ?? '')).status).toBe('completed');
    expect((await api.listHistory()).map((item) => item.sessionId)).toEqual([session.id]);
  });

  it("records a test catalog's canned earlier result and leaves its new submissions pending", async () => {
    const api = createFixtureParticipantApi({
      getSubject: () => 'dev-mock|household_lead',
      latencyMs: 0,
      now: () => NOW,
      catalog: PLACEHOLDER_CATALOG,
    });
    const [earlier] = await api.listHistory();
    expect(sessionResultSchema.parse(await api.getResult(earlier?.sessionId ?? ''))).toMatchObject({
      state: 'complete',
      totalScore: 8,
      tierCode: 'dev_placeholder_tier',
    });

    const sessionId = (await api.listAssignments()).find((a) => a.status === 'in_progress')?.activeSessionId ?? '';
    await api.saveAnswers(sessionId, answersAt(itemsOf(PLACEHOLDER_FIXTURE.version, 'A'), 0));
    await expect(api.submitSession(sessionId, 'key-p')).resolves.toMatchObject({ status: 'submitted' });
    expect(sessionResultSchema.parse(await api.getResult(sessionId))).toMatchObject({ state: 'pending' });
    await expect(api.getPublishedVersion('household_assessment', 1)).rejects.toMatchObject({ kind: 'not_found' });
  });

  it('gives score values to the head of household only, like the server (D-103)', async () => {
    const api = createFixtureParticipantApi({
      getSubject: () => 'dev-mock|household_lead',
      latencyMs: 0,
      now: () => NOW,
      catalog: PLACEHOLDER_CATALOG,
    });
    const [earlier] = await api.listHistory();
    const sessionId = earlier?.sessionId ?? '';
    expect(earlier?.tierCode).toBe('dev_placeholder_tier');
    expect(sessionResultSchema.parse(await api.getResult(sessionId))).toMatchObject({ scoreVisibility: 'visible', totalScore: 8 });

    const household = (await api.getMe()).household;
    const members = await api.listMembers(household?.householdId ?? '');
    const lead = members.find((member) => member.isCaller);
    const spouse = members.find((member) => member.relationshipToHousehold === 'spouse');
    await api.updateMember(household?.householdId ?? '', lead?.id ?? '', { isHeadOfHousehold: false, relationshipToHousehold: 'spouse' });
    await api.updateMember(household?.householdId ?? '', spouse?.id ?? '', { isHeadOfHousehold: true, relationshipToHousehold: null });

    expect(sessionResultSchema.parse(await api.getResult(sessionId))).toMatchObject({
      state: 'complete',
      scoreVisibility: 'head_of_household_only',
      totalScore: null,
      tierCode: null,
      categoryScores: [],
    });
    expect((await api.listHistory())[0]?.tierCode).toBeNull();
  });

  it('finishes a session in one demo submission, scored in the band asked for, and answers a retry as before (D-143)', async () => {
    const api = apiFor('dev-mock|household_lead');
    const open = (await api.listAssignments()).find((a) => a.status === 'in_progress');
    const sessionId = open?.activeSessionId ?? '';

    const submitted = await api.submitDemoAnswers(sessionId, 'low', 'key-1');
    const again = await api.submitDemoAnswers(sessionId, 'low', 'key-1');

    expect(submitted).toMatchObject({ sessionId, outcome: 'submitted', status: 'scored' });
    expect(again).toEqual(submitted);
    expect((await api.getAnswers(sessionId)).responses).toHaveLength(HOUSEHOLD_ITEMS.length);
    expect(sessionResultSchema.parse(await api.getResult(sessionId)).tierCode).toBe('vulnerable');
    expect((await api.listAssignments()).find((a) => a.id === open?.id)?.status).toBe('completed');
    await expect(api.submitDemoAnswers(sessionId, 'high', 'key-2')).rejects.toMatchObject({ code: 'sessions.not_in_progress' });
  });

  it('opens a new round of the household assessment once none is open, and adds a point to the trend (D-144)', async () => {
    const api = apiFor('dev-mock|household_lead');
    const inProgress = (await api.listAssignments()).find((a) => a.status === 'in_progress');

    // Still open: handed back, nothing new.
    expect(await api.openDemoCheckIn()).toMatchObject({ assignmentId: inProgress?.id, opened: false });
    await api.submitDemoAnswers(inProgress?.activeSessionId ?? '', 'medium', 'key-3');
    const before = (await api.getResilienceTrend()).points.length;
    const opened = await api.openDemoCheckIn();
    // Module A stays open beside it, and is not what the new survey opens.
    expect(opened).toMatchObject({ opened: true, activeSessionId: null, waveCode: 'dev-wave-3' });
    const round = assignmentSchema.parse((await api.listAssignments()).find((a) => a.id === opened.assignmentId));
    expect(round).toMatchObject({ instrumentCode: 'household_assessment', status: 'assigned', instrumentKind: 'household_assessment' });
    const session = await api.startSession(opened.assignmentId);
    await api.submitDemoAnswers(session.id, 'high', 'key-4');

    const trend = await api.getResilienceTrend();
    expect(trend.points).toHaveLength(before + 1);
    expect(trend.points[trend.points.length - 1]).toMatchObject({ sessionId: session.id, waveCode: 'dev-wave-3', tierCode: 'flourishing' });
  });

  it('leaves a family-voices module pending: the codebook has no scoring proposal', async () => {
    const api = apiFor('dev-mock|household_lead');
    const moduleA = (await api.listAssignments()).find((a) => a.instrumentCode === 'abu_dhabi_family_survey');

    const session = sessionSchema.parse(await api.startSession(moduleA?.id ?? ''));
    expect(session.requiredCount).toBe(10);
    await api.saveAnswers(session.id, answersAt(MODULE_A_ITEMS, 1));
    await expect(api.submitSession(session.id, 'key-a')).resolves.toMatchObject({ outcome: 'submitted', status: 'submitted' });

    expect(sessionResultSchema.parse(await api.getResult(session.id))).toMatchObject({ state: 'pending', totalScore: null });
  });

  it('refuses unknown items, options and resources like the API', async () => {
    const api = apiFor('dev-mock|household_lead');
    const inProgress = (await api.listAssignments()).find((a) => a.status === 'in_progress');
    const sessionId = inProgress?.activeSessionId ?? '';

    await expect(api.saveAnswers(sessionId, [{ itemId: 'nope', optionId: 'nope' }])).rejects.toMatchObject({
      kind: 'bad_request',
      code: 'sessions.item_not_in_section',
    });
    await expect(
      api.saveAnswers(sessionId, [{ itemId: HOUSEHOLD_ITEMS[0]?.id ?? '', optionId: 'nope' }]),
    ).rejects.toMatchObject({ kind: 'bad_request', code: 'sessions.option_not_in_item' });
    await expect(api.getSession('missing')).rejects.toMatchObject({ kind: 'not_found' });
    await expect(api.getHousehold('missing')).rejects.toMatchObject({ code: 'households.household_not_found' });
    await expect(api.getResult(sessionId)).rejects.toMatchObject({ code: 'sessions.not_submitted' });
  });

  it('keeps each persona in its own world', async () => {
    await apiFor('dev-mock|new_participant').createHousehold({ members: [self] });

    await expect(apiFor('dev-mock|new_participant').getMe()).resolves.toMatchObject({ household: null });
  });

  it("refuses roster changes with the server's problem codes for its published rules", async () => {
    const api = apiFor('dev-mock|household_lead');
    const me = await api.getMe();
    const householdId = me.household?.householdId ?? '';
    const members = await api.listMembers(householdId);
    const spouse = members.find((member) => member.relationshipToHousehold === 'spouse');
    const child = members.find((member) => member.relationshipToHousehold === 'child');
    const adult = {
      birthYear: 1960,
      birthMonth: 1,
      relationshipToHousehold: 'parent' as const,
      employmentStatus: 'retired' as const,
      isHeadOfHousehold: false,
      isPrimaryIncomeEarner: false,
    };

    await expect(api.addMember(householdId, { ...adult, relationshipToHousehold: null, isHeadOfHousehold: true }))
      .rejects.toMatchObject({ kind: 'conflict', code: 'households.head_already_exists' });
    await expect(api.addMember(householdId, { ...adult, isPrimaryIncomeEarner: true })).rejects.toMatchObject({
      code: 'households.primary_earner_already_exists',
    });
    await expect(api.updateMember(householdId, child?.id ?? '', { isPrimaryIncomeEarner: true })).rejects.toMatchObject({
      kind: 'bad_request',
      code: 'households.primary_earner_under_18',
    });
    await expect(api.addMember(householdId, { ...adult, birthYear: 2026, birthMonth: 12 })).rejects.toMatchObject({
      code: 'households.birth_month_in_future',
    });
    await expect(api.removeMember(householdId, me.household?.memberId ?? '')).rejects.toMatchObject({
      code: 'households.member_is_manager',
    });

    await api.removeMember(householdId, spouse?.id ?? '');
    await expect(api.removeMember(householdId, spouse?.id ?? '')).rejects.toMatchObject({
      kind: 'not_found',
      code: 'households.member_not_found',
    });
    expect((await api.getHousehold(householdId)).rosterConfirmedAt).toBeNull();
    expect((await api.getHousehold(householdId)).activeMemberCount).toBe(2);
  });

  it('confirms only a complete roster, as the approved rules say', async () => {
    const api = apiFor('dev-mock|new_participant');
    const household = await api.createHousehold({
      members: [{ ...self, isHeadOfHousehold: false, relationshipToHousehold: 'spouse' }],
    });
    await expect(api.confirmRoster(household.id)).rejects.toMatchObject({ code: 'households.roster_has_no_head' });

    const head = await api.addMember(household.id, { ...self, birthYear: 1985, isPrimaryIncomeEarner: false });
    await api.updateMember(household.id, head.id, { employmentStatus: null });
    await expect(api.confirmRoster(household.id)).rejects.toMatchObject({
      code: 'households.roster_missing_employment_status',
    });

    await api.updateMember(household.id, head.id, { employmentStatus: 'retired' });
    await api.addMember(household.id, { ...self, birthYear: 2015, employmentStatus: null, isHeadOfHousehold: false, isPrimaryIncomeEarner: false });
    await expect(api.confirmRoster(household.id)).rejects.toMatchObject({
      code: 'households.roster_missing_relationship',
    });
  });

  it('refuses a household whose creator is under 18', async () => {
    const api = apiFor('dev-mock|new_participant');

    await expect(
      api.createHousehold({
        members: [{ ...self, birthYear: 2012, isHeadOfHousehold: false, isPrimaryIncomeEarner: false, relationshipToHousehold: 'child' }],
      }),
    ).rejects.toMatchObject({ code: 'households.manager_under_18' });
    expect((await api.getMe()).household).toBeNull();
  });

  it("applies the server's adults-only rule: certainly 18 only from the month after the 18th birthday month", async () => {
    // NOW is in October 2026. Born in October 2008, a person may still be 17 this month; born in September 2008, they
    // are certainly 18. The roster's own rules (head, main earner, age shown) keep the first-of-the-month convention.
    const turnsEighteenThisMonth = { birthYear: 2008, birthMonth: 10 };
    const turnedEighteenLastMonth = { birthYear: 2008, birthMonth: 9 };
    const api = apiFor('dev-mock|new_participant');

    await expect(api.createHousehold({ members: [{ ...self, ...turnsEighteenThisMonth }] })).rejects.toMatchObject({
      kind: 'conflict',
      code: 'households.manager_under_18',
    });
    const household = await api.createHousehold({ members: [{ ...self, ...turnedEighteenLastMonth }] });
    const callerId = (await api.getMe()).household?.memberId ?? '';

    const child = { relationshipToHousehold: 'child' as const, employmentStatus: 'student' as const, isHeadOfHousehold: false, isPrimaryIncomeEarner: false };
    const young = await api.addMember(household.id, { ...child, ...turnsEighteenThisMonth });
    const adult = await api.addMember(household.id, { ...child, ...turnedEighteenLastMonth });
    expect(young.age).toBe(18);
    const participation = (await api.getCurrentConsentPolicies()).find((p) => p.consentType === 'participation');
    const grant = (memberId: string) =>
      api.recordConsent({ memberId, policyVersionId: participation?.id ?? '', decision: 'grant' });
    await expect(grant(young.id)).rejects.toMatchObject({ kind: 'conflict', code: 'consent.member_under_18' });
    await expect(grant(adult.id)).resolves.toMatchObject({ memberId: adult.id, isActive: true });

    await expect(api.updateMember(household.id, callerId, turnsEighteenThisMonth)).rejects.toMatchObject({
      code: 'households.manager_under_18',
    });
    await expect(api.updateMember(household.id, adult.id, turnsEighteenThisMonth)).rejects.toMatchObject({
      code: 'households.member_has_consent',
    });
    await expect(api.updateMember(household.id, adult.id, { birthYear: 2008, birthMonth: 8 })).resolves.toMatchObject({
      birthMonth: 8,
    });

    // The questionnaires go to the caller only, who is certainly 18.
    await api.confirmRoster(household.id);
    expect((await api.listAssignments()).map((assignment) => assignment.memberId)).toEqual([callerId, callerId]);
  });

  it('withdraws consent and refuses to withdraw twice', async () => {
    const api = apiFor('dev-mock|household_lead');
    const me = await api.getMe();
    const participation = (await api.getCurrentConsentPolicies()).find((p) => p.consentType === 'participation');
    const request = {
      memberId: me.household?.memberId ?? '',
      policyVersionId: participation?.id ?? '',
      decision: 'withdraw' as const,
    };

    await expect(api.recordConsent(request)).resolves.toMatchObject({ isActive: false });
    await expect(api.recordConsent(request)).rejects.toMatchObject({ code: 'consent.not_active' });
  });
});

describe('fixture participant API: rights requests', () => {
  /** A clock that moves a second per reading, so requests made one after another have distinct times. */
  const ticking = (subject: string) => {
    let at = NOW.getTime();
    return createFixtureParticipantApi({ getSubject: () => subject, latencyMs: 0, now: () => new Date((at += 1000)) });
  };

  it("exports the household lead's data in the contract's shape, answers included and scores left out", async () => {
    const api = apiFor('dev-mock|household_lead');

    const download = await api.exportMyData();
    const exported = myDataExportSchema.parse(download.data);

    expect(download.raw).toEqual(download.data);
    expect(exported.subject).toBe('dev-mock|household_lead');
    expect(exported.household?.reference).toBe('DEV-4821');
    expect(exported.members).toHaveLength(3);
    expect(exported.sessions.map((entry) => entry.session.status).sort()).toEqual(['in_progress', 'scored', 'scored']);
    expect(exported.sessions.find((entry) => entry.session.status === 'in_progress')?.answers).toHaveLength(2);
    expect(exported.consents).toHaveLength(1);
    expect(exported.privacyRequests).toEqual([expect.objectContaining({ type: 'access', resolution: 'completed' })]);
    expect(exported.notIncluded).toEqual(['score_results', 'support_recommendations']);
    expect(JSON.stringify(exported)).not.toMatch(/totalScore|tierCode|categoryScores/);
  });

  it('exports only the account for someone without a household', async () => {
    const exported = myDataExportSchema.parse((await apiFor('dev-mock|new_participant').exportMyData()).data);

    expect(exported).toMatchObject({ household: null, members: [], sessions: [], privacyRequests: [] });
  });

  it('creates requests for the caller by default or a member they act for, listed newest first', async () => {
    const api = ticking('dev-mock|household_lead');
    const me = await api.getMe();
    const spouse = (await api.listMembers(me.household?.householdId ?? '')).find(
      (member) => member.relationshipToHousehold === 'spouse',
    );

    const own = await api.createPrivacyRequest({ type: 'erasure' });
    const theirs = await api.createPrivacyRequest({ type: 'erasure', memberId: spouse?.id });
    const page = privacyRequestPageSchema.parse(await api.listPrivacyRequests(1));

    expect(own).toMatchObject({ memberId: me.household?.memberId, type: 'erasure', status: 'open', resolution: null });
    expect(theirs.memberId).toBe(spouse?.id);
    expect(page.items.map((request) => request.id)).toEqual([theirs.id, own.id, page.items[2]?.id]);
    expect(page).toMatchObject({ page: 1, pageSize: 20, totalCount: 3 });
    expect((await api.listPrivacyRequests(2)).items).toEqual([]);
  });

  it('allows one open request per member and type', async () => {
    const api = ticking('dev-mock|household_lead');
    await api.createPrivacyRequest({ type: 'correction' });

    await expect(api.createPrivacyRequest({ type: 'correction' })).rejects.toMatchObject({
      kind: 'conflict',
      status: 409,
      code: 'privacy.request_already_open',
    });
    // The seeded access request is closed, so another one may be opened.
    await expect(api.createPrivacyRequest({ type: 'access' })).resolves.toMatchObject({ status: 'open' });
  });

  it('refuses a request about someone the caller may not act for', async () => {
    await expect(
      apiFor('dev-mock|household_lead').createPrivacyRequest({
        type: 'access',
        memberId: '0199e0a1-0000-7000-8000-00000000ffff',
      }),
    ).rejects.toMatchObject({ kind: 'not_found', code: 'households.member_not_found' });
    await expect(apiFor('dev-mock|new_participant').createPrivacyRequest({ type: 'access' })).rejects.toMatchObject({
      code: 'households.member_not_found',
    });
  });
});
