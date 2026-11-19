import type { AssignmentStatus } from '@/api/participant/schemas';
import type { BadgeTone } from '@/design-system/Badge';

export interface AssignmentStatusView {
  tone: BadgeTone;
  /** Translation keys. */
  labelKey: string;
  actionKey: string;
  open: boolean;
}

/** How each server status is shown. Display only: the server decides every status. */
export const ASSIGNMENT_STATUS_VIEW: Record<AssignmentStatus, AssignmentStatusView> = {
  assigned: { tone: 'neutral', labelKey: 'assignmentStatus.assigned', actionKey: 'assessments.actions.start', open: true },
  in_progress: {
    tone: 'info',
    labelKey: 'assignmentStatus.in_progress',
    actionKey: 'assessments.actions.continue',
    open: true,
  },
  completed: {
    tone: 'success',
    labelKey: 'assignmentStatus.completed',
    actionKey: 'assessments.actions.view',
    open: false,
  },
  expired: { tone: 'warning', labelKey: 'assignmentStatus.expired', actionKey: 'assessments.actions.view', open: false },
  cancelled: {
    tone: 'neutral',
    labelKey: 'assignmentStatus.cancelled',
    actionKey: 'assessments.actions.view',
    open: false,
  },
};
