import { zodResolver } from '@hookform/resolvers/zod';
import { render, screen, userEvent, waitFor } from '@testing-library/react-native';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { AppButton } from '../AppButton';
import { FormField } from '../FormField';

const schema = z.object({ name: z.string().trim().min(1, 'Enter a name') });

function NameForm({ onValid }: { onValid: (values: { name: string }) => void }) {
  const { control, handleSubmit } = useForm({ resolver: zodResolver(schema), defaultValues: { name: '' } });
  return (
    <>
      <FormField control={control} name="name" label="Name" />
      <AppButton label="Save" onPress={() => void handleSubmit(onValid)()} />
    </>
  );
}

describe('FormField', () => {
  it('shows the schema message and does not submit when empty', async () => {
    const onValid = jest.fn();
    const user = userEvent.setup();
    await render(<NameForm onValid={onValid} />);

    await user.press(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Enter a name');
    expect(onValid).not.toHaveBeenCalled();
  });

  it('submits what the person typed', async () => {
    const onValid = jest.fn();
    const user = userEvent.setup();
    await render(<NameForm onValid={onValid} />);

    await user.type(screen.getByLabelText('Name'), 'Layla');
    await user.press(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(onValid).toHaveBeenCalledWith({ name: 'Layla' }, undefined));
  });
});
