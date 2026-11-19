'use client';

import { Inbox } from 'lucide-react';
import type { ReactNode } from 'react';

import { StateLayout } from './state-layout';

export function EmptyState({ title, description, action, icon }: { title: string; description?: ReactNode; action?: ReactNode; icon?: ReactNode }) {
  return (
    <StateLayout icon={icon ?? <Inbox />} title={title} body={description} role="status">
      {action}
    </StateLayout>
  );
}
