import type { Metadata } from 'next';
import { Markazi_Text, Readex_Pro } from 'next/font/google';
import type { ReactNode } from 'react';

import { directionOf, messages } from '@/lib/i18n';
import { getRequestLanguage } from '@/lib/i18n-server';

import { Providers } from './providers';
import './globals.css';

const readexPro = Readex_Pro({ subsets: ['arabic', 'latin'], variable: '--font-readex-pro', display: 'swap' });
const markaziText = Markazi_Text({ subsets: ['arabic', 'latin'], variable: '--font-markazi-text', display: 'swap' });

/** The browser tab's title and the description, in the language the reader chose. */
export async function generateMetadata(): Promise<Metadata> {
  const t = messages[await getRequestLanguage()];
  return { title: t.app.name, description: t.app.description, robots: { index: false, follow: false } };
}

export default async function RootLayout({ children }: { children: ReactNode }) {
  const language = await getRequestLanguage();
  return (
    <html lang={language} dir={directionOf(language)} className={`${readexPro.variable} ${markaziText.variable}`}>
      <body className="min-h-screen bg-background font-sans text-foreground antialiased">
        <Providers language={language}>{children}</Providers>
      </body>
    </html>
  );
}
