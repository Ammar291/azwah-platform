import Link from 'next/link';

import { DevBanner } from '@/components/app-shell/dev-banner';
import { buttonVariants } from '@/components/ui/button';
import { messages } from '@/lib/i18n';
import { getRequestLanguage } from '@/lib/i18n-server';
import { getAdminConfig } from '@/lib/server/config';

export default async function NotFound() {
  const t = messages[await getRequestLanguage()];
  // Simulated sign-in and data are labelled on every page, this one included (CONTRIBUTING.md).
  const config = getAdminConfig();
  const developmentSignIn = config.ok && config.config.auth.mode === 'development';
  const mockData = config.ok && config.config.data.source === 'mock';
  return (
    <div className="flex min-h-screen flex-col">
      <DevBanner developmentSignIn={developmentSignIn} mockData={mockData} />
      <main id="main" className="mx-auto flex w-full max-w-xl flex-1 flex-col justify-center gap-4 px-4">
        <h1 className="font-heading text-4xl font-semibold text-primary">{t.notFoundPage.title}</h1>
        <p className="text-muted-foreground">{t.notFoundPage.body}</p>
        <div>
          <Link href="/" className={buttonVariants({ size: 'lg' })}>
            {t.notFoundPage.home}
          </Link>
        </div>
      </main>
    </div>
  );
}
