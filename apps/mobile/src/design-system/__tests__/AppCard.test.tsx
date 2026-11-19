import { render, screen, userEvent } from '@testing-library/react-native';

import { AppCard } from '../AppCard';
import { AppText } from '../AppText';

describe('AppCard', () => {
  it('is a plain container without onPress', async () => {
    await render(
      <AppCard>
        <AppText>Household</AppText>
      </AppCard>,
    );

    expect(screen.getByText('Household')).toBeOnTheScreen();
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('becomes one labelled button with onPress', async () => {
    const onPress = jest.fn();
    const user = userEvent.setup();
    await render(
      <AppCard onPress={onPress} accessibilityLabel="Open your household" variant="highlight">
        <AppText>Household</AppText>
      </AppCard>,
    );

    await user.press(screen.getByRole('button', { name: 'Open your household' }));

    expect(onPress).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('icon-chevron-forward', { includeHiddenElements: true })).toBeOnTheScreen();
  });
});
