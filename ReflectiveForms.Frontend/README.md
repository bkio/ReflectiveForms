# @reflectiveforms/frontend

React + TypeScript admin panel library for [ReflectiveForms](../README.md). Renders schema-driven CRUD forms with auto-save, entity locking, display conditions, nested repeaters, searchable selects, and more.

## Installation

```bash
npm install @reflectiveforms/frontend react react-dom react-router-dom
```

**Peer dependencies:** React 18+, React DOM 18+, React Router DOM 6+.

## Quick Start

```tsx
// main.tsx
import { createReflectiveFormsApp } from '@reflectiveforms/frontend';

createReflectiveFormsApp({
  apiBaseUrl: 'http://localhost:9000/rf/api',
});
```

This renders a full admin panel into `#root` with login, dashboard, entity list/edit/view pages, and sidebar navigation — all driven by schemas from the backend.

## Configuration

```tsx
import { createReflectiveFormsApp } from '@reflectiveforms/frontend';
import { BarChart3 } from 'lucide-react';
import AnalyticsPage from './pages/AnalyticsPage';

createReflectiveFormsApp({
  // Required
  apiBaseUrl: 'http://localhost:9000/rf/api',

  // Branding
  appName: 'My Admin',
  logo: '/logo.svg',             // URL string or React component
  primaryColor: '#7c3aed',       // Sets --rf-primary CSS variable

  // Routing
  basePath: '/admin',

  // Auth
  auth: {
    mode: 'sso',                 // 'local' (default) or 'sso'
    ssoLoginUrl: '/auth/sso/login',
  },

  // Custom sidebar pages
  customPages: [
    {
      path: '/analytics',
      label: 'Analytics',
      icon: BarChart3,
      component: AnalyticsPage,
      section: 'Reports',
    },
  ],
});
```

### Config Reference (`RfConfig`)

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `apiBaseUrl` | `string` | Yes | — | Backend API URL |
| `appName` | `string` | No | `"ReflectiveForms"` | Sidebar brand name |
| `logo` | `string \| ComponentType` | No | — | Logo URL or React component |
| `primaryColor` | `string` | No | `"#2563eb"` | Theme color (hex) |
| `basePath` | `string` | No | `"/rf/app"` | Router base path |
| `auth.mode` | `"local" \| "sso"` | No | `"local"` | Authentication mode |
| `auth.ssoLoginUrl` | `string` | No | — | SSO redirect URL |
| `customPages` | `CustomPage[]` | No | `[]` | Extra sidebar pages |

### Custom Pages

```tsx
interface CustomPage {
  path: string;          // Route path (e.g. '/analytics')
  label: string;         // Sidebar label
  icon?: ComponentType;  // Lucide-react icon or any component
  component: ComponentType; // Page component to render
  section?: string;      // Sidebar section group (default: 'Custom')
}
```

## Theming

The primary color is set via the `--rf-primary` CSS variable. All blue-ish UI elements (buttons, active sidebar items, links) use this variable through Tailwind's `primary` color scale.

When using `@reflectiveforms/frontend` as an npm package, include its CSS and extend your Tailwind config:

```js
// tailwind.config.js
export default {
  content: [
    './src/**/*.{js,ts,jsx,tsx}',
    './node_modules/@reflectiveforms/frontend/dist/**/*.{js,mjs}',
  ],
  theme: {
    extend: {
      colors: {
        primary: {
          DEFAULT: 'var(--rf-primary, #2563eb)',
          50: 'color-mix(in srgb, var(--rf-primary, #2563eb) 5%, white)',
          100: 'color-mix(in srgb, var(--rf-primary, #2563eb) 10%, white)',
          600: 'var(--rf-primary, #2563eb)',
          700: 'color-mix(in srgb, var(--rf-primary, #2563eb) 90%, black)',
        },
      },
    },
  },
};
```

## Advanced Usage

### Using Individual Components

Instead of `createReflectiveFormsApp`, you can compose the pieces yourself:

```tsx
import {
  RfConfigProvider,
  AdminLayout,
  RfRoutes,
  DashboardPage,
  LoginPage,
  useRfConfig,
} from '@reflectiveforms/frontend';

function App() {
  return (
    <RfConfigProvider config={{ apiBaseUrl: '...' }}>
      <BrowserRouter basename="/admin">
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route element={<AdminLayout />}>
            <Route index element={<DashboardPage />} />
            {/* RfRoutes provides entity list/edit/view */}
            {RfRoutes()}
            {/* Add your own routes */}
            <Route path="/custom" element={<MyPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </RfConfigProvider>
  );
}
```

### Exports

The library exports:

