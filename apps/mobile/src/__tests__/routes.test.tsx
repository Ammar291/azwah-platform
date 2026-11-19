import { act, screen } from '@testing-library/react-native';

import Index from '../../app/index';
import NotFound from '../../app/+not-found';
import { isDevelopmentMockAuth } from '@/auth/developmentMockAuth';
import { renderWithProviders } from '@/test-utils/renderWithProviders';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { redirects, resetRouterMock } = jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

beforeEach(() => resetRouterMock());

describe('the anchor route', () => {
  it('sends a signed-in person home', async () => {
    await renderWithProviders(<Index />);

    expect(redirects.at(-1)).toBe('/home');
  });

  it('sends a signed-out person to welcome', async () => {
    await renderWithProviders(<Index />, { persona: null });

    expect(redirects.at(-1)).toBe('/welcome');
  });

  it('sends an expired session to sign-in', async () => {
    const { auth } = await renderWithProviders(<Index />);
    if (!isDevelopmentMockAuth(auth)) throw new Error('expected the development mock');

    await act(async () => auth.expireSession());

    expect(redirects.at(-1)).toBe('/auth/sign-in');
  });

  it('sends everyone to welcome when sign-in is not configured', async () => {
    await renderWithProviders(<Index />, { mode: 'api' });

    expect(redirects.at(-1)).toBe('/welcome');
  });
});

describe('not found', () => {
  it('explains and leads back to the start', async () => {
    await renderWithProviders(<NotFound />, { language: 'ar' });

    expect(screen.getByRole('header', { name: 'الصفحة غير موجودة' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'الذهاب إلى البداية' })).toBeOnTheScreen();
  });
});
