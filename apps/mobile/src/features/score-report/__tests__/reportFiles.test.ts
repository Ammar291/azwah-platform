import * as Print from 'expo-print';
import * as Sharing from 'expo-sharing';
import { Platform } from 'react-native';

import { EXPORT_FOLDER, ShareInterruptedError } from '@/storage/exportFiles';
import { chooseFolder, Directory, fakeFiles, resetFakeFiles } from '@/test-utils/fakeExpoFileSystem';

import { expoReportFiles } from '../reportFiles';

jest.mock('expo-file-system', () => jest.requireActual('@/test-utils/fakeExpoFileSystem'));
jest.mock('expo-sharing', () => ({ isAvailableAsync: jest.fn(), shareAsync: jest.fn() }));
jest.mock('expo-print', () => ({ printToFileAsync: jest.fn() }));

const print = jest.mocked(Print);
const sharing = jest.mocked(Sharing);
const PRINTED = 'file:///cache/Print/temporary.pdf';
const OPTIONS = { dialogTitle: 'Your Azwah report', mayShare: () => true };
const NAME = 'azwah-report-2026-11-05.pdf';

function onPlatform(os: 'android' | 'ios') {
  Object.defineProperty(Platform, 'OS', { configurable: true, get: () => os });
}

beforeEach(() => {
  resetFakeFiles();
  sharing.isAvailableAsync.mockReset().mockResolvedValue(true);
  sharing.shareAsync.mockReset().mockResolvedValue(undefined);
  print.printToFileAsync.mockReset().mockImplementation(async () => {
    fakeFiles.leave(PRINTED, 'printed');
    return { uri: PRINTED, numberOfPages: 2, base64: 'UERG' };
  });
});

describe('saving a report on Android', () => {
  beforeEach(() => onPlatform('android'));

  it('prints A4 from the page, writes into the chosen folder and leaves no copy of its own', async () => {
    chooseFolder('content://downloads/azwah');

    await expect(expoReportFiles.savePdf(NAME, '<p>report</p>', OPTIONS)).resolves.toBe('saved');

    expect(print.printToFileAsync).toHaveBeenCalledWith(
      expect.objectContaining({ html: '<p>report</p>', base64: true, width: 595, height: 842 }),
    );
    expect(fakeFiles.snapshot()).toEqual({ [`content://downloads/azwah/${NAME}`]: 'UERG' });
    expect(sharing.shareAsync).not.toHaveBeenCalled();
  });

  it('says cancelled and keeps nothing when the person closes the chooser', async () => {
    chooseFolder(null);

    await expect(expoReportFiles.savePdf(NAME, '<p>report</p>', OPTIONS)).resolves.toBe('cancelled');

    expect(fakeFiles.snapshot()).toEqual({});
  });

  it('opens the chooser on Documents, because Android refuses the storage root', async () => {
    chooseFolder('content://documents');
    const pick = jest.spyOn(Directory, 'pickDirectoryAsync');

    await expoReportFiles.savePdf(NAME, '<p>report</p>', OPTIONS);

    expect(pick).toHaveBeenCalledWith(expect.stringMatching(/primary%3ADocuments$/));
    pick.mockRestore();
  });

  it('waits for the app to come back to the foreground after the chooser, then saves', async () => {
    chooseFolder('content://documents');
    // Before printing, after printing, then "not yet" as the chooser hands back, as seen on a real phone.
    const answers = [true, true, false, false];
    const mayShare = () => answers.shift() ?? true;

    await expect(expoReportFiles.savePdf(NAME, '<p>report</p>', { ...OPTIONS, mayShare })).resolves.toBe('saved');

    expect(fakeFiles.snapshot()).toEqual({ [`content://documents/${NAME}`]: 'UERG' });
  });

  it('writes nothing when the app stays locked after the chooser', async () => {
    chooseFolder('content://documents');
    const answers = [true, true];
    const mayShare = () => answers.shift() ?? false;

    await expect(expoReportFiles.savePdf(NAME, '<p>report</p>', { ...OPTIONS, mayShare })).rejects.toBeInstanceOf(ShareInterruptedError);

    expect(fakeFiles.snapshot()).toEqual({});
  });

  it('can always save', async () => {
    await expect(expoReportFiles.isAvailable()).resolves.toBe(true);
    expect(sharing.isAvailableAsync).not.toHaveBeenCalled();
  });
});

describe('saving a report on iOS', () => {
  beforeEach(() => onPlatform('ios'));

  it('opens the share sheet on a PDF in the export folder, then deletes both copies', async () => {
    const folder = `file:///cache/${EXPORT_FOLDER}/`;
    let whileSharing: Record<string, string> = {};
    sharing.shareAsync.mockImplementation(async () => {
      whileSharing = fakeFiles.snapshot();
    });

    await expect(expoReportFiles.savePdf(NAME, '<p>report</p>', OPTIONS)).resolves.toBe('saved');

    expect(sharing.shareAsync).toHaveBeenCalledWith(`${folder}${NAME}`, {
      mimeType: 'application/pdf',
      UTI: 'com.adobe.pdf',
      dialogTitle: 'Your Azwah report',
    });
    expect(whileSharing[`${folder}${NAME}`]).toBe('UERG');
    expect(fakeFiles.snapshot()).toEqual({});
  });

  it('deletes both copies when sharing fails', async () => {
    sharing.shareAsync.mockRejectedValueOnce(new Error('The share sheet failed'));

    await expect(expoReportFiles.savePdf(NAME, '<p>report</p>', OPTIONS)).rejects.toThrow('The share sheet failed');

    expect(fakeFiles.snapshot()).toEqual({});
  });

  it('asks the phone whether it can share', async () => {
    sharing.isAvailableAsync.mockResolvedValue(false);

    await expect(expoReportFiles.isAvailable()).resolves.toBe(false);
  });
});

describe('a report the app may not reveal', () => {
  beforeEach(() => onPlatform('ios'));

  it('is not even printed when the app is locked', async () => {
    const outcome = expoReportFiles.savePdf(NAME, '<p>report</p>', { ...OPTIONS, mayShare: () => false });

    await expect(outcome).rejects.toBeInstanceOf(ShareInterruptedError);
    expect(print.printToFileAsync).not.toHaveBeenCalled();
  });

  it('stops and deletes the printed copy when the app locks during printing', async () => {
    const answers = [true, false];
    const mayShare = () => answers.shift() ?? false;

    await expect(expoReportFiles.savePdf(NAME, '<p>report</p>', { ...OPTIONS, mayShare })).rejects.toBeInstanceOf(ShareInterruptedError);

    expect(sharing.shareAsync).not.toHaveBeenCalled();
    expect(fakeFiles.snapshot()).toEqual({});
  });

  it('deletes the printed copy when the printer gives no file', async () => {
    print.printToFileAsync.mockImplementationOnce(async () => {
      fakeFiles.leave(PRINTED, 'printed');
      return { uri: PRINTED, numberOfPages: 0 };
    });

    await expect(expoReportFiles.savePdf(NAME, '<p>report</p>', OPTIONS)).rejects.toThrow('no file');

    expect(fakeFiles.snapshot()).toEqual({});
  });
});
