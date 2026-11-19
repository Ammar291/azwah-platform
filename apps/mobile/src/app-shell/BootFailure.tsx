import { StyleSheet, Text, View } from 'react-native';

/** Shown when local storage cannot be opened. Translations are not loaded yet, so both languages are inline. */
export function BootFailure() {
  return (
    <View style={styles.container} accessibilityRole="alert">
      <Text style={styles.text}>Azwah could not start. Close the app and open it again.</Text>
      <Text style={styles.text}>تعذّر تشغيل عزوة. أغلق التطبيق وافتحه مرة أخرى.</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: 16, padding: 24, backgroundColor: '#faf8f3' },
  text: { fontSize: 16, color: '#0b2b1f', textAlign: 'center' },
});
