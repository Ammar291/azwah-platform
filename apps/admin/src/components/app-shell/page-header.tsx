'use client';

import { ChevronRight } from 'lucide-react';
import Link from 'next/link';
import { Fragment, type ReactNode } from 'react';

import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb';
import { useI18n } from '@/lib/i18n-context';

export interface Crumb {
  label: string;
  href?: string;
}

/** A page's breadcrumbs, title (the brand heading face), one-line description and actions. */
export function PageHeader({ title, description, crumbs = [], actions }: { title: string; description?: ReactNode; crumbs?: Crumb[]; actions?: ReactNode }) {
  const { t } = useI18n();
  const trail: Crumb[] = [{ label: t.shell.home, href: '/' }, ...crumbs];
  return (
    <header className="flex flex-col gap-3">
      <Breadcrumb aria-label={t.shell.breadcrumb}>
        <BreadcrumbList>
          {trail.map((crumb, index) => (
            <Fragment key={`${crumb.label}-${index}`}>
              {index > 0 ? (
                <BreadcrumbSeparator>
                  <ChevronRight className="rtl:rotate-180" />
                </BreadcrumbSeparator>
              ) : null}
              <BreadcrumbItem>
                {crumb.href ? (
                  <BreadcrumbLink render={<Link href={crumb.href} />}>{crumb.label}</BreadcrumbLink>
                ) : (
                  <BreadcrumbPage>{crumb.label}</BreadcrumbPage>
                )}
              </BreadcrumbItem>
            </Fragment>
          ))}
          <BreadcrumbSeparator>
            <ChevronRight className="rtl:rotate-180" />
          </BreadcrumbSeparator>
          <BreadcrumbItem>
            <BreadcrumbPage>{title}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="font-heading text-4xl leading-tight font-semibold text-primary">{title}</h1>
          {description ? <div className="max-w-prose text-sm text-muted-foreground">{description}</div> : null}
        </div>
        {actions ? <div className="flex flex-wrap items-center gap-2">{actions}</div> : null}
      </div>
    </header>
  );
}
