import type * as ExpoWebBrowser from 'expo-web-browser';
import { Linking } from 'react-native';

/**
 * Opens a page in the in-app browser, or the phone's browser when that is missing. Used by All services (directory
 * entries, D-110, and the knowledge graph's organisations, D-115), the score report's next steps and the sources under
 * an assistant reply (D-122); every link it is given is https (Zod).
 */
export async function openPage(url: string): Promise<void> {
  try {
    // eslint-disable-next-line @typescript-eslint/no-require-imports -- loaded when used, like sign-in's browser
    const browser = require('expo-web-browser') as typeof ExpoWebBrowser;
    await browser.openBrowserAsync(url);
  } catch {
    await Linking.openURL(url).catch(() => undefined);
  }
}
