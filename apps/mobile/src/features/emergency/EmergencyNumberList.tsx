import { useMemo, useState, type ReactNode } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { AppButton } from '@/design-system/AppButton';
import { AppInput } from '@/design-system/AppInput';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { Notice } from '@/design-system/Notice';
import { Section } from '@/design-system/Section';
import { borderWidths, colors, minTouchTarget, radii, spacing } from '@/design-system/tokens';
import { LANGUAGES } from '@/i18n/languages';
import { useLocalization } from '@/i18n/useLocalization';

import { openDialler } from './dialler';
import { EMERGENCY_GROUPS, EMERGENCY_NUMBERS, POLICE, type EmergencyNumber } from './emergencyNumbers';
import { matchesSearch, normalizeSearchText } from './emergencySearch';

/**
 * Emergency and support numbers in the UAE (D-102). They live in the app, not on the server, so the list works
 * offline and before anything has loaded. Tapping one opens the dialler with the number filled in; the person still
 * presses call. The search finds a service by its name, description, emirate or number in either language; "Dial 999"
 * stays above it whatever is typed.
 */
export function EmergencyNumberList({
  renderAbove,
  renderBelow,
}: {
  renderAbove?: (query: string) => ReactNode;
  renderBelow?: (query: string) => ReactNode;
} = {}) {
  const { t } = useLocalization();
  const [failed, setFailed] = useState<EmergencyNumber | null>(null);
  const [query, setQuery] = useState('');
  const searchable = useMemo(
    () =>
      EMERGENCY_NUMBERS.map((entry) => ({
        entry,
        dial: entry.dial,
        texts: LANGUAGES.flatMap((lng) => [
          t(`emergency.services.${entry.id}.name`, { lng }),
          t(`emergency.services.${entry.id}.description`, { lng }),
          t(`emergency.coverage.${entry.coverage}`, { lng }),
        ]),
      })),
    [t],
  );
  const searching = normalizeSearchText(query) !== '';
  const shown = searchable.filter((row) => matchesSearch(row, query)).map((row) => row.entry);

  const dial = async (entry: EmergencyNumber) => {
    setFailed((await openDialler(entry.dial)) ? null : entry);
  };

  return (
    <>
      {/* All services puts the crisis support card here, before everything else (D-110). */}
      {renderAbove?.(query)}
      <AppText tone="secondary">{t('emergency.intro')}</AppText>
      <View style={styles.danger}>
        <Notice
          tone="danger"
          title={t('emergency.danger.title')}
          message={t('emergency.danger.message')}
          testID="emergency-danger"
        />
        <AppButton label={t('emergency.danger.action')} variant="danger" onPress={() => void dial(POLICE)} />
      </View>
      {failed ? (
        <Notice
          tone="warning"
          message={t('emergency.dialFailed', { number: failed.display })}
          announce
          testID="emergency-dial-failed"
        />
      ) : null}
      <View style={styles.search}>
        <View style={styles.searchField}>
          <AppInput
            label={t('emergency.search.label')}
            placeholder={t('emergency.search.placeholder')}
            value={query}
            onChangeText={setQuery}
            autoCapitalize="none"
            autoCorrect={false}
            returnKeyType="search"
            testID="emergency-search"
          />
        </View>
        {searching ? (
          <AppButton label={t('emergency.search.clear')} variant="tertiary" onPress={() => setQuery('')} />
        ) : null}
      </View>
      {searching ? (
        <AppText variant="caption" tone="secondary" accessibilityLiveRegion="polite">
          {t('emergency.search.count', { count: shown.length })}
        </AppText>
      ) : null}
      {searching && shown.length === 0 ? (
        <Notice tone="info" message={t('emergency.search.none', { query: query.trim() })} testID="emergency-search-none" />
      ) : null}
      {EMERGENCY_GROUPS.map((group) => {
        const rows = shown.filter((entry) => entry.group === group);
        return rows.length === 0 ? null : (
          <Section key={group} title={t(`emergency.groups.${group}`)}>
            <View style={styles.list}>
              {rows.map((entry) => (
                <NumberRow key={entry.id} entry={entry} onPress={() => void dial(entry)} />
              ))}
            </View>
          </Section>
        );
      })}
      <AppText variant="caption" tone="muted">
        {t('emergency.footnote')}
      </AppText>
      {/* All services adds the support directory here, searched with the same field (D-110). */}
      {renderBelow?.(query)}
    </>
  );
}

function NumberRow({ entry, onPress }: { entry: EmergencyNumber; onPress: () => void }) {
  const { t } = useLocalization();
  const name = t(`emergency.services.${entry.id}.name`);
  const coverage = t(`emergency.coverage.${entry.coverage}`);
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={t('emergency.dialLabel', { name, number: entry.display })}
      accessibilityHint={t('emergency.dialHint')}
      onPress={onPress}
      style={({ pressed }) => [styles.row, pressed ? styles.pressed : null]}
      testID={`emergency-${entry.id}`}
    >
      <View style={styles.words}>
        <AppText variant="bodyStrong">{name}</AppText>
        <AppText variant="caption" tone="secondary">
          {t(`emergency.services.${entry.id}.description`)}
        </AppText>
        <Badge label={coverage} />
      </View>
      <View style={styles.number}>
        <AppText variant="title" tone="primary" maxFontSizeMultiplier={1.5}>
          {entry.display}
        </AppText>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  danger: { gap: spacing.sm },
  search: { flexDirection: 'row', alignItems: 'flex-end', gap: spacing.sm },
  searchField: { flex: 1 },
  list: {
    borderWidth: borderWidths.hairline,
    borderColor: colors.borderSoft,
    borderRadius: radii.lg,
    backgroundColor: colors.surface,
    overflow: 'hidden',
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    minHeight: minTouchTarget + spacing.md,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.md,
    borderBottomWidth: borderWidths.hairline,
    borderBottomColor: colors.borderSoft,
  },
  pressed: { backgroundColor: colors.surfaceMuted },
  words: { flex: 1, gap: spacing.xxs },
  number: { flexShrink: 0 },
});
