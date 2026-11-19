import { PermissionDenied } from '@/components/states/permission-denied';
import { RuleSetView } from '@/features/configuration/rule-set-view';
import { PAGE_PERMISSIONS } from '@/lib/permissions';
import { requirePermission } from '@/lib/server/guard';

export default async function RuleSetPage({ params }: { params: Promise<{ ruleSetId: string }> }) {
  const caller = await requirePermission(PAGE_PERMISSIONS.configuration);
  if (!caller) return <PermissionDenied />;
  const { ruleSetId } = await params;
  return <RuleSetView ruleSetId={ruleSetId} />;
}
