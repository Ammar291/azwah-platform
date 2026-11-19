import { Drawer } from 'expo-router/drawer';

import { colors } from '@/design-system/tokens';
import { SIDE_MENU_WIDTH, SideMenu } from '@/features/navigation/SideMenu';
import { useLocalization } from '@/i18n/useLocalization';

export const unstable_settings = {
  // A link straight to another place still has Home beneath it, so back leads home.
  initialRouteName: 'home',
};

/**
 * The app's main places side by side, reached from the side menu (D-106). The menu comes in from the start edge and
 * closes on back, a tap outside it or a swipe. Each place shows the menu button in its header; screens opened from a
 * place (a household, a questionnaire, a result) stack on top with a back button.
 */
export default function MainPlacesLayout() {
  const { t } = useLocalization();
  return (
    <Drawer
      drawerContent={(props) => <SideMenu {...props} />}
      backBehavior="initialRoute"
      screenOptions={{
        headerShown: false,
        drawerType: 'front',
        drawerStyle: { width: SIDE_MENU_WIDTH, backgroundColor: colors.background },
        overlayColor: colors.scrim,
        overlayAccessibilityLabel: t('menu.close'),
        sceneStyle: { backgroundColor: colors.background },
      }}
    >
      <Drawer.Screen name="home" />
      <Drawer.Screen name="assistant" />
      <Drawer.Screen name="consultant" />
      <Drawer.Screen name="resilience" />
      <Drawer.Screen name="services" />
      <Drawer.Screen name="settings" />
    </Drawer>
  );
}
