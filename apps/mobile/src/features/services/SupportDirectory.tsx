import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { isApiError } from '@/api/client';
import { useSupportResources, useSupportSearch } from '@/api/participant/queries';
import type { SupportResource } from '@/api/participant/schemas';
import { ProblemState } from '@/components/ProblemState';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { LoadingState } from '@/design-system/LoadingState';
import { Section } from '@/design-system/Section';
import { spacing } from '@/design-system/tokens';
import { normalizeSearchText } from '@/features/emergency/emergencySearch';
import { CrisisSupportCard } from '@/features/safety/CrisisSupportCard';
import { useLocalization } from '@/i18n/useLocalization';

import { openPage } from './openPage';

/** The server is asked only for this much text, after the person pauses typing for SEARCH_PAUSE_MS. */
const MIN_SEARCH_LENGTH = 2;
const SEARCH_PAUSE_MS = 400;

/** The text the server is asked for, once the person has paused; empty while it is too short. */
export function useSettledQuery(query: string): string {
  const text = normalizeSearchText(query);
  const wanted = text.length >= MIN_SEARCH_LENGTH ? text : '';
  const [settled, setSettled] = useState(wanted);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(wanted), SEARCH_PAUSE_MS);
    return () => clearTimeout(timer);
  }, [wanted]);
  return wanted === '' ? '' : settled;
}

/**
 * The approved support directory (D-92, D-110), below the numbers in All services: every entry, or the server's
 * search for what is typed in the services search. Entries are public information with a link to their source; the
 * app neither ranks nor recommends them.
 */
export function SupportDirectory({ query }: { query: string }) {
  const { t } = useLocalization();
  const searchText = useSettledQuery(query);
  const all = useSupportResources();
  const search = useSupportSearch(searchText);
  const active = searchText ? search : all;
  const resources = searchText ? search.data?.resources : all.data;

  return (
    <Section title={t('services.directory.title')} description={t('services.directory.description')}>
      {active.isPending ? <LoadingState label={t('services.directory.loading')} /> : null}
      {active.error ? (
        <ProblemState error={active.error} variant="inline" onRetry={() => void active.refetch()} />
      ) : null}
      {resources && resources.length === 0 ? (
        <AppCard variant="muted" testID="directory-empty">
          <AppText tone="secondary">
            {searchText ? t('services.directory.none', { query: query.trim() }) : t('services.directory.empty')}
          </AppText>
        </AppCard>
      ) : null}
      {resources && resources.length > 0 ? (
        <View style={styles.list}>
          {resources.map((resource) => (
            <ResourceCard key={resource.id} resource={resource} />
          ))}
        </View>
      ) : null}
    </Section>
  );
}

/**
 * The crisis support card for the services search, shown at the top of the screen (above the numbers) while the latest
 * search carries the server's notice. A dismissal lasts only until the searched text changes. It reads the same
 * cached search as the directory, so it adds no request.
 */
export function SupportCrisisCard({ query }: { query: string }) {
  const searchText = useSettledQuery(query);
  const search = useSupportSearch(searchText);
  const [dismissed, setDismissed] = useState(false);
  const [shownFor, setShownFor] = useState(searchText);
  if (shownFor !== searchText) {
    // Adjusted while rendering, not in an effect: a changed search starts without the dismissal.
    setShownFor(searchText);
    setDismissed(false);
  }
  // A search that failed can still carry the notice (the server screened the text before it failed).
  const noticed =
    search.data?.safetyNotice === 'crisis_support' ||
    (isApiError(search.error) && search.error.safetyNotice === 'crisis_support');
  if (searchText === '' || !noticed || dismissed) return null;
  return <CrisisSupportCard onDismiss={() => setDismissed(true)} />;
}

function ResourceCard({ resource }: { resource: SupportResource }) {
  const { t, language } = useLocalization();
  const arabic = language === 'ar';
  const name = arabic ? resource.nameAr : resource.nameEn;
  return (
    <AppCard testID={`directory-${resource.code}`}>
      <AppText variant="bodyStrong">{name}</AppText>
      <AppText variant="caption" tone="secondary">
        {arabic ? resource.providerNameAr : resource.providerNameEn}
      </AppText>
      <AppText>{arabic ? resource.summaryAr : resource.summaryEn}</AppText>
      <AppButton
        label={t('services.directory.visit')}
        accessibilityLabel={t('services.directory.visitLabel', { name })}
        variant="secondary"
        onPress={() => void openPage(resource.url)}
      />
    </AppCard>
  );
}

const styles = StyleSheet.create({
  list: { gap: spacing.sm },
});
