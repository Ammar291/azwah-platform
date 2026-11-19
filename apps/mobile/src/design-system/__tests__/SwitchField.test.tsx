import { render, screen, userEvent } from '@testing-library/react-native';

import { SwitchField } from '../SwitchField';

describe('SwitchField', () => {
  it('is one labelled switch that toggles from the whole row', async () => {
    const onValueChange = jest.fn();
    const user = userEvent.setup();
    await render(
      <SwitchField label="Head of household" description="One per household" value={false} onValueChange={onValueChange} />,
    );
    const toggle = screen.getByRole('switch', { name: 'Head of household' });

    expect(toggle).not.toBeChecked();
    await user.press(toggle);

    expect(onValueChange).toHaveBeenCalledWith(true);
  });

  it('reports the checked state', async () => {
    await render(<SwitchField label="Main earner" value onValueChange={jest.fn()} />);

    expect(screen.getByRole('switch', { name: 'Main earner' })).toBeChecked();
  });
});
