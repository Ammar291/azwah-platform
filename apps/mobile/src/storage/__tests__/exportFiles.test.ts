import * as Sharing from 'expo-sharing';

import { failNextDelete, fakeFiles, resetFakeFiles } from '@/test-utils/fakeExpoFileSystem';

import { EXPORT_FOLDER, expoDataFileSharing, isShareInterrupted } from '../exportFiles';

jest.mock('expo-file-system', () => jest.requireActual('@/test-utils/fakeExpoFileSystem'));
jest.mock('expo-sharing', () => ({ isAvailableAsync: jest.fn(), shareAsync: jest.fn() }));

const sharing = jest.mocked(Sharing);
const FOLDER = `file:///cache/${EXPORT_FOLDER}/`;
const OPTIONS = { dialogTitle: 'Your Azwah data', mayShare: () => true };

beforeEach(() => {
  resetFakeFiles();
  sharing.isAvailableAsync.mockReset().mockResolvedValue(true);
  sharing.shareAsync.mockReset().mockResolvedValue(undefined);
});

describe('export files', () => {
  it('writes into their own cache folder, opens the share sheet for JSON, then deletes the file', async () => {
    let existedWhileSharing: Record<string, string> = {};
    sharing.shareAsync.mockImplementation(async () => {
      existedWhileSharing = fakeFiles.snapshot();
    });

    await expoDataFileSharing.shareJson('azwah-data-2026-10-18.json', '{"a":1}', OPTIONS);

    expect(sharing.shareAsync).toHaveBeenCalledWith(`${FOLDER}azwah-data-2026-10-18.json`, {
      mimeType: 'application/json',
      UTI: 'public.json',
      dialogTitle: 'Your Azwah data',
    });
    expect(existedWhileSharing).toEqual({ [`${FOLDER}azwah-data-2026-10-18.json`]: '{"a":1}' });
    expect(fakeFiles.snapshot()).toEqual({});
  });

  it('deletes the file when sharing fails', async () => {
    sharing.shareAsync.mockRejectedValueOnce(new Error('The share sheet failed'));

    await expect(expoDataFileSharing.shareJson('azwah-data.json', '{}', OPTIONS)).rejects.toThrow();

    expect(fakeFiles.snapshot()).toEqual({});
  });

  it('removes an export left by an earlier run before writing a new one', async () => {
    fakeFiles.leave(`${FOLDER}azwah-data-2026-10-01.json`, '{"old":true}');
    let existedWhileSharing: string[] = [];
    sharing.shareAsync.mockImplementation(async () => {
      existedWhileSharing = Object.keys(fakeFiles.snapshot());
    });

    await expoDataFileSharing.shareJson('azwah-data-2026-10-18.json', '{}', OPTIONS);

    expect(existedWhileSharing).toEqual([`${FOLDER}azwah-data-2026-10-18.json`]);
  });

  it('opens no share sheet, and writes nothing, when the app is locked or away before it starts', async () => {
    const error = await expoDataFileSharing
      .shareJson('azwah-data.json', '{}', { ...OPTIONS, mayShare: () => false })
      .catch((caught: unknown) => caught);

    expect(isShareInterrupted(error)).toBe(true);
    expect(sharing.shareAsync).not.toHaveBeenCalled();
    expect(fakeFiles.written).toEqual([]);
  });

  it('asks again just before the share sheet, and deletes the file when the answer has changed', async () => {
    const answers = [true, false];
    const mayShare = () => answers.shift() ?? false;

    const error = await expoDataFileSharing
      .shareJson('azwah-data.json', '{}', { ...OPTIONS, mayShare })
      .catch((caught: unknown) => caught);

    expect(isShareInterrupted(error)).toBe(true);
    expect(sharing.shareAsync).not.toHaveBeenCalled();
    expect(fakeFiles.written).toEqual([`${FOLDER}azwah-data.json`]);
    expect(fakeFiles.snapshot()).toEqual({});
  });

  it('clears the whole folder on request, and never fails doing so', async () => {
    fakeFiles.leave(`${FOLDER}azwah-data-2026-10-01.json`, '{"old":true}');
    fakeFiles.leave(`${FOLDER}azwah-data-2026-10-02.json`, '{"old":true}');
    fakeFiles.leave('file:///cache/other.txt', 'not ours');

    await expoDataFileSharing.clearAll();
    await expoDataFileSharing.clearAll();
    failNextDelete();
    fakeFiles.leave(`${FOLDER}azwah-data-2026-10-03.json`, '{}');
    await expect(expoDataFileSharing.clearAll()).resolves.toBeUndefined();

    expect(Object.keys(fakeFiles.snapshot()).sort()).toEqual([
      `${FOLDER}azwah-data-2026-10-03.json`,
      'file:///cache/other.txt',
    ]);
  });

  it('says sharing is unavailable when the phone cannot share or the module fails', async () => {
    sharing.isAvailableAsync.mockResolvedValueOnce(false).mockRejectedValueOnce(new Error('no native module'));

    await expect(expoDataFileSharing.isAvailable()).resolves.toBe(false);
    await expect(expoDataFileSharing.isAvailable()).resolves.toBe(false);
  });
});
