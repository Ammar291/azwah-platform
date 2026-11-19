import { useTranslation } from 'react-i18next';

import { AppScreen } from '@/components/AppScreen';
import { EmergencyNumberList } from '@/features/emergency/EmergencyNumberList';
import { SupportAuthorityList } from '@/features/services/SupportAuthorityList';
import { SupportCrisisCard, SupportDirectory } from '@/features/services/SupportDirectory';

/**
 * Every service: the numbers in the app (D-102), searchable (D-105), then from the server the organisations in the knowledge
 * graph (D-115) and the approved support directory (D-110), all searched with the same field. SOS opens the numbers alone.
 * A search the server finds a crisis in puts the crisis support card above the numbers (D-138).
 */
export default function Services() {
  const { t } = useTranslation();
  return (
    <AppScreen title={t('services.title')} menu keyboardAware>
      <EmergencyNumberList
        renderAbove={(query) => <SupportCrisisCard query={query} />}
        renderBelow={(query) => (
          <>
            <SupportAuthorityList query={query} />
            <SupportDirectory query={query} />
          </>
        )}
      />
    </AppScreen>
  );
}
