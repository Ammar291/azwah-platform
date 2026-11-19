import { PermissionDenied } from '@/components/states/permission-denied';
import { VersionView } from '@/features/configuration/version-view';
import { PAGE_PERMISSIONS } from '@/lib/permissions';
import { requirePermission } from '@/lib/server/guard';

export default async function VersionPage({ params }: { params: Promise<{ versionId: string }> }) {
  const caller = await requirePermission(PAGE_PERMISSIONS.configuration);
  if (!caller) return <PermissionDenied />;
  const { versionId } = await params;
  return <VersionView versionId={versionId} />;
}
