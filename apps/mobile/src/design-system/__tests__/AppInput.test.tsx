import { render, screen, userEvent } from '@testing-library/react-native';

import { AppInput } from '../AppInput';

describe('AppInput', () => {
  it('is labelled and reports typing', async () => {
    const onChangeText = jest.fn();
    const user = userEvent.setup();
    await render(<AppInput label="Birth year" hint="Four digits" onChangeText={onChangeText} />);

    expect(screen.getByText('Four digits')).toBeOnTheScreen();
    await user.type(screen.getByLabelText('Birth year'), '1980');

    expect(onChangeText).toHaveBeenLastCalledWith('1980');
  });

  it('announces a validation message', async () => {
    await render(<AppInput label="Birth year" error="Enter a year" />);

    expect(screen.getByRole('alert')).toHaveTextContent('Enter a year');
  });

  it('cannot be edited while disabled', async () => {
    await render(<AppInput label="Reference" disabled value="HH-1" />);

    expect(screen.getByLabelText('Reference')).toHaveProp('editable', false);
  });
});
