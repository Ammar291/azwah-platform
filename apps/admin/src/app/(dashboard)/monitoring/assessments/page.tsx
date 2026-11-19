import { PermissionDenied } from '@/components/states/permission-denied';
import { AssessmentsView } from '@/features/monitoring/assessments-view';
import { PAGE_PERMISSIONS } from '@/lib/permissions';
import { requirePermission } from '@/lib/server/guard';

export default async function AssessmentMonitoringPage() {
  const caller = await requirePermission(PAGE_PERMISSIONS.assessments);
  return caller ? <AssessmentsView /> : <PermissionDenied />;
}
