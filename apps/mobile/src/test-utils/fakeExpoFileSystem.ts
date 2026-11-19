/**
 * A stand-in for expo-file-system's File, Directory and Paths (SDK 57) in tests:
 * `jest.mock('expo-file-system', () => jest.requireActual('@/test-utils/fakeExpoFileSystem'))`.
 * Files live in a map by URI; `fakeFiles` exposes it, `resetFakeFiles` empties it, and `failNextDelete` makes the next
 * delete throw, as a busy file can on a phone.
 */
const files = new Map<string, string>();
const directories = new Set<string>();
let deleteFailures = 0;
let chosenFolder: string | null = null;

export const fakeFiles = {
  /** URI → contents of every file that exists. */
  snapshot: (): Record<string, string> => Object.fromEntries(files),
  /** Every URI ever written, in order, so a test can check what existed for a moment. */
  written: [] as string[],
  /** A file left behind by an earlier run (for example a process killed while the share sheet was open). */
  leave(uri: string, contents: string) {
    files.set(uri, contents);
    directories.add(uri.slice(0, uri.lastIndexOf('/') + 1));
  },
};

export function resetFakeFiles() {
  files.clear();
  directories.clear();
  fakeFiles.written.length = 0;
  deleteFailures = 0;
  chosenFolder = null;
}

/** What the folder chooser answers next; null closes it without choosing. */
export function chooseFolder(uri: string | null) {
  chosenFolder = uri;
}

export function failNextDelete() {
  deleteFailures += 1;
}

const takeDeleteFailure = () => {
  if (deleteFailures > 0) {
    deleteFailures -= 1;
    throw new Error('The file is busy');
  }
};

interface HasUri {
  uri: string;
}

const join = (parent: HasUri | string, name: string) =>
  `${typeof parent === 'string' ? parent : parent.uri}${name}`;

export class Directory {
  readonly uri: string;
  constructor(parent: HasUri | string, name: string) {
    this.uri = `${join(parent, name).replace(/\/+$/, '')}/`;
  }
  get exists() {
    return directories.has(this.uri);
  }
  create() {
    directories.add(this.uri);
  }
  /** The system folder chooser: the folder the test chose with `chooseFolder`, or a rejection as if it was closed. */
  static async pickDirectoryAsync(): Promise<Directory> {
    if (!chosenFolder) throw new Error('The chooser was closed');
    const folder = new Directory(chosenFolder, '');
    folder.create();
    return folder;
  }
  createFile(name: string, _mimeType: string | null) {
    const file = new File(this, name);
    file.create();
    return file;
  }
  delete() {
    takeDeleteFailure();
    if (!directories.has(this.uri)) throw new Error('No such directory');
    for (const uri of [...files.keys()]) {
      if (uri.startsWith(this.uri)) files.delete(uri);
    }
    for (const uri of [...directories]) {
      if (uri.startsWith(this.uri)) directories.delete(uri);
    }
  }
}

export class File {
  readonly uri: string;
  constructor(parent: HasUri | string, name = '') {
    this.uri = join(parent, name);
  }
  get exists() {
    return files.has(this.uri);
  }
  create() {
    const folder = this.uri.slice(0, this.uri.lastIndexOf('/') + 1);
    if (!directories.has(folder)) throw new Error('No such directory');
    files.set(this.uri, '');
  }
  write(contents: string) {
    if (!files.has(this.uri)) this.create();
    files.set(this.uri, contents);
    fakeFiles.written.push(this.uri);
  }
  delete() {
    takeDeleteFailure();
    if (!files.delete(this.uri)) throw new Error('No such file');
  }
}

const cache = { uri: 'file:///cache/' };
directories.add(cache.uri);

export const Paths = {
  get cache() {
    directories.add(cache.uri);
    return cache;
  },
};
