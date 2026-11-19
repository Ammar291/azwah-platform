import { useRouter } from 'expo-router';
import { StyleSheet, View } from 'react-native';

import { useMe } from '@/api/participant/queries';
import type { Me } from '@/api/participant/schemas';
import { AppScreen } from '@/components/AppScreen';
import { ProblemState } from '@/components/ProblemState';
import { QueryView } from '@/components/QueryView';
import { useSingleFlight } from '@/components/useSingleFlight';
import { demonstrationBuild } from '@/config';
import { AppButton } from '@/design-system/AppButton';
import { AppText } from '@/design-system/AppText';
import { Notice } from '@/design-system/Notice';
import { Section } from '@/design-system/Section';
import { borderWidths, colors, spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import { useCreateDemoHousehold } from './householdMutations';

const STEPS = ['aboutYou', 'people', 'confirm'] as const;

/**
 * Household creation, step one: the three steps and what is never asked. An account that already has a household is
 * sent to it (one household per account). A development build, or a release build made as a demonstration build
 * (D-148), also offers the demo family (D-131), which creates the household at once, as a quiet "Auto-fill" with no
 * explanation (D-142).
 */
export function HouseholdStart() {
  const { t } = useLocalization();
  const router = useRouter();
  const me = useMe();
  const demo = useDemoHousehold();
  const canStart = me.data !== undefined && me.data.household === null;
  const offersDemo = __DEV__ || demonstrationBuild;

  return (
    <AppScreen
      title={t('household.start.title')}
      footer={
        canStart ? (
          <>
            {offersDemo && demo.error ? <ProblemState error={demo.error} variant="inline" onRetry={demo.create} /> : null}
            <AppButton
              label={t('household.start.begin')}
              trailingIcon="chevron-forward"
              disabled={demo.isPending}
              onPress={() => router.push('/household/create/about-you')}
            />
            {offersDemo ? (
              // The demo family (D-131): a quiet text button with a neutral name and no explanation (D-142).
              <AppButton
                label={t('household.start.demo.action')}
                variant="tertiary"
                loading={demo.isPending}
                onPress={demo.create}
              />
            ) : null}
          </>
        ) : undefined
      }
    >
      <QueryView query={me} loadingLabel={t('household.start.checking')}>
        {(data) => (data.household ? <AlreadySetUp household={data.household} /> : <Introduction />)}
      </QueryView>
    </AppScreen>
  );
}

/**
 * The demo family's household, created on the server in one request and then opened on its roster. A second press
 * while the first is running is ignored, and retrying after a lost answer never creates a second household.
 */
function useDemoHousehold() {
  const router = useRouter();
  const mutation = useCreateDemoHousehold();
  const once = useSingleFlight();
  const create = () =>
    void once(async () => {
      try {
        const { householdId, outcome } = await mutation.mutateAsync();
        router.replace({
          pathname: '/household/[householdId]/members',
          params: { householdId, notice: outcome === 'created' ? 'demo_created' : outcome },
        });
      } catch {
        // Shown from the mutation's error, with a retry.
      }
    });
  return { create, isPending: mutation.isPending, error: mutation.error };
}

function Introduction() {
  const { t, formatNumber } = useLocalization();
  return (
    <>
      <AppText variant="title" accessibilityRole="header">
        {t('household.start.heading')}
      </AppText>
      <Section title={t('household.start.stepsTitle')}>
        {/* A trail of steps, in the same language as Home's family path, rather than a stack of equal cards. */}
        <View>
          {STEPS.map((step, index) => {
            const last = index === STEPS.length - 1;
            return (
              <View key={step} style={styles.step}>
                <View style={styles.rail}>
                  <View style={styles.marker} />
                  {last ? null : <View style={styles.connector} />}
                </View>
                <View style={[styles.words, last ? null : styles.wordsSpaced]}>
                  <AppText variant="bodyStrong">
                    {t('household.start.stepNumber', {
                      number: formatNumber(index + 1),
                      title: t(`household.start.steps.${step}.title`),
                    })}
                  </AppText>
                </View>
              </View>
            );
          })}
        </View>
      </Section>
      <Notice tone="info" message={t('household.start.privacy')} />
    </>
  );
}

/** Shared by household creation and the "about you" form, for an account that already has a household. */
export function AlreadySetUp({ household }: { household: NonNullable<Me['household']> }) {
  const { t } = useLocalization();
  const router = useRouter();
  return (
    <Notice
      tone="info"
      title={t('household.start.existingTitle')}
      message={t('household.reference', { reference: household.reference })}
      action={{
        label: t('household.start.openExisting'),
        onPress: () =>
          router.replace({ pathname: '/household/[householdId]', params: { householdId: household.householdId } }),
      }}
    />
  );
}

const MARKER = 14;

const styles = StyleSheet.create({
  step: { flexDirection: 'row', gap: spacing.md },
  // The marker sits on the first line of the step's title (26 pt line height).
  rail: { width: MARKER, alignItems: 'center', paddingTop: 6 },
  marker: {
    width: MARKER,
    height: MARKER,
    borderRadius: MARKER / 2,
    borderWidth: borderWidths.strong,
    borderColor: colors.primary,
    backgroundColor: colors.surface,
  },
  connector: { flex: 1, width: borderWidths.strong, marginTop: spacing.xs, backgroundColor: colors.border },
  words: { flex: 1, gap: spacing.xxs },
  wordsSpaced: { paddingBottom: spacing.lg },
});
