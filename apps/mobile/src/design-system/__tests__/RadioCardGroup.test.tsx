import { render, screen, userEvent } from '@testing-library/react-native';

import { RadioCardGroup } from '../RadioCardGroup';

const OPTIONS = [
  { value: 'spouse', label: 'Spouse' },
  { value: 'child', label: 'Child', description: 'Son or daughter' },
  { value: 'other', label: 'Other relative' },
] as const;

describe('RadioCardGroup', () => {
  it('is a named radio group with one radio per option', async () => {
    await render(<RadioCardGroup label="Relationship" options={OPTIONS} value={null} onChange={jest.fn()} />);

    expect(screen.getByLabelText('Relationship')).toHaveProp('accessibilityRole', 'radiogroup');
    expect(screen.getAllByRole('radio')).toHaveLength(3);
    expect(screen.getByText('Son or daughter')).toBeOnTheScreen();
  });

  it('reports the chosen value and marks only it as checked', async () => {
    const onChange = jest.fn();
    const user = userEvent.setup();
    await render(<RadioCardGroup label="Relationship" options={OPTIONS} value="child" onChange={onChange} />);

    expect(screen.getByRole('radio', { name: 'Child' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'Spouse' })).not.toBeChecked();

    await user.press(screen.getByRole('radio', { name: 'Spouse' }));
    expect(onChange).toHaveBeenCalledWith('spouse');
  });

  it('shows selection with a check mark as well as colour', async () => {
    await render(<RadioCardGroup label="Relationship" options={OPTIONS} value="other" onChange={jest.fn()} />);

    expect(screen.getAllByTestId('icon-check', { includeHiddenElements: true })).toHaveLength(1);
  });

  it('can hide the visible label but keeps the accessible name', async () => {
    await render(
      <RadioCardGroup label="How often?" hideLabel options={OPTIONS} value={null} onChange={jest.fn()} />,
    );

    expect(screen.queryByText('How often?')).toBeNull();
    expect(screen.getByLabelText('How often?')).toHaveProp('accessibilityRole', 'radiogroup');
  });

  it('announces an error', async () => {
    await render(
      <RadioCardGroup label="Relationship" options={OPTIONS} value={null} onChange={jest.fn()} error="Choose one" />,
    );

    expect(screen.getByRole('alert')).toHaveTextContent('Choose one');
  });

  it('ignores presses while disabled', async () => {
    const onChange = jest.fn();
    const user = userEvent.setup();
    await render(<RadioCardGroup label="R" options={OPTIONS} value={null} onChange={onChange} disabled />);

    await user.press(screen.getByRole('radio', { name: 'Spouse' }));

    expect(onChange).not.toHaveBeenCalled();
  });
});
