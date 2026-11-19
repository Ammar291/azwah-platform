import { PermissionDenied } from '@/components/states/permission-denied';
import { SafetyAlertsView } from '@/features/safety-alerts/safety-alerts-view';
import { PAGE_PERMISSIONS } from '@/lib/permissions';
import { requirePermission } from '@/lib/server/guard';

/** Acknowledging and resolving need this as well; without it the page is read-only. */
const MANAGE_PERMISSION = 'safety_alerts.manage';

export default async function SafetyAlertsPage() {
  const caller = await requirePermission(PAGE_PERMISSIONS.safetyAlerts);
  return caller ? <SafetyAlertsView canManage={caller.permissions.includes(MANAGE_PERMISSION)} /> : <PermissionDenied />;
}
