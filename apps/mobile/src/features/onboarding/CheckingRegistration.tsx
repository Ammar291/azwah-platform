import { LoadingState } from '@/design-system/LoadingState';
import { Screen } from '@/design-system/Screen';
import { useLocalization } from '@/i18n/useLocalization';

/** The moment after signing in while the server says whether the person still confirms their details. */
export function CheckingRegistration() {
  const { t } = useLocalization();
  return (
    <Screen scroll={false}>
      <LoadingState label={t('onboarding.checking')} />
    </Screen>
  );
}
