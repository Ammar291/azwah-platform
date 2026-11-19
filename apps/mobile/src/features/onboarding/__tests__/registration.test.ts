import { createFixtureParticipantApi } from '@/dev-fixtures/fixtureParticipantApi';
import { PLACEHOLDER_CATALOG } from '@/test-utils/placeholderContent';

import { registrationStatusOf } from '../registration';

describe('registrationStatusOf', () => {
  it('asks for details only when the server says the person is not registered', () => {
    expect(registrationStatusOf({ registeredAt: null }, false)).toBe('required');
    expect(registrationStatusOf({ registeredAt: '2026-10-24T08:00:00Z' }, false)).toBe('registered');
    // A server that predates registration sends no field: nothing is asked.
    expect(registrationStatusOf({}, false)).toBe('registered');
  });

  it('waits for the first answer, and does not guess when the server cannot be asked', () => {
    expect(registrationStatusOf(undefined, false)).toBe('checking');
    expect(registrationStatusOf(undefined, true)).toBe('unknown');
  });
});

describe('fixture registration', () => {
  const api = (subject: string) =>
    createFixtureParticipantApi({ getSubject: () => subject, latencyMs: 0, catalog: PLACEHOLDER_CATALOG });

  it('registers a new person once, as the server does', async () => {
    const fixture = api('dev-mock|new_participant');

    expect((await fixture.getMe()).registeredAt).toBeNull();
    const { registeredAt } = await fixture.registerMe();
    expect((await fixture.registerMe()).registeredAt).toBe(registeredAt);
    expect((await fixture.getMe()).registeredAt).toBe(registeredAt);
  });

  it('starts the household lead registered', async () => {
    expect((await api('dev-mock|household_lead').getMe()).registeredAt).toEqual(expect.any(String));
  });
});
