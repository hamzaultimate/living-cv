# Admin redesign: shell, template picker and setup wizard

Date: 2026-11-15
Status: design approved in brainstorming, awaiting written-spec review
Branch: `feature/admin-redesign` from `develop`
Delivery: three pull requests in order (A shell, B template picker, C wizard)

## 1. Goal

Make the admin side easier to use and more modern without changing what it can do. Four requests drove this, plus gaps found in review.

Requested:

1. A collapsible left menu in place of the top bar.
2. A template selection screen with no long scroll: template names in a box on the right, the selected template and its details in the centre.
3. A redesigned setup wizard.
4. A step indicator whose steps can be clicked to jump to that step.

Gaps found in review:

- Two different template pickers do the same job: the wizard's (`/admin/welcome`, static thumbnails, radio cards) and the standalone one (`/admin/template`, live preview, card list).
- Export and import, and the Hero, Metrics and About editors, are reachable only from dashboard tiles, not from the menu.
- The dashboard is a tile grid and shows no status.
- Pages have no shared header or breadcrumb.
- The wizard's last step holds six unrelated things on one long page.

## 2. Decisions made

| Topic | Decision |
|---|---|
| Split | One spec, three PRs: A shell and dashboard, B template picker, C wizard |
| Menu collapse | Icon rail, with expandable sub-menus for Profile and Content. Expanded by default |
| Template picker | Inside one card. Names box on the right, live preview in the centre, details strip under the preview |
| Step jumping | Free. Every step is always clickable. The current step is saved as a draft, even if incomplete. Completeness is enforced once, at Publish |
| Wizard frame | Inside the admin shell, with the menu collapsed to its rail on wizard pages |
| Build approach | Server-rendered. One small script for the menu toggle. Picker and step jumping use links and form buttons only |

## 3. Constraints

- The admin CSP (`AdminAuth.cs`) is `default-src 'none'; style-src 'self'; img-src 'self'`, with `script-src 'self'` only on paths `AllowsScript` permits. No inline script and no inline style. Icons are inline SVG in markup.
- `admin.css` is one hand-written file with light and dark colour tokens. No CSS build step is added.
- Template wording drives labels. A menu or dashboard label for a section comes from the active template (`EditorView`, `TemplateDefinition.Section(...).Label`), never from a hardcoded profession-specific string.
- Every form must work without script. Script only adds convenience.
- Out of scope: the public site, the fields of the content editors, authentication, and the template manifests.

## 4. PR A: admin shell and dashboard

### 4.1 Menu

Structure (labels shown are the defaults; section labels come from the template):

| Group | Items |
|---|---|
| (none) | Dashboard |
| Your site | Profile: Basics, Home intro, Metrics, About, Photo. Content: Overview, then one item per content section (Experience, Skills, Projects, Certifications, Talks, Testimonials, Blog) |
| Appearance | Template, Sections |
| Tools | Setup wizard, Export and import |
| Footer | View site (new tab), Sign out |

Behaviour:

- Profile and Content are sub-menus built from native `<details>`. The one containing the current page is rendered open.
- A content section that is switched off under the active template stays in the menu, dimmed, with a text tag "hidden".
- Each content item shows its entry count. If the counts cannot be read, the menu renders without them; a failed count never breaks a page.
- The current page is marked with `aria-current="page"`.

Collapse:

- Expanded is the default. A toggle button in the menu collapses it to an icon rail and expands it again. The button carries `aria-expanded`.
- In rail mode sub-menus are not shown. The Profile icon links to `/admin/profile` and the Content icon to `/admin/content`. Every icon keeps an accessible name and shows a tooltip on hover and focus.
- State is stored in a cookie, `adm-menu=collapsed`, scoped to `/admin`, `SameSite=Lax`. It is written by script and only ever compared against the literal `collapsed` on the server. The layout reads it so the first paint is already in the right state.
- Wizard pages (`/admin/welcome/...`) render the rail regardless of the cookie. Toggling on a wizard page affects that page only and does not write the cookie.
- Below 40rem the menu is an overlay opened by a menu button in a slim top bar, and closed by the same button, the Escape key, or choosing a link.
- Without script the menu stays expanded on wide screens. On narrow screens it renders in the page flow above the content, so every link is still reachable.

### 4.2 Units

- `AdminMenu` (service): builds the menu model from the active template and the request path. Output: groups, items (label, URL, icon key, count, hidden flag, children), and which item is current. No HTML. Unit tested directly.
- `AdminMenuViewComponent`: renders the model. Holds no logic beyond markup.
- `admin-shell.js`: toggle, cookie write, overlay open and close. No other behaviour.
- `_AdminLayout.cshtml`: hosts the menu, the page header and the flash messages. The existing skip link and flash focus behaviour are kept.

### 4.3 Content Security Policy

`AllowsScript` returns true for every admin path except the login page. The layout emits the `admin-shell.js` tag only when the owner is signed in. `admin-unsaved.js` continues to be loaded only by the pages that load it today.

### 4.4 Page header

