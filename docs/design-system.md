# Design system — example.com

Direction (per build spec §6): **engineered, high-signal, quietly confident.** Dark UI,
disciplined grid, generous whitespace, a monospace utility face for labels/metrics, and one
restrained accent (deliberately **not** terracotta). Signature motif: a faint **telemetry
waveform/grid** that nods to the monitoring + IoT background.

> These tokens are the single source of truth (Tailwind theme + CSS variables). Review and
> tweak the hex/fonts here; everything else references them.

## Palette (dark, near-black base — not pure black)

| Token | Hex | Use |
|---|---|---|
| `bg` | `#0B0F14` | Page background (deep blue-black) |
| `surface` | `#111821` | Cards / raised panels |
| `surface-2` | `#18212B` | Nested / hover surfaces |
| `border` | `#243040` | Hairlines, dividers, card borders |
| `text` | `#E6EDF3` | Primary text (near-white) |
| `muted` | `#8896A5` | Secondary text, metadata |
| `accent` | `#60A5FA` | Links, emphasis, signal motif (blue) |
| `accent-strong` | `#93C5FD` | Hover/active accent |

Accent is used **sparingly** — for links, focus rings, the eyebrow labels, and the telemetry
motif. Everything structural stays in the neutrals.

## Type

- **Display** (headings): `Space Grotesk` → falls back to a clean system sans. Tight tracking.
- **Body**: system sans stack (`Inter`/`system-ui`) — fast, no external fonts yet.
- **Mono utility** (eyebrows, metrics, labels, metadata): `JetBrains Mono` → `ui-monospace`.

> Fonts currently use high-quality **system fallbacks** (zero network cost, great Lighthouse).
> Upgrading to self-hosted `Space Grotesk` + `Inter` + `JetBrains Mono` (woff2) is a one-step
> polish task — flagged for Phase 5. The look already reads correctly with fallbacks.

Type scale: Tailwind default scale + display sizes (`text-5xl`/`6xl`) for the hero.

## Motion

Minimal and purposeful: one hero waveform animation, subtle hover states. All motion is wrapped
in `@media (prefers-reduced-motion: no-preference)` so reduced-motion users get a static scene.

## Accessibility floor

Visible `:focus-visible` ring in accent; semantic headings; AA contrast (near-white on
near-black); responsive to mobile.
