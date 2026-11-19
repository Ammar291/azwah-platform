import { ApiError } from '@/api/client';
import type { MemberDetails } from '@/api/participant/schemas';
import { fixtureApi, leadHousehold, NEW_PARTICIPANT_SUBJECT } from '@/test-utils/householdWorlds';

import {
  addMember,
  createHousehold,
  mayHaveReachedServer,
  removeMember,
  roleReleases,
  updateMember,
} from '../rosterRequests';
import { activeMembers } from '../rosterRules';

const self: MemberDetails = {
  birthYear: 1990,
  birthMonth: 6,
  relationshipToHousehold: null,
  employmentStatus: 'employed',
  isHeadOfHousehold: true,
  isPrimaryIncomeEarner: true,
};

const grandparent: MemberDetails = {
  birthYear: 1950,
  birthMonth: 2,
  relationshipToHousehold: 'grandparent',
  employmentStatus: 'retired',
  isHeadOfHousehold: false,
  isPrimaryIncomeEarner: false,
};

describe('which failures may have reached the server', () => {
  it('treats dropped connections, timeouts and server faults as uncertain, and refusals as certain', () => {
    expect(mayHaveReachedServer(new ApiError('network'))).toBe(true);
    expect(mayHaveReachedServer(new ApiError('timeout'))).toBe(true);
    expect(mayHaveReachedServer(new ApiError('server', 503))).toBe(true);
    expect(mayHaveReachedServer(new ApiError('bad_request', 400))).toBe(false);
    expect(mayHaveReachedServer(new ApiError('conflict', 409))).toBe(false);
  });
});

describe('creating a household', () => {
  it('creates it with the caller as its first and only member', async () => {
    const { api, argsOf } = fixtureApi(NEW_PARTICIPANT_SUBJECT);

    const created = await createHousehold(api, self, false);

    expect(created.outcome).toBe('created');
    expect(argsOf('createHousehold')).toEqual([[{ members: [{ ...self, isSelf: true }] }]]);
    expect((await api.getMe()).household?.householdId).toBe(created.householdId);
  });

  it('creates it with everyone else in the same request, marking only the caller as themselves', async () => {
    const { api, argsOf } = fixtureApi(NEW_PARTICIPANT_SUBJECT);

    const created = await createHousehold(api, self, false, [grandparent]);

    expect(argsOf('createHousehold')).toEqual([
      [{ members: [{ ...self, isSelf: true }, { ...grandparent, isSelf: false }] }],
    ]);
    expect(await api.listMembers(created.householdId)).toHaveLength(2);
  });

  it('after a lost answer, uses the household the lost request created instead of creating a second one', async () => {
    const { api, lose, count } = fixtureApi(NEW_PARTICIPANT_SUBJECT);
    lose('createHousehold');

    await expect(createHousehold(api, self, false)).rejects.toMatchObject({ kind: 'network' });
    const retried = await createHousehold(api, self, true);

    expect(count('createHousehold')).toBe(1);
    expect(retried).toEqual({ householdId: (await api.getMe()).household?.householdId, outcome: 'created' });
  });

  it('sends the request again after a failure that never reached the server', async () => {
    const { api, fail, count } = fixtureApi(NEW_PARTICIPANT_SUBJECT);
    fail('createHousehold');

    await expect(createHousehold(api, self, false)).rejects.toMatchObject({ kind: 'network' });
    const retried = await createHousehold(api, self, true);

    expect(count('createHousehold')).toBe(2);
    expect(retried.outcome).toBe('created');
  });

  it('leads to the existing household when the account already has one', async () => {
    const { api, householdId } = await leadHousehold();

    await expect(createHousehold(api, self, false)).resolves.toEqual({ householdId, outcome: 'already_set_up' });
  });
});

describe('moving roles', () => {
  it('clears the role from its current holder only for the roles the member takes', async () => {
    const { api, householdId, self: lead } = await leadHousehold();
    const roster = await api.listMembers(householdId);

    expect(roleReleases(roster, { isHeadOfHousehold: true, isPrimaryIncomeEarner: false }, null)).toEqual([
      { memberId: lead.id, patch: { isHeadOfHousehold: false } },
    ]);
    expect(roleReleases(roster, { isHeadOfHousehold: true, isPrimaryIncomeEarner: true }, null)).toEqual([
      { memberId: lead.id, patch: { isHeadOfHousehold: false, isPrimaryIncomeEarner: false } },
    ]);
    expect(roleReleases(roster, { isHeadOfHousehold: true, isPrimaryIncomeEarner: true }, lead.id)).toEqual([]);
  });
});

