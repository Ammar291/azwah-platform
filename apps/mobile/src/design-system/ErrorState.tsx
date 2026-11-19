import { StateLayout, type StateAction } from './StateLayout';
import { statusColors } from './tokens';

export interface ErrorStateProps {
  title: string;
  message?: string;
  retry?: StateAction;
  /** Another way forward, for example "Go home". */
  action?: StateAction;
}

/** Something failed. The title is announced. */
export function ErrorState({ title, message, retry, action }: ErrorStateProps) {
  return (
    <StateLayout
      icon="alert"
      iconColor={statusColors.danger}
      iconBackground={statusColors.dangerSoft}
      title={title}
      titleRole="alert"
      message={message}
      primary={retry}
      secondary={action}
    />
  );
}
