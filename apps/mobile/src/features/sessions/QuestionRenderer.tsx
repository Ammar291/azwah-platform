import type { PublishedItem } from '@/api/participant/schemas';
import { LikertScale } from '@/design-system/LikertScale';
import { Notice } from '@/design-system/Notice';
import { useLocalization } from '@/i18n/useLocalization';

export interface QuestionRendererProps {
  item: PublishedItem;
  value: string | null;
  onChange: (optionId: string) => void;
  disabled?: boolean;
}

/**
 * Renders one question from the server's schema, with explicit support per item type (decision M-43). The options
 * are the published version's own, in its order; nothing here holds questionnaire content or interprets answers.
 * Adding a type means adding its case here and its answer rule in questionnaire.ts.
 */
export function QuestionRenderer({ item, value, onChange, disabled = false }: QuestionRendererProps) {
  const { t, pickText } = useLocalization();
  switch (item.itemType) {
    case 'single_choice':
      return (
        <LikertScale
          label={pickText(item.prompt)}
          hideLabel
          options={item.options.map((option) => ({
            value: option.id,
            label: pickText(option.label),
            nonSubstantive: option.isNonSubstantive,
          }))}
          value={value}
          onChange={onChange}
          disabled={disabled}
        />
      );
    case 'unsupported':
      // Never answered as something else: the whole questionnaire waits for an app that supports the type.
      return <Notice tone="warning" title={t('questionnaire.unsupported.title')} message={t('questionnaire.unsupported.message')} />;
  }
}
