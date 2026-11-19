import { act, screen, userEvent, waitFor, within } from '@testing-library/react-native';
import * as Sharing from 'expo-sharing';

import { ApiError } from '@/api/client';
import type { PrivacyRequest } from '@/api/participant/schemas';
import { myDataExportSchema } from '@/api/participant/schemas';
import { AppLockProvider } from '@/security/AppLockContext';
import { createFakeAppState } from '@/security/testing/fakeAppState';
import { createFakeDeviceAuthentication } from '@/security/testing/fakeDeviceAuthentication';
import { createFakeConnectivity } from '@/test-utils/fakeConnectivity';
import { fakeFiles, resetFakeFiles } from '@/test-utils/fakeExpoFileSystem';
import { leadHousehold } from '@/test-utils/householdWorlds';
import { queriesIdle, renderWithProviders, settleQueries, type RenderOptions } from '@/test-utils/renderWithProviders';

import { ProfileView } from '../ProfileView';

jest.mock('expo-file-system', () => jest.requireActual('@/test-utils/fakeExpoFileSystem'));
jest.mock('expo-sharing', () => ({ isAvailableAsync: jest.fn(), shareAsync: jest.fn() }));
jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));

const sharing = jest.mocked(Sharing);
const EXPORT = /^file:\/\/\/cache\/azwah-exports\/azwah-data-\d{4}-\d{2}-\d{2}\.json$/;

/** What was in the export folder while the share sheet was open. */
let sharedFiles: Record<string, string> = {};

beforeEach(() => {
  resetFakeFiles();
  sharedFiles = {};
  sharing.isAvailableAsync.mockReset().mockResolvedValue(true);
  sharing.shareAsync.mockReset().mockImplementation(async () => {
    sharedFiles = fakeFiles.snapshot();
  });
});
afterEach(settleQueries);

const download = () => screen.findByRole('button', { name: 'Download my data' });

/**
 * The profile inside the app lock (unlocked, in the foreground), with everything it reads loaded, so no response
 * lands after a test has finished.
 */
async function renderProfile(options: RenderOptions = {}) {
  const appState = createFakeAppState();
  const rendered = await renderWithProviders(
    <AppLockProvider
      dependencies={{ appState, authentication: createFakeDeviceAuthentication() }}
      lockRestoredSession={false}
    >
      <ProfileView />
    </AppLockProvider>,
    options,
  );
  await queriesIdle();
  return { ...rendered, appState };
}

