import { PermissionDenied } from '@/components/states/permission-denied';
import { InstrumentView } from '@/features/configuration/instrument-view';
import { PAGE_PERMISSIONS } from '@/lib/permissions';
import { requirePermission } from '@/lib/server/guard';

export default async function InstrumentPage({ params }: { params: Promise<{ instrumentId: string }> }) {
  const caller = await requirePermission(PAGE_PERMISSIONS.configuration);
  if (!caller) return <PermissionDenied />;
  const { instrumentId } = await params;
  return <InstrumentView instrumentId={instrumentId} />;
}
