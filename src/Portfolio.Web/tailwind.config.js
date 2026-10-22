/** @type {import('tailwindcss').Config} */
module.exports = {
  // Admin and setup pages use wwwroot/css/admin.css, so they stay out of the public stylesheet build.
  content: ['./Pages/**/*.cshtml', '!./Pages/Admin/**', '!./Pages/Setup.cshtml'],
  theme: {
    extend: {
      colors: {
        bg: 'rgb(var(--c-bg) / <alpha-value>)',
        surface: 'rgb(var(--c-surface) / <alpha-value>)',
        'surface-2': 'rgb(var(--c-surface-2) / <alpha-value>)',
        border: 'rgb(var(--c-border) / <alpha-value>)',
        text: 'rgb(var(--c-text) / <alpha-value>)',
        muted: 'rgb(var(--c-muted) / <alpha-value>)',
        accent: {
          DEFAULT: 'rgb(var(--c-accent) / <alpha-value>)',
          strong: 'rgb(var(--c-accent-strong) / <alpha-value>)',
        },
      },
      // Fonts and radii read a CSS variable that a template theme may set (see Styles/themes); the fallback is the base look.
      fontFamily: {
        sans: ['var(--font-sans, Inter)', 'ui-sans-serif', 'system-ui', '-apple-system', 'Segoe UI', 'Roboto', 'Helvetica', 'Arial', 'sans-serif'],
        display: ['var(--font-display, "Space Grotesk")', 'Inter', 'ui-sans-serif', 'system-ui', 'sans-serif'],
        mono: ['var(--font-mono, "JetBrains Mono")', 'ui-monospace', 'SFMono-Regular', 'Menlo', 'Consolas', 'monospace'],
      },
      borderRadius: {
        lg: 'var(--radius-lg, 0.5rem)',
        md: 'var(--radius-md, 0.375rem)',
      },
      maxWidth: {
        content: '72rem',
      },
      letterSpacing: {
        eyebrow: '0.2em',
      },
    },
  },
  plugins: [],
};
