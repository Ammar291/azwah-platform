import { render, screen, userEvent } from '@testing-library/react-native';

import { Badge } from '../Badge';
import { EmptyState } from '../EmptyState';
import { ErrorState } from '../ErrorState';
import { LoadingState } from '../LoadingState';
import { Notice } from '../Notice';
import { ProgressIndicator } from '../ProgressIndicator';

describe('ProgressIndicator', () => {
  it('exposes its position as a progressbar with text', async () => {
    await render(<ProgressIndicator current={3} total={10} label="Question 3 of 10" />);
    const bar = screen.getByRole('progressbar');

    expect(screen.getByText('Question 3 of 10')).toBeOnTheScreen();
    expect(bar).toHaveAccessibilityValue({ min: 0, max: 10, now: 3, text: 'Question 3 of 10' });
    expect(screen.getByTestId('progress-fill')).toHaveStyle({ width: '30%' });
  });

  it('stays within bounds', async () => {
    await render(<ProgressIndicator current={12} total={0} label="Done" />);

    expect(screen.getByRole('progressbar')).toHaveAccessibilityValue({ min: 0, max: 1, now: 1 });
  });
});

describe('Badge', () => {
  it.each([
    ['success', 'icon-check'],
    ['warning', 'icon-alert'],
    ['danger', 'icon-alert'],
    ['info', 'icon-info'],
    ['neutral', 'icon-dot'],
  ] as const)('pairs the %s tone with a glyph and words', async (tone, glyph) => {
    await render(<Badge label="Completed" tone={tone} />);

    expect(screen.getByText('Completed')).toBeOnTheScreen();
    expect(screen.getByTestId(glyph, { includeHiddenElements: true })).toBeOnTheScreen();
  });
});

describe('Notice', () => {
  it('shows a title, message and action', async () => {
    const onPress = jest.fn();
    const user = userEvent.setup();
    await render(
      <Notice tone="warning" title="Placeholder" message="Awaiting approval" action={{ label: 'More', onPress }} />,
    );

    expect(screen.getByText('Placeholder')).toBeOnTheScreen();
    expect(screen.getByText('Awaiting approval')).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'More' }));
    expect(onPress).toHaveBeenCalledTimes(1);
  });

  it('is announced only when asked', async () => {
    await render(<Notice message="Saved" />);
    expect(screen.queryByRole('alert')).toBeNull();

    await render(<Notice tone="danger" message="Your session ended" announce />);
    expect(screen.getByRole('alert')).toHaveTextContent('Your session ended');
  });
});

describe('states', () => {
  it('LoadingState is busy and labelled', async () => {
    await render(<LoadingState label="Loading your household" />);

    expect(screen.getByLabelText('Loading your household')).toBeBusy();
  });

  it('ErrorState announces the problem and offers both ways forward', async () => {
    const retry = jest.fn();
    const home = jest.fn();
    const user = userEvent.setup();
    await render(
      <ErrorState
        title="Can't reach Azwah"
        message="Check your connection"
        retry={{ label: 'Try again', onPress: retry }}
        action={{ label: 'Go home', onPress: home }}
      />,
    );

    expect(screen.getByRole('alert')).toHaveTextContent("Can't reach Azwah");
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    await user.press(screen.getByRole('button', { name: 'Go home' }));
    expect(retry).toHaveBeenCalledTimes(1);
    expect(home).toHaveBeenCalledTimes(1);
  });

  it('EmptyState explains and offers the next step', async () => {
    const onPress = jest.fn();
    await render(
      <EmptyState title="No questionnaires yet" message="Confirm your household first" action={{ label: 'Open household', onPress }} />,
    );

    expect(screen.getByRole('header', { name: 'No questionnaires yet' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Open household' })).toBeOnTheScreen();
  });
});
