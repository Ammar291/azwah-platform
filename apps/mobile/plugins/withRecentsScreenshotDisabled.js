// @ts-check
const { withMainActivity } = require('expo/config-plugins');
const { mergeContents } = require('@expo/config-plugins/build/utils/generateCode');

const TAG = 'azwah-recents-screenshot';

/**
 * Android takes the Recents screenshot while the activity pauses, before JavaScript can draw the privacy cover, so on
 * Android 13 (API 33) and later the app asks the system not to keep one (Activity.setRecentsScreenshotEnabled).
 * Recents then shows a blank card. Screenshots themselves are not blocked: FLAG_SECURE is not used (a spec non-goal).
 * Older Android versions rely on the cover alone.
 *
 * @param {string} contents MainActivity source
 * @param {'kt' | 'java'} language
 * @returns {string}
 */
function disableRecentsScreenshot(contents, language) {
  const end = language === 'java' ? ';' : '';
  const newSrc = [
    '    if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.TIRAMISU) {',
    `      setRecentsScreenshotEnabled(false)${end}`,
    '    }',
  ].join('\n');
  // Throws when there is no `super.onCreate(` to attach to, so a changed template fails the build instead of
  // silently leaving the screenshot on.
  return mergeContents({ src: contents, newSrc, tag: TAG, anchor: /super\.onCreate\(/, offset: 1, comment: '    //' })
    .contents;
}

/** @type {import('expo/config-plugins').ConfigPlugin} */
const withRecentsScreenshotDisabled = (config) =>
  withMainActivity(config, (activity) => ({
    ...activity,
    modResults: {
      ...activity.modResults,
      contents: disableRecentsScreenshot(
        activity.modResults.contents,
        activity.modResults.language === 'java' ? 'java' : 'kt',
      ),
    },
  }));

module.exports = withRecentsScreenshotDisabled;
module.exports.default = withRecentsScreenshotDisabled;
module.exports.disableRecentsScreenshot = disableRecentsScreenshot;
