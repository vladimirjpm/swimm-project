# Copilot Instructions for AI Coding Agents

## Project Overview
- This is a React + TypeScript project, bootstrapped with Create React App and using Redux Toolkit for state management.
- The main app code is in `src/`, with feature modules under `src/projects/` and shared components in `src/projects/components/`.
- Data files (JSON, JS) are in `public/data/`. Images and static assets are in `public/images/`.
- The project uses Vite for build tooling (see `vite.config.js`).

## Key Patterns & Structure
- **Pages**: Route-level pages are in `src/pages/` (e.g., `about-page.tsx`, `home-page.tsx`).
- **Feature Projects**: Major features are grouped in `src/projects/` (e.g., `about-project/`, `home-project/`, `results-main-project/`).
- **Reusable Components**: Shared UI and logic are in `src/projects/components/` (e.g., `filter-section/`, `popup/`).
- **Types & Interfaces**: All shared types are in `src/types/` and `src/utils/interfaces/`.
- **Helpers**: Utility functions are in `src/utils/helpers/`.
- **Store**: Redux store setup is in `src/store/store.ts`.

## Developer Workflows
- **Start Dev Server**: `npm start` (runs on http://localhost:3000)
- **Build for Production**: `npm run build` (output in `build/`)
- **Run Tests**: `npm test`
- **Vite Config**: Custom build settings in `vite.config.js` (overrides CRA defaults)

## Project Conventions
- **TypeScript**: Use strict typing; prefer interfaces from `src/types/` or `src/utils/interfaces/`.
- **Component Structure**: Prefer function components. Co-locate CSS with components (e.g., `component-name.css`).
- **UI Component Naming**: All reusable components in `src/projects/components/mix/` **must** use the `UI_` prefix (e.g., `UI_ClubIcon`, `UI_AgeLabel`, `UI_SwimmerNameCell`). Export name and const name must both start with `UI_`. This convention applies to every component in the `mix/` folder without exception.
- **Data Loading**: Static data is loaded from `public/data/` JSON files. Use helpers in `src/utils/helpers/` for data transformation.
- **Naming**: Use kebab-case for folders, camelCase for files and variables, PascalCase for components (with `UI_` prefix for mix/ components).
- **No Eject**: Avoid `npm run eject` unless absolutely necessary.

## Integration Points
- **External Data**: All competition and training data is in `public/data/`.
- **Images**: Use `public/images/` for all static images, organized by type.
- **Routing**: Page-level routing is handled in `src/index.tsx`.

## Examples
- To add a new results table: create a component in `src/projects/results-table/`, add types in `src/types/results.ts`, and update data helpers as needed.
- For a new data source: add JSON to `public/data/`, update helpers, and connect via a component in `src/projects/components/`.

## References
- See [README.md](../../README.md) for basic scripts and Create React App info.
- See `vite.config.js` for build customization.
- See `src/store/store.ts` for Redux setup.

---

If any conventions or workflows are unclear, please ask for clarification or examples from the codebase.
