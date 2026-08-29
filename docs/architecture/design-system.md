<!-- markdownlint-disable MD003 MD013 MD041 -->

---

name: Unified Technical Connectivity
colors:
  surface: '#f7f9fb'
  surface-dim: '#d8dadc'
  surface-bright: '#f7f9fb'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f2f4f6'
  surface-container: '#eceef0'
  surface-container-high: '#e6e8ea'
  surface-container-highest: '#e0e3e5'
  on-surface: '#191c1e'
  on-surface-variant: '#45464d'
  inverse-surface: '#2d3133'
  inverse-on-surface: '#eff1f3'
  outline: '#76777d'
  outline-variant: '#c6c6cd'
  surface-tint: '#565e74'
  primary: '#000000'
  on-primary: '#ffffff'
  primary-container: '#131b2e'
  on-primary-container: '#7c839b'
  inverse-primary: '#bec6e0'
  secondary: '#0058be'
  on-secondary: '#ffffff'
  secondary-container: '#2170e4'
  on-secondary-container: '#fefcff'
  tertiary: '#000000'
  on-tertiary: '#ffffff'
  tertiary-container: '#0b1c30'
  on-tertiary-container: '#75859d'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#dae2fd'
  primary-fixed-dim: '#bec6e0'
  on-primary-fixed: '#131b2e'
  on-primary-fixed-variant: '#3f465c'
  secondary-fixed: '#d8e2ff'
  secondary-fixed-dim: '#adc6ff'
  on-secondary-fixed: '#001a42'
  on-secondary-fixed-variant: '#004395'
  tertiary-fixed: '#d3e4fe'
  tertiary-fixed-dim: '#b7c8e1'
  on-tertiary-fixed: '#0b1c30'
  on-tertiary-fixed-variant: '#38485d'
  background: '#f7f9fb'
  on-background: '#191c1e'
  surface-variant: '#e0e3e5'
typography:
  display:
    fontFamily: Sora
    fontSize: 48px
    fontWeight: '700'
    lineHeight: 56px
    letterSpacing: -0.02em
  headline-lg:
    fontFamily: Sora
    fontSize: 32px
    fontWeight: '600'
    lineHeight: 40px
    letterSpacing: -0.01em
  headline-lg-mobile:
    fontFamily: Sora
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
  headline-md:
    fontFamily: Sora
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
  headline-sm:
    fontFamily: Sora
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
  body-lg:
    fontFamily: Inter
    fontSize: 18px
    fontWeight: '400'
    lineHeight: 28px
  body-md:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-sm:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  label-md:
    fontFamily: Inter
    fontSize: 12px
    fontWeight: '600'
    lineHeight: 16px
    letterSpacing: 0.05em
  label-sm:
    fontFamily: Inter
    fontSize: 11px
    fontWeight: '500'
    lineHeight: 14px
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  base: 4px
  xs: 4px
  sm: 8px
  md: 16px
  lg: 24px
  xl: 40px
  xxl: 64px
  container-max: 1280px
  gutter: 24px
---

## Brand & Style

The design system is built upon the core concept of **Unification**—the seamless integration of fragmented technical processes into a single, cohesive experience. The brand personality is authoritative yet innovative, positioning the platform as a robust infrastructure for modern technical assistance.

The visual style is **Corporate Modern with a Minimalist edge**. It prioritizes clarity through generous whitespace and a highly structured information architecture. Visual interest is generated not through decoration, but through precision: perfectly aligned grids, subtle depth markers, and a sophisticated interplay between deep monochromatic surfaces and vibrant technical accents. The emotional response should be one of total reliability and "hidden complexity"—where the user feels the power of the system without being overwhelmed by its mechanics.

## Colors

The color strategy uses a **High-Contrast Professional** palette to ensure accessibility and clear visual hierarchy.

