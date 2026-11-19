import { screen, userEvent, within } from '@testing-library/react-native';
import { Linking } from 'react-native';

import type { SupportAuthority } from '@/api/participant/schemas';
import { createFixtureParticipantApi } from '@/dev-fixtures/fixtureParticipantApi';
import { PLACEHOLDER_CATALOG } from '@/test-utils/placeholderContent';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';

import { SupportAuthorityList } from '../SupportAuthorityList';

jest.mock('expo-web-browser', () => ({ openBrowserAsync: jest.fn(async () => undefined) }));

const AUTHORITY: SupportAuthority = {
  code: 'family_care_authority',
  kind: 'government',
  name: { en: 'A synthetic authority', ar: 'جهة تجريبية' },
  purpose: { en: 'Helps with synthetic things.', ar: 'تساعد في أمور تجريبية.' },
  emirates: ['abu_dhabi', 'dubai'],
  website: 'https://example.org/synthetic',
  phone: '+971 2 000 0000',
  services: [
    {
      code: 'synthetic_service',
      name: { en: 'A synthetic service', ar: 'خدمة تجريبية' },
      summary: { en: 'A summary.', ar: 'ملخص.' },
      procedure: [{ en: 'Book a visit.', ar: 'احجزوا زيارة.' }],
      documents: [{ en: 'An identity card.', ar: 'بطاقة الهوية.' }],
      website: 'https://example.org/synthetic/service',
      via: { en: 'A portal', ar: 'بوابة' },
    },
    {
      code: 'unpublished_service',
      name: { en: 'An unpublished service', ar: 'خدمة غير منشورة' },
      summary: { en: 'Another summary.', ar: 'ملخص آخر.' },
      procedure: [],
      documents: [],
      website: null,
      via: null,
    },
  ],
  checkedOn: '2026-11-06',
};

const CLINIC: SupportAuthority = {
  ...AUTHORITY,
  code: 'acpn',
  kind: 'private',
  name: { en: 'A synthetic clinic', ar: 'عيادة تجريبية' },
  purpose: { en: 'Treats synthetic things.', ar: 'تعالج أمورًا تجريبية.' },
  website: null,
  phone: null,
  services: [],
};

const render = (query = '', authorities: SupportAuthority[] = [AUTHORITY, CLINIC], language: 'en' | 'ar' = 'en') =>
  renderWithProviders(<SupportAuthorityList query={query} />, {
    language,
    api: {
      ...createFixtureParticipantApi({ getSubject: () => null, latencyMs: 0, catalog: PLACEHOLDER_CATALOG }),
      listSupportAuthorities: async () => authorities,
    },
  });

afterEach(settleQueries);

describe('the organisations on All services', () => {
  it('lists each with its symbol, name, purpose and where it serves', async () => {
    await render();

    const card = within(await screen.findByTestId('authority-family_care_authority'));
    // Hidden from screen readers on purpose: the name beside it says the same.
    expect(card.getByTestId('authority-mark-family_care_authority', { includeHiddenElements: true })).toBeTruthy();
    expect(card.getByText('A synthetic authority')).toBeOnTheScreen();
    expect(card.getByText('Helps with synthetic things.')).toBeOnTheScreen();
    expect(card.getByText('Abu Dhabi, Dubai')).toBeOnTheScreen();
    expect(card.getByText(/^Checked /)).toBeOnTheScreen();
  });

  it('says the symbols are not the organisations\' logos, and that Azwah refers nobody', async () => {
    await render();

    await screen.findByTestId('authority-family_care_authority');
    expect(screen.getByText(/not their logos/)).toBeOnTheScreen();
    expect(screen.getByText(/Azwah doesn't refer you/)).toBeOnTheScreen();
  });

  it('says when a provider is private, and only then', async () => {
    await render();

    expect(within(await screen.findByTestId('authority-acpn')).getByText(/Private: fees may apply/)).toBeOnTheScreen();
    expect(within(screen.getByTestId('authority-family_care_authority')).queryByText(/Private: fees may apply/)).toBeNull();
  });

  it('opens the services and their procedures on request', async () => {
    const user = userEvent.setup();
    await render();
    await screen.findByTestId('authority-family_care_authority');
    expect(screen.queryByTestId('authority-service-synthetic_service')).toBeNull();

    await user.press(screen.getByRole('button', { name: 'Show the services of A synthetic authority' }));

    const service = within(screen.getByTestId('authority-service-synthetic_service'));
    expect(service.getByText('1. Book a visit.')).toBeOnTheScreen();
    expect(service.getByText('• An identity card.')).toBeOnTheScreen();
    expect(service.getByText('Apply through A portal')).toBeOnTheScreen();
    expect(
      within(screen.getByTestId('authority-service-unpublished_service')).getByText(/No published steps/),
    ).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Hide the services of A synthetic authority' }));

    expect(screen.queryByTestId('authority-service-synthetic_service')).toBeNull();
  });

  it('offers no services for a body that lists none, and no website or call it does not have', async () => {
    await render();

    const clinic = within(await screen.findByTestId('authority-acpn'));
    expect(clinic.queryByRole('button')).toBeNull();
  });

  it('opens a website in the in-app browser and a number in the dial pad', async () => {
    const user = userEvent.setup();
    const browser = jest.requireMock<{ openBrowserAsync: jest.Mock }>('expo-web-browser');
    const openURL = jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
    await render();
    await screen.findByTestId('authority-family_care_authority');

    await user.press(screen.getByRole('button', { name: "Visit A synthetic authority's website" }));
    await user.press(screen.getByRole('button', { name: 'Call +971 2 000 0000' }));

    expect(browser.openBrowserAsync).toHaveBeenCalledWith('https://example.org/synthetic');
    expect(openURL).toHaveBeenCalledWith('tel:+97120000000');
    openURL.mockRestore();
  });

  it('finds an organisation by what the person types, in either language', async () => {
    await render('تجريبية خدمة');

    expect(await screen.findByTestId('authority-family_care_authority')).toBeOnTheScreen();
    expect(screen.queryByTestId('authority-acpn')).toBeNull();
  });

  it('says so when nothing matches', async () => {
    await render('nothing like this');

    expect(await screen.findByText('No match for "nothing like this".')).toBeOnTheScreen();
  });

  it('says so when none are listed', async () => {
    await render('', []);

    expect(await screen.findByText('None listed yet.')).toBeOnTheScreen();
  });

  it('reads in Arabic', async () => {
    await render('', [AUTHORITY], 'ar');

    const card = within(await screen.findByTestId('authority-family_care_authority'));
    expect(card.getByText('جهة تجريبية')).toBeOnTheScreen();
    expect(card.getByText('أبوظبي، دبي')).toBeOnTheScreen();
  });
});
