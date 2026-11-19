import { StatusPageView } from '@/features/status/status-page-view';

/** Any signed-in caller may see whether the platform is reachable; the check goes through the BFF. */
export default function StatusPage() {
  return <StatusPageView />;
}
