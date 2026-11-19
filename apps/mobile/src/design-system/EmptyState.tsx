import { StateLayout, type StateAction } from './StateLayout';
import { colors } from './tokens';

export interface EmptyStateProps {
  title: string;
  message?: string;
  action?: StateAction;
}

/** Nothing to show yet, with what to do next when there is something. */
export function EmptyState({ title, message, action }: EmptyStateProps) {
  return (
    <StateLayout
      icon="info"
      iconColor={colors.primary}
      iconBackground={colors.primarySoft}
      title={title}
      titleRole="header"
      message={message}
      primary={action}
    />
  );
}
