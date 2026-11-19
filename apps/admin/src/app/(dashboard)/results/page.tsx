import { PermissionDenied } from '@/components/states/permission-denied';
import { ResultsView } from '@/features/results/results-view';
import { PAGE_PERMISSIONS } from '@/lib/permissions';
import { requirePermission } from '@/lib/server/guard';

export default async function HouseholdResultsPage() {
  const caller = await requirePermission(PAGE_PERMISSIONS.results);
  return caller ? <ResultsView /> : <PermissionDenied />;
}
