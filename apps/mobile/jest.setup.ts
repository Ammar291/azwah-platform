/*
 * The side menu (expo-router's Drawer, D-106) runs on react-native-gesture-handler and Reanimated, whose native parts
 * Jest does not have. The libraries ship these stand-ins for tests; Reanimated's needs the one for Worklets too.
 */
import 'react-native-gesture-handler/jestSetup';

jest.mock('react-native-worklets', () => require('react-native-worklets/src/mock'));
jest.mock('react-native-reanimated', () => require('react-native-reanimated/mock'));

// expo-audio (the consultant, D-109) has no native module under Jest either: screens that load it get an inert
// microphone that is never allowed. The consultant's own tests replace this with one they drive.
jest.mock('expo-audio', () => ({
  useAudioStream: () => ({ stream: { start: async () => undefined, stop: () => undefined }, isStreaming: false }),
  requestRecordingPermissionsAsync: async () => ({ granted: false, status: 'denied' }),
  setAudioModeAsync: async () => undefined,
}));
