import { SvgXml } from 'react-native-svg';

import { authorityMarkSvg } from './authorityMarks';

/** Azwah's symbol for an organisation. Decorative: the name always sits beside it, so screen readers skip it. */
export function AuthorityMark({ code, size = 44 }: { code: string; size?: number }) {
  return (
    <SvgXml
      xml={authorityMarkSvg(code)}
      width={size}
      height={size}
      accessibilityElementsHidden
      importantForAccessibility="no-hide-descendants"
      testID={`authority-mark-${code}`}
    />
  );
}
