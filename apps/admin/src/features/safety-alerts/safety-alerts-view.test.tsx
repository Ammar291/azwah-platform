import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { Toaster } from '@/components/ui/sonner';
import { SAFETY_LIST_KEY } from '@/lib/api/queries';
import type { Language } from '@/lib/i18n';
import { problem, stubBff } from '@/test/bff';
import { paged, safetyAlert } from '@/test/fixtures';
import { navigation } from '@/test/navigation';
import { renderWithProviders } from '@/test/render';

import { SafetyAlertsView } from './safety-alerts-view';

vi.mock('next/navigation', async () => (await import('@/test/navigation')).nextNavigation);

beforeEach(() => navigation.reset('/safety-alerts'));

const LIST = 'admin/safety-alerts';
const critical = safetyAlert();
const acknowledged = safetyAlert({
  id: '0190f0e2-7777-7000-8000-000000000002',
  householdReference: 'HH-0000-0001',
  memberRelationship: null,
  source: 'household_assessment',
  category: 'low_resilience_score',
  severity: 'high',
  detector: 'score_band',
  signalCode: 'vulnerable',
  language: null,
  occurrenceCount: 1,
  status: 'acknowledged',
  acknowledgedAt: '2026-10-15T10:00:00+00:00',
});
const resolved = safetyAlert({
  id: '0190f0e2-7777-7000-8000-000000000003',
  category: 'abuse_or_danger',
  source: 'consultant_voice',
  detector: 'ai_moderation',
  signalCode: 'some.future_code',
  language: 'ar',
  status: 'resolved',
  acknowledgedAt: '2026-10-15T10:00:00+00:00',
  resolvedAt: '2026-10-15T12:00:00+00:00',
  resolution: 'family_contacted',
});

function view(language: Language = 'en', canManage = true) {
  return renderWithProviders(
    <>
      <SafetyAlertsView canManage={canManage} />
      <Toaster />
    </>,
    { language },
  );
}

function posts(bff: ReturnType<typeof stubBff>, action: string) {
  return bff.calls.filter((call) => call.url.pathname.endsWith(`/${action}`) && call.init.method === 'POST');
}

