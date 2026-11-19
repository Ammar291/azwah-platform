import { useRouter } from 'expo-router';
import type { DrawerContentComponentProps } from 'expo-router/drawer';
import { useState } from 'react';
import { Image, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import { useAssistantAvailability, useMe } from '@/api/participant/queries';
import { useAuth } from '@/auth/AuthContext';
import { isDevelopmentMockAuth } from '@/auth/developmentMockAuth';
import { fullName } from '@/auth/identityProfile';
import { SignOutDialog } from '@/components/SignOutDialog';
import { AppText } from '@/design-system/AppText';
import { brand } from '@/design-system/brand';
import { Icon, type IconName } from '@/design-system/Icon';
import { borderWidths, colors, minTouchTarget, radii, spacing, statusColors } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

export interface MenuItem {
  key: string;
  icon: IconName;
  /** The drawer screen it opens. Without one (the household), the item opens a screen over the menu's places. */
  route?: string;
  /** An AI helper: labelled "not switched on yet" while the server says no model is connected (D-108, D-109). */
  ai?: 'assistant' | 'consultant';
}

/** The main places, in the owner's order (D-106). Labels are `menu.items.<key>`. */
export const MENU_ITEMS: readonly MenuItem[] = [
  { key: 'home', icon: 'home', route: 'home' },
  { key: 'assistant', icon: 'chat', route: 'assistant', ai: 'assistant' },
  { key: 'consultant', icon: 'mic', route: 'consultant', ai: 'consultant' },
  { key: 'household', icon: 'people' },
  { key: 'resilience', icon: 'heart', route: 'resilience' },
  { key: 'services', icon: 'grid', route: 'services' },
  { key: 'settings', icon: 'settings', route: 'settings' },
];

export const SIDE_MENU_WIDTH = 304;

/**
 * The side menu (D-106): who is signed in, the app's main places, and signing out at the bottom. It comes in from the
 * start edge, so from the right in Arabic. Choosing the place already open just closes the menu.
 */
export function SideMenu({ state, navigation }: DrawerContentComponentProps) {
  const { t, language } = useLocalization();
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const { state: auth, provider } = useAuth();
  const me = useMe();
  const availability = useAssistantAvailability();
  const [confirmingSignOut, setConfirmingSignOut] = useState(false);
  const current = state.routes[state.index]?.name;
  const session = auth.status === 'signed_in' ? auth.session : null;
  const simulated = isDevelopmentMockAuth(provider);
  const name =
    fullName(session?.profile ?? null, language) ??
    (simulated && session?.personaCode ? t(`devFixtures.personas.${session.personaCode}.name`) : null);
  const household = me.data?.household;

  const open = (item: MenuItem) => {
    if (!item.route) {
      navigation.closeDrawer();
      // Before the account has loaded, the start screen finds out and sends people to their household if they have one.
      router.push(
        household
          ? { pathname: '/household/[householdId]', params: { householdId: household.householdId } }
          : '/household/create',
      );
    } else if (item.route === current) {
      navigation.closeDrawer();
    } else {
      navigation.navigate(item.route);
    }
  };

  return (
    <View style={[styles.menu, { paddingTop: insets.top + spacing.md, paddingBottom: insets.bottom + spacing.sm }]}>
      <View style={styles.top}>
        <View style={styles.brand}>
          <Image source={brand.emblem} style={styles.emblem} resizeMode="contain" accessibilityIgnoresInvertColors />
          <AppText variant="heading" accessibilityRole="header">
            {t(brand.nameKey)}
          </AppText>
        </View>
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={t('menu.close')}
          onPress={() => navigation.closeDrawer()}
          hitSlop={spacing.xs}
          style={({ pressed }) => [styles.close, pressed ? styles.closePressed : null]}
        >
          <Icon name="close" size={22} color={colors.primary} />
        </Pressable>
      </View>
      {name || household ? (
        <View style={styles.account}>
          {name ? <AppText variant="bodyStrong">{name}</AppText> : null}
          {household ? (
            <AppText variant="caption" tone="secondary">
              {t('household.reference', { reference: household.reference })}
            </AppText>
          ) : null}
        </View>
      ) : null}

      <ScrollView contentContainerStyle={styles.items} accessibilityRole="menu">
        {MENU_ITEMS.map((item) => {
          const label = t(`menu.items.${item.key}`);
          const selected = item.route !== undefined && item.route === current;
          const off = item.ai !== undefined && availability.data?.[item.ai] === false;
          return (
            <Pressable
              key={item.key}
              accessibilityRole="menuitem"
              accessibilityLabel={off ? [label, t('menu.notOnYet')].join(t('common.listSeparator')) : label}
              accessibilityState={{ selected }}
              onPress={() => open(item)}
              style={({ pressed }) => [styles.item, selected ? styles.selected : null, pressed ? styles.pressed : null]}
              testID={`menu-${item.key}`}
            >
              <View style={[styles.marker, selected ? styles.markerSelected : null]} />
              <Icon name={item.icon} size={22} color={selected ? colors.primary : colors.textSecondary} />
              <View style={styles.itemText}>
                <AppText variant="bodyStrong" tone={selected ? 'primary' : 'default'}>
                  {label}
                </AppText>
                {off ? (
                  <AppText variant="caption" tone="muted">
                    {t('menu.notOnYet')}
                  </AppText>
                ) : null}
              </View>
            </Pressable>
          );
        })}
      </ScrollView>

      <View style={styles.footer}>
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={t('menu.signOut')}
          onPress={() => setConfirmingSignOut(true)}
          style={({ pressed }) => [styles.item, pressed ? styles.pressed : null]}
          testID="menu-sign-out"
        >
          <View style={styles.marker} />
          <Icon name="sign-out" size={22} color={statusColors.danger} />
          <AppText variant="bodyStrong" tone="danger" style={styles.itemText}>
            {t('menu.signOut')}
          </AppText>
        </Pressable>
      </View>

      <SignOutDialog visible={confirmingSignOut} onClose={() => setConfirmingSignOut(false)} />
    </View>
  );
}

const styles = StyleSheet.create({
  menu: { flex: 1, backgroundColor: colors.background },
  top: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingStart: spacing.lg,
    paddingEnd: spacing.sm,
  },
  brand: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  emblem: { width: 40, height: Math.round(40 / brand.emblemAspectRatio) },
  close: {
    width: minTouchTarget,
    height: minTouchTarget,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: radii.pill,
  },
  account: {
    gap: spacing.xxs,
    marginTop: spacing.md,
    marginHorizontal: spacing.lg,
    padding: spacing.md,
    borderRadius: radii.md,
    backgroundColor: colors.surfaceMuted,
  },
  items: { paddingVertical: spacing.md, gap: spacing.xxs },
  item: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    minHeight: minTouchTarget + spacing.sm,
    paddingEnd: spacing.lg,
    marginEnd: spacing.md,
    borderTopEndRadius: radii.pill,
    borderBottomEndRadius: radii.pill,
  },
  selected: { backgroundColor: colors.primarySoft },
  // A bar at the start edge marks the open place, so it never relies on colour alone.
  marker: { width: borderWidths.accent, alignSelf: 'stretch', marginEnd: spacing.sm },
  markerSelected: { backgroundColor: colors.primary },
  itemText: { flex: 1, paddingVertical: spacing.xs },
  footer: {
    borderTopWidth: borderWidths.hairline,
    borderTopColor: colors.borderSoft,
    paddingTop: spacing.sm,
  },
  pressed: { backgroundColor: colors.surfaceMuted },
  closePressed: { backgroundColor: colors.primarySoft },
});
