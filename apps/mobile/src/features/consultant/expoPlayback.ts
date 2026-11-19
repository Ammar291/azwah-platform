import type * as ExpoAudio from 'expo-audio';
import type * as ExpoFileSystem from 'expo-file-system';

import type { PlaybackFiles, PlaybackPlayer } from './replyPlayback';

/** The cache subfolder that holds reply segments only while they wait to be played. */
export const PLAYBACK_FOLDER = 'azwah-consultant';

// Required when first used, like the export files: a build without the native modules fails here, not at start-up.
function fileSystem(): typeof ExpoFileSystem {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- loaded lazily on purpose (see above)
  return require('expo-file-system') as typeof ExpoFileSystem;
}

function audio(): typeof ExpoAudio {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- as above
  return require('expo-audio') as typeof ExpoAudio;
}

export function expoPlaybackFiles(): PlaybackFiles {
  const { Directory, File, Paths } = fileSystem();
  const folder = new Directory(Paths.cache, PLAYBACK_FOLDER);
  return {
    write(name, bytes) {
      folder.create({ intermediates: true, idempotent: true });
      const file = new File(folder, name);
      file.create({ overwrite: true });
      file.write(bytes);
      return file.uri;
    },
    remove(uri) {
      try {
        const file = new File(uri);
        if (file.exists) file.delete();
      } catch {
        // Left for the next clear (the start and end of every call).
      }
    },
    clear() {
      try {
        if (folder.exists) folder.delete();
      } catch {
        // As above.
      }
    },
  };
}

/** How often the player reports its status, and so how soon a finished segment is followed by the next. */
const STATUS_INTERVAL_MS = 50;

export function expoPlaybackPlayer(): PlaybackPlayer {
  const player = audio().createAudioPlayer(null, { updateInterval: STATUS_INTERVAL_MS });
  let finished: (() => void) | null = null;
  const subscription = player.addListener('playbackStatusUpdate', (status) => {
    if (status.didJustFinish && finished) {
      const done = finished;
      finished = null;
      done();
    }
  });
  return {
    play(uri, onFinished) {
      finished = onFinished;
      player.replace({ uri });
      player.play();
    },
    stop() {
      finished = null;
      player.pause();
    },
    release() {
      finished = null;
      subscription.remove();
      player.remove();
    },
  };
}
