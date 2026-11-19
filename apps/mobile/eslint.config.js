// https://docs.expo.dev/guides/using-eslint/
const { defineConfig } = require('eslint/config');
const expoConfig = require('eslint-config-expo/flat');

const physicalDirection =
  '^(left|right|marginLeft|marginRight|paddingLeft|paddingRight|borderLeftWidth|borderRightWidth|borderLeftColor|borderRightColor|borderTopLeftRadius|borderTopRightRadius|borderBottomLeftRadius|borderBottomRightRadius)$';

module.exports = defineConfig([
  expoConfig,
  { ignores: ['dist/*', '.expo/*', 'screenshots/*'] },
  {
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector: `Property[key.name=/${physicalDirection}/], Property[key.value=/${physicalDirection}/]`,
          message: 'Use start/end styles (marginStart, paddingEnd, start, end, borderStartWidth...) so layouts mirror in Arabic.',
        },
        {
          selector: "Property[key.name='textAlign'][value.value=/^(left|right)$/]",
          message: "Leave textAlign unset or use 'center' so text follows the language direction.",
        },
      ],
    },
  },
  {
    // Screens, features and app components show only translated text (src/i18n/resources). The design system
    // receives its strings as props; BootFailure and RootErrorFallback are bilingual by design (no translations yet).
    files: ['app/**/*.tsx', 'src/features/**/*.tsx', 'src/components/**/*.tsx'],
    ignores: ['**/__tests__/**'],
    rules: {
      'react/jsx-no-literals': ['error', { noStrings: true, ignoreProps: true }],
    },
  },
  {
    // Sign-in handles tokens, codes and provider responses: nothing there may be logged.
    files: ['src/auth/**/*.ts', 'src/auth/**/*.tsx'],
    rules: {
      'no-console': 'error',
    },
  },
]);
