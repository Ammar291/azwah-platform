import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { ApiError } from '@/lib/api/errors';
import { renderWithProviders } from '@/test/render';

import { EmptyState } from './empty-state';
import { ErrorState } from './error-state';
import { PermissionDenied } from './permission-denied';
import { TableSkeleton } from './table-skeleton';

describe('ErrorState', () => {
  it('shows permission denied for a 403, without the server text', () => {
    renderWithProviders(<ErrorState error={new ApiError(403, null, '00-trace-01')} />);

    expect(screen.getByRole('heading', { name: 'You do not have access to this page' })).toBeInTheDocument();
  });

  it('offers sign-in again for a 401', () => {
    renderWithProviders(<ErrorState error={new ApiError(401, 'bff.signed_out', null)} />);

    expect(screen.getByRole('heading', { name: 'Your session has ended' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Sign in again' })).toHaveAttribute('href', '/auth/sign-in');
  });

  it('shows the trace ID of a server failure and retries on request', async () => {
    const onRetry = vi.fn();
    renderWithProviders(<ErrorState error={new ApiError(500, null, '00-abc-01')} onRetry={onRetry} />);

    expect(screen.getByRole('heading', { name: 'Something went wrong' })).toBeInTheDocument();
    expect(screen.getByText('Reference: 00-abc-01')).toBeInTheDocument();
    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it.each([
    [new ApiError(400, null, 't', ['versionNumber']), 'Check the filters'],
    [new ApiError(404, 'questionnaires.version_not_found', 't'), 'Not found'],
    [new ApiError(0, 'network', null), 'Cannot reach the dashboard server'],
    [new ApiError(502, 'bff.api_unreachable', null), 'The platform is not answering'],
    [new ApiError(503, 'bff.dev_token_missing', null), 'Development token not set'],
    [new ApiError(200, 'unexpected_response', null), 'The platform sent a response this page cannot read'],
  ])('maps %o to a translated state', (error, title) => {
    renderWithProviders(<ErrorState error={error} />);

    expect(screen.getByRole('heading', { name: title })).toBeInTheDocument();
  });

  it('is announced to assistive technology', () => {
    renderWithProviders(<ErrorState error={new ApiError(500, null, null)} />);

    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('speaks Arabic', () => {
    renderWithProviders(<ErrorState error={new ApiError(403, null, null)} />, { language: 'ar' });

    expect(screen.getByRole('heading', { name: 'لا تملك صلاحية الوصول إلى هذه الصفحة' })).toBeInTheDocument();
  });
});

describe('PermissionDenied', () => {
  it('explains that the platform decides access', () => {
    renderWithProviders(<PermissionDenied />);

    // It is the whole page, so it carries the page's one level-1 heading.
    expect(screen.getByRole('heading', { level: 1, name: 'You do not have access to this page' })).toBeInTheDocument();
    expect(screen.getByText(/The platform decides access/)).toBeInTheDocument();
  });
});

describe('EmptyState', () => {
  it('shows a title, a description and an optional action', () => {
    renderWithProviders(<EmptyState title="No exports yet" description="Request one below." action={<button type="button">Request</button>} />);

    expect(screen.getByRole('heading', { name: 'No exports yet' })).toBeInTheDocument();
    expect(screen.getByText('Request one below.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Request' })).toBeInTheDocument();
  });
});

describe('TableSkeleton', () => {
  it('tells assistive technology the table is loading', () => {
    renderWithProviders(<TableSkeleton columns={4} rows={3} />);

    expect(screen.getByRole('status')).toHaveTextContent('Loading…');
  });
});
