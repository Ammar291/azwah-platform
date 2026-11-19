import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { AppScreen } from '@/components/AppScreen';
import { FLOATING_ACTIONS_CLEARANCE, FloatingActions } from '@/features/home/FloatingActions';
import { HomeDashboard } from '@/features/home/HomeDashboard';

export default function Home() {
  const { t } = useTranslation();
  const router = useRouter();
  return (
    <AppScreen
      title={t('app.name')}
      menu
      overlay={
        <FloatingActions onEmergency={() => router.push('/emergency')} onAssistant={() => router.push('/assistant')} />
      }
    >
      <HomeDashboard />
      <View style={styles.clearance} />
    </AppScreen>
  );
}

const styles = StyleSheet.create({
  clearance: { height: FLOATING_ACTIONS_CLEARANCE },
});
