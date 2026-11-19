import { PermissionDenied } from '@/components/states/permission-denied';
import { AnalyticsView } from '@/features/analytics/analytics-view';
import { PAGE_PERMISSIONS } from '@/lib/permissions';
import { requirePermission } from '@/lib/server/guard';

export default async function AnalyticsPage() {
  const caller = await requirePermission(PAGE_PERMISSIONS.analytics);
  return caller ? <AnalyticsView /> : <PermissionDenied />;
}
