import { screen, userEvent } from '@testing-library/react-native';

import { ApiError } from '@/api/client';
import { AppText } from '@/design-system/AppText';
import { createI18n } from '@/i18n/i18n';
import { renderWithProviders } from '@/test-utils/renderWithProviders';

import { describeProblem } from '../describeProblem';
import { ProblemState } from '../ProblemState';
import { QueryView } from '../QueryView';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { router, resetRouterMock } = jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

beforeEach(() => resetRouterMock());

describe('describeProblem', () => {
  let exists: (key: string) => boolean;

  beforeAll(async () => {
    const i18n = await createI18n('en');
    exists = (key) => i18n.exists(key);
  });

  it('uses the message for a known problem code', () => {
    expect(describeProblem(new ApiError('conflict', 409, 'sessions.required_items_unanswered'), exists)).toEqual({
      kind: 'conflict',
      code: 'sessions.required_items_unanswered',
      titleKey: 'errors.kinds.conflict.title',
      messageKey: 'problems.sessions.required_items_unanswered',
    });
  });

  it('falls back to the error kind for an unknown code', () => {
    expect(describeProblem(new ApiError('conflict', 409, 'sessions.something_new'), exists)).toMatchObject({
      code: null,
      messageKey: 'errors.kinds.conflict.message',
    });
  });

  it('says nothing specific about errors that are not from the API', () => {
    expect(describeProblem(new TypeError('x is undefined'), exists)).toMatchObject({
      kind: 'unexpected',
      titleKey: 'errors.kinds.unexpected.title',
    });
  });
});

describe('ProblemState', () => {
  it('shows translated text, never the error message, with a retry', async () => {
    const onRetry = jest.fn();
    const user = userEvent.setup();
    await renderWithProviders(<ProblemState error={new ApiError('network')} onRetry={onRetry} />);

    expect(screen.getByRole('alert')).toHaveTextContent("Can't connect");
    expect(screen.queryByText(/API request failed/)).toBeNull();
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('leads to consent when participation consent is missing', async () => {
    const user = userEvent.setup();
    await renderWithProviders(
      <ProblemState error={new ApiError('forbidden', 403, 'sessions.participation_consent_required')} onRetry={jest.fn()} />,
    );

    expect(screen.getByText('Give consent to take part first.')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    await user.press(screen.getByRole('button', { name: 'Review consent' }));
    expect(router.push).toHaveBeenCalledWith('/consent');
  });

  it('leads home from something not found', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<ProblemState error={new ApiError('not_found', 404)} onRetry={jest.fn()} />);

    await user.press(screen.getByRole('button', { name: 'Go to Home' }));
    expect(router.replace).toHaveBeenCalledWith('/home');
  });

  it('can sit inside a screen as an announced notice', async () => {
    await renderWithProviders(
      <ProblemState error={new ApiError('conflict', 409, 'households.roster_has_no_head')} variant="inline" />,
    );

    expect(screen.getByRole('alert')).toHaveTextContent(
      'Something changedChoose a head of household first.',
    );
  });

  it('speaks Arabic', async () => {
    await renderWithProviders(<ProblemState error={new ApiError('timeout')} />, { language: 'ar' });

    expect(screen.getByRole('alert')).toHaveTextContent('استغرق الأمر طويلًا');
  });
});

describe('QueryView', () => {
  const base = { refetch: jest.fn(async () => undefined) };

  it('shows loading, then the problem, then the data', async () => {
    const view = (query: Parameters<typeof QueryView<string>>[0]['query']) => (
      <QueryView query={query} loadingLabel="Loading things">
        {(data) => <AppText>{data}</AppText>}
      </QueryView>
    );
    const { rerender } = await renderWithProviders(view({ ...base, data: undefined, error: null, isPending: true }));
    expect(screen.getByLabelText('Loading things')).toBeBusy();

    await rerender(view({ ...base, data: undefined, error: new ApiError('server', 500), isPending: false }));
    expect(screen.getByRole('alert')).toHaveTextContent('Azwah has a problem');

    await rerender(view({ ...base, data: 'Loaded', error: null, isPending: false }));
    expect(screen.getByText('Loaded')).toBeOnTheScreen();
  });
});
