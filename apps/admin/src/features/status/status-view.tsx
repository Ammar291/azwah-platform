import { CircleCheck, FlaskConical, LoaderCircle, Settings, TriangleAlert, Unplug, type LucideIcon } from 'lucide-react';

import { StatusPill, type StatusTone } from '@/components/data/status-badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader } from '@/components/ui/card';
import type { ApiStatus } from '@/lib/api/health';
import type { Messages } from '@/lib/i18n';

export type StatusViewState = ApiStatus | 'mock' | 'checking';

/** Each state's reserved status tone and icon, in the pill every other status badge uses; the text carries the meaning. */
const STATES: Record<StatusViewState, [StatusTone, LucideIcon]> = {
  checking: ['neutral', LoaderCircle],
  ready: ['success', CircleCheck],
  not_ready: ['warning', TriangleAlert],
  unreachable: ['danger', Unplug],
  not_configured: ['danger', Settings],
  mock: ['warning', FlaskConical],
};

export function StatusView({
  status,
  messages,
  onRetry,
}: {
  status: StatusViewState;
  messages: Messages;
  onRetry: () => void;
}) {
  const canRetry = status !== 'ready' && status !== 'checking' && status !== 'mock';
  const [tone, icon] = STATES[status];
  return (
    <Card>
      <CardHeader>
        {/* A section heading in the sans, like every other card's; CardTitle's display face read as tiny print here. */}
        <h2 className="text-base font-semibold">{messages.statusPage.title}</h2>
      </CardHeader>
      <CardContent className="flex flex-wrap items-center gap-3">
        <StatusPill role="status" size="lg" tone={tone} icon={icon}>
          {messages.statusPage.states[status]}
        </StatusPill>
        {canRetry ? (
          <Button variant="outline" size="lg" onClick={onRetry}>
            {messages.common.retry}
          </Button>
        ) : null}
      </CardContent>
    </Card>
  );
}
