import { render, screen, userEvent } from '@testing-library/react-native';
import { StyleSheet } from 'react-native';

import { LikertScale } from '../LikertScale';
import { colors } from '../tokens';

const FOUR = [
  { value: 1, label: 'Strongly disagree' },
  { value: 2, label: 'Disagree' },
  { value: 3, label: 'Agree' },
  { value: 4, label: 'Strongly agree' },
];
const FIVE = [
  { value: 1, label: 'Never' },
  { value: 2, label: 'Rarely' },
  { value: 3, label: 'Sometimes' },
  { value: 4, label: 'Often' },
  { value: 5, label: 'Always' },
];
const PHYSICAL_KEYS = ['left', 'right', 'marginLeft', 'marginRight', 'paddingLeft', 'paddingRight'];

describe('LikertScale', () => {
  it('shows the question and one radio per option', async () => {
    await render(<LikertScale label="How much do you agree?" options={FOUR} value={null} onChange={jest.fn()} />);

    expect(screen.getByText('How much do you agree?')).toBeOnTheScreen();
    expect(screen.getAllByRole('radio')).toHaveLength(4);
  });

  it('supports five options', async () => {
    await render(<LikertScale label="How often?" options={FIVE} value={null} onChange={jest.fn()} />);

    expect(screen.getAllByRole('radio').map((radio) => radio.props.accessibilityLabel)).toEqual([
      'Never',
      'Rarely',
      'Sometimes',
      'Often',
      'Always',
    ]);
  });

  it('reports the chosen value', async () => {
    const onChange = jest.fn();
    const user = userEvent.setup();
    await render(<LikertScale label="Q" options={FOUR} value={null} onChange={onChange} />);

    await user.press(screen.getByRole('radio', { name: 'Agree' }));

    expect(onChange).toHaveBeenCalledWith(3);
  });

  it('marks only the selected option as checked', async () => {
    await render(<LikertScale label="Q" options={FOUR} value={2} onChange={jest.fn()} />);

    expect(screen.getByRole('radio', { name: 'Disagree' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'Agree' })).not.toBeChecked();
  });

  it('ignores presses while disabled', async () => {
    const onChange = jest.fn();
    const user = userEvent.setup();
    await render(<LikertScale label="Q" options={FOUR} value={null} onChange={onChange} disabled />);

    await user.press(screen.getByRole('radio', { name: 'Agree' }));

    expect(onChange).not.toHaveBeenCalled();
  });

  const OUTSIDE = { value: 99, label: 'Prefer not to answer', nonSubstantive: true };
  const labels = () => screen.getAllByRole('radio').map((radio) => radio.props.accessibilityLabel as string);
  const styleOf = (name: string) =>
    (StyleSheet.flatten(screen.getByRole('radio', { name }).props.style) ?? {}) as Record<string, unknown>;

  it('keeps the order it is given, exactly: the version decides where an answer outside the scale goes', async () => {
    const first = await render(
      <LikertScale label="How often?" options={[OUTSIDE, ...FIVE]} value={null} onChange={jest.fn()} />,
    );
    expect(labels()).toEqual(['Prefer not to answer', 'Never', 'Rarely', 'Sometimes', 'Often', 'Always']);
    await first.unmount();

    await render(
      <LikertScale
        label="How often?"
        options={[...FIVE.slice(0, 2), OUTSIDE, ...FIVE.slice(2)]}
        value={null}
        onChange={jest.fn()}
      />,
    );
    expect(labels()).toEqual(['Never', 'Rarely', 'Prefer not to answer', 'Sometimes', 'Often', 'Always']);
  });

  it('sets an answer outside the scale apart with a divider and a quieter card, without changing its label', async () => {
    await render(<LikertScale label="How often?" options={[...FIVE, OUTSIDE]} value={null} onChange={jest.fn()} />);

    expect(labels()).toEqual(['Never', 'Rarely', 'Sometimes', 'Often', 'Always', 'Prefer not to answer']);
    expect(screen.getAllByTestId('radio-divider')).toHaveLength(1);
    expect(styleOf('Prefer not to answer').backgroundColor).toBe(colors.surfaceMuted);
    expect(styleOf('Always').backgroundColor).toBe(colors.surface);
    expect(screen.getByText('Prefer not to answer')).toBeOnTheScreen();
  });

  it('draws the divider only before an answer outside the scale that follows the scale, and none without one', async () => {
    const inside = await render(
      <LikertScale
        label="Q"
        options={[FIVE[0], OUTSIDE, FIVE[1]].flatMap((o) => (o ? [o] : []))}
        value={null}
        onChange={jest.fn()}
      />,
    );
    expect(screen.getAllByTestId('radio-divider')).toHaveLength(1);
    await inside.unmount();

    // First in the version's order: nothing comes before it to divide from, but its card is still the quieter one.
    const first = await render(<LikertScale label="Q" options={[OUTSIDE, ...FIVE]} value={null} onChange={jest.fn()} />);
    expect(screen.queryByTestId('radio-divider')).toBeNull();
    expect(styleOf('Prefer not to answer').backgroundColor).toBe(colors.surfaceMuted);
    await first.unmount();

    await render(<LikertScale label="Q" options={FIVE} value={null} onChange={jest.fn()} />);
    expect(screen.queryByTestId('radio-divider')).toBeNull();
  });

  it('reports the value of an answer outside the scale like any other, and shows it selected', async () => {
    const onChange = jest.fn();
    const user = userEvent.setup();
    const chosen = await render(
      <LikertScale label="Q" options={[...FIVE, OUTSIDE]} value={null} onChange={onChange} />,
    );
    await user.press(screen.getByRole('radio', { name: 'Prefer not to answer' }));
    expect(onChange).toHaveBeenCalledWith(99);
    await chosen.unmount();

    await render(<LikertScale label="Q" options={[...FIVE, OUTSIDE]} value={99} onChange={onChange} />);
    expect(screen.getByRole('radio', { name: 'Prefer not to answer' })).toBeChecked();
    expect(styleOf('Prefer not to answer').backgroundColor).toBe(colors.primarySoft);
  });

  it('accepts option IDs as values', async () => {
    const onChange = jest.fn();
    const user = userEvent.setup();
    const options = [
      { value: 'opt-a', label: 'A' },
      { value: 'opt-b', label: 'B' },
    ];
    await render(<LikertScale label="Q" options={options} value="opt-a" onChange={onChange} />);

    await user.press(screen.getByRole('radio', { name: 'B' }));

    expect(onChange).toHaveBeenCalledWith('opt-b');
  });

  it('uses only start/end styles so it mirrors in Arabic', async () => {
    await render(<LikertScale label="Q" options={FOUR} value={1} onChange={jest.fn()} />);

    for (const radio of screen.getAllByRole('radio')) {
      const style = (StyleSheet.flatten(radio.props.style) ?? {}) as Record<string, unknown>;
      expect(Object.keys(style).filter((key) => PHYSICAL_KEYS.includes(key))).toEqual([]);
    }
  });
});
