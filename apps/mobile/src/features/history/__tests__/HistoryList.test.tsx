import { screen } from '@testing-library/react-native';

import { ApiError } from '@/api/client';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';
import { PLACEHOLDER_INSTRUMENT_CODE } from '@/test-utils/placeholderContent';
import { leadSession } from '@/test-utils/sessionWorlds';

import { HistoryList } from '../HistoryList';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));

afterEach(async () => {
  await settleQueries();
  jest.restoreAllMocks();
});

const row = () => screen.getByRole('button', { name: /Result recorded$/ });

describe('past questionnaires', () => {
  it("names each one by its assignment and shows its round, never the server's codes", async () => {
    const { api } = await leadSession();
    await renderWithProviders(<HistoryList />, { api });

    expect(await screen.findByText('[DEV PLACEHOLDER] Sample questionnaire')).toBeOnTheScreen();
    expect(screen.getByText('[DEV PLACEHOLDER] Section A')).toBeOnTheScreen();
    expect(screen.getByText(/· Round 1$/)).toBeOnTheScreen();
    expect(JSON.stringify(screen.toJSON())).not.toMatch(new RegExp(`${PLACEHOLDER_INSTRUMENT_CODE}|dev-wave`));
  });

  it("names one whose assignment is not listed by its published version's name", async () => {
    const { api } = await leadSession();
    jest.spyOn(api, 'listAssignments').mockResolvedValue([]);
    const version = jest.spyOn(api, 'getPublishedVersion');
    await renderWithProviders(<HistoryList />, { api });

    expect(await screen.findByText('[DEV PLACEHOLDER] Sample questionnaire')).toBeOnTheScreen();
    expect(screen.getByText('[DEV PLACEHOLDER] Section A')).toBeOnTheScreen();
    expect(version).toHaveBeenCalledWith(PLACEHOLDER_INSTRUMENT_CODE, 1);
    expect(row()).toHaveAccessibleName(/^\[DEV PLACEHOLDER\] Sample questionnaire, \[DEV PLACEHOLDER\] Section A, /);
  });

  it('says "Questionnaire" rather than a code while no name can be found', async () => {
    const { api } = await leadSession();
    jest.spyOn(api, 'listAssignments').mockResolvedValue([]);
    jest.spyOn(api, 'getPublishedVersion').mockRejectedValue(new ApiError('server', 500));
    await renderWithProviders(<HistoryList />, { api });

    expect(await screen.findByText('Questionnaire')).toBeOnTheScreen();
    expect(row()).toBeOnTheScreen();
    expect(JSON.stringify(screen.toJSON())).not.toMatch(PLACEHOLDER_INSTRUMENT_CODE);
  });
});
