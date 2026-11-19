import { useNavigation, useRouter } from 'expo-router';
import { DrawerActions } from 'expo-router/react-navigation';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

import { AppHeader, type HeaderAction } from '@/design-system/AppHeader';
import { Screen } from '@/design-system/Screen';

export interface AppScreenProps {
  title: string;
  /** Show the back button (default). Home turns it off. */
  back?: boolean;
  /**
   * Show the menu button in place of back: set on the main places the side menu leads to (D-106), which sit side by
   * side rather than on top of each other.
   */
  menu?: boolean;
  headerAction?: HeaderAction;
  footer?: ReactNode;
  keyboardAware?: boolean;
  /** Floats over the content; see Screen. */
  overlay?: ReactNode;
  /** Keeps the newest content in view; see Screen. */
  followEnd?: boolean;
  children: ReactNode;
}

/**
 * The frame of every signed-in screen: translated header with back navigation (or the side menu), safe areas and
 * scrolling. Development fixtures carry no banner (D-142): the demonstration guide says what is synthetic. Back falls back to home when there is no history (for example
 * after a deep link).
 */
export function AppScreen({
  title,
  back = true,
  menu = false,
  headerAction,
  footer,
  keyboardAware,
  overlay,
  followEnd,
  children,
}: AppScreenProps) {
  const { t } = useTranslation();
  const router = useRouter();
  const navigation = useNavigation();
  const goBack = () => (router.canGoBack() ? router.back() : router.replace('/home'));
  return (
    <Screen
      header={
        <AppHeader
          title={title}
          menu={menu ? { label: t('menu.open'), onPress: () => navigation.dispatch(DrawerActions.openDrawer()) } : undefined}
          onBack={back && !menu ? goBack : undefined}
          backLabel={t('common.back')}
          action={headerAction}
        />
      }
      footer={footer}
      keyboardAware={keyboardAware}
      overlay={overlay}
      followEnd={followEnd}
    >
      {children}
    </Screen>
  );
}
