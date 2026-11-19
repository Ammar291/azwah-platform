import { useTranslation } from 'react-i18next';
import { Image, StyleSheet, View } from 'react-native';

import { brand } from '@/design-system/brand';
import { colors } from '@/design-system/tokens';

/**
 * Shown over everything while the app is inactive or in the background, so the snapshot the system keeps for the app
 * switcher shows the brand mark and nothing else.
 */
export function PrivacyCover() {
  const { t } = useTranslation();
  return (
    <View style={[StyleSheet.absoluteFill, styles.cover]} testID="privacy-cover">
      <Image
        source={brand.emblem}
        style={styles.emblem}
        resizeMode="contain"
        accessible
        accessibilityLabel={t(brand.nameKey)}
        accessibilityIgnoresInvertColors
      />
    </View>
  );
}

const styles = StyleSheet.create({
  cover: { alignItems: 'center', justifyContent: 'center', backgroundColor: colors.background },
  // Explicit size: Android ignores aspectRatio on an Image sized by width alone.
  emblem: { width: 144, height: Math.round(144 / brand.emblemAspectRatio) },
});
