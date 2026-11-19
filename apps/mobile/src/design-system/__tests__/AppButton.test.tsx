import { fireEvent, render, screen, userEvent } from '@testing-library/react-native';

import { AppButton } from '../AppButton';
import { ButtonRow } from '../ButtonRow';

/** The phone the owner tested on: 360 points wide. Jest's default window is 750 wide at 200% text. */
const mockWindow = { width: 360, height: 800, scale: 2, fontScale: 1 };
jest.mock('react-native/Libraries/Utilities/useWindowDimensions', () => ({
  __esModule: true,
  default: () => mockWindow,
}));

beforeEach(() => {
  mockWindow.width = 360;
  mockWindow.fontScale = 1;
});

describe('AppButton', () => {
  it('is an accessible button that reports presses', async () => {
    const onPress = jest.fn();
    const user = userEvent.setup();
    await render(<AppButton label="Try again" onPress={onPress} />);

    await user.press(screen.getByRole('button', { name: 'Try again' }));

    expect(onPress).toHaveBeenCalledTimes(1);
  });

  it('can carry a fuller name for screen readers that starts with the visible label', async () => {
    await render(<AppButton label="Edit" accessibilityLabel="Edit Spouse, born September 1987" onPress={jest.fn()} />);

    expect(screen.getByRole('button', { name: 'Edit Spouse, born September 1987' })).toHaveTextContent('Edit');
  });

  it('ignores presses while disabled', async () => {
    const onPress = jest.fn();
    const user = userEvent.setup();
    await render(<AppButton label="Save" onPress={onPress} disabled />);
    const button = screen.getByRole('button', { name: 'Save' });

    expect(button).toBeDisabled();
    await user.press(button);
    expect(onPress).not.toHaveBeenCalled();
  });

  it('announces busy and ignores presses while loading', async () => {
    const onPress = jest.fn();
    const user = userEvent.setup();
    await render(<AppButton label="Submit" onPress={onPress} loading />);
    const button = screen.getByRole('button', { name: 'Submit' });

    expect(button).toBeBusy();
    await user.press(button);
    expect(onPress).not.toHaveBeenCalled();
  });

  it.each(['primary', 'secondary', 'tertiary', 'danger'] as const)('renders the %s variant', async (variant) => {
    await render(<AppButton label="Go" variant={variant} onPress={jest.fn()} />);

    expect(screen.getByRole('button', { name: 'Go' })).toBeOnTheScreen();
  });

  it('is at least 48 points tall', async () => {
    await render(<AppButton label="Go" onPress={jest.fn()} />);

    expect(screen.getByRole('button', { name: 'Go' })).toHaveStyle({ minHeight: 48 });
  });

  it('keeps its label on one line at ordinary text sizes, shrinking it a little before it would break', async () => {
    mockWindow.fontScale = 1;
    await render(<AppButton label="Review answers" onPress={jest.fn()} />);

    const label = screen.getByText('Review answers');
    expect(label).toHaveProp('numberOfLines', 1);
    expect(label).toHaveProp('adjustsFontSizeToFit', true);
    expect(label.props.minimumFontScale).toBeGreaterThanOrEqual(0.8);
  });

  it('lets the label wrap at large text sizes rather than cut it off', async () => {
    mockWindow.fontScale = 2;
    await render(<AppButton label="Review answers" onPress={jest.fn()} />);

    const label = screen.getByText('Review answers');
    expect(label.props.numberOfLines).toBeUndefined();
    expect(label.props.adjustsFontSizeToFit).toBeFalsy();
    // Font scaling is never turned off.
    expect(label.props.allowFontScaling).not.toBe(false);
  });
});

describe('ButtonRow', () => {
  it('puts buttons side by side with equal widths and compact padding when they fit', async () => {
    mockWindow.fontScale = 1;
    await render(
      <ButtonRow>
        <AppButton label="Low" onPress={jest.fn()} />
        <AppButton label="Medium" onPress={jest.fn()} />
        <AppButton label="High" onPress={jest.fn()} />
      </ButtonRow>,
    );
    fireEvent(screen.getByTestId('button-row'), 'layout', { nativeEvent: { layout: { width: 312, height: 48 } } });

    expect(screen.getByTestId('button-row')).toHaveStyle({ flexDirection: 'row' });
    for (const cell of screen.getAllByTestId('button-row-cell')) {
      expect(cell).toHaveStyle({ flexGrow: 1, flexBasis: 0 });
    }
    expect(screen.getByRole('button', { name: 'Medium' })).toHaveStyle({ paddingHorizontal: 12 });
  });

  it('stacks the buttons when they cannot fit side by side on a 360-point phone', async () => {
    mockWindow.fontScale = 1;
    await render(
      <ButtonRow minButtonWidth={180}>
        <AppButton label="Start" onPress={jest.fn()} />
        <AppButton label="Auto-fill" onPress={jest.fn()} />
      </ButtonRow>,
    );
    fireEvent(screen.getByTestId('button-row'), 'layout', { nativeEvent: { layout: { width: 312, height: 48 } } });

    expect(screen.getByTestId('button-row')).not.toHaveStyle({ flexDirection: 'row' });
    expect(screen.getByRole('button', { name: 'Start' })).toHaveStyle({ paddingHorizontal: 24 });
  });

  it('stacks at large text sizes, where labels need more room', async () => {
    mockWindow.fontScale = 2;
    await render(
      <ButtonRow>
        <AppButton label="Previous" onPress={jest.fn()} />
        <AppButton label="Next" onPress={jest.fn()} />
      </ButtonRow>,
    );
    fireEvent(screen.getByTestId('button-row'), 'layout', { nativeEvent: { layout: { width: 312, height: 48 } } });

    expect(screen.getByTestId('button-row')).not.toHaveStyle({ flexDirection: 'row' });
  });

  it('measures the screen when it has not been laid out yet', async () => {
    mockWindow.fontScale = 1;
    mockWindow.width = 360;
    await render(
      <ButtonRow>
        <AppButton label="Previous" onPress={jest.fn()} />
        <AppButton label="Next" onPress={jest.fn()} />
      </ButtonRow>,
    );

    expect(screen.getByTestId('button-row')).toHaveStyle({ flexDirection: 'row' });
  });
});
