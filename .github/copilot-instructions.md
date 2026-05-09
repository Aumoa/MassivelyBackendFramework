# UI/UX Design System & Global Standards

This project adheres to a clean, minimal design language inspired by **Ghost** (the open-source publishing platform). The guiding principle is "show only what's needed" — quiet UI that lets content and workflow take center stage. All new UI components, pages, and modifications must follow these standards to ensure consistency across the entire codebase.

## General Guidelines
- Write code comments in English (not Korean).

## NPay Design System (Ghost-inspired)
The NPay frontend uses a single authoritative design token file: `NPay/wwwroot/css/app.css`.
All visual decisions (colors, spacing, typography, radius, shadow) are expressed as CSS custom properties in the `:root` block.
**Never use raw hex values or Bootstrap utility classes in component markup.** Always use semantic tokens or design-system classes.

### Key classes
| Class | Purpose |
|---|---|
| `.npay-shell` / `.npay-topbar` / `.npay-main` | Page shell and top-nav layout |
| `.page-header`, `.page-title`, `.page-subtitle` | Consistent page heading section |
| `.npay-card`, `.npay-card-hover` | Content cards |
| `.quick-card` | Home-screen action cards |
| `.settlement-list`, `.settlement-row` | Row-based list UI |
| `.item-list`, `.item-row` | Generic item lists (expenses, participants) |
| `.inline-form`, `.inline-form-field` | Inline add forms |
| `.npay-tabs`, `.npay-tab` | Tab navigation |
| `.transfer-card` | Settlement result rows |
| `.badge`, `.badge-open`, `.badge-closed` | Status pills |
| `.empty-state` | Centered empty/zero-state |
| `.skeleton` | Loading skeleton animation |
| `.btn-primary`, `.btn-ghost`, `.btn-danger` | The three button variants |
| `.form-input`, `.form-select`, `.form-textarea`, `.form-label` | Form controls |

### Design rules
- **No sidebar navigation.** Use the top navigation bar (`.npay-topbar`) for all global nav.
- **Max content width is 900 px**, centered in `.npay-main`. Do not use full-width layouts.
- **Typography:** Inter font. Use `--text-*` tokens for font sizes.
- **Color accent** is `--color-accent` (`#6d28d9`). Use sparingly — only for primary CTAs, active states, and key data.
- **Buttons:** Use `.btn-primary` for the single primary action per screen, `.btn-ghost` for secondary, `.btn-danger` only for destructive inline actions.
- **Loading states:** Always show `.skeleton` placeholders instead of a spinner or text.
- **Empty states:** Always use `.empty-state` with a Material Symbol icon and a short descriptive sentence.
- **Hover reveals:** Action buttons inside list rows (`.settlement-row-actions`, `.item-row-action`) are hidden by default and revealed on row hover. On mobile they are always visible.

## 1. Core Design Philosophy
- **Clarity & Hierarchy:** Information must be prioritized. Critical actions should be prominent, while secondary information remains accessible but unobtrusive.
- **Visual Consistency:** Maintain generous whitespace and consistent typographic hierarchy to minimize cognitive load.
- **Context Preservation:** Prioritize inline interactions and side-panel navigations over disruptive modal dialogs to keep the user within their current context.

## 2. Global Component Guidelines
- **Forms & Inputs:**
  - Use top-aligned labels for all input fields.
  - Group related fields into logical sections with clear headers and helper texts.
  - Ensure consistent spacing between inputs and sections.
- **Action Buttons:**
  - **Primary Actions (e.g., Save, Create):** Use filled, high-contrast styles. Align them consistently (usually bottom-right or top-header).
  - **Secondary Actions (e.g., Cancel, Back):** Use outlined or ghost styles.
- **Feedback & State:**
  - Use SnackBar (Toast) notifications for status updates instead of full-page reloads.
  - Implement Skeleton UI for all loading states to improve perceived performance.
  - Error messages must be immediate, placed below the relevant input field, and clear.

## 3. Implementation Policy
- **Responsive Design:** Every component must be fully responsive, ensuring usability across both desktop and mobile viewports.
- **Accessibility:** Ensure high color contrast, descriptive labels, and logical focus management.
- **Reusability:** Always prefer existing shared components (Buttons, Inputs, Cards, etc.) over creating ad-hoc, one-off styles.
- **Code Style:** When modifying or creating UI, prioritize maintainability and clear component separation.

## 4. Usage Instructions for Copilot
When asked to create or modify UI components, act as a Senior UX Engineer. Reference this global design system and ensure the generated code aligns with the "clean and modern" aesthetic. If a request conflicts with these guidelines, provide a suggestion that improves consistency. For example, if asked to create a form with left-aligned labels, suggest changing to top-aligned labels and explain the rationale based on the design principles outlined above. Always aim to enhance the user experience while adhering to the established standards.

## 5. Blazor Implementation Standards
- **Event Handling:** Always use the Razor directive syntax @on{EVENT} (e.g., @onclick="HandleClick") instead of the HTML attribute syntax onclick="@HandleClick".
- **Component Naming:** Use PascalCase for all component files and C# method names to maintain .NET naming conventions.
- **Event Handler Prefix:** Prefix private event handler methods with On (e.g., OnLogoutClick, OnSaveSubmit) for better code traceability.
- **Directive Precedence:** Place Blazor-specific directives (e.g., @ref, @bind, @onclick) at the beginning of the element tag, before standard HTML attributes, to improve scannability.

## 6. Database & SQL Standards
- Use backticks around all MySQL identifiers (table names, column names, aliases) without exception.
- Keep queries consistent and readable; prefer explicit column lists and consistent casing.
- Example:SELECT `id`, `amount`, `created_at`
FROM `npay_settlement`
WHERE `status` = 'open'