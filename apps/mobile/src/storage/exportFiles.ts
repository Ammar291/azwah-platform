import type * as ExpoFileSystem from 'expo-file-system';
import type * as ExpoSharing from 'expo-sharing';

/** The cache subfolder that holds a data export for as long as the share sheet needs it, and only that. */
export const EXPORT_FOLDER = 'azwah-exports';

export interface ShareJsonOptions {
  dialogTitle: string;
  /** Asked before the file is written and again just before the share sheet opens: false stops there. */
  mayShare: () => boolean;
}

/**
 * Hands a copy of the person's data to the phone's share sheet. The export is plaintext, so it lives only in
 * `EXPORT_FOLDER` and only while it is being shared; the folder is emptied before each export, at start-up, at
 * sign-out and whenever the app locks.
 */
export interface DataFileSharing {
  /** Whether this phone can open a share sheet at all. Never rejects. */
  isAvailable(): Promise<boolean>;
  /**
   * Writes the JSON to a file in the export folder, opens the share sheet and deletes the file afterwards, also when
   * sharing fails or is cancelled. Rejects with a ShareInterruptedError, without opening the sheet, when `mayShare`
   * says no (the app locked or left the foreground meanwhile).
   */
  shareJson(fileName: string, json: string, options: ShareJsonOptions): Promise<void>;
  /** Deletes every export file. Never rejects. */
  clearAll(): Promise<void>;
}

/** The app locked or left the foreground before the share sheet opened, so nothing was shared. */
export class ShareInterruptedError extends Error {
  constructor() {
    super('The share sheet was not opened: the app is locked or not in the foreground.');
    this.name = 'ShareInterruptedError';
  }
}

export function isShareInterrupted(error: unknown): error is ShareInterruptedError {
  return error instanceof ShareInterruptedError;
}

/*
 * The native modules are required when first used, not imported: a build without them then says sharing is not
 * available instead of failing at start-up.
 */
function fileSystem(): typeof ExpoFileSystem {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- loaded lazily on purpose (see above)
  return require('expo-file-system') as typeof ExpoFileSystem;
}

function sharing(): typeof ExpoSharing {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- as above
  return require('expo-sharing') as typeof ExpoSharing;
}

/** Deletes a file or folder if it is there. A failure is left for the next clearing (start, sign-out, lock, export). */
function removeQuietly(entry: { exists: boolean; delete(): void }): void {
  try {
    if (entry.exists) {
      entry.delete();
    }
  } catch {
    // See above: the folder is emptied again at the next start, sign-out, lock or export.
  }
}

function exportFolder() {
  const { Directory, Paths } = fileSystem();
  return new Directory(Paths.cache, EXPORT_FOLDER);
}

/** expo-file-system's File and Directory API (SDK 57) and expo-sharing. */
export const expoDataFileSharing: DataFileSharing = {
  async isAvailable() {
    try {
      return await sharing().isAvailableAsync();
    } catch {
      return false;
    }
  },

  async shareJson(fileName, json, { dialogTitle, mayShare }) {
    if (!mayShare()) {
      throw new ShareInterruptedError();
    }
    const { File } = fileSystem();
    const folder = exportFolder();
    removeQuietly(folder);
    folder.create({ intermediates: true, idempotent: true });
    const file = new File(folder, fileName);
    try {
      file.create({ overwrite: true });
      file.write(json);
      if (!mayShare()) {
        throw new ShareInterruptedError();
      }
      await sharing().shareAsync(file.uri, { mimeType: 'application/json', UTI: 'public.json', dialogTitle });
    } finally {
      removeQuietly(file);
    }
  },

  async clearAll() {
    try {
      removeQuietly(exportFolder());
    } catch {
      // No file system module in this build: there is nothing to clear.
    }
  },
};

/** The file's name holds only the date the export was made, never anything about the household. */
export function exportFileName(generatedAt: string): string {
  return `azwah-data-${generatedAt.slice(0, 10)}.json`;
}
