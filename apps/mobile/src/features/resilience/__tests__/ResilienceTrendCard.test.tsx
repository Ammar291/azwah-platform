import { screen, userEvent, within } from '@testing-library/react-native';

import { ApiError } from '@/api/client';
import type { ParticipantApi } from '@/api/participant/participantApi';
import type { ResilienceTrend } from '@/api/participant/schemas';
import { createFixtureParticipantApi } from '@/dev-fixtures/fixtureParticipantApi';
import { formatDate } from '@/i18n/format';
import { PLACEHOLDER_CATALOG } from '@/test-utils/placeholderContent';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';

import { ResilienceTrendCard } from '../ResilienceTrendCard';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));

afterEach(settleQueries);

// The owner-approved cut-offs and band names for the household assessment (D-103); the totals are synthetic.
const SCALE: NonNullable<ResilienceTrend['scale']> = {
  minimum: 39,
  maximum: 156,
  bands: [
    { code: 'vulnerable', min: 39, max: 78 },
    { code: 'transitional', min: 79, max: 117 },
    { code: 'flourishing', min: 118, max: 156 },
  ],
};

const point = (wave: number, submittedAt: string, totalScore: number, tierCode: string | null, segment = 0) => ({
  sessionId: `0199e0a1-0000-7000-8000-0000000000a${wave}`,
  waveCode: `wave_${wave}`,
  submittedAt,
  totalScore,
  tierCode,
  segment,
});

const THREE_ROUNDS: ResilienceTrend = {
  scoreVisibility: 'visible',
  scale: SCALE,
  points: [
    point(1, '2025-11-12T09:00:00Z', 69, 'vulnerable'),
    point(2, '2026-05-10T09:00:00Z', 88, 'transitional'),
    point(3, '2026-11-08T09:00:00Z', 121, 'flourishing'),
  ],
  notShownCount: 0,
};

/** The fixture API with the trend replaced, so each test states exactly what the server sent. */
function apiReturning(trend: ResilienceTrend | Error): ParticipantApi & { getResilienceTrend: jest.Mock } {
  const api = createFixtureParticipantApi({
    getSubject: () => 'dev-mock|household_lead',
    latencyMs: 0,
    catalog: PLACEHOLDER_CATALOG,
  });
  return {
    ...api,
    getResilienceTrend: jest.fn(() => (trend instanceof Error ? Promise.reject(trend) : Promise.resolve(trend))),
  };
}

const date = (iso: string) => formatDate(iso, 'en');

