# ReflectiveForms Frontend

A modern React-based frontend for ReflectiveForms that renders dynamic forms from JSON schemas.

## Features

- **Schema-driven forms**: Dynamically renders forms based on JSON schemas from the backend
- **Type-safe**: Full TypeScript support with types matching the backend schema
- **Modern stack**: React 18, TanStack Query, React Hook Form, Zod validation
- **Performance**:
  - Schema caching (1 hour stale time)
  - Code splitting with vendor chunks
  - Minified production builds
  - No `eval()` - safe condition parsing
- **Entity locking**: Pessimistic locking for concurrent editing
- **Auto-save**: Debounced auto-save with visual feedback
- **Dynamic default values**: Fields pre-filled with runtime-computed defaults from the backend
- **Read-only entity view**: Public view page for entities at `/entities-view/:entityName`
- **Searchable select**: Filterable dropdowns for Relation and Select fields
- **Paginated lists**: Server-side pagination with page tokens
- **Responsive**: Mobile-friendly admin layout

## Getting Started

```bash
# Install dependencies
npm install

# Start development server
npm run dev

# Build for production
npm run build

# Preview production build
npm run preview
```

## Project Structure

```
src/
├── api/
│   └── client.ts              # Fetch wrapper with credentials and error handling
├── components/
│   ├── fields/
│   │   ├── FormField.tsx      # Field wrapper with condition logic
│   │   ├── TextField.tsx      # Text and TextArea fields
│   │   ├── SelectField.tsx    # Select, Checkbox, Number, DatePicker, Range
│   │   ├── RelationField.tsx  # Foreign key relations (uses SearchableSelect)
│   │   ├── GroupField.tsx     # Field groups with grid layout
│   │   ├── RepeaterField.tsx  # Repeatable field arrays
│   │   ├── MediaField.tsx     # Base64 image upload with drag-drop
│   │   ├── WysiwygField.tsx   # Rich text editor
│   │   └── types.ts           # Field component props
│   ├── form/
│   │   ├── DynamicForm.tsx    # Main form renderer with auto-save and lock
│   │   ├── SearchableSelect.tsx       # Searchable dropdown for relations
│   │   └── SearchableChoicesSelect.tsx # Searchable dropdown for selects
│   └── layout/
│       └── AdminLayout.tsx    # Admin sidebar and navigation
├── hooks/
│   ├── useEntity.ts           # React Query hooks for CRUD + pagination
│   ├── useSchema.ts           # Schema fetching hooks
│   ├── useEntityLock.ts       # Pessimistic entity locking
│   └── useAutoSave.ts         # Debounced auto-save logic
├── lib/
│   ├── schemaToZod.ts         # Converts JSON schema to Zod validators + defaults
│   ├── conditionParser.ts     # Parses display conditions without eval()
│   ├── formUtils.ts           # Form utility helpers
│   └── sanitize.ts            # HTML sanitization with DOMPurify
├── pages/
│   ├── LoginPage.tsx          # Login page
│   ├── DashboardPage.tsx      # Admin dashboard
│   ├── EntityListPage.tsx     # Entity listing with pagination and delete
│   ├── EntityEditPage.tsx     # Create/edit/clone entity
│   └── EntityViewPage.tsx     # Read-only entity view
├── types/
│   └── schema.ts              # TypeScript types matching backend
├── index.css                  # Tailwind + custom styles
└── main.tsx                   # App entry point with routing
```

## Field Components

| Component | Field Types | Features |
|-----------|------------|----------|
| `TextField` | Text, Email, Url | Placeholder, max length |
| `TextAreaField` | TextArea | Multiline text |
| `WysiwygField` | WysiwygEditor | Rich text with toolbar |
| `SelectField` | Select | Single/multiple choice, searchable for large sets |
| `CheckboxField` | Checkbox | Boolean toggle |
| `NumberField` | Number, Range | Min/max/step |
| `DatePickerField` | DatePicker | Date input with dynamic defaults |
| `RelationField` | Relation | Foreign key with searchable dropdown |
| `GroupField` | Group | Nested fields with grid |
| `RepeaterField` | Repeater | Add/remove/reorder items |
| `MediaField` | MediaSourceBase64 | Drag-drop image upload |

