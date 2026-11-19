import { StyleSheet, Text, View, type ViewStyle } from 'react-native';

import { useIsRTLLayout } from './LayoutDirection';
import { colors, fontFamilies } from './tokens';

export type IconName =
  | 'chevron-forward'
  | 'chevron-back'
  | 'check'
  | 'alert'
  | 'info'
  | 'plus'
  | 'close'
  | 'dot'
  | 'fingerprint'
  | 'lock'
  | 'chat'
  | 'menu'
  | 'home'
  | 'mic'
  | 'people'
  | 'heart'
  | 'grid'
  | 'settings'
  | 'sign-out';

/** Glyphs that point along the reading direction and so mirror in right-to-left layouts. */
const DIRECTIONAL: ReadonlySet<IconName> = new Set(['chevron-forward', 'chevron-back', 'sign-out']);

export interface IconProps {
  name: IconName;
  size?: number;
  color?: string;
}

/**
 * Small drawn glyphs; the app ships no icon font. Decorative: the control or text beside an icon carries the meaning
 * for screen readers. Forward and back chevrons and the sign-out arrow follow the layout direction, so in Arabic
 * forward points left.
 */
export function Icon({ name, size = 20, color = colors.text }: IconProps) {
  const rtl = useIsRTLLayout();
  // "Back" is drawn as a mirrored "forward"; mirror again when the layout runs right to left.
  const mirrored = DIRECTIONAL.has(name) && (name === 'chevron-back') !== rtl;
  return (
    <View
      testID={`icon-${name}`}
      accessible={false}
      importantForAccessibility="no-hide-descendants"
      // Drawn in a fixed left-to-right box so the shapes never flip by accident; mirroring is explicit.
      style={[styles.box, { width: size, height: size }, mirrored ? styles.mirrored : null]}
    >
      <Glyph name={name} size={size} color={color} />
    </View>
  );
}