describe('ResilienceTrendCard', () => {
  it('shows the latest round above the chart, and another round once it is chosen', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<ResilienceTrendCard />, { api: apiReturning(THREE_ROUNDS) });

    expect(await screen.findByRole('header', { name: 'Over time' })).toBeOnTheScreen();
    const selected = () => within(screen.getByTestId('trend-selected'));
    expect(selected().getByText('Latest round')).toBeOnTheScreen();
    expect(selected().getByText('121')).toBeOnTheScreen();
    expect(selected().getByText('Flourishing')).toBeOnTheScreen();
    expect(selected().getByText(`Round 3 · ${date('2026-11-08T09:00:00Z')}`)).toBeOnTheScreen();
    expect(
      screen.getByRole('image', {
        name: `Line chart of your family's total score in 3 rounds, on a scale from 39 to 156. Latest: 121 · Flourishing, sent on ${date('2026-11-08T09:00:00Z')}.`,
      }),
    ).toBeOnTheScreen();

    await user.press(
      screen.getByRole('button', { name: `Round 1, sent on ${date('2025-11-12T09:00:00Z')}: 69 · Vulnerable` }),
    );

    expect(selected().getByText('Chosen round')).toBeOnTheScreen();
    expect(selected().getByText('69')).toBeOnTheScreen();
    expect(selected().getByText('Vulnerable')).toBeOnTheScreen();
    expect(
      screen.getByRole('button', { name: /^Round 1,/, selected: true }),
    ).toBeOnTheScreen();
  });

  it('labels the scale and its bands from the server, and the first and last rounds', async () => {
    await renderWithProviders(<ResilienceTrendCard />, { api: apiReturning(THREE_ROUNDS) });
    await screen.findByTestId('trend-chart');

    for (const text of ['39', '79', '118', '156', 'Vulnerable', 'Transitional', 'Flourishing']) {
      expect(screen.getAllByText(text, { includeHiddenElements: true }).length).toBeGreaterThan(0);
    }
    expect(screen.getByText(formatDate('2025-11-12T09:00:00Z', 'en', 'monthYear'), { includeHiddenElements: true })).toBeOnTheScreen();
    expect(screen.getByText(formatDate('2026-11-08T09:00:00Z', 'en', 'monthYear'), { includeHiddenElements: true })).toBeOnTheScreen();
  });

  it('lists every round, newest first, for reading without the chart', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<ResilienceTrendCard />, { api: apiReturning(THREE_ROUNDS) });

    await user.press(await screen.findByRole('button', { name: 'All rounds' }));

    const rows = within(screen.getByTestId('trend-list')).getAllByLabelText(/^Round \d/);
    expect(rows.map((row) => row.props.accessibilityLabel)).toEqual([
      `Round 3 · ${date('2026-11-08T09:00:00Z')}: 121 · Flourishing`,
      `Round 2 · ${date('2026-05-10T09:00:00Z')}: 88 · Transitional`,
      `Round 1 · ${date('2025-11-12T09:00:00Z')}: 69 · Vulnerable`,
    ]);

    await user.press(screen.getByRole('button', { name: 'Hide list' }));
    expect(screen.queryByTestId('trend-list')).toBeNull();
  });

  it('says which rounds are left off and that a change of scoring rules breaks the line', async () => {
    const trend: ResilienceTrend = {
      ...THREE_ROUNDS,
      points: [
        point(1, '2025-11-12T09:00:00Z', 69, 'vulnerable', 0),
        point(2, '2026-05-10T09:00:00Z', 88, 'transitional', 0),
        point(3, '2026-11-08T09:00:00Z', 121, 'flourishing', 1),
      ],
      notShownCount: 2,
    };
    await renderWithProviders(<ResilienceTrendCard />, { api: apiReturning(trend) });

    expect(
      await screen.findByText(
        'Not charted: 2.',
      ),
    ).toBeOnTheScreen();
    expect(
      screen.getByText('Rules changed, so some points aren\'t joined.'),
    ).toBeOnTheScreen();
  });

  it('shows a single round as a point, and a total without a band as the number alone', async () => {
    const trend: ResilienceTrend = { ...THREE_ROUNDS, points: [point(1, '2026-05-10T09:00:00Z', 88, null)] };
    await renderWithProviders(<ResilienceTrendCard />, { api: apiReturning(trend) });

    expect(await screen.findByText('The line starts next round.')).toBeOnTheScreen();
    expect(
      screen.getByRole('button', { name: `Round 1, sent on ${date('2026-05-10T09:00:00Z')}: 88` }),
    ).toBeOnTheScreen();
    expect(screen.queryByText('Not charted', { exact: false })).toBeNull();
  });

  it('waits for the first recorded score, counting the rounds still without one', async () => {
    const trend: ResilienceTrend = { scoreVisibility: 'visible', scale: null, points: [], notShownCount: 1 };
    await renderWithProviders(<ResilienceTrendCard />, { api: apiReturning(trend) });

    const empty = within(await screen.findByTestId('trend-empty'));
    expect(empty.getByText('No scores yet')).toBeOnTheScreen();
    expect(empty.getByText('Not charted: 1.')).toBeOnTheScreen();
    expect(screen.queryByTestId('trend-chart')).toBeNull();
  });

  it("tells anyone but the head of household that the chart is the head's, with no value", async () => {
    const trend: ResilienceTrend = { scoreVisibility: 'head_of_household_only', scale: null, points: [], notShownCount: 0 };
    await renderWithProviders(<ResilienceTrendCard />, { api: apiReturning(trend) });

    expect(
      await screen.findByText("Only the head sees this chart."),
    ).toBeOnTheScreen();
    expect(screen.queryByTestId('trend-chart')).toBeNull();
  });

  it('shows a translated problem with a retry when the chart cannot be read', async () => {
    const user = userEvent.setup();
    const api = apiReturning(new ApiError('network'));
    await renderWithProviders(<ResilienceTrendCard />, { api });

    await user.press(await screen.findByRole('button', { name: 'Try again' }));

    expect(api.getResilienceTrend).toHaveBeenCalledTimes(2);
  });

  it('names the bands in Arabic and keeps Western digits', async () => {
    await renderWithProviders(<ResilienceTrendCard />, { api: apiReturning(THREE_ROUNDS), language: 'ar' });

    const selected = within(await screen.findByTestId('trend-selected'));
    expect(selected.getByText('121')).toBeOnTheScreen();
    expect(selected.getByText('مزدهر')).toBeOnTheScreen();
    expect(screen.getByRole('header', { name: 'عبر الوقت' })).toBeOnTheScreen();
  });
});
