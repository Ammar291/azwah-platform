import { render, screen, userEvent } from '@testing-library/react-native';

import { AppText } from '../AppText';
import { ListGroup, ListRow } from '../ListGroup';

describe('ListGroup', () => {
  it('makes each row one labelled button that reports its press', async () => {
    const openHousehold = jest.fn();
    const openHelp = jest.fn();
    const user = userEvent.setup();
    await render(
      <ListGroup>
        <ListRow title="Open your household" description="Household reference: DEV-4821" onPress={openHousehold} />
        <ListRow title="Help" onPress={openHelp} />
      </ListGroup>,
    );

    await user.press(screen.getByRole('button', { name: 'Open your household' }));
    await user.press(screen.getByRole('button', { name: 'Help' }));

    expect(openHousehold).toHaveBeenCalledTimes(1);
    expect(openHelp).toHaveBeenCalledTimes(1);
    expect(screen.getByText('Household reference: DEV-4821')).toBeOnTheScreen();
    expect(screen.getAllByTestId('icon-chevron-forward', { includeHiddenElements: true })).toHaveLength(2);
  });

  it('uses a fuller screen-reader name when one is given', async () => {
    await render(
      <ListGroup>
        <ListRow title="Edit" accessibilityLabel="Edit Spouse" onPress={jest.fn()} />
      </ListGroup>,
    );

    expect(screen.getByRole('button', { name: 'Edit Spouse' })).toBeOnTheScreen();
  });

  it('shows richer content in a row, read as one button with the given name', async () => {
    const onPress = jest.fn();
    const user = userEvent.setup();
    await render(
      <ListGroup>
        <ListRow testID="review-item-1" accessibilityLabel="Question 1: Agree" onPress={onPress}>
          <AppText>Question 1</AppText>
          <AppText>Agree</AppText>
        </ListRow>
      </ListGroup>,
    );

    await user.press(screen.getByRole('button', { name: 'Question 1: Agree' }));

    expect(onPress).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('review-item-1')).toBeOnTheScreen();
  });

  it('skips empty slots, so a row shown only sometimes leaves no stray divider', async () => {
    const shown = false;
    await render(
      <ListGroup testID="group">
        {shown ? <ListRow title="Hidden" onPress={jest.fn()} /> : null}
        <ListRow title="Help" onPress={jest.fn()} />
      </ListGroup>,
    );

    expect(screen.getAllByRole('button')).toHaveLength(1);
    expect(screen.getByTestId('group').children).toHaveLength(1);
  });
});
