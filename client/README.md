# Swimm — Client

React 18 · TypeScript · Vite · Redux Toolkit · Tailwind CSS.

The public-facing results browser. See the [root README](../README.md) for the full stack and
quick-start, and [`.github/copilot-instructions.md`](../.github/copilot-instructions.md) for
architecture and conventions.

## Scripts

```bash
npm install        # install dependencies
npm run dev        # dev server → http://localhost:5173
npm run build      # production build → dist/
npm run preview    # preview the production build
```

`prebuild` regenerates the club-icons manifest automatically before `build`.

## Structure

- `src/pages/` — route-level pages
- `src/projects/` — feature modules; `src/projects/components/` — shared UI
  (components under `components/mix/` use the `UI_` prefix)
- `src/types/`, `src/utils/interfaces/` — shared types
- `src/utils/helpers/` — utilities
- `src/store/store.ts` — Redux store
- `public/data/` — static JSON · `public/images/` — static images

## Conventions

- Strict TypeScript; prefer interfaces from `src/types/`
- Function components only; co-locate CSS with the component
- Folders kebab-case · files camelCase · components PascalCase

The dev server talks to the API at `http://localhost:5078` (CORS is configured for
`localhost:5173`).