- **Deep Navy (#0F172A):** Used for primary navigation, headings, and high-level structural elements to ground the UI in authority.
- **Tech Blue (#3B82F6):** The "Innovation" color. Used for primary actions, progress indicators, and active states. It represents the "connective tissue" of the system.
- **Slate Gray (#64748B):** Used for secondary text, metadata, and UI borders to maintain neutrality without losing legibility.
- **Surface Neutrals:** A range of cool grays (from #F8FAFC to #E2E8F0) is used to differentiate card layers and background sections.
- **Functional Accents:** Success (Emerald), Warning (Amber), and Error (Rose) should follow the same saturation levels as the Tech Blue to maintain a unified vibrance.

## Typography

This design system employs a dual-typeface system to balance technical precision with modern friendliness.

- **Sora** is used for all headlines and display text. Its geometric structure and unique "ink traps" reflect the technical nature of the brand while appearing contemporary.
- **Inter** is used for all body copy, labels, and data visualizations. It provides exceptional legibility at small sizes and maintains a neutral, systematic feel.

**Hierarchy Rules:**

- Use **Sora Bold** for primary headers to create high-impact "anchor points" for the eye.
- Use **Inter SemiBold** for interactive labels and button text to ensure they stand out from static body text.
- Maintain a strictly tight letter-spacing on headlines to emphasize the "Unified" concept.

## Layout & Spacing

The layout is governed by a **12-column fluid grid** for desktop and a **4-column grid** for mobile. The system uses a strict 4px base unit to ensure all components and spacing values are multiples of 4 or 8, creating a rhythmic, mechanical harmony.

**Structural Standards:**

- **Page Margins:** 24px on mobile, 40px on tablet, and 64px on desktop to allow the content to "breathe."
- **Section Spacing:** Use `xxl` (64px) to separate major functional blocks.
- **Component Padding:** Internal card padding should default to `lg` (24px) to emphasize the "clean lines" and "generous whitespace" requirement.
- **Card-Based Layouts:** Content should be grouped into cards to signify "units" of technical data, utilizing the grid to span 4, 6, or 12 columns depending on complexity.

## Elevation & Depth

To convey a sense of modern robustness, this design system uses **Tonal Layers** combined with **Ambient Shadows**.

1. **Level 0 (Canvas):** The base background layer uses `neutral_color_hex` (#F8FAFC).
2. **Level 1 (Cards):** Primary containers are pure white (#FFFFFF) with a very soft, diffused shadow (0px 4px 20px rgba(15, 23, 42, 0.05)).
3. **Level 2 (Overlays/Dropdowns):** Use a slightly more defined shadow (0px 8px 30px rgba(15, 23, 42, 0.12)) to indicate temporary interaction layers.

**Subtle Gradients:** To avoid a flat, "cheap" look, primary buttons and active indicators should feature a subtle linear gradient from Tech Blue (#3B82F6) to a slightly deeper shade (#2563EB) at a 145-degree angle. This adds a sense of "tactile technology."

## Shapes

The shape language reflects the "Professional and Robust" personality. We avoid hyper-rounded or "bubble" shapes in favor of **precise, geometric corners**.

- **Standard Elements:** Buttons, input fields, and small tags use a `0.5rem` (8px) radius. This provides a modern softening of the UI without sacrificing the professional look.
- **Large Containers:** Cards and modals use `1rem` (16px) to clearly define content boundaries.
- **Connectivity Motifs:** Icons and decorative lines should remain sharp or have very minimal rounding to maintain the "technical" feel.

## Components

- **Buttons:**
  - *Primary:* Tech Blue gradient with white text. High-contrast, bold weight.
  - *Secondary:* Deep Navy outline with transparent background.
  - *Tertiary:* Ghost style (text only) using Tech Blue for action or Slate Gray for navigation.
- **Input Fields:** Flat, white background with a 1px Slate Gray border (#E2E8F0). On focus, the border transitions to Tech Blue with a 2px glow.
- **Cards:** White surfaces with a 1px subtle border (#F1F5F9). Headers within cards should have a thin bottom divider to separate metadata from content.
- **Chips/Badges:** Small, caps-lock labels with a subtle background tint of the status color (e.g., light blue background for "Processing").
- **Lists:** High-density lists for technical data should use alternating row tints or clear dividers, with monospaced numbers (using Inter's tabular figures) for technical specifications.
- **Iconography:** Use 24px geometric line icons. Stroke weight should be consistently 1.5px or 2px. Avoid filled icons unless used for a "selected" state.
- **Progress Connectors:** Use a 2px Tech Blue dashed or solid line to visually "connect" related cards or steps in a process, reinforcing the "Unification" concept.
