import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { AppButton } from '@/design-system/AppButton';
import { AppText } from '@/design-system/AppText';
import { RadioCardGroup } from '@/design-system/RadioCardGroup';
import { colors, minTouchTarget, radii, spacing } from '@/design-system/tokens';
import { LANGUAGE_ENDONYMS, LANGUAGES, type Language } from '@/i18n/languages';
import { useLocalization } from '@/i18n/useLocalization';

export interface LanguageSwitcherProps {
  /**
   * `pill`: a compact control naming the other language (the welcome screen's top bar). `button`: one full-width
   * button naming it. `choice`: both languages as radio cards (profile).
   */
  variant?: 'pill' | 'button' | 'choice';
  /** For `choice` under a heading that already says "Language"; the group keeps its accessible name. */
  hideLabel?: boolean;
}

/** Changes the language; the app then restarts once to lay out in the new direction (D-15). */
export function LanguageSwitcher({ variant = 'choice', hideLabel = false }: LanguageSwitcherProps) {
  const { t, language, changeLanguage } = useLocalization();
  const [changing, setChanging] = useState(false);

  const choose = async (next: Language) => {
    if (next === language || changing) {
      return;
    }
    setChanging(true);
    try {
      await changeLanguage(next);
    } finally {
      setChanging(false);
    }
  };

  if (variant === 'pill') {
    const other: Language = language === 'ar' ? 'en' : 'ar';
    return (
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={LANGUAGE_ENDONYMS[other]}
        accessibilityHint={t('language.switchHint')}
        accessibilityState={{ busy: changing, disabled: changing }}
        disabled={changing}
        onPress={() => void choose(other)}
        hitSlop={8}
        style={({ pressed }) => [pillStyles.pill, pressed ? pillStyles.pressed : null]}
      >
        <AppText variant="label" tone="primary">
          {LANGUAGE_ENDONYMS[other]}
        </AppText>
      </Pressable>
    );
  }

  if (variant === 'button') {
    const other: Language = language === 'ar' ? 'en' : 'ar';
    return (
      <AppButton
        variant="secondary"
        label={LANGUAGE_ENDONYMS[other]}
        accessibilityHint={t('language.switchHint')}
        loading={changing}
        onPress={() => void choose(other)}
      />
    );
  }

  return (
    <View style={{ gap: spacing.sm }}>
      <RadioCardGroup
        label={t('language.title')}
        hideLabel={hideLabel}
        options={LANGUAGES.map((code) => {
          // The name in the current language helps only when it differs from the language's own name.
          const localName = t(`language.names.${code}`);
          return {
            value: code,
            label: LANGUAGE_ENDONYMS[code],
            description: localName === LANGUAGE_ENDONYMS[code] ? undefined : localName,
          };
        })}
        value={language}
        onChange={(next) => void choose(next)}
        disabled={changing}
      />
      <AppText variant="caption" tone="muted">
        {t('language.restartNote')}
      </AppText>
    </View>
  );
}

const pillStyles = StyleSheet.create({
  pill: {
    minHeight: minTouchTarget,
    minWidth: minTouchTarget,
    paddingHorizontal: spacing.lg,
    borderRadius: radii.pill,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  pressed: { backgroundColor: colors.primarySoft, borderColor: colors.primary },
});
