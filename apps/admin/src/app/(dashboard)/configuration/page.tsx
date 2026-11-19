import { PermissionDenied } from '@/components/states/permission-denied';
import { InstrumentsView } from '@/features/configuration/instruments-view';
import { PAGE_PERMISSIONS } from '@/lib/permissions';
import { requirePermission } from '@/lib/server/guard';

export default async function ConfigurationPage() {
  const caller = await requirePermission(PAGE_PERMISSIONS.configuration);
  return caller ? <InstrumentsView /> : <PermissionDenied />;
}
