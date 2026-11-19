import type * as ExpoFileSystem from 'expo-file-system';
import type * as ExpoPrint from 'expo-print';
import type * as ExpoSharing from 'expo-sharing';
import { Platform } from 'react-native';

import { EXPORT_FOLDER, ShareInterruptedError } from '@/storage/exportFiles';

const PDF = 'application/pdf';
/** Where the chooser opens: Android refuses "Use this folder" at the storage root, its default. */
const ANDROID_DOCUMENTS = 'content://com.android.externalstorage.documents/document/primary%3ADocuments';
const FOREGROUND_WAIT_MS = 2_000;
const FOREGROUND_CHECK_MS = 100;
/** A4 in PDF points; printers default to US Letter, which families in the UAE do not use. */
const A4 = { width: 595, height: 842 };
// eslint-disable-next-line no-restricted-syntax -- a sheet of paper has physical edges; expo-print's own option shape
const MARGINS = { top: 40, bottom: 40, left: 40, right: 40 };

export interface SaveReportOptions {
  dialogTitle: string;
  /** Asked before the file is made and again just before it leaves the app: false stops there. */
  mayShare: () => boolean;
}

/** `saved`: the person chose where the file went. `cancelled`: they closed the chooser, and nothing left the app. */
export type SaveReportOutcome = 'saved' | 'cancelled';

/** Turns a report's page into a PDF and lets the person keep it, leaving no plaintext copy in the app's own storage. */
export interface ReportFiles {
  /** Whether this phone can save the file at all. Never rejects. */
  isAvailable(): Promise<boolean>;
  /**
   * Prints the page to a PDF and hands it over: on Android a folder chooser and a file written there, on iOS the share
   * sheet, whose "Save to Files" keeps it. Rejects with a ShareInterruptedError when `mayShare` says no.
   */
  savePdf(fileName: string, html: string, options: SaveReportOptions): Promise<SaveReportOutcome>;
}

/*
 * The native modules are required when first used, not imported: a build without them then says saving is not
 * available instead of failing at start-up.
 */
function fileSystem(): typeof ExpoFileSystem {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- loaded lazily on purpose (see above)
  return require('expo-file-system') as typeof ExpoFileSystem;
}

function print(): typeof ExpoPrint {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- as above
  return require('expo-print') as typeof ExpoPrint;
}

function sharing(): typeof ExpoSharing {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- as above
  return require('expo-sharing') as typeof ExpoSharing;
}

function removeQuietly(entry: { exists: boolean; delete(): void }): void {
  try {
    if (entry.exists) entry.delete();
  } catch {
    // The export folder is emptied again at the next start, sign-out, lock or export.
  }
}

/** The folder chooser on Android, where the share sheet often has no way to keep a file on the phone. */
async function saveToChosenFolder(fileName: string, base64: string, mayShare: () => boolean): Promise<SaveReportOutcome> {
  const { Directory } = fileSystem();
  let folder: ExpoFileSystem.Directory;
  try {
    folder = await Directory.pickDirectoryAsync(ANDROID_DOCUMENTS);
  } catch {
    return 'cancelled';
  }
  if (!(await backInForeground(mayShare))) throw new ShareInterruptedError();
  folder.createFile(fileName, PDF).write(base64, { encoding: 'base64' });
  return 'saved';
}

/**
 * The chooser sends the app to the background, and its answer arrives a moment before the app is active again (seen on
 * Android 16), so the first check after it would always refuse. This waits briefly; a locked app stays refused.
 */
async function backInForeground(mayShare: () => boolean): Promise<boolean> {
  for (let waited = 0; !mayShare(); waited += FOREGROUND_CHECK_MS) {
    if (waited >= FOREGROUND_WAIT_MS) return false;
    await new Promise((resolve) => setTimeout(resolve, FOREGROUND_CHECK_MS));
  }
  return true;
}

/** The share sheet on iOS. The file exists only in the export folder and only while the sheet is open. */
async function saveThroughShareSheet(fileName: string, base64: string, dialogTitle: string): Promise<SaveReportOutcome> {
  const { Directory, File, Paths } = fileSystem();
  const folder = new Directory(Paths.cache, EXPORT_FOLDER);
  removeQuietly(folder);
  folder.create({ intermediates: true, idempotent: true });
  const file = new File(folder, fileName);
  try {
    file.create({ overwrite: true });
    file.write(base64, { encoding: 'base64' });
    await sharing().shareAsync(file.uri, { mimeType: PDF, UTI: 'com.adobe.pdf', dialogTitle });
    return 'saved';
  } finally {
    removeQuietly(file);
  }
}

export const expoReportFiles: ReportFiles = {
  async isAvailable() {
    try {
      print();
      return Platform.OS === 'android' ? true : await sharing().isAvailableAsync();
    } catch {
      return false;
    }
  },

  async savePdf(fileName, html, { dialogTitle, mayShare }) {
    if (!mayShare()) throw new ShareInterruptedError();
    const { File } = fileSystem();
    const printed = await print().printToFileAsync({ html, base64: true, ...A4, margins: MARGINS });
    try {
      if (!printed.base64) throw new Error('The printer returned no file.');
      if (!mayShare()) throw new ShareInterruptedError();
      return Platform.OS === 'android'
        ? await saveToChosenFolder(fileName, printed.base64, mayShare)
        : await saveThroughShareSheet(fileName, printed.base64, dialogTitle);
    } finally {
      // The printer always leaves its own plaintext copy in the cache.
      removeQuietly(new File(printed.uri));
    }
  },
};