- **Bootstrap:** `createReflectiveFormsApp`
- **Provider:** `RfConfigProvider`, `useRfConfig`
- **Layout:** `AdminLayout`
- **Routes:** `RfRoutes`
- **Pages:** `LoginPage`, `SsoLoginPage`, `DashboardPage`, `EntityListPage`, `EntityEditPage`, `EntityViewPage`, `RevisionDiffPage`
- **Hooks:** `useSchema`, `useEntity`, `useEntityLock`, `useAutoSave`
- **Types:** `RfConfig`, `CustomPage`, `EntitySchema`, `FieldSchema`
- **Utilities:** `schemaToZod`, `conditionParser`, `sanitize`, `formUtils`

## Project Structure (source)

```
src/
├── api/client.ts                  # API client with configurable base URL
├── components/
│   ├── fields/                    # TextField, SelectField, RepeaterField, etc.
│   ├── form/                      # DynamicForm, SearchableSelect
│   └── layout/AdminLayout.tsx     # Config-driven sidebar layout
├── hooks/                         # useEntity, useSchema, useAutoSave, useEntityLock
├── lib/
│   ├── createApp.tsx              # createReflectiveFormsApp()
│   ├── RfConfigProvider.tsx       # React context for config
│   ├── RfRoutes.tsx               # Pre-wired entity routes
│   ├── types.ts                   # RfConfig, CustomPage interfaces
│   ├── index.ts                   # Library barrel exports
│   ├── schemaToZod.ts             # Schema → Zod conversion
│   ├── conditionParser.ts         # Display condition evaluator
│   └── sanitize.ts                # HTML sanitization
├── pages/                         # Login, SSO, Dashboard, List, Edit, View, RevisionDiff
└── types/schema.ts                # TypeScript schema types
```

## Testing

### Unit Tests (Vitest)

```bash
npm run test:run       # 278+ tests
npm run test:coverage  # With coverage report
```

### E2E Tests (Playwright)

Requires the sample backend running at `localhost:9000`.

```bash
npx playwright install
npm run test:e2e       # 30 suites (Chromium)
npm run test:e2e:ui    # Interactive mode
```

## Building the Library

```bash
npm run build:lib   # Outputs to dist/ (ES module + types)
```

The library build uses `vite.config.lib.ts` with React, React DOM, and React Router DOM externalized.

## Environment Variables (dev mode)

| Variable | Default | Description |
|----------|---------|-------------|
| `VITE_API_BASE_URL` | `http://localhost:9000/rf/api` | Backend API URL |

## License

AGPL-3.0

## Testing

### Unit Tests (Vitest)

```bash
# Watch mode
npm run test

# Single run
npm run test:run

# With coverage
npm run test:coverage
```

Unit tests cover:
- **Hooks**: `useEntity`, `useEntityLock`, `useSchema`, `useAutoSave`
- **Components**: `DynamicForm`, `TextField`, `SelectField`, `CheckboxField`, `NumberField`, `RepeaterField`, `WysiwygField`, `SearchableSelect`, `ErrorBoundary`, `AdminLayout`
- **Libraries**: `conditionParser`, `schemaToZod` (including dynamic defaults)
- **Pages**: `EntityViewPage` (read-only rendering, metadata section)

### E2E Tests (Playwright)

```bash
# Install browsers (first time only)
npx playwright install

# Run E2E tests
npm run test:e2e

# Run with UI
npm run test:e2e:ui

# Run headed (see browser)
npm run test:e2e:headed

# View test report
npm run test:e2e:report
```

E2E test suites:
- **api-endpoints.spec.ts** - Schema and CRUD API contract tests
- **entity-crud.spec.ts** - Entity CRUD operations via UI
- **entity-lock.spec.ts** - Entity locking with concurrent edit protection
- **entity-view-page.spec.ts** - Read-only entity view page
- **dynamic-default-value.spec.ts** - Dynamic default values (schema + frontend)
- **auto-save.spec.ts** - Auto-save and validation
- **conditional-fields.spec.ts** - Conditional field visibility
- **form-fields.spec.ts** - All field types
- **repeater.spec.ts** - Repeater operations
- **searchable-select.spec.ts** - Searchable select and relation fields
- **sample-objective.spec.ts** - Objective entity full CRUD
- **sample-blog-post.spec.ts** - Blog post entity full CRUD
- **sample-team-member.spec.ts** - Team member entity full CRUD
- **sample-product.spec.ts** - Product entity full CRUD
- **sample-event.spec.ts** - Event entity full CRUD
- **sample-survey.spec.ts** - Survey entity with 3-level nested repeaters
- **sample-cross-entity.spec.ts** - Cross-entity workflows
- **sample-auth-dashboard.spec.ts** - Auth and dashboard rendering
- **integration-*.spec.ts** - 9 integration suites (auto-save, data persistence, display conditions, locking, navigation, pagination, relations, schema API, validation)
- **authorization.spec.ts** - Role-based access control and IAM
- **list-sort-filter.spec.ts** - Entity list search, sorting, and filtering
- **revision-diff.spec.ts** - Revision diff comparison page

### Running All Tests

```bash
npm run test:all
```

**Note**: E2E tests require the backend to be running at `http://localhost:9000`.

