import { render, screen, userEvent } from '@testing-library/react-native';
import { I18nManager, StyleSheet } from 'react-native';

import { AppHeader } from '../AppHeader';

const isMirrored = (testID: string) => {
  const style = StyleSheet.flatten(screen.getByTestId(testID, { includeHiddenElements: true }).props.style) as { transform?: object[] };
  return JSON.stringify(style.transform ?? []).includes('"scaleX":-1');
};

describe('AppHeader', () => {
  afterEach(() => jest.restoreAllMocks());

  it('shows the title as a header', async () => {
    await render(<AppHeader title="Your household" />);

    expect(screen.getByRole('header', { name: 'Your household' })).toBeOnTheScreen();
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('offers a labelled back button and an action', async () => {
    const onBack = jest.fn();
    const onAction = jest.fn();
    const user = userEvent.setup();
    await render(
      <AppHeader title="Members" onBack={onBack} backLabel="Back" action={{ label: 'Help', onPress: onAction }} />,
    );

    await user.press(screen.getByRole('button', { name: 'Back' }));
    await user.press(screen.getByRole('button', { name: 'Help' }));

    expect(onBack).toHaveBeenCalledTimes(1);
    expect(onAction).toHaveBeenCalledTimes(1);
  });

  it('offers the menu button in place of back on the main places', async () => {
    const onMenu = jest.fn();
    const user = userEvent.setup();
    await render(
      <AppHeader title="Home" menu={{ label: 'Open the menu', onPress: onMenu }} onBack={jest.fn()} backLabel="Back" />,
    );

    await user.press(screen.getByRole('button', { name: 'Open the menu' }));

    expect(onMenu).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button', { name: 'Back' })).toBeNull();
    expect(screen.getByTestId('icon-menu', { includeHiddenElements: true })).toBeOnTheScreen();
  });

  it('points the back chevron to the start edge in both directions', async () => {
    await render(<AppHeader title="Members" onBack={jest.fn()} backLabel="Back" />);
    expect(isMirrored('icon-chevron-back')).toBe(true);

    jest.replaceProperty(I18nManager, 'isRTL', true);
    await render(<AppHeader title="الأعضاء" onBack={jest.fn()} backLabel="رجوع" />);
    expect(isMirrored('icon-chevron-back')).toBe(false);
  });
});