describe('a copy of my data', () => {
  it('says what the file holds and that where it goes is their choice, before anything is downloaded', async () => {
    const { api } = await renderProfile();
    const exportData = jest.spyOn(api, 'exportMyData');

    expect(await download()).toBeOnTheScreen();
    const card = screen.getByTestId('data-export');
    expect(within(card).getByText(/Household, consent, questionnaires and answers/)).toBeOnTheScreen();
    expect(within(card).getByText(/Anyone you share it with can read it/)).toBeOnTheScreen();
    expect(exportData).not.toHaveBeenCalled();
  });

  it('writes the export as JSON into its own cache folder, opens the share sheet for JSON, then deletes it', async () => {
    const user = userEvent.setup();
    await renderProfile();

    await user.press(await download());

    await waitFor(() => expect(sharing.shareAsync).toHaveBeenCalledTimes(1));
    const [uri] = Object.keys(sharedFiles);
    expect(uri).toMatch(EXPORT);
    expect(sharing.shareAsync).toHaveBeenCalledWith(uri, {
      mimeType: 'application/json',
      UTI: 'public.json',
      dialogTitle: 'Your Azwah data',
    });
    const contents = sharedFiles[uri ?? ''] ?? 'null';
    const written = myDataExportSchema.parse(JSON.parse(contents));
    expect(contents).toMatch(/^\{\n {2}"generatedAt"/);
    expect(written.household?.reference).toBe('DEV-4821');
    expect(written.sessions.some((entry) => entry.answers.length > 0)).toBe(true);
    await waitFor(() => expect(fakeFiles.snapshot()).toEqual({}));
  });

  it('writes the body as the server sent it, so fields this app does not know are kept in the copy', async () => {
    const user = userEvent.setup();
    const world = await leadHousehold();
    const original = world.api.exportMyData;
    world.api.exportMyData = async () => {
      const download = await original();
      return { ...download, raw: { ...(download.raw as object), addedLater: 'kept' } };
    };
    await renderProfile({ api: world.api });

    await user.press(await download());

    await waitFor(() => expect(sharing.shareAsync).toHaveBeenCalledTimes(1));
    expect(JSON.parse(Object.values(sharedFiles)[0] ?? 'null')).toMatchObject({ addedLater: 'kept' });
  });

  it('deletes the file when sharing fails, and says something went wrong', async () => {
    const user = userEvent.setup();
    sharing.shareAsync.mockRejectedValueOnce(new Error('The share sheet failed'));
    await renderProfile();

    await user.press(await download());

    expect(await screen.findByText('Something went wrong')).toBeOnTheScreen();
    expect(fakeFiles.written).toHaveLength(1);
    expect(fakeFiles.snapshot()).toEqual({});
  });

  it('says so on a phone that cannot share, without spending a download', async () => {
    const user = userEvent.setup();
    sharing.isAvailableAsync.mockResolvedValue(false);
    const { api } = await renderProfile();
    const exportData = jest.spyOn(api, 'exportMyData');

    await user.press(await download());

    expect(await screen.findByText("Can't share from this phone")).toBeOnTheScreen();
    expect(exportData).not.toHaveBeenCalled();
    expect(fakeFiles.written).toEqual([]);
  });

  it('explains the hourly limit', async () => {
    const user = userEvent.setup();
    const world = await leadHousehold();
    world.fail('exportMyData', new ApiError('rate_limited', 429, 'requests.rate_limited'));
    await renderProfile({ api: world.api });

    await user.press(await download());

    expect(await screen.findByText(/Too many downloads this hour/)).toBeOnTheScreen();
    expect(fakeFiles.written).toEqual([]);
  });

  it('fails at once when offline, and is never sent later on its own', async () => {
    const user = userEvent.setup();
    const connectivity = createFakeConnectivity('offline');
    const world = await leadHousehold();
    world.fail('exportMyData', new ApiError('network'));
    await renderProfile({ api: world.api, connectivity });

    await user.press(await download());
    expect(await screen.findByText("Can't connect")).toBeOnTheScreen();
    await act(async () => connectivity.set('online'));
    await queriesIdle();

    expect(world.count('exportMyData')).toBe(1);
    expect(sharing.shareAsync).not.toHaveBeenCalled();
  });

  it('opens no share sheet when the app leaves the foreground while the export downloads', async () => {
    const user = userEvent.setup();
    const world = await leadHousehold();
    const original = world.api.exportMyData;
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => (release = resolve));
    world.api.exportMyData = async () => {
      await held;
      return original();
    };
    const { appState } = await renderProfile({ api: world.api });

    await user.press(await download());
    await act(async () => appState.change('background'));
    await act(async () => release());

    expect(await screen.findByText(/Nothing shared/)).toBeOnTheScreen();
    await act(async () => appState.change('active'));
    expect(sharing.shareAsync).not.toHaveBeenCalled();
    expect(fakeFiles.written).toEqual([]);
  });
});

