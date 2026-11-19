import { screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { renderWithProviders } from '@/test/render';

import { SignInView } from './sign-in-view';

vi.mock('@/app/actions', () => ({ setLanguage: vi.fn() }));

describe('SignInView', () => {
  it('offers the organisation sign-in in OIDC mode and keeps the return path', () => {
    renderWithProviders(<SignInView mode={{ kind: 'oidc' }} returnTo="/exports" error={null} signedOut={false} />);

    expect(screen.getByRole('link', { name: 'Sign in with your organisation account' })).toHaveAttribute(
      'href',
      '/auth/sign-in?returnTo=%2Fexports',
    );
    expect(screen.queryByText(/Development/)).toBeNull();
  });

  it('never names UAE PASS as a way to sign in', () => {
    for (const mode of [{ kind: 'oidc' as const }, { kind: 'development' as const, mockData: true }]) {
      const { unmount } = renderWithProviders(<SignInView mode={mode} returnTo="/" error={null} signedOut={false} />);
      expect(document.body.textContent).not.toMatch(/UAE\s*PASS/i);
      unmount();
    }
  });

  it('offers each development persona, clearly labelled, as a same-origin form post', () => {
    renderWithProviders(<SignInView mode={{ kind: 'development', mockData: true }} returnTo="/analytics" error={null} signedOut={false} />);

    expect(screen.getByRole('heading', { name: 'Development sign-in' })).toBeInTheDocument();
    expect(screen.getByText(/This is not a real identity provider/)).toBeInTheDocument();
    expect(screen.getByText('Pages will show synthetic development data.')).toBeInTheDocument();
    const group = screen.getByRole('radiogroup', { name: 'Act as' });
    expect(within(group).getAllByRole('radio').map((radio) => radio.getAttribute('value'))).toEqual([
      'researcher',
      'program_administrator',
      'system_administrator',
      'support_coordinator',
    ]);
    const form = screen.getByRole('button', { name: 'Continue' }).closest('form');
    expect(form).toHaveAttribute('action', '/auth/development');
    expect(form).toHaveAttribute('method', 'post');
    expect(form?.querySelector('input[name="returnTo"]')).toHaveValue('/analytics');
  });

  it('lists what is missing when sign-in is not configured, and offers nothing else', () => {
    renderWithProviders(
      <SignInView mode={{ kind: 'not_configured', problems: ['session_secret_missing', 'oidc_issuer_missing'] }} returnTo="/" error={null} signedOut={false} />,
    );

    expect(screen.getByRole('heading', { name: 'Sign-in is not configured' })).toBeInTheDocument();
    expect(screen.getByText('ADMIN_SESSION_SECRET is not set.')).toBeInTheDocument();
    expect(screen.getByText('OIDC_ISSUER is not set.')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Sign in/ })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Continue' })).toBeNull();
  });

  it('explains a failed sign-in in its own words', () => {
    renderWithProviders(<SignInView mode={{ kind: 'oidc' }} returnTo="/" error="transaction_missing" signedOut={false} />);

    expect(screen.getByRole('alert')).toHaveTextContent('Signing in took too long or was started in another tab. Start again.');
  });

  it('explains a request refused as cross-origin in plain words, naming no server setting', () => {
    renderWithProviders(<SignInView mode={{ kind: 'oidc' }} returnTo="/" error="cross_origin" signedOut={false} />);

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent(
      "The dashboard refused this request because it didn't come from the dashboard's own address. Nothing was changed. Open the dashboard from its own address, then try again.",
    );
    expect(alert).not.toHaveTextContent(/ADMIN_|set up for|server/);
  });

  it('says the same in Arabic, naming no server setting', () => {
    renderWithProviders(<SignInView mode={{ kind: 'oidc' }} returnTo="/" error="cross_origin" signedOut={false} />, { language: 'ar' });

    expect(screen.getByRole('alert')).toHaveTextContent(/رفضت اللوحة هذا الطلب/);
    expect(screen.getByRole('alert')).not.toHaveTextContent(/ADMIN_|الخادم/);
  });

  it('in development, also says which setting must match the address the dashboard was opened at', () => {
    renderWithProviders(
      <SignInView mode={{ kind: 'development', mockData: true }} returnTo="/" error="cross_origin" signedOut={false} development />,
    );

    expect(screen.getByRole('alert')).toHaveTextContent(/Nothing was changed\..*Development: .*set ADMIN_APP_ORIGIN on the server/);
  });

  it('gives the development hint only for a cross-origin refusal', () => {
    renderWithProviders(<SignInView mode={{ kind: 'development', mockData: true }} returnTo="/" error="transaction_missing" signedOut={false} development />);

    expect(screen.getByRole('alert')).not.toHaveTextContent(/ADMIN_APP_ORIGIN/);
  });

  it('ignores an unknown error code', () => {
    renderWithProviders(<SignInView mode={{ kind: 'oidc' }} returnTo="/" error="<script>" signedOut={false} />);

    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('confirms a sign-out', () => {
    renderWithProviders(<SignInView mode={{ kind: 'oidc' }} returnTo="/" error={null} signedOut />);

    expect(screen.getByRole('status')).toHaveTextContent('You have signed out.');
  });

  it('reads in Arabic', () => {
    renderWithProviders(<SignInView mode={{ kind: 'oidc' }} returnTo="/" error={null} signedOut={false} />, { language: 'ar' });

    expect(screen.getByRole('link', { name: 'سجّل الدخول بحساب جهتك' })).toBeInTheDocument();
  });
});
