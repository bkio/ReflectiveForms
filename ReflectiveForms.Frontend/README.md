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
│   │   ├── SelectField.tsx    # Select, Checkbox, Number, DatePicker
│   │   ├── RelationField.tsx  # Foreign key relations
│   │   ├── GroupField.tsx     # Field groups with grid layout
│   │   ├── RepeaterField.tsx  # Repeatable field arrays
│   │   ├── MediaField.tsx     # Base64 image upload with drag-drop
│   │   ├── WysiwygField.tsx   # Rich text editor
│   │   ├── types.ts           # Field component props
│   │   └── index.ts           # Exports
│   ├── form/
│   │   └── DynamicForm.tsx    # Main form renderer with auto-save
│   └── layout/
│       └── AdminLayout.tsx    # Admin sidebar and navigation
├── hooks/
│   ├── useEntity.ts           # React Query hooks for CRUD operations
│   ├── useSchema.ts           # Schema fetching hooks
│   ├── useEntityLock.ts       # Pessimistic entity locking
│   └── useAutoSave.ts         # Debounced auto-save logic
├── lib/
│   ├── schemaToZod.ts         # Converts JSON schema to Zod validators
│   └── conditionParser.ts     # Parses display conditions without eval()
├── pages/
│   ├── DashboardPage.tsx      # Admin dashboard
│   ├── EntityListPage.tsx     # Entity listing with delete/clone
│   └── EntityEditPage.tsx     # Create/edit/clone entity
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
| `SelectField` | Select | Single/multiple choice |
| `CheckboxField` | Checkbox | Boolean toggle |
| `NumberField` | Number, Range | Min/max/step |
| `DatePickerField` | DatePicker | Date input |
| `RelationField` | Relation | Foreign key dropdown |
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
| `/rf/api/sanity_check?type={name}` | POST | Validate entity data |
| `/rf/api/entity_lock?type={name}&id={id}&action=lock` | POST | Acquire edit lock |
| `/rf/api/entity_lock?type={name}&id={id}&action=unlock` | POST | Release edit lock |

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
- **Hooks**: `useEntity`, `useEntityLock`, `useSchema`
- **Components**: `DynamicForm`, `TextField`, `SelectField`, `CheckboxField`, `NumberField`
- **Libraries**: `conditionParser`, `schemaToZod`

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
- **entity-crud.spec.ts** - Entity CRUD operations
- **form-fields.spec.ts** - All field types
- **repeater.spec.ts** - Repeater operations
- **entity-lock.spec.ts** - Entity locking
- **auto-save.spec.ts** - Auto-save and validation
- **conditional-fields.spec.ts** - Conditional visibility

### Running All Tests

```bash
npm run test:all
```

**Note**: E2E tests require the backend to be running at `http://localhost:9000`.