describe('asking about my data', () => {
  it('sends the chosen type and member after a confirmation, then lists the request', async () => {
    const user = userEvent.setup();
    const world = await leadHousehold();
    await renderProfile({ api: world.api });

    await user.press(await screen.findByRole('radio', { name: 'Delete the information' }));
    await user.press(await screen.findByRole('radio', { name: 'Spouse' }));
    await user.press(screen.getByRole('button', { name: 'Send request' }));
    const dialog = screen.getByTestId('confirmation-dialog');
    expect(
      within(dialog).getByText(
        'Delete the information: Spouse, born September 1987.',
      ),
    ).toBeOnTheScreen();
    await user.press(within(dialog).getByRole('button', { name: 'Send' }));

    expect(await screen.findByText('Request sent')).toBeOnTheScreen();
    expect(world.argsOf('createPrivacyRequest')).toEqual([[{ type: 'erasure', memberId: world.spouse.id }]]);
    const [created] = await world.api.listPrivacyRequests(1).then((page) => page.items);
    const card = await screen.findByTestId(`privacy-request-${created?.id}`);
    expect(within(card).getByText('Delete the information')).toBeOnTheScreen();
    expect(within(card).getByText('Open')).toBeOnTheScreen();
    expect(within(card).getByLabelText('About: Spouse, born September 1987')).toBeOnTheScreen();
  });

  it('is about the person signed in unless another member is chosen', async () => {
    const user = userEvent.setup();
    const world = await leadHousehold();
    await renderProfile({ api: world.api });

    expect(await screen.findByRole('radio', { name: 'You' })).toBeChecked();
    await user.press(await screen.findByRole('radio', { name: 'See the information' }));
    await user.press(screen.getByRole('button', { name: 'Send request' }));
    await user.press(within(screen.getByTestId('confirmation-dialog')).getByRole('button', { name: 'Send' }));

    await screen.findByText('Request sent');
    expect(world.argsOf('createPrivacyRequest')).toEqual([[{ type: 'access', memberId: world.self.id }]]);
  });

  it('asks what the request is for before sending anything', async () => {
    const user = userEvent.setup();
    const world = await leadHousehold();
    await renderProfile({ api: world.api });
    await screen.findByRole('radio', { name: 'Spouse' });

    await user.press(screen.getByRole('button', { name: 'Send request' }));

    expect(await screen.findByText("Choose a request.")).toBeOnTheScreen();
    expect(screen.queryByTestId('confirmation-dialog')).toBeNull();
    expect(world.count('createPrivacyRequest')).toBe(0);
  });

  it('shows the earlier request the programme team closed, with its dates', async () => {
    await renderProfile();

    const cards = await screen.findAllByTestId(/^privacy-request-/);
    expect(cards).toHaveLength(1);
    const [closed] = cards;
    if (!closed) throw new Error('expected the seeded request');
    expect(within(closed).getByText('See the information')).toBeOnTheScreen();
    expect(within(closed).getByText('Closed: done')).toBeOnTheScreen();
    expect(within(closed).getByLabelText(/^Sent: \d{1,2} \w+ \d{4}$/)).toBeOnTheScreen();
    expect(within(closed).getByLabelText(/^Closed: \d{1,2} \w+ \d{4}$/)).toBeOnTheScreen();
    expect(within(closed).getByLabelText('About: You')).toBeOnTheScreen();
  });

  it.each([
    [new ApiError('conflict', 409, 'privacy.request_already_open'), /A request like this is already open/],
    [new ApiError('not_found', 404, 'households.member_not_found'), "Person not found."],
    [new ApiError('rate_limited', 429, 'requests.rate_limited'), /Wait a minute, then try again/],
  ])('explains a refused request in words (%s)', async (error, message) => {
    const user = userEvent.setup();
    const world = await leadHousehold();
    world.fail('createPrivacyRequest', error);
    await renderProfile({ api: world.api });

    await user.press(await screen.findByRole('radio', { name: 'Correct the information' }));
    await user.press(screen.getByRole('button', { name: 'Send request' }));
    await user.press(within(screen.getByTestId('confirmation-dialog')).getByRole('button', { name: 'Send' }));

    expect(await screen.findByText(message)).toBeOnTheScreen();
    expect(screen.queryByText('Request sent')).toBeNull();
  });

  it('refuses a second open request of the same kind with the translated reason', async () => {
    const user = userEvent.setup();
    await renderProfile();
    const sendCorrection = async () => {
      await user.press(await screen.findByRole('radio', { name: 'Correct the information' }));
      await user.press(screen.getByRole('button', { name: 'Send request' }));
      await user.press(within(screen.getByTestId('confirmation-dialog')).getByRole('button', { name: 'Send' }));
    };

    await sendCorrection();
    await screen.findByText('Request sent');
    await sendCorrection();

    expect(
      await screen.findByText("A request like this is already open."),
    ).toBeOnTheScreen();
  });

  it('pages by the page size the server answers with', async () => {
    const user = userEvent.setup();
    const world = await leadHousehold();
    const requests: PrivacyRequest[] = Array.from({ length: 12 }, (_, index) => ({
      id: `0199e0a1-0000-7000-8000-0000000001${String(index).padStart(2, '0')}`,
      memberId: world.self.id,
      type: 'access',
      status: 'closed',
      createdAt: '2026-09-01T08:00:00Z',
      closedAt: '2026-09-02T08:00:00Z',
      resolution: 'completed',
    }));
    world.api.listPrivacyRequests = async (page) => ({
      items: requests.slice((page - 1) * 10, page * 10),
      page,
      pageSize: 10,
      totalCount: requests.length,
    });
    await renderProfile({ api: world.api });
    expect(await screen.findAllByTestId(/^privacy-request-/)).toHaveLength(10);

    await user.press(screen.getByRole('button', { name: 'Show more' }));

    await waitFor(() => expect(screen.getAllByTestId(/^privacy-request-/)).toHaveLength(12));
    expect(screen.queryByRole('button', { name: 'Show more' })).toBeNull();
  });

  it('asks a participant without a household to set one up first, and still offers the copy', async () => {
    await renderProfile({ persona: 'new_participant' });

    expect(await screen.findByText(/Set up your household first/)).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Send request' })).toBeNull();
    expect(await download()).toBeOnTheScreen();
  });
});

describe('your data in Arabic', () => {
  it('reads in Arabic with Western digits in the dates', async () => {
    await renderProfile({ language: 'ar' });

    expect(await screen.findByRole('header', { name: 'بياناتك' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'تنزيل بياناتي' })).toBeOnTheScreen();
    expect(await screen.findByRole('radio', { name: 'حذف المعلومات' })).toBeOnTheScreen();
    const [closed] = await screen.findAllByTestId(/^privacy-request-/);
    if (!closed) throw new Error('expected the seeded request');
    expect(within(closed).getByText('مغلق: تم التنفيذ')).toBeOnTheScreen();
    const sent = within(closed).getByLabelText(/^تاريخ الإرسال: /);
    expect(sent.props.accessibilityLabel).toMatch(/\d{4}/);
    expect(sent.props.accessibilityLabel).not.toMatch(/[٠-٩]/);
  });
});