## Environment Variables

Create a `.env` file:

```env
VITE_API_BASE_URL=http://localhost:9000/rf/api
```

## API Integration

The frontend expects these backend endpoints:

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/rf/api/schema?type={name}` | GET | Fetch entity schema |
| `/rf/api/schema` | GET | Fetch all schemas |
| `/rf/api/crud?operation=READ&type={name}` | POST | Read entity |
| `/rf/api/crud?operation=CREATE&type={name}` | POST | Create entity |
| `/rf/api/crud?operation=UPDATE&type={name}` | POST | Update entity |
| `/rf/api/crud?operation=DELETE&type={name}` | POST | Delete entity |
| `/rf/api/crud?operation=PEEK_ALL&type={name}` | POST | List all entities |
| `/rf/api/crud?operation=PEEK_ALL_PAGINATED&type={name}&page_size={n}` | POST | Paginated entity list |
| `/rf/api/sanity_check?type={name}` | POST | Validate entity data |
| `/rf/api/entity_lock_control?type={name}&id={id}&operation=try_lock` | POST | Acquire edit lock |
| `/rf/api/entity_lock_control?type={name}&id={id}&operation=try_unlock` | POST | Release edit lock |
| `/rf/api/entity_lock_control?type={name}&id={id}&operation=heartbeat` | POST | Refresh lock |

## Deployment

### Option 1: Serve from ASP.NET

Copy the `dist/` folder contents to your ASP.NET wwwroot:

```bash
npm run build
cp -r dist/* ../ReflectiveForms.Sample1/wwwroot/rf-app/
```

Add to ASP.NET `Program.cs`:
```csharp
app.UseStaticFiles();
app.MapFallbackToFile("/rf/app/{**path}", "rf-app/index.html");
```

### Option 2: Separate Hosting

Deploy to any static hosting (Vercel, Cloudflare Pages, etc.) and configure CORS:

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactFrontend", policy =>
    {
        policy.WithOrigins("https://your-frontend-domain.com")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});
```

## Key Differences from Server-Side Rendering

| Aspect | Old (Server-Side) | New (React SPA) |
|--------|-------------------|-----------------|
| Code location | C# string literals | TypeScript files |
| Rendering | On every request | Static + JSON data |
| Caching | None | Schema cached 1hr |
| Bundle size | Unbundled JS | ~100KB gzipped |
| Validation | Inline eval() | Zod schemas |
| State management | ObservableSlim | React Hook Form |
| IDE support | None for JS | Full TypeScript |
| Entity locking | Manual refresh | Auto-refresh |
| Auto-save | Manual implementation | Built-in hook |

## Development

### Adding a New Field Type

1. Create the component in `src/components/fields/`:
```tsx
import { FieldComponentProps } from './types';

export function MyField({ schema, path }: FieldComponentProps) {
  // Implementation
}
```

2. Register in `src/components/fields/FormField.tsx`:
```tsx
const fieldRegistry = {
  // ...existing fields
  MyFieldType: MyField,
};
```

3. Add Zod validation in `src/lib/schemaToZod.ts`:
```tsx
case 'MyFieldType':
  schema = z.string(); // or appropriate type
  break;
```

### Customizing Styles

The project uses Tailwind CSS. Edit `tailwind.config.js` for theme customization or `src/index.css` for custom styles.

## License

AGPL-3.0 - See LICENSE file for details.

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
- **Pages**: `EntityViewPage` (read-only rendering)

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

### Running All Tests

```bash
npm run test:all
```

**Note**: E2E tests require the backend to be running at `http://localhost:9000`.

