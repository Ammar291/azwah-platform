import { screen, userEvent, within } from '@testing-library/react-native';
import { Linking } from 'react-native';

import Emergency from '../../../../app/(app)/emergency';
import { renderWithProviders } from '@/test-utils/renderWithProviders';

import { EMERGENCY_GROUPS, EMERGENCY_NUMBERS } from '../emergencyNumbers';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));

let openURL: jest.SpyInstance;
beforeEach(() => {
  openURL = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
});
afterEach(() => openURL.mockRestore());

describe('Emergency numbers', () => {
  it('lists every number under its group, with where it works', async () => {
    await renderWithProviders(<Emergency />);

    expect(screen.getByRole('header', { name: 'Emergency numbers' })).toBeOnTheScreen();
    for (const group of ['Emergencies', 'Family and wellbeing', 'In your emirate']) {
      expect(screen.getByRole('header', { name: group })).toBeOnTheScreen();
    }
    const police = screen.getByRole('button', { name: 'Police, 999' });
    expect(within(police).getByText('Across the UAE')).toBeOnTheScreen();
    const sharjah = screen.getByRole('button', { name: 'Child and Family Protection Centre, 800 700' });
    expect(within(sharjah).getByText('Sharjah')).toBeOnTheScreen();
    expect(screen.getAllByRole('button', { name: /^.+, [\d ]+$/ })).toHaveLength(EMERGENCY_NUMBERS.length);
  });

  it("opens the phone's dialler with the number filled in, and calls nothing itself", async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Emergency />);

    await user.press(screen.getByRole('button', { name: 'Ambulance, 998' }));
    await user.press(screen.getByRole('button', { name: 'Dubai Foundation for Women and Children, 800 111' }));
    await user.press(screen.getByRole('button', { name: 'Aman Centre for Women and Children, 07 235 6666' }));

    expect(openURL.mock.calls).toEqual([['tel:998'], ['tel:800111'], ['tel:072356666']]);
  });

  it('narrows the list to the services that match the search, by name, emirate or number', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Emergency />);
    const search = screen.getByLabelText('Search services');

    await user.type(search, 'sharjah');

    expect(screen.getByRole('button', { name: 'Child and Family Protection Centre, 800 700' })).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Police, 999' })).toBeNull();
    expect(screen.queryByRole('header', { name: 'Emergencies' })).toBeNull();
    expect(screen.getByText('Found: 1')).toBeOnTheScreen();

    await user.clear(search);
    await user.type(search, '800 111');

    expect(screen.getAllByRole('button', { name: /^.+, [\d ]+$/ }).map((row) => row.props.accessibilityLabel)).toEqual([
      'Dubai Foundation for Women and Children, 800 111',
    ]);

    await user.press(screen.getByRole('button', { name: 'Clear search' }));

    expect(screen.getAllByRole('button', { name: /^.+, [\d ]+$/ })).toHaveLength(EMERGENCY_NUMBERS.length);
  });

  it('keeps "Dial 999" in reach when nothing matches, and says so', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Emergency />);

    await user.type(screen.getByLabelText('Search services'), 'zzz');

    expect(screen.getByText('No match for "zzz". In danger? Call 999.')).toBeOnTheScreen();
    expect(screen.queryAllByRole('button', { name: /^.+, [\d ]+$/ })).toHaveLength(0);
    await user.press(screen.getByRole('button', { name: 'Dial 999' }));
    expect(openURL).toHaveBeenCalledWith('tel:999');
  });

  it('finds a service in Arabic by its Arabic or English name', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Emergency />, { language: 'ar' });
    const search = screen.getByLabelText('ابحث في الخدمات');

    await user.type(search, 'الشرطه');
    expect(screen.getByRole('button', { name: 'الشرطة، 999' })).toBeOnTheScreen();
    expect(screen.getAllByRole('button', { name: /^.+، [\d ]+$/ })).toHaveLength(1);

    await user.clear(search);
    await user.type(search, 'ambulance');
    expect(screen.getAllByRole('button', { name: /^.+، [\d ]+$/ }).map((row) => row.props.accessibilityLabel)).toEqual([
      'الإسعاف، 998',
    ]);
  });

  it('dials the police from the danger notice', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Emergency />);

    await user.press(screen.getByRole('button', { name: 'Dial 999' }));

    expect(openURL).toHaveBeenCalledWith('tel:999');
  });

  it('shows the number to dial by hand when the device has no dialler', async () => {
    const user = userEvent.setup();
    openURL.mockRejectedValue(new Error('No app can open tel:'));
    await renderWithProviders(<Emergency />);

    await user.press(screen.getByRole('button', { name: 'Child protection, 116111' }));

    expect(await screen.findByText("Can't dial here. Call 116111 from a phone.")).toBeOnTheScreen();

    openURL.mockResolvedValue(true);
    await user.press(screen.getByRole('button', { name: 'Police, 999' }));
    expect(screen.queryByTestId('emergency-dial-failed')).toBeNull();
  });

  it('reads in Arabic with Western digits', async () => {
    await renderWithProviders(<Emergency />, { language: 'ar' });

    expect(screen.getByRole('header', { name: 'أرقام الطوارئ' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'الشرطة، 999' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'اتصل بالرقم 999' })).toBeOnTheScreen();
  });
});

describe('the emergency number list', () => {
  it('dials exactly the digits it shows', () => {
    for (const entry of EMERGENCY_NUMBERS) {
      expect([entry.id, entry.dial]).toEqual([entry.id, entry.display.replace(/ /g, '')]);
      expect([entry.id, /^\d{3,10}$/.test(entry.dial)]).toEqual([entry.id, true]);
    }
  });

  it('has no duplicates, and every group has numbers', () => {
    expect(new Set(EMERGENCY_NUMBERS.map((entry) => entry.id)).size).toBe(EMERGENCY_NUMBERS.length);
    expect(new Set(EMERGENCY_NUMBERS.map((entry) => entry.dial)).size).toBe(EMERGENCY_NUMBERS.length);
    for (const group of EMERGENCY_GROUPS) {
      expect([group, EMERGENCY_NUMBERS.some((entry) => entry.group === group)]).toEqual([group, true]);
    }
  });

  it('records where and when each number was confirmed', () => {
    for (const entry of EMERGENCY_NUMBERS) {
      expect([entry.id, entry.source.startsWith('https://')]).toEqual([entry.id, true]);
      expect([entry.id, /^\d{4}-\d{2}-\d{2}$/.test(entry.checkedOn)]).toEqual([entry.id, true]);
    }
  });

  it('keeps 999, 998 and 997 for police, ambulance and civil defence', () => {
    const byId = Object.fromEntries(EMERGENCY_NUMBERS.map((entry) => [entry.id, entry.dial]));
    expect(byId).toMatchObject({ police: '999', ambulance: '998', civilDefence: '997' });
  });
});
