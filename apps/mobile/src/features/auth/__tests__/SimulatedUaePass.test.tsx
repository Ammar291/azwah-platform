import { act, screen, userEvent, waitFor } from '@testing-library/react-native';

import { redirects, resetRouterMock } from '@/test-utils/expoRouterMock';
import { renderWithProviders } from '@/test-utils/renderWithProviders';

import { SIMULATED_HANDOVER_MS, SimulatedUaePass } from '../SimulatedUaePass';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));

beforeEach(() => resetRouterMock());

afterEach(() => jest.useRealTimers());

describe('SimulatedUaePass', () => {
  it('is a plain account choice that never calls itself UAE PASS, listing the made-up accounts (D-142)', async () => {
    await renderWithProviders(<SimulatedUaePass />, { persona: null });

    expect(screen.getByText('Choose an account')).toBeOnTheScreen();
    expect(screen.queryByText(/UAE PASS|simulated/i)).toBeNull();
    expect(screen.getByRole('radio', { name: /Layla Hassan Al Amiri/ })).toBeOnTheScreen();
    expect(screen.getByRole('radio', { name: /Omar Khalid Al Suwaidi/ })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
  });

  it('shows no UAE PASS hand-over, then signs in as the chosen account with its made-up details', async () => {
    jest.useFakeTimers();
    const user = userEvent.setup({ advanceTimers: jest.advanceTimersByTime });
    const { auth } = await renderWithProviders(<SimulatedUaePass />, { persona: null });

    await user.press(screen.getByRole('radio', { name: /Omar Khalid Al Suwaidi/ }));
    await user.press(screen.getByRole('button', { name: 'Continue' }));

    expect(screen.queryByText('Connecting to UAE PASS')).toBeNull();
    expect(screen.getByRole('button', { name: 'Continue' })).toBeBusy();
    expect(auth.getState().status).toBe('signed_out');

    await act(async () => jest.advanceTimersByTime(SIMULATED_HANDOVER_MS));

    await waitFor(() =>
      expect(auth.getState()).toMatchObject({
        status: 'signed_in',
        session: { personaCode: 'new_participant', profile: { source: 'simulated', givenNameEn: 'Omar' } },
      }),
    );
  });

  it('is unavailable outside the development fixtures', async () => {
    await renderWithProviders(<SimulatedUaePass />, { mode: 'api' });

    expect(redirects).toEqual(['/welcome']);
  });
});