describe('SafetyAlertsView', () => {
  it('lists alerts with their concern, source, household and status, and states that no conversation is held', async () => {
    stubBff({ [LIST]: paged([critical, acknowledged]) });

    view();

    expect(await screen.findByText(/Alerts hold no conversation text/)).toBeInTheDocument();
    expect(screen.getByText(/call 999/)).toBeInTheDocument();
    const table = await screen.findByRole('table', { name: 'Safety alerts' });
    const [first, second] = within(table).getAllByRole('row').slice(1);
    expect(within(first).getByText('Critical')).toBeInTheDocument();
    expect(within(first).getByText('Suicide or self-harm')).toBeInTheDocument();
    expect(within(first).getByText('Assistant chat')).toBeInTheDocument();
    expect(within(first).getByText(/Intends to end their life/)).toBeInTheDocument();
    expect(within(first).getByText('HH-7K2M-9QXA')).toBeInTheDocument();
    expect(within(first).getByText('Open')).toBeInTheDocument();
    expect(within(second).getByText('High')).toBeInTheDocument();
    expect(within(second).getByText('Very low resilience score')).toBeInTheDocument();
    expect(within(second).getByText('Family survey score')).toBeInTheDocument();
    expect(within(second).getByText(/Lowest score band/)).toBeInTheDocument();
    expect(within(second).getByText('Acknowledged')).toBeInTheDocument();
  });

  it('names the relationship in the dashboard language and shows an unknown code as recorded', async () => {
    const head = safetyAlert({ memberRelationship: 'head' });
    const unknown = safetyAlert({ id: '0190f0e2-7777-7000-8000-000000000009', memberRelationship: 'step_cousin' });
    stubBff({ [LIST]: paged([head, unknown]) });

    const english = view();
    let table = await screen.findByRole('table', { name: 'Safety alerts' });
    let [first, second] = within(table).getAllByRole('row').slice(1);
    expect(within(first).getByText('Head of household')).toBeInTheDocument();
    expect(within(second).getByText('step_cousin')).toBeInTheDocument();
    english.unmount();

    view('ar');
    table = await screen.findByRole('table');
    [first, second] = within(table).getAllByRole('row').slice(1);
    expect(within(first).getByText('رب الأسرة')).toBeInTheDocument();
    expect(within(second).getByText('step_cousin')).toBeInTheDocument();
  });

  it('shows an unknown signal code as recorded, and the outcome of a resolved alert without actions', async () => {
    navigation.reset('/safety-alerts', 'status=resolved');
    stubBff({ [LIST]: paged([resolved]) });

    view();

    const table = await screen.findByRole('table', { name: 'Safety alerts' });
    const [row] = within(table).getAllByRole('row').slice(1);
    expect(within(row).getByText(/some\.future_code/)).toBeInTheDocument();
    expect(within(row).getByText('Outcome: Family contacted')).toBeInTheDocument();
    expect(within(table).queryByRole('button')).toBeNull();
  });

  it('shows the table in Arabic', async () => {
    stubBff({ [LIST]: paged([critical]) });

    view('ar');

    expect(await screen.findByText(/لا تحتوي التنبيهات على أي نص من المحادثات/)).toBeInTheDocument();
    const table = await screen.findByRole('table', { name: 'تنبيهات السلامة' });
    const [row] = within(table).getAllByRole('row').slice(1);
    expect(within(row).getByText('حرج')).toBeInTheDocument();
    expect(within(row).getByText('الانتحار أو إيذاء النفس')).toBeInTheDocument();
    expect(within(row).getByText('محادثة المساعد')).toBeInTheDocument();
    expect(within(row).getByText(/ينوي إنهاء حياته/)).toBeInTheDocument();
    expect(within(row).getByText('مفتوح')).toBeInTheDocument();
    expect(row.textContent).not.toMatch(/[٠-٩۰-۹]/);
  });

  it('asks for alerts that need attention by default, and keeps filters in the address', async () => {
    const bff = stubBff({ [LIST]: paged([critical]) });
    const user = userEvent.setup();
    view();
    await screen.findByRole('table', { name: 'Safety alerts' });

    expect(bff.lastQuery(LIST)?.get('status')).toBe('unresolved');
    expect(screen.getByRole('button', { name: 'Needs attention' })).toHaveAttribute('aria-pressed', 'true');

    await user.click(screen.getByRole('button', { name: 'All' }));
    expect(navigation.replace).toHaveBeenLastCalledWith('/safety-alerts?status=all', { scroll: false });
    await waitFor(() => expect(bff.lastQuery(LIST)?.has('status')).toBe(false));

    await user.click(screen.getByRole('button', { name: 'Resolved' }));
    await waitFor(() => expect(bff.lastQuery(LIST)?.get('status')).toBe('resolved'));

    await user.selectOptions(screen.getByLabelText('Concern'), 'harm_to_others');
    await waitFor(() => expect(bff.lastQuery(LIST)?.get('category')).toBe('harm_to_others'));
    expect(navigation.replace).toHaveBeenLastCalledWith('/safety-alerts?category=harm_to_others&status=resolved', { scroll: false });
  });

  it('shows an empty list as good news when nothing needs attention', async () => {
    stubBff({ [LIST]: paged([]) });

    view();

    expect(await screen.findByRole('heading', { name: 'No alerts need attention' })).toBeInTheDocument();
  });

  it('shows a translated error and never the server text', async () => {
    stubBff({ [LIST]: problem(500, undefined, { title: 'SECRET SERVER TITLE', detail: 'SECRET SERVER DETAIL' }) });

    view();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByText(/SECRET/)).toBeNull();
  });

  it('acknowledges an open alert and says so', async () => {
    const bff = stubBff({
      [LIST]: paged([critical]),
      [`${LIST}/${critical.id}/acknowledge`]: { ...critical, status: 'acknowledged', acknowledgedAt: '2026-10-15T10:00:00+00:00' },
    });
    const user = userEvent.setup();
    view();

    await user.click(await screen.findByRole('button', { name: 'Acknowledge alert: Suicide or self-harm, HH-7K2M-9QXA' }));

    expect(await screen.findByText('Alert acknowledged.')).toBeInTheDocument();
    expect(posts(bff, 'acknowledge')).toHaveLength(1);
  });

  it('offers only Resolve on an acknowledged alert', async () => {
    stubBff({ [LIST]: paged([acknowledged]) });
    view();

    const table = await screen.findByRole('table', { name: 'Safety alerts' });
    expect(within(table).queryByRole('button', { name: /^Acknowledge alert/ })).toBeNull();
    expect(within(table).getByRole('button', { name: /^Resolve alert/ })).toBeInTheDocument();
  });

  it('explains a conflict in its own words and reads the list again', async () => {
    let reads = 0;
    stubBff({
      [LIST]: () => {
        reads += 1;
        return paged([critical]);
      },
      [`${LIST}/${critical.id}/acknowledge`]: problem(409, 'safeguarding.alert_not_open', { title: 'SECRET SERVER TITLE', detail: 'SECRET SERVER DETAIL' }),
    });
    const user = userEvent.setup();
    view();

    await user.click(await screen.findByRole('button', { name: /^Acknowledge alert/ }));

    expect(await screen.findByText(/Someone else changed this alert at the same time/)).toBeInTheDocument();
    expect(screen.queryByText(/SECRET/)).toBeNull();
    await waitFor(() => expect(reads).toBeGreaterThan(1));
  });

  it('keeps the actions in view: few columns, both dates in one cell, the actions last in the row', async () => {
    stubBff({ [LIST]: paged([critical]) });
    view();

    const table = await screen.findByRole('table', { name: 'Safety alerts' });
    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'Severity and status',
      'Concern and source',
      'Household and member',
      'Seen',
      'Action',
    ]);
    const [row] = within(table).getAllByRole('row').slice(1);
    const cells = within(row).getAllByRole('cell');
    expect(cells).toHaveLength(5);
    expect(cells[3].textContent).toMatch(/First: .*Last: .*Times: 2/);
    expect(within(cells[4]).getAllByRole('button')).toHaveLength(2);
  });

  it('shows a source and a detector this dashboard does not know yet exactly as recorded', async () => {
    stubBff({ [LIST]: paged([safetyAlert({ source: 'sms_line', detector: 'new_detector' })]) });
    view();

    const table = await screen.findByRole('table', { name: 'Safety alerts' });
    expect(within(table).getByText('sms_line')).toBeInTheDocument();
    expect(within(table).getByText(/Found by: new_detector/)).toBeInTheDocument();
  });

  it('offers Acknowledge and Resolve only with the manage permission', async () => {
    stubBff({ [LIST]: paged([critical, acknowledged]) });
    const readOnly = view('en', false);

    const table = await screen.findByRole('table', { name: 'Safety alerts' });
    expect(within(table).queryByRole('button')).toBeNull();
    expect(within(table).queryByRole('columnheader', { name: 'Action' })).toBeNull();
    expect(within(table).getByText('Suicide or self-harm')).toBeInTheDocument();
    readOnly.unmount();

    view('en', true);
    const managed = await screen.findByRole('table', { name: 'Safety alerts' });
    expect(within(managed).getAllByRole('button', { name: /^Resolve alert/ })).toHaveLength(2);
    expect(within(managed).getAllByRole('button', { name: /^Acknowledge alert/ })).toHaveLength(1);
  });

  it('keeps the last list and says so when a refresh fails, and shows the full error only with no list at all', async () => {
    let reads = 0;
    stubBff({ [LIST]: () => (reads++ === 0 ? paged([critical]) : problem(500)) });
    const { client } = view();
    await screen.findByRole('table', { name: 'Safety alerts' });

    await act(() => client.invalidateQueries({ queryKey: SAFETY_LIST_KEY }));

    expect(await screen.findByText('Could not refresh. Showing the last list.')).toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Safety alerts' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('does not read the list again on a timer', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const bff = stubBff({ [LIST]: paged([critical]) });
      view();
      await screen.findByRole('table', { name: 'Safety alerts' });

      await act(() => vi.advanceTimersByTimeAsync(120_000));

      expect(bff.calls.filter((call) => call.url.pathname === `/bff/${LIST}`)).toHaveLength(1);
    } finally {
      vi.useRealTimers();
    }
  });
});