describe('adding a member', () => {
  it('moves the head-of-household role to the new member, leaving the main earner where it was', async () => {
    const { api, householdId, self: lead, argsOf } = await leadHousehold();
    const roster = await api.listMembers(householdId);

    const added = await addMember(
      api,
      householdId,
      roster,
      { ...grandparent, relationshipToHousehold: null, isHeadOfHousehold: true },
      null,
    );

    expect(argsOf('updateMember')).toEqual([[householdId, lead.id, { isHeadOfHousehold: false }]]);
    const active = activeMembers(await api.listMembers(householdId));
    expect(active.filter((member) => member.isHeadOfHousehold).map((member) => member.id)).toEqual([added.id]);
    expect(active.filter((member) => member.isPrimaryIncomeEarner).map((member) => member.id)).toEqual([lead.id]);
  });

  it('gives the head-of-household role back when adding the new head fails', async () => {
    const { api, householdId, self: lead, fail, argsOf } = await leadHousehold();
    const roster = await api.listMembers(householdId);
    fail('addMember', new ApiError('conflict', 409, 'households.roster_full'));

    await expect(
      addMember(api, householdId, roster, { ...grandparent, relationshipToHousehold: null, isHeadOfHousehold: true }, null),
    ).rejects.toMatchObject({ code: 'households.roster_full' });

    expect(argsOf('updateMember')).toEqual([
      [householdId, lead.id, { isHeadOfHousehold: false }],
      [householdId, lead.id, { isHeadOfHousehold: true }],
    ]);
    const heads = activeMembers(await api.listMembers(householdId)).filter((member) => member.isHeadOfHousehold);
    expect(heads.map((member) => member.id)).toEqual([lead.id]);
  });

  it('after a lost answer that did move the role, leaves it with the new member and completes on retry', async () => {
    const { api, householdId, lose, count } = await leadHousehold();
    const before = await api.listMembers(householdId);
    const newHead = { ...grandparent, relationshipToHousehold: null, isHeadOfHousehold: true };
    lose('addMember');

    await expect(addMember(api, householdId, before, newHead, null)).rejects.toMatchObject({ kind: 'network' });
    const pending = { baseline: new Set(before.map((member) => member.id)), details: newHead };
    const added = await addMember(api, householdId, await api.listMembers(householdId), newHead, pending);

    expect(count('addMember')).toBe(1);
    const heads = activeMembers(await api.listMembers(householdId)).filter((member) => member.isHeadOfHousehold);
    expect(heads.map((member) => member.id)).toEqual([added.id]);
  });

  it('after a lost answer, finds the member the lost request added instead of adding them again', async () => {
    const { api, householdId, lose, count } = await leadHousehold();
    const before = await api.listMembers(householdId);
    lose('addMember');

    await expect(addMember(api, householdId, before, grandparent, null)).rejects.toMatchObject({ kind: 'network' });
    const pending = { baseline: new Set(before.map((member) => member.id)), details: grandparent };
    const added = await addMember(api, householdId, await api.listMembers(householdId), grandparent, pending);

    expect(count('addMember')).toBe(1);
    expect(count('updateMember')).toBe(0);
    const active = activeMembers(await api.listMembers(householdId));
    expect(active).toHaveLength(before.length + 1);
    expect(active.map((member) => member.id)).toContain(added.id);
  });

  it('after a lost answer, applies changes made since to the member the lost request added', async () => {
    const { api, householdId, lose, count } = await leadHousehold();
    const before = await api.listMembers(householdId);
    lose('addMember');

    await expect(addMember(api, householdId, before, grandparent, null)).rejects.toMatchObject({ kind: 'network' });
    const pending = { baseline: new Set(before.map((member) => member.id)), details: grandparent };
    const corrected = { ...grandparent, birthYear: 1948 };
    const added = await addMember(api, householdId, await api.listMembers(householdId), corrected, pending);

    expect(count('addMember')).toBe(1);
    expect(added.birthYear).toBe(1948);
    expect(activeMembers(await api.listMembers(householdId))).toHaveLength(before.length + 1);
  });
});

describe('updating a member', () => {
  it('sends only the fields that changed', async () => {
    const { api, householdId, spouse, argsOf } = await leadHousehold();
    const roster = await api.listMembers(householdId);

    await updateMember(api, householdId, roster, spouse, {
      birthYear: spouse.birthYear,
      birthMonth: spouse.birthMonth,
      relationshipToHousehold: spouse.relationshipToHousehold,
      employmentStatus: 'self_employed',
      isHeadOfHousehold: false,
      isPrimaryIncomeEarner: false,
    });

    expect(argsOf('updateMember')).toEqual([[householdId, spouse.id, { employmentStatus: 'self_employed' }]]);
  });

  it('sends nothing when nothing changed', async () => {
    const { api, householdId, spouse, count } = await leadHousehold();
    const roster = await api.listMembers(householdId);
    const { birthYear, birthMonth, relationshipToHousehold, employmentStatus } = spouse;

    await updateMember(api, householdId, roster, spouse, {
      birthYear,
      birthMonth,
      relationshipToHousehold,
      employmentStatus,
      isHeadOfHousehold: false,
      isPrimaryIncomeEarner: false,
    });

    expect(count('updateMember')).toBe(0);
  });

  it('makes a different person the main earner without touching the head of household', async () => {
    const { api, householdId, self: lead, spouse } = await leadHousehold();
    const roster = await api.listMembers(householdId);

    await updateMember(api, householdId, roster, spouse, {
      birthYear: spouse.birthYear,
      birthMonth: spouse.birthMonth,
      relationshipToHousehold: spouse.relationshipToHousehold,
      employmentStatus: 'employed',
      isHeadOfHousehold: false,
      isPrimaryIncomeEarner: true,
    });

    const active = activeMembers(await api.listMembers(householdId));
    expect(active.find((member) => member.isHeadOfHousehold)?.id).toBe(lead.id);
    expect(active.find((member) => member.isPrimaryIncomeEarner)?.id).toBe(spouse.id);
  });
});

describe('removing a member', () => {
  it('after a lost answer, counts "not found" as already removed', async () => {
    const { api, householdId, spouse, lose } = await leadHousehold();
    lose('removeMember');

    await expect(removeMember(api, householdId, spouse.id, false)).rejects.toMatchObject({ kind: 'network' });
    await expect(removeMember(api, householdId, spouse.id, true)).resolves.toBeUndefined();

    expect((await api.listMembers(householdId)).find((member) => member.id === spouse.id)?.status).toBe('inactive');
  });

  it('reports "not found" when no earlier attempt was lost', async () => {
    const { api, householdId, spouse } = await leadHousehold();
    await api.removeMember(householdId, spouse.id);

    await expect(removeMember(api, householdId, spouse.id, false)).rejects.toMatchObject({
      code: 'households.member_not_found',
    });
  });
});
