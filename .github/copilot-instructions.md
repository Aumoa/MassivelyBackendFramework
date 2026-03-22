# UI/UX Design System & Global Standards

This project adheres to a clean, modern, and user-centric design language inspired by Google Workspace. All new UI components, pages, and modifications must strictly follow these global standards to ensure consistency, clarity, and ease of use.

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

- Always write code comments in English, not Korean.

## 5. Blazor Implementation Standards
- **Event Handling:** Always use the Razor directive syntax @on{EVENT} (e.g., @onclick="HandleClick") instead of the HTML attribute syntax onclick="@HandleClick".
- **Component Naming:** Use PascalCase for all component files and C# method names to maintain .NET naming conventions.
- **Event Handler Prefix:** Prefix private event handler methods with On (e.g., OnLogoutClick, OnSaveSubmit) for better code traceability.
- **Directive Precedence:** Place Blazor-specific directives (e.g., @ref, @bind, @onclick) at the beginning of the element tag, before standard HTML attributes, to improve scannability.