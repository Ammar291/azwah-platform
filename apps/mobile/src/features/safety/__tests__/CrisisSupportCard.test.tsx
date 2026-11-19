import { screen, userEvent } from '@testing-library/react-native';
import { AccessibilityInfo, Linking } from 'react-native';

import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';

import { CrisisSupportCard } from '../CrisisSupportCard';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { router, resetRouterMock } = jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

beforeEach(() => resetRouterMock());
afterEach(settleQueries);

describe('the crisis support card', () => {
  it('speaks plainly in English, opens the dialler for 999 and opens the emergency numbers', async () => {
    const user = userEvent.setup();
    const openURL = jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
    const announce = jest.spyOn(AccessibilityInfo, 'announceForAccessibility').mockImplementation(() => undefined);
    await renderWithProviders(<CrisisSupportCard onDismiss={jest.fn()} />);

    expect(screen.getByRole('alert')).toBeOnTheScreen();
    expect(screen.getByText("You don't have to face this alone")).toBeOnTheScreen();
    expect(
      screen.getByText('If you might hurt yourself or someone else, or you are in danger, call 999 now.'),
    ).toBeOnTheScreen();
    expect(screen.getByText("We've told the Azwah support team.")).toBeOnTheScreen();
    expect(announce).toHaveBeenCalledWith('Help is available. Call 999 if you are in danger.');

    await user.press(screen.getByRole('button', { name: 'Dial 999' }));
    expect(openURL).toHaveBeenCalledWith('tel:999');

    await user.press(screen.getByRole('button', { name: 'Emergency numbers' }));
    expect(router.push).toHaveBeenCalledWith('/emergency');
  });

  it('reads in Arabic with Western digits', async () => {
    await renderWithProviders(<CrisisSupportCard onDismiss={jest.fn()} />, {
      language: 'ar',
    });

    expect(screen.getByText('لست وحدك في هذا')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'اتصل بالرقم 999' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'أرقام الطوارئ' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'إغلاق' })).toBeOnTheScreen();
    expect(screen.getByText('تم إبلاغ فريق الدعم في عزوة.')).toBeOnTheScreen();
  });

  it('tells the person when the device cannot open a dialler', async () => {
    const user = userEvent.setup();
    jest.spyOn(Linking, 'openURL').mockRejectedValue(new Error('no dialler'));
    await renderWithProviders(<CrisisSupportCard onDismiss={jest.fn()} />);
    expect(screen.queryByTestId('crisis-dial-failed')).toBeNull();

    await user.press(screen.getByRole('button', { name: 'Dial 999' }));

    expect(await screen.findByText("Can't dial here. Call 999 from a phone.")).toBeOnTheScreen();
  });

  it('runs onLeave before opening the dialler or the emergency numbers', async () => {
    const user = userEvent.setup();
    jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
    const onLeave = jest.fn();
    await renderWithProviders(<CrisisSupportCard onDismiss={jest.fn()} onLeave={onLeave} />);

    await user.press(screen.getByRole('button', { name: 'Dial 999' }));
    expect(onLeave).toHaveBeenCalledTimes(1);
    await user.press(screen.getByRole('button', { name: 'Emergency numbers' }));
    expect(onLeave).toHaveBeenCalledTimes(2);
    expect(router.push).toHaveBeenCalledWith('/emergency');
  });

  it('is dismissed by its own button', async () => {
    const user = userEvent.setup();
    const onDismiss = jest.fn();
    await renderWithProviders(<CrisisSupportCard onDismiss={onDismiss} />);

    await user.press(screen.getByRole('button', { name: 'Dismiss' }));

    expect(onDismiss).toHaveBeenCalledTimes(1);
  });
});
