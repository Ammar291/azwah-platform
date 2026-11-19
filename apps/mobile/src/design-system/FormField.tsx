import { Controller, type Control, type FieldPath, type FieldValues } from 'react-hook-form';
import type { KeyboardTypeOptions } from 'react-native';

import { AppInput } from './AppInput';

export interface FormFieldProps<T extends FieldValues> {
  control: Control<T>;
  name: FieldPath<T>;
  label: string;
  hint?: string;
  keyboardType?: KeyboardTypeOptions;
  maxLength?: number;
  disabled?: boolean;
}

/** An AppInput bound to React Hook Form. Validation messages come from the form's Zod schema. */
export function FormField<T extends FieldValues>({ control, name, ...input }: FormFieldProps<T>) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field: { onChange, onBlur, value, ref }, fieldState: { error } }) => (
        <AppInput
          {...input}
          ref={ref}
          value={typeof value === 'string' ? value : value == null ? '' : String(value)}
          onChangeText={onChange}
          onBlur={onBlur}
          error={error?.message}
        />
      )}
    />
  );
}
