import { render, screen, userEvent } from '@testing-library/react-native';

import { ConfirmationDialog } from '../ConfirmationDialog';

const props = {
  title: 'Withdraw consent?',
  message: 'You can give it again later.',
  confirmLabel: 'Withdraw',
  cancelLabel: 'Keep consent',
};

describe('ConfirmationDialog', () => {
  it('shows nothing while hidden', async () => {
    await render(<ConfirmationDialog {...props} visible={false} onConfirm={jest.fn()} onCancel={jest.fn()} />);

    expect(screen.queryByText('Withdraw consent?')).toBeNull();
  });

  it('asks, then confirms or cancels', async () => {
    const onConfirm = jest.fn();
    const onCancel = jest.fn();
    const user = userEvent.setup();
    await render(<ConfirmationDialog {...props} visible destructive onConfirm={onConfirm} onCancel={onCancel} />);

    expect(screen.getByRole('header', { name: 'Withdraw consent?' })).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Withdraw' }));
    await user.press(screen.getByRole('button', { name: 'Keep consent' }));

    expect(onConfirm).toHaveBeenCalledTimes(1);
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('says inside the dialog when the action failed, so it can be tried again or cancelled', async () => {
    const onConfirm = jest.fn();
    const user = userEvent.setup();
    await render(
      <ConfirmationDialog
        {...props}
        visible
        problem={{ title: 'Something went wrong', message: 'Try again.' }}
        onConfirm={onConfirm}
        onCancel={jest.fn()}
      />,
    );

    expect(screen.getByRole('alert')).toHaveTextContent('Something went wrongTry again.');
    await user.press(screen.getByRole('button', { name: 'Withdraw' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('cannot be dismissed while the action runs', async () => {
    const onCancel = jest.fn();
    const user = userEvent.setup();
    await render(<ConfirmationDialog {...props} visible busy onConfirm={jest.fn()} onCancel={onCancel} />);

    expect(screen.getByRole('button', { name: 'Withdraw' })).toBeBusy();
    await user.press(screen.getByRole('button', { name: 'Keep consent' }));

    expect(onCancel).not.toHaveBeenCalled();
  });
});
