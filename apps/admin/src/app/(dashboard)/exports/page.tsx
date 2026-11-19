import { PermissionDenied } from '@/components/states/permission-denied';
import { ExportsView } from '@/features/exports/exports-view';
import { PAGE_PERMISSIONS } from '@/lib/permissions';
import { requirePermission } from '@/lib/server/guard';

export default async function ExportsPage() {
  const caller = await requirePermission(PAGE_PERMISSIONS.exports);
  return caller ? <ExportsView /> : <PermissionDenied />;
}
