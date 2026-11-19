import { screen, userEvent, waitFor, within } from '@testing-library/react-native';

import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';

import { SignOutDialog } from '../SignOutDialog';

afterEach(settleQueries);

const confirm = () => within(screen.getByTestId('confirmation-dialog')).getByRole('button', { name: 'Sign out' });

describe('SignOutDialog', () => {
  it('signs out and closes', async () => {
    const user = userEvent.setup();
    const onClose = jest.fn();
    const { auth } = await renderWithProviders(<SignOutDialog visible onClose={onClose} />);

    await user.press(confirm());

    await waitFor(() => expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'signed_out' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('says so and stays open when signing out fails, then signs out on another try', async () => {
    const user = userEvent.setup();
    const onClose = jest.fn();
    const { auth } = await renderWithProviders(<SignOutDialog visible onClose={onClose} />);
    const signOut = jest.spyOn(auth, 'signOut').mockRejectedValueOnce(new Error('storage'));

    await user.press(confirm());

    const dialog = within(screen.getByTestId('confirmation-dialog'));
    expect(await dialog.findByRole('alert')).toHaveTextContent(/Something went wrong/);
    expect(dialog.getByRole('alert')).toHaveTextContent(/Try again, or restart the app\./);
    expect(onClose).not.toHaveBeenCalled();
    expect(auth.getState()).toMatchObject({ status: 'signed_in' });
    expect(confirm()).not.toBeBusy();

    await user.press(confirm());

    await waitFor(() => expect(auth.getState()).toMatchObject({ status: 'signed_out' }));
    expect(signOut).toHaveBeenCalledTimes(2);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('forgets the failure when the person cancels', async () => {
    const user = userEvent.setup();
    const onClose = jest.fn();
    const { auth } = await renderWithProviders(<SignOutDialog visible onClose={onClose} />);
    jest.spyOn(auth, 'signOut').mockRejectedValueOnce(new Error('storage'));
    await user.press(confirm());
    await within(screen.getByTestId('confirmation-dialog')).findByRole('alert');

    await user.press(within(screen.getByTestId('confirmation-dialog')).getByRole('button', { name: 'Cancel' }));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('alert')).toBeNull();
  });
});
