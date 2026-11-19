import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';

import { EmptyState } from '@/design-system/EmptyState';
import { Screen } from '@/design-system/Screen';

/** Any address the app does not have. The start route sends people where their sign-in state allows. */
export default function NotFound() {
  const { t } = useTranslation();
  const router = useRouter();
  return (
    <Screen scroll={false}>
      <EmptyState
        title={t('notFound.title')}
        message={t('notFound.message')}
        action={{ label: t('common.goToStart'), onPress: () => router.replace('/') }}
      />
    </Screen>
  );
}
