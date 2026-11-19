import { RadioCardGroup, type RadioValue } from './RadioCardGroup';

export interface LikertOption<V extends RadioValue = number> {
  value: V;
  label: string;
  /**
   * An answer outside the scale, such as the codebook's 99 "prefer not to answer" (D-39). Set apart from the scale by
   * a divider and a quieter card, where the version places it.
   */
  nonSubstantive?: boolean;
}

export interface LikertScaleProps<V extends RadioValue = number> {
  /** The statement or question; also the group's accessible name. */
  label: string;
  hideLabel?: boolean;
  /**
   * Supplied by the caller from an approved instrument version, in the version's own display order. This component
   * holds no questionnaire content and never reorders it.
   */
  options: readonly LikertOption<V>[];
  value: V | null;
  onChange: (value: V) => void;
  disabled?: boolean;
}

/**
 * One answer scale (4 or 5 options for the two Azwah instruments), as large radio cards, in exactly the order given.
 * An answer outside the scale keeps its place, label and value; it is only drawn apart, in a quieter card with a
 * divider before it when an answer of the scale comes first, so it does not read as one more step of the scale.
 */
export function LikertScale<V extends RadioValue = number>({ options, ...rest }: LikertScaleProps<V>) {
  return (
    <RadioCardGroup
      {...rest}
      options={options.map(({ value, label, nonSubstantive = false }, index) => {
        const previous = options[index - 1];
        return {
          value,
          label,
          subdued: nonSubstantive,
          separated: nonSubstantive && previous !== undefined && !previous.nonSubstantive,
        };
      })}
    />
  );
}