Every admin page renders the same header: breadcrumb, `h1`, and an optional action slot. The breadcrumb is derived from the menu model (Admin / group item / page). Pages set the title as they do today through `ViewData["Title"]`; pages below a menu item (for example Content / Projects / Edit) pass their own last crumb.

### 4.5 Dashboard

The tile grid is replaced by three blocks:

1. Status row: setup pending or finished, the active template's name, a link to the public site.
2. "Finish your site" checklist: setup not finished, no photo, and each enabled content section with no entries. Each line links to the page that fixes it. The block is omitted when nothing is outstanding.
3. Content counts per section in the template's wording, with blog drafts called out.

The existing rule stays: the dashboard must open even if the database is struggling. Any block whose data cannot be read is left out and the failure is logged.

The "Continue setup" callout stays while setup is pending.

### 4.6 Visual refresh

Applied in this PR so later screens inherit it.

- Existing colour tokens are kept. New tokens: spacing scale, radius, shadow, menu surface, and a warning pair (background and text) for "needs attention", each with a dark-mode value.
- Restyled to one consistent set: card, button, field, tabs, pill, flash.
- New: page header, menu, split layout (used by B and C).
- The main area uses the full width beside the menu. Forms keep a readable maximum width.
- `prefers-color-scheme` and `prefers-reduced-motion` handling is kept.

## 5. PR B: template picker

### 5.1 Layout

One shared partial, `_TemplatePicker`, rendered inside a card:

- Right: a box of template names grouped by profession. Each name is a link, `?show=<key>`. The box scrolls internally if the list outgrows it. Tags: "In use" on the live site's template; in the wizard, "Selected" on the draft's template.
- Centre top: name, profession, layout type.
- Centre middle: live preview in an iframe (the public site with `RequestTemplate.PreviewParameter`), page tabs, and a desktop or mobile width switch. Tabs and the switch are links, as on `/admin/template` today.
- Centre bottom: details strip. Pages included, what a piece of work is called, whether sample content is offered, and the template's "before you publish" guidance.
- Below 40rem the names box moves above the preview inside a `<details>` whose summary shows the selected name.

The static thumbnail endpoint is kept and used only as the preview's fallback content.

### 5.2 Two hosts

| | Wizard step 1 (`/admin/welcome`) | Appearance, Template (`/admin/template`) |
|---|---|---|
| Action | "Use [name] and continue": saves to the draft, goes to Basics | "Switch to [name]": changes the live site |
| On the already chosen template | "Keep [name] and continue" | No button; "In use" |
| Extra notes | None | Hidden-but-kept sections, sections that reappear, sections kept on, and the refusal reason when a switch would empty the menu (button disabled) |

### 5.3 Units

- `TemplatePickerModel`: selected template, grouped names with tags, preview pages, preview path, width, preview URL, details.
- `TemplatePickerBuilder`: fills the model from the catalog, the selected key and the host's base URL. Both page models call it, so grouping and details exist once.
- `show`, `tab` and `size` remain untrusted query values that only ever select a known catalog entry, a known preview page, or one of two widths. An unknown `show` falls back to the current template.

## 6. PR C: setup wizard

### 6.1 Frame

Each step is one card:

- Top: step indicator, and "Draft saved at HH:mm" from the draft's `UpdatedAt` when a draft exists.
- Middle: the step's content.
- Bottom: "Start over" (when a draft exists) and "Skip setup" or "Leave without publishing" on the left; one "Continue" button on the right. The Back button is removed.

The existing live region and heading focus behaviour in `_WizardHeader` are kept: focus goes to the step heading on load, or to the error summary when the page has errors.

### 6.2 Step status

Status is computed from the draft's contents, not from `OnboardingDraft.Step`.

| Step | Done | To do | Needs attention | Optional |
|---|---|---|---|---|
| Template | Draft names a catalog template | No draft, or no template in it | | |
| Basics | `BasicsForm.IsComplete` | Every basics field empty | Some fields filled but not complete | |
| Photo | Draft photo present | | | No draft photo |
| Review and launch | | Until published | Site address filled but not a valid address | |

The current step is marked separately with `aria-current="step"`. Every state is conveyed in text as well as colour.

`OnboardingDraft.Step` stays in the stored JSON so existing drafts still parse, and is still written, but nothing gates on it.

### 6.3 Jumping

- Each step in the indicator is a submit button, `name="goto"` with the step number, associated with the step's form through the `form` attribute. "Continue" is the same button with the next step's number. `goto` is untrusted and is clamped to the known steps.
- Posting saves the current step, then redirects to the requested step.
- Two kinds of rule:
  - Hard rules, enforced on every save: length limits and no markup characters. A value that breaks one is not saved; the owner stays on the step and sees the error.
  - Completeness rules, enforced only at Publish: required fields, a valid email address, a valid site address.
- Template step: jumping away keeps the draft's template. Only "Use [name] and continue" changes it. If there is no draft yet, the jump creates one on the site's current template (or the default on first run).
- Photo step: a file chosen but not yet uploaded is uploaded as part of the jump. An invalid file keeps the owner on the step with the error.
- Review step: jumping away saves the site address, accent and starting content under the same hard rules.

