import { PermissionDenied } from '@/components/states/permission-denied';
import { ParticipationView } from '@/features/monitoring/participation-view';
import { PAGE_PERMISSIONS } from '@/lib/permissions';
import { requirePermission } from '@/lib/server/guard';

export default async function ParticipationPage() {
  const caller = await requirePermission(PAGE_PERMISSIONS.participation);
  return caller ? <ParticipationView /> : <PermissionDenied />;
}
