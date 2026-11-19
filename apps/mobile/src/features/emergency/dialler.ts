import { Linking } from 'react-native';

/**
 * Opens the phone's dialler with the number filled in. Nothing is called: Android shows the dial pad, iOS asks the
 * person to confirm. False when the device has no dialler (a tablet, a simulator) or refuses the link.
 */
export async function openDialler(digits: string): Promise<boolean> {
  try {
    await Linking.openURL(`tel:${digits}`);
    return true;
  } catch {
    return false;
  }
}