function Glyph({ name, size, color }: Required<IconProps>) {
  const stroke = Math.max(2, Math.round(size / 10));
  const bar = (length: number, top: number, start: number, rotate: string) => (
    <View
      style={[
        styles.bar,
        { width: length, height: stroke, top, start, backgroundColor: color, transform: [{ rotate }] },
      ]}
    />
  );
  const centre = size / 2;
  /** A stroke from one point to another, each given as fractions of the box (0 to 1). */
  const line = (x1: number, y1: number, x2: number, y2: number) => {
    const dx = (x2 - x1) * size;
    const dy = (y2 - y1) * size;
    const length = Math.hypot(dx, dy);
    const angle = (Math.atan2(dy, dx) * 180) / Math.PI;
    return bar(length, ((y1 + y2) / 2) * size - stroke / 2, ((x1 + x2) / 2) * size - length / 2, `${angle}deg`);
  };
  /** A shape placed in the box; `at` gives its top and start as fractions of the box. */
  const shape = (at: [top: number, start: number], style: ViewStyle, key?: string) => (
    <View key={key} style={[styles.bar, { top: at[0] * size, start: at[1] * size }, style]} />
  );

  switch (name) {
    case 'chevron-forward':
    case 'chevron-back': {
      const length = size * 0.45;
      const start = centre - length / 2;
      const offset = length * 0.32;
      return (
        <>
          {bar(length, centre - offset - stroke / 2, start, '45deg')}
          {bar(length, centre + offset - stroke / 2, start, '-45deg')}
        </>
      );
    }
    case 'check':
      return (
        <>
          {bar(size * 0.32, size * 0.56, size * 0.14, '45deg')}
          {bar(size * 0.6, size * 0.46, size * 0.3, '-50deg')}
        </>
      );
    case 'plus':
    case 'close': {
      const length = size * 0.7;
      const start = centre - length / 2;
      const top = centre - stroke / 2;
      const angle = name === 'plus' ? 0 : 45;
      return (
        <>
          {bar(length, top, start, `${angle}deg`)}
          {bar(length, top, start, `${angle + 90}deg`)}
        </>
      );
    }
    case 'alert':
    case 'info':
      return (
        <View style={[styles.ring, { borderRadius: size / 2, borderWidth: stroke, borderColor: color }]}>
          <Text
            allowFontScaling={false}
            style={{ color, fontFamily: fontFamilies.bodyStrong, fontSize: size * 0.6, lineHeight: size * 0.8 }}
          >
            {name === 'alert' ? '!' : 'i'}
          </Text>
        </View>
      );
    case 'fingerprint': {
      // Nested open rings, the usual shorthand for a fingerprint.
      const ringStroke = Math.max(1.5, size / 14);
      return (
        <>
          {[1, 0.7, 0.4].map((scale) => {
            const diameter = size * scale;
            return (
              <View
                key={scale}
                style={[
                  styles.bar,
                  {
                    width: diameter,
                    height: diameter,
                    top: centre - diameter / 2,
                    start: centre - diameter / 2,
                    borderRadius: diameter / 2,
                    borderWidth: ringStroke,
                    borderColor: color,
                    borderBottomColor: 'transparent',
                    backgroundColor: 'transparent',
                  },
                ]}
              />
            );
          })}
          <View
            style={[
              styles.bar,
              {
                width: ringStroke,
                height: size * 0.32,
                top: centre - size * 0.06,
                start: centre - ringStroke / 2,
                backgroundColor: color,
              },
            ]}
          />
        </>
      );
    }
    case 'lock':
      return (
        <>
          <View
            style={[
              styles.bar,
              {
                width: size * 0.5,
                height: size * 0.5,
                top: size * 0.06,
                start: size * 0.25,
                borderRadius: size * 0.25,
                borderWidth: stroke,
                borderColor: color,
                borderBottomColor: 'transparent',
              },
            ]}
          />
          <View
            style={[
              styles.bar,
              { width: size * 0.7, height: size * 0.48, top: size * 0.42, start: size * 0.15, backgroundColor: color },
            ]}
          />
        </>
      );
    case 'chat': {
      // A speech bubble with three dots, its tail at the bottom start (the bubble does not mirror: it is not a direction).
      const dot = size * 0.1;
      return (
        <>
          <View
            style={[
              styles.bar,
              {
                width: size * 0.84,
                height: size * 0.62,
                top: size * 0.1,
                start: size * 0.08,
                borderRadius: size * 0.2,
                borderWidth: stroke,
                borderColor: color,
              },
            ]}
          />
          <View
            style={[
              styles.bar,
              {
                width: size * 0.22,
                height: size * 0.22,
                top: size * 0.62,
                start: size * 0.2,
                backgroundColor: color,
                borderRadius: 1,
                transform: [{ skewX: '-20deg' }],
              },
            ]}
          />
          {[0.3, 0.5, 0.7].map((at) => (
            <View
              key={at}
              style={[
                styles.bar,
                { width: dot, height: dot, top: size * 0.41 - dot / 2, start: size * at - dot / 2, backgroundColor: color, borderRadius: dot },
              ]}
            />
          ))}
        </>
      );
    }
    case 'menu':
      return (
        <>
          {[0.27, 0.5, 0.73].map((at) =>
            shape([at - stroke / size / 2, 0.14], { width: size * 0.72, height: stroke, backgroundColor: color }, `${at}`),
          )}
        </>
      );
    case 'home':
      return (
        <>
          {line(0.1, 0.48, 0.5, 0.12)}
          {line(0.5, 0.12, 0.9, 0.48)}
          {shape([0.42, 0.22], {
            width: size * 0.56,
            height: size * 0.44,
            borderWidth: stroke,
            borderTopWidth: 0,
            borderColor: color,
          })}
        </>
      );
    case 'mic':
      return (
        <>
          {shape([0.06, 0.34], {
            width: size * 0.32,
            height: size * 0.5,
            borderRadius: size * 0.16,
            borderWidth: stroke,
            borderColor: color,
          })}
          {/* The cradle: open at the top, rounded at the bottom. */}
          {shape([0.32, 0.22], {
            width: size * 0.56,
            height: size * 0.38,
            borderWidth: stroke,
            borderTopWidth: 0,
            borderColor: color,
            borderRadius: 0,
            borderBottomStartRadius: size * 0.28,
            borderBottomEndRadius: size * 0.28,
          })}
          {shape([0.7, 0.5 - stroke / size / 2], { width: stroke, height: size * 0.14, backgroundColor: color })}
          {shape([0.84 - stroke / size / 2, 0.3], { width: size * 0.4, height: stroke, backgroundColor: color })}
        </>
      );
    case 'people': {
      // Two people: the one behind, at the end side, is fainter.
      const person = (
        head: number,
        headAt: [number, number],
        body: [number, number],
        bodyAt: [number, number],
        faded: boolean,
      ) => (
        <>
          {shape(headAt, {
            width: head * size,
            height: head * size,
            borderRadius: size,
            backgroundColor: color,
            opacity: faded ? 0.55 : 1,
          })}
          {shape(bodyAt, {
            width: body[0] * size,
            height: body[1] * size,
            borderRadius: 2,
            borderTopStartRadius: (body[0] * size) / 2,
            borderTopEndRadius: (body[0] * size) / 2,
            backgroundColor: color,
            opacity: faded ? 0.55 : 1,
          })}
        </>
      );
      return (
        <>
          {person(0.24, [0.2, 0.62], [0.36, 0.34], [0.5, 0.56], true)}
          {person(0.3, [0.12, 0.18], [0.5, 0.36], [0.48, 0.08], false)}
        </>
      );
    }
    case 'heart': {
      // A square turned on its corner with a circle on each upper side.
      const lobe = size * 0.5;
      const offset = lobe / (2 * Math.SQRT2);
      const middle = size * 0.56;
      return (
        <>
          {shape([(middle - lobe / 2) / size, (centre - lobe / 2) / size], {
            width: lobe,
            height: lobe,
            borderRadius: 1,
            backgroundColor: color,
            transform: [{ rotate: '45deg' }],
          })}
          {[-offset, offset].map((dx) =>
            shape(
              [(middle - offset - lobe / 2) / size, (centre + dx - lobe / 2) / size],
              { width: lobe, height: lobe, borderRadius: lobe / 2, backgroundColor: color },
              `${dx}`,
            ),
          )}
        </>
      );
    }
    case 'grid':
      return (
        <>
          {(
            [
              [0.1, 0.1],
              [0.1, 0.56],
              [0.56, 0.1],
              [0.56, 0.56],
            ] as const
          ).map(([top, start]) =>
            shape(
              [top, start],
              { width: size * 0.34, height: size * 0.34, borderRadius: size * 0.08, borderWidth: stroke, borderColor: color },
              `${top}-${start}`,
            ),
          )}
        </>
      );
    case 'settings': {
      // Three sliders, each knob at a different place.
      const knob = size * 0.22;
      const rows = [
        [0.24, 0.3],
        [0.5, 0.68],
        [0.76, 0.44],
      ] as const;
      return (
        <>
          {rows.map(([at]) =>
            shape([at - stroke / size / 2, 0.1], { width: size * 0.8, height: stroke, backgroundColor: color }, `line-${at}`),
          )}
          {rows.map(([at, x]) =>
            shape(
              [at - knob / size / 2, x - knob / size / 2],
              { width: knob, height: knob, borderRadius: knob, backgroundColor: color },
              `knob-${at}`,
            ),
          )}
        </>
      );
    }
    case 'sign-out':
      // A door open towards the end, with an arrow leaving through it (mirrored in right-to-left layouts).
      return (
        <>
          {shape([0.14, 0.1], {
            width: size * 0.42,
            height: size * 0.72,
            borderWidth: stroke,
            borderEndWidth: 0,
            borderColor: color,
          })}
          {line(0.36, 0.5, 0.9, 0.5)}
          {line(0.7, 0.3, 0.9, 0.5)}
          {line(0.7, 0.7, 0.9, 0.5)}
        </>
      );
    case 'dot':
      return (
        <View
          style={{
            width: size * 0.45,
            height: size * 0.45,
            borderRadius: size,
            backgroundColor: color,
            margin: size * 0.275,
          }}
        />
      );
  }
}

const styles = StyleSheet.create({
  box: { direction: 'ltr', position: 'relative' },
  mirrored: { transform: [{ scaleX: -1 }] },
  bar: { position: 'absolute', borderRadius: 2 },
  ring: { flex: 1, alignItems: 'center', justifyContent: 'center' },
});
