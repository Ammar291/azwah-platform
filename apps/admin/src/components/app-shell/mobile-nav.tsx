'use client';

import { Menu, X } from 'lucide-react';
import { useState } from 'react';

import { Button } from '@/components/ui/button';
import { Sheet, SheetClose, SheetContent, SheetTitle, SheetTrigger } from '@/components/ui/sheet';
import { useI18n } from '@/lib/i18n-context';
import type { NavItem } from '@/lib/permissions';

import { BrandMark } from './brand-mark';
import { SidebarNav } from './sidebar-nav';

/** Below the medium breakpoint the sidebar becomes a sheet from the start edge, opened from the top bar. */
export function MobileNav({ navigation }: { navigation: NavItem[] }) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger render={<Button variant="ghost" size="icon-lg" className="md:hidden" aria-label={t.shell.openMenu} />}>
        <Menu aria-hidden />
      </SheetTrigger>
      {/* `left` is the start edge in both directions (see SheetContent); flipping it here too would undo that. */}
      <SheetContent
        side="left"
        showCloseButton={false}
        className="w-72 gap-6 bg-sidebar p-4 text-sidebar-foreground"
      >
        <div className="flex items-start justify-between gap-3">
          <SheetTitle className="text-sidebar-primary">
            <BrandMark />
          </SheetTitle>
          <SheetClose
            render={
              <Button
                variant="ghost"
                size="icon-lg"
                className="text-sidebar-foreground hover:bg-sidebar-accent hover:text-sidebar-accent-foreground"
                aria-label={t.shell.closeMenu}
              />
            }
          >
            <X aria-hidden />
          </SheetClose>
        </div>
        <SidebarNav navigation={navigation} onNavigate={() => setOpen(false)} />
      </SheetContent>
    </Sheet>
  );
}
