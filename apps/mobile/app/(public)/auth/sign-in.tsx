import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';

import { AppHeader } from '@/design-system/AppHeader';
import { Screen } from '@/design-system/Screen';
import { SignInPanel } from '@/features/auth/SignInPanel';
import { UaePassConnecting } from '@/features/auth/UaePassConnecting';
import { useUaePassSignIn } from '@/features/auth/useUaePassSignIn';

/** Where an expired session lands (entryHref): why it ended, and UAE PASS again. */
export default function SignIn() {
  const { t } = useTranslation();
  const router = useRouter();
  const signIn = useUaePassSignIn();
  return (
    <>
      <Screen
        header={
          <AppHeader
            title={t('signIn.title')}
            onBack={() => (router.canGoBack() ? router.back() : router.replace('/welcome'))}
            backLabel={t('common.back')}
          />
        }
      >
        <SignInPanel signIn={signIn} />
      </Screen>
      <UaePassConnecting visible={signIn.connecting} />
    </>
  );
}
