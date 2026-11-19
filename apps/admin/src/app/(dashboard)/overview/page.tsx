import { PermissionDenied } from '@/components/states/permission-denied';
import { OverviewView } from '@/features/overview/overview-view';
import { PAGE_PERMISSIONS } from '@/lib/permissions';
import { requirePermission } from '@/lib/server/guard';

export default async function OverviewPage() {
  const caller = await requirePermission(PAGE_PERMISSIONS.overview);
  return caller ? <OverviewView /> : <PermissionDenied />;
}
