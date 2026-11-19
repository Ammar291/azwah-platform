import { screen, userEvent, waitFor, within } from '@testing-library/react-native';

import { renderWithProviders } from '@/test-utils/renderWithProviders';

import { ConsentPanel } from '../ConsentPanel';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));

describe('ConsentPanel', () => {
  it('lists each purpose, with no note that the wording awaits legal review (D-142)', async () => {
    await renderWithProviders(<ConsentPanel />);

    expect(await screen.findByRole('radio', { name: 'You' })).toBeChecked();
    expect(screen.queryByText(/Awaiting approval|legal review/)).toBeNull();
    expect(screen.getAllByRole('header').map((header) => header.props.children)).toEqual([
      'Taking part',
      'Research use',
      'Support referrals',
    ]);
    expect(within(screen.getByTestId('consent-participation')).getByText('Given')).toBeOnTheScreen();
    expect(within(screen.getByTestId('consent-research_use')).getByText('Not given')).toBeOnTheScreen();
  });

  it('names the consent document only once its text is approved', async () => {
    const { api, queryClient, unmount } = await renderWithProviders(<ConsentPanel />);
    await screen.findByTestId('consent-participation');

    // Unapproved: the notice above already says so, and a placeholder reference would only be noise.
    expect(screen.queryByText(/^Document /)).toBeNull();
    expect(JSON.stringify(screen.toJSON())).not.toMatch(/DEV-PLACEHOLDER/);

    await waitFor(() => expect(queryClient.isFetching()).toBe(0));
    await unmount();
    const policies = await api.getCurrentConsentPolicies();
    jest.spyOn(api, 'getCurrentConsentPolicies').mockResolvedValue(
      policies.map((policy) => ({
        ...policy,
        documentReferenceEn: `consent-${policy.consentType}-en`,
        approvedAt: '2026-09-01T00:00:00Z',
      })),
    );
    await renderWithProviders(<ConsentPanel />, { api });

    const participation = await screen.findByTestId('consent-participation');
    expect(within(participation).getByText('Document consent-participation-en, version 1')).toBeOnTheScreen();
  });

  it('asks before withdrawing, then shows the new state', async () => {
    const user = userEvent.setup();
    const { api } = await renderWithProviders(<ConsentPanel />);
    const record = jest.spyOn(api, 'recordConsent');
    const participation = await screen.findByTestId('consent-participation');
    await screen.findByRole('radio', { name: 'You' });

    await user.press(within(participation).getByRole('button', { name: 'Withdraw' }));
    expect(screen.getByRole('header', { name: 'Withdraw consent for Taking part?' })).toBeOnTheScreen();
    expect(
      screen.getByText(/You can't answer until you consent again/),
    ).toBeOnTheScreen();
    await user.press(within(screen.getByTestId('confirmation-dialog')).getByRole('button', { name: 'Withdraw' }));

    expect(await within(participation).findByText('Not given')).toBeOnTheScreen();
    expect(record).toHaveBeenCalledWith(expect.objectContaining({ decision: 'withdraw' }));
  });

  it('gives consent with one press', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<ConsentPanel />);
    const research = await screen.findByTestId('consent-research_use');
    await screen.findByRole('radio', { name: 'You' });

    await user.press(within(research).getByRole('button', { name: 'Give consent' }));

    expect(await within(research).findByText('Given')).toBeOnTheScreen();
  });

  it('lets the household lead choose a relative without an account', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<ConsentPanel />);

    const spouse = await screen.findByRole('radio', { name: 'Spouse' });
    expect(screen.getByRole('radio', { name: 'You' })).toBeChecked();
    await user.press(spouse);

    expect(within(screen.getByTestId('consent-participation')).getByText('Not given')).toBeOnTheScreen();
  });

  it('asks for a household first', async () => {
    await renderWithProviders(<ConsentPanel />, { persona: 'new_participant' });

    expect(await screen.findByText('Set up your household first')).toBeOnTheScreen();
  });
});