### 6.4 Steps

1. Template: the picker from section 5.
2. Basics: the same five fields. The template's suggested title and summary are shown beside their fields, as today.
3. Photo: the current photo or monogram on the left, upload on the right, one card.
4. Review and launch, as a split layout:
   - Left: "Ready to publish?" checklist (template, basics, photo, site address) with a link to the step that fixes each line; site address; accent colour; starting content.
   - Right: live draft preview with page tabs and the width switch, plus a "Search result" tab that shows the existing search-result card in place of the iframe.
   - "Save and update preview" stays, since the preview reflects saved choices.

### 6.5 Publishing

- The Publish button is disabled, with the reason beside it, until the checklist is complete.
- `OnboardingService.PublishAsync` keeps re-validating everything on the server, as it does now. The disabled button is a convenience, not the safeguard. A refused publish returns to Review with the checklist showing what is missing.

### 6.6 Service changes

- `OnboardingService.SaveBasicsAsync` accepts partial input under the hard rules. `SaveChoicesAsync` likewise for the site address.
- New `StepStatus` calculation (section 6.2), exposed to the pages and the dashboard.
- `WelcomePageModel.RequireStep` and `Reachable` are removed.
- `DraftPreviewAsync` applies each draft field that has a value and leaves the saved profile's value for any field still empty, instead of switching on `Step >= 2`.
- The dashboard's "Continue setup" link opens the first step that is not Done or Optional.

## 7. Acceptance criteria

Shell (PR A)

1. Every signed-in admin page shows the left menu with the groups in 4.1, and every admin page that exists today is reachable from it.
2. Section labels in the menu match the active template's wording.
3. The toggle collapses the menu to a rail and expands it; the choice persists across pages and reloads without a visible flicker.
4. With script disabled, every menu link is reachable and every form still submits.
5. Below 40rem the menu opens as an overlay and closes with the button, Escape, or a link.
6. A content section that is off under the template appears dimmed with the text "hidden".
7. Every page shows a breadcrumb and one `h1`.
8. The dashboard shows status, outstanding items and counts, and still opens when its data cannot be read.
9. The CSP allows `script-src 'self'` on signed-in admin pages and not on the login page.

Template picker (PR B)

10. On both hosts, choosing a name shows that template's live preview and details in the centre without scrolling the page at 1280 by 800.
11. Every catalog template can be selected and previewed on both hosts.
12. An unknown `show` value falls back to the current template.
13. `/admin/template` still shows the hidden, reappearing and kept-on notes, and disables the switch with a reason when it would empty the menu.
14. The wizard host saves the choice to the draft only through its action button.

Wizard (PR C)

15. Every step in the indicator is clickable from every step, including with no draft.
16. Jumping saves what was typed on the current step, including incomplete input.
17. A value that breaks a hard rule blocks the jump and shows the error.
18. Each step shows the status in 6.2, in text.
19. Publish is disabled with a reason until the checklist is complete, and the server refuses an incomplete publish even if the button is bypassed.
20. A full first-run pass (template, basics, photo, publish) and a re-run on a finished site both still work.
21. "Start over", "Skip setup" and "Leave without publishing" behave as they do today.

## 8. Testing

Each criterion maps to an automated test, or to a manual check where it is visual.

- Unit: `AdminMenu` (grouping, current item, template wording, hidden sections, missing counts); `TemplatePickerBuilder` (grouping, tags, fallback, details); `StepStatus` for each draft shape; hard-rule versus completeness validation.
- Integration (existing web test host): CSP header per path (9); every admin page reachable from the menu (1); picker selection on both hosts (11, 12, 13, 14); jump saves partial input (15, 16); hard-rule refusal (17); publish refusal (19); first run and re-run flows (20, 21). `OnboardingFlowTests` is updated for the new flow.
- Manual, in a browser, at 1280 by 800 and 390 by 844, in light and dark: 3, 4, 5, 7, 10, 18, and keyboard-only passes through the menu and the wizard.

## 9. How a person sees each change

- Shell: sign in at `/admin/login`. The left menu is on every page; use the toggle at its foot to collapse it.
- Dashboard: `/admin` after signing in.
- Template picker: menu, Appearance, Template; or menu, Tools, Setup wizard (step 1).
- Wizard: menu, Tools, Setup wizard; or "Continue setup" on the dashboard while setup is pending. Click any step in the indicator to jump.

## 10. Risks

- Widening `script-src` to all signed-in admin pages. Mitigation: `'self'` only, no inline script, one small audited file, login stays script-free, covered by a header test.
- Partial drafts hold values that are not yet valid (for example a half-typed email). Mitigation: they live only in the private draft, hard rules still apply on every save, and publish re-validates everything.
- Count queries on every admin page for the menu badges. Mitigation: failures are tolerated; if the cost is measurable the counts move behind a short per-request cache during implementation.
- The redesign touches the layout every admin page uses. Mitigation: PR A changes the shell only and leaves page bodies as they are, so a regression is confined to chrome.
