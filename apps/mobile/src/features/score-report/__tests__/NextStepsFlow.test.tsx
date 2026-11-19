import { screen, userEvent, within } from '@testing-library/react-native';
import { Linking } from 'react-native';

import type { ReportNextStep } from '@/api/participant/schemas';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';

import { NextStepsFlow } from '../NextStepsFlow';

jest.mock('expo-web-browser', () => ({ openBrowserAsync: jest.fn(async () => undefined) }));

const SERVICE: ReportNextStep['authorities'][number]['services'][number] = {
  code: 'synthetic_service',
  name: 'A synthetic service',
  summary: 'What the service is.',
  need: 'Family counselling',
  procedure: ['Book a visit.', 'Bring your papers.'],
  documents: ['An identity card.'],
  website: 'https://example.org/synthetic/service',
  via: null,
};

const AUTHORITY: ReportNextStep['authorities'][number] = {
  code: 'synthetic_ministry',
  kind: 'government',
  name: 'A synthetic ministry',
  purpose: 'Helps with synthetic things.',
  emirates: ['abu_dhabi'],
  services: [SERVICE],
  website: 'https://example.org/synthetic',
  phone: '+971 2 000 0000',
  checkedOn: '2026-01-15',
};

const step = (overrides: Partial<ReportNextStep> = {}): ReportNextStep => ({
  sectionCode: 'wealth_debt',
  area: 'Family money',
  reason: 'It adds pressure at home.',
  steps: ['List your monthly costs.'],
  standing: 0.1,
  authorities: [AUTHORITY],
  ...overrides,
});

afterEach(settleQueries);

describe('the next steps flow', () => {
  it('numbers the steps, with why and what to start at home in a line each', async () => {
    await renderWithProviders(<NextStepsFlow steps={[step(), step({ sectionCode: 'seniors', area: 'Older relatives' })]} />);

    expect(screen.getByText('Your next steps')).toBeOnTheScreen();
    expect(screen.getByText('Step 1: Family money')).toBeOnTheScreen();
    expect(screen.getByText('Step 2: Older relatives')).toBeOnTheScreen();
    expect(within(screen.getByTestId('report-step-1')).getByText('It adds pressure at home.')).toBeOnTheScreen();
    expect(within(screen.getByTestId('report-step-1')).getByText('• List your monthly costs.')).toBeOnTheScreen();
  });

  it('names where to go and its service compactly, leaving the procedure to the service page (D-145)', async () => {
    await renderWithProviders(<NextStepsFlow steps={[step()]} />);

    const card = within(screen.getByTestId('report-authority-synthetic_ministry'));
    expect(card.getByTestId('authority-mark-synthetic_ministry', { includeHiddenElements: true })).toBeTruthy();
    expect(card.getByText('A synthetic ministry')).toBeOnTheScreen();
    expect(card.getByText('Abu Dhabi')).toBeOnTheScreen();
    expect(card.getByText('A synthetic service')).toBeOnTheScreen();
    expect(card.getByText(/^Checked /)).toBeOnTheScreen();
    for (const detail of ['Helps with synthetic things.', 'What the service is.', 'Helps with: Family counselling', '1. Book a visit.', '• An identity card.']) {
      expect(card.queryByText(detail)).toBeNull();
    }
    // One reminder for the whole flow, not one on every card.
    expect(screen.getAllByText(/Confirm before you go/)).toHaveLength(1);
  });

  it('says plainly when the graph reached no organisation, and invents none', async () => {
    await renderWithProviders(<NextStepsFlow steps={[step({ authorities: [] })]} />);

    expect(screen.getByText("None listed yet.")).toBeOnTheScreen();
    expect(screen.queryByTestId(/^report-authority-/)).toBeNull();
  });

  it('says when a provider is private, and only then', async () => {
    await renderWithProviders(<NextStepsFlow steps={[step({ authorities: [{ ...AUTHORITY, kind: 'private' }] })]} />);
    expect(screen.getByText(/Private: fees may apply/)).toBeOnTheScreen();
  });

  it('adds no private notice for a public body', async () => {
    await renderWithProviders(<NextStepsFlow steps={[step()]} />);
    expect(screen.queryByText(/Private: fees may apply/)).toBeNull();
  });

  it('says Azwah contacts nobody and that the organisations decide', async () => {
    await renderWithProviders(<NextStepsFlow steps={[step()]} />);

    expect(screen.getByText(/Azwah doesn't contact or endorse them/)).toBeOnTheScreen();
    expect(screen.getByText(/They decide what applies to you\./)).toBeOnTheScreen();
  });

  it('opens the service page in the in-app browser', async () => {
    const user = userEvent.setup();
    const browser = jest.requireMock<{ openBrowserAsync: jest.Mock }>('expo-web-browser');
    await renderWithProviders(<NextStepsFlow steps={[step()]} />);

    await user.press(screen.getByRole('button', { name: 'Open the page for A synthetic service' }));

    expect(browser.openBrowserAsync).toHaveBeenCalledWith('https://example.org/synthetic/service');
  });

  it('opens the website in the in-app browser', async () => {
    const user = userEvent.setup();
    const browser = jest.requireMock<{ openBrowserAsync: jest.Mock }>('expo-web-browser');
    await renderWithProviders(<NextStepsFlow steps={[step()]} />);

    await user.press(screen.getByRole('button', { name: 'Website' }));

    expect(browser.openBrowserAsync).toHaveBeenCalledWith('https://example.org/synthetic');
  });

  it('opens the dial pad with the number, calling nobody by itself', async () => {
    const user = userEvent.setup();
    const openURL = jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
    await renderWithProviders(<NextStepsFlow steps={[step()]} />);

    await user.press(screen.getByRole('button', { name: 'Call +971 2 000 0000' }));

    expect(openURL).toHaveBeenCalledWith('tel:+97120000000');
    openURL.mockRestore();
  });

  it('offers no website or call when the graph has none', async () => {
    const bare = { ...AUTHORITY, website: null, phone: null, services: [{ ...SERVICE, website: null, documents: [] }] };
    await renderWithProviders(<NextStepsFlow steps={[step({ authorities: [bare] })]} />);

    expect(screen.queryByRole('button', { name: 'Website' })).toBeNull();
    expect(screen.queryByRole('button', { name: /^Open the page for / })).toBeNull();
    expect(screen.queryByRole('button', { name: /^Call / })).toBeNull();
  });

  it('shows nothing without any step', async () => {
    await renderWithProviders(<NextStepsFlow steps={[]} />);

    expect(screen.queryByTestId('report-next-steps')).toBeNull();
  });

  it('reads in Arabic', async () => {
    await renderWithProviders(<NextStepsFlow steps={[step()]} />, { language: 'ar' });

    expect(screen.getByText('خطواتكم التالية')).toBeOnTheScreen();
    expect(screen.getByText('الخطوة 1: Family money')).toBeOnTheScreen();
  });
});
