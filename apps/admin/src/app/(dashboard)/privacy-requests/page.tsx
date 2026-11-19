import { PermissionDenied } from '@/components/states/permission-denied';
import { PrivacyRequestsView } from '@/features/privacy-requests/privacy-requests-view';
import { PAGE_PERMISSIONS } from '@/lib/permissions';
import { requirePermission } from '@/lib/server/guard';

export default async function PrivacyRequestsPage() {
  const caller = await requirePermission(PAGE_PERMISSIONS.privacyRequests);
  return caller ? <PrivacyRequestsView /> : <PermissionDenied />;
}
