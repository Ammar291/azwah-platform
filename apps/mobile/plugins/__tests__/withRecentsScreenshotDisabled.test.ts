import type { ExpoConfig } from '@expo/config-types';

// The plugin is plain CommonJS so the Expo config can load it without a build step; typed here for the test.
// eslint-disable-next-line @typescript-eslint/no-require-imports -- see above
const plugin = require('../withRecentsScreenshotDisabled') as {
  default: (config: ExpoConfig) => ExpoConfig & { mods?: { android?: { mainActivity?: unknown } } };
  disableRecentsScreenshot: (contents: string, language: 'kt' | 'java') => string;
};

/** MainActivity.kt as Expo SDK 57's prebuild writes it, after expo-splash-screen's own edit. */
const MAIN_ACTIVITY_KT = `package ae.azwah.mobile

import android.os.Build
import android.os.Bundle

import com.facebook.react.ReactActivity
import com.facebook.react.ReactActivityDelegate
import com.facebook.react.defaults.DefaultNewArchitectureEntryPoint.fabricEnabled
import com.facebook.react.defaults.DefaultReactActivityDelegate

import expo.modules.ReactActivityDelegateWrapper
import expo.modules.splashscreen.SplashScreenManager

class MainActivity : ReactActivity() {
  override fun onCreate(savedInstanceState: Bundle?) {
    // setTheme(R.style.AppTheme);
    // @generated begin expo-splashscreen - expo prebuild (DO NOT MODIFY) sync-f3ff59a738c56c9a6119210cb55f0b613eb8b6af
    SplashScreenManager.registerOnActivity(this)
    // @generated end expo-splashscreen
    super.onCreate(null)
  }

  override fun getMainComponentName(): String = "main"

  override fun createReactActivityDelegate(): ReactActivityDelegate {
    return ReactActivityDelegateWrapper(
          this,
          BuildConfig.IS_NEW_ARCHITECTURE_ENABLED,
          object : DefaultReactActivityDelegate(
              this,
              mainComponentName,
              fabricEnabled
          ){})
  }
}
`;

const MAIN_ACTIVITY_JAVA = `package ae.azwah.mobile;

public class MainActivity extends ReactActivity {
  @Override
  protected void onCreate(Bundle savedInstanceState) {
    super.onCreate(null);
  }
}
`;

const lines = (text: string) => text.split('\n').map((line) => line.trim());

describe('the Recents screenshot plugin', () => {
  it('turns the Recents screenshot off right after onCreate, on Android 13 and later only', () => {
    const result = lines(plugin.disableRecentsScreenshot(MAIN_ACTIVITY_KT, 'kt'));

    const created = result.indexOf('super.onCreate(null)');
    expect(result.slice(created + 1, created + 6)).toEqual([
      expect.stringMatching(/^\/\/ @generated begin azwah-recents-screenshot/),
      'if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.TIRAMISU) {',
      'setRecentsScreenshotEnabled(false)',
      '}',
      '// @generated end azwah-recents-screenshot',
    ]);
  });

  it('changes nothing the second time, so prebuild can run again', () => {
    const once = plugin.disableRecentsScreenshot(MAIN_ACTIVITY_KT, 'kt');

    expect(plugin.disableRecentsScreenshot(once, 'kt')).toBe(once);
    expect(once.match(/setRecentsScreenshotEnabled/g)).toHaveLength(1);
  });

  it('writes Java with semicolons for a Java MainActivity', () => {
    expect(lines(plugin.disableRecentsScreenshot(MAIN_ACTIVITY_JAVA, 'java'))).toContain(
      'setRecentsScreenshotEnabled(false);',
    );
  });

  it('never blocks screenshots (FLAG_SECURE is not part of this)', () => {
    expect(plugin.disableRecentsScreenshot(MAIN_ACTIVITY_KT, 'kt')).not.toMatch(/FLAG_SECURE/);
  });

  it('fails loudly when MainActivity has no onCreate to attach to', () => {
    expect(() => plugin.disableRecentsScreenshot('class MainActivity : ReactActivity() {}', 'kt')).toThrow();
  });

  it('registers a MainActivity change with Expo', () => {
    const config = plugin.default({ name: 'Azwah', slug: 'azwah' });

    expect(config.mods?.android?.mainActivity).toEqual(expect.any(Function));
  });
});
