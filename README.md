# ReflectiveForms

A C# library that generates dynamic, schema-driven forms from entity configurations. The backend produces JSON schemas from decorated C# models, and a modern React + TypeScript frontend renders them as fully functional admin panels with auto-save, display conditions, nested repeaters, entity relations, and more.

## Key Features

- **Declarative Entity Models** — Define entities with C# attributes (`Text`, `TextArea`, `Select`, `Checkbox`, `DatePicker`, `Number`, `Range`, `Url`, `Email`, `Repeater`, `Group`, `Relation`, `WysiwygEditor`, `MediaSourceBase64`)
- **Display Conditions** — Show/hide fields based on sibling field values, works at any nesting level including inside repeaters
- **Nested Repeaters** — Up to 3+ levels of nesting (e.g. Survey → Sections → Questions → Choices) with min/max row enforcement
- **Auto-Save** — Debounced auto-save with toast notifications, date format normalization, and error handling
- **Entity Locking** — Concurrent edit protection with lock/unlock lifecycle
- **Dynamic Choices** — Select fields populated at compile-time or runtime from async C# methods
- **Dynamic Default Values** — Field defaults computed at runtime via async C# methods (e.g. today's date)
- **Read-Only Entity View** — Public/read-only view page for entities at `/entities-view/:entityName`
- **Searchable Select** — Filterable dropdown for Relation and Select fields with large option sets
- **Paginated Entity Lists** — Server-side pagination with page tokens for entity listing
- **Role-Based Access** — SuperAdmin-only or all-authorized visibility per entity type
- **CRUD API** — Create, Read, Update, Delete, Peek All (with pagination) operations via a single endpoint
- **Sanity Checks** — Server-side validation with custom async logic (e.g. uniqueness checks)

## Architecture

```
┌─────────────────────────┐     ┌─────────────────────────────┐
│   React SPA (Vite)      │     │  ASP.NET Core Backend       │
│   localhost:3000         │────▶│  localhost:9000              │
│                         │     │                             │
│  • React 18 + TypeScript│     │  • Kestrel HTTP server      │
│  • React Hook Form + Zod│     │  • JSON schema generation   │
│  • TanStack Query v5    │     │  • CRUD via CrossCloudKit   │
│  • Tailwind CSS 3       │     │  • JWT + Cookie auth        │
│  • Sonner toasts        │     │  • Entity locking           │
│  • Playwright E2E tests │     │  • Sanity check pipeline    │
└─────────────────────────┘     └─────────────────────────────┘
```

## Project Structure

```
ReflectiveForms/
├── ReflectiveForms.Core/             # Core library (NuGet-ready)
│   ├── Attributes/                   #   Field attribute definitions
│   │   └── Fields/                   #   Text, Select, Repeater, Group, etc.
│   ├── Endpoints/                    #   API endpoints (CRUD, Schema, Login, Lock)
│   ├── Models/                       #   Base models (EntityFieldsModel, EntityModel)
│   ├── Operation/                    #   Sanity checking, defaults, locking
│   ├── Repositories/                 #   CrossCloudKit DB integration
│   ├── Schema/                       #   JSON schema generator
│   └── Utilities/                    #   HTML sanitizer, date helpers
│
├── ReflectiveForms.Core.Tests/       # Backend unit tests (xUnit)
│
├── ReflectiveForms.Frontend/         # React SPA frontend
│   ├── src/
│   │   ├── api/                      #   API client (fetchSchema, CRUD, login)
│   │   ├── components/
│   │   │   ├── fields/               #   Field components (Text, Select, Repeater, etc.)
│   │   │   ├── form/                 #   DynamicForm (auto-save, Zod validation)
│   │   │   └── layout/              #   AdminLayout (responsive sidebar)
│   │   ├── hooks/                    #   React Query hooks (useEntity, useEntityLock, useAutoSave)
│   │   ├── lib/                      #   conditionParser, schemaToZod, sanitize, formUtils
│   │   └── pages/                    #   Dashboard, EntityList, EntityEdit, EntityView, Login
│   └── e2e/                          #   Playwright E2E tests (266 Chromium tests)
│       ├── helpers.ts                #   ApiHelper, UiHelper, test fixtures
│       ├── sample-*.spec.ts          #   Per-entity CRUD tests (6 entity types)
│       ├── integration-*.spec.ts     #   Cross-cutting integration tests (9 suites)
│       ├── entity-*.spec.ts          #   CRUD, locking, view page tests
│       ├── dynamic-default-value.spec.ts  # Dynamic default value tests
│       └── searchable-select.spec.ts #   Searchable select component tests
│
└── ReflectiveForms.Sample1/          # Sample ASP.NET application
    ├── Program.cs                    #   App entry point, CORS, Kestrel config
    ├── RfBuilder.cs                  #   Entity type registration
    ├── RfObjectiveExampleModel.cs    #   Objective (OKR) entity model
    ├── Models/
    │   ├── BlogPostModel.cs          #   Blog post entity
    │   ├── EventModel.cs             #   Event entity (venues, sessions)
    │   ├── ProductModel.cs           #   Product entity (variants, specs)
    │   ├── SurveyModel.cs            #   Survey entity (3-level nesting)
    │   └── TeamMemberModel.cs        #   Team member entity
    ├── Pages/                        #   Razor Pages (Login, Logout, Index)
    └── wwwroot/                      #   Static assets
```

## Sample Entities

| Entity | Key Features |
|--------|-------------|
| **Objective** | Repeater (key results with nested comments), Group, Relation, DatePicker, Select (static + dynamic), LogicSanityCheck, title uniqueness, DynamicDefaultValueAsync (work start date) |
| **Blog Post** | WysiwygEditor, MediaSourceBase64, DisplayCondition (status → scheduled date), Repeater (external links), slug uniqueness, DynamicChoicesCompileTimeAsync |
| **Team Member** | DisplayCondition (is_remote → office address), Repeater (emergency contacts min 1/max 3), Relation to blog-post, Range slider |
| **Product** | DisplayCondition (is_digital → weight), Repeater ×3 (gallery, variants, specs), DynamicChoicesRuntimeAsync (category → subcategory) |
| **Event** | DisplayCondition (is_online → meeting URL / venue), nested Groups (Venue → Address), Repeater (sessions, sponsors), DynamicDefaultValueAsync (start/end dates) |
| **Survey** | 3-level nesting (Sections → Questions → Choices), DisplayCondition at every level, min/max row enforcement |

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or later
- [Node.js 18+](https://nodejs.org/) with npm

### Backend

```bash
cd ReflectiveForms.Sample1
dotnet restore
dotnet run
# Kestrel runs at http://localhost:9000
```

### Frontend (React SPA)

```bash
cd ReflectiveForms.Frontend
npm install
npm run dev
# Vite dev server at http://localhost:3000
```

### Default Credentials

| Field | Value |
|-------|-------|
| Email | `admin@karasoftware.com` |
| Password | `123456` |

> The React frontend is at `http://localhost:3000/rf/app/` (note the `/rf/app` base path).

## API Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/rf/api/schema` | GET | Fetch all entity schemas |
| `/rf/api/schema?type={name}` | GET | Fetch single entity schema |
| `/rf/api/crud?operation=CREATE&type={name}` | POST | Create entity |
| `/rf/api/crud?operation=READ&type={name}` | POST | Read entity (body: `{ id }`) |
| `/rf/api/crud?operation=UPDATE&type={name}` | POST | Update entity |
| `/rf/api/crud?operation=DELETE&type={name}` | POST | Delete entity (body: `{ id }`) |
| `/rf/api/crud?operation=PEEK_ALL&type={name}` | POST | List all entities |
| `/rf/api/crud?operation=PEEK_ALL_PAGINATED&type={name}&page_size={n}` | POST | List entities with pagination |
| `/rf/api/sanity_check?type={name}` | POST | Validate entity data |
| `/rf/api/entity_lock_control?type={name}&id={id}&operation=try_lock` | POST | Acquire entity lock |
| `/rf/api/entity_lock_control?type={name}&id={id}&operation=try_unlock` | POST | Release entity lock |
| `/rf/api/entity_lock_control?type={name}&id={id}&operation=heartbeat` | POST | Refresh lock |
| `/rf/api/entity_lock_control?type={name}&operation=all_locked` | GET | List all locked entities |
| `/rf/api/login` | POST | Authentication |
| `/rf/api/logout` | POST | Logout |

## Testing

### Backend Unit Tests

```bash
cd ReflectiveForms.Core.Tests
dotnet test
```

### Frontend Unit Tests (Vitest)

```bash
cd ReflectiveForms.Frontend
npm run test           # Watch mode
npm run test:run       # Single run
npm run test:coverage  # With coverage
```

### E2E Tests (Playwright)

```bash
cd ReflectiveForms.Frontend
npx playwright install   # First time only
npm run test:e2e         # Run all E2E tests
npm run test:e2e:ui      # Interactive UI mode
npm run test:e2e:headed  # See browser
```

The E2E suite includes 266 tests (Chromium) across 27 spec files covering:
- Per-entity CRUD flows (Objective, Blog Post, Team Member, Product, Event, Survey)
- Display condition visibility at all nesting levels
- Repeater add/remove with min/max enforcement (3 levels deep)
- Auto-save with round-trip verification
- Entity relations and cross-entity workflows
- Entity locking with concurrent edit protection
- Read-only entity view page
- Dynamic default values
- Searchable select and relation fields
- Pagination and data persistence
- Schema API contract validation
- Form validation and sanity check errors

### Run All Tests

```bash
cd ReflectiveForms.Frontend
npm run test:all   # Unit + E2E tests
```

## Technical Details

### Display Conditions

Fields can be conditionally shown/hidden based on sibling values:

```csharp
[JsonProperty("is_digital"),
 Checkbox(label: "Digital Product", defaultValue: false)]
public bool IsDigital;

[JsonProperty("weight_kg"),
 DisplayCondition("is_digital == false"),
 Number(label: "Weight (kg)", mandatory: false)]
public double WeightKg;
```

Conditions work at any nesting depth — inside repeater items, the evaluator scopes to the current item's values.

### Nested Repeaters

Repeaters can nest arbitrarily. The Survey entity demonstrates 3 levels:

```csharp
// Level 1: Sections (min 1, max 10)
[Repeater(repeaterFor: typeof(SurveySectionModel), minimumRows: 1, maximumRows: 10)]
public List<SurveySectionModel> Sections = [];

// Level 2 (inside Section): Questions (min 1, max 20)
[Repeater(repeaterFor: typeof(SurveyQuestionModel), minimumRows: 1, maximumRows: 20)]
public List<SurveyQuestionModel> Questions = [];

// Level 3 (inside Question): Choices (min 2, max 8)
[DisplayCondition("question_type == choice"),
 Repeater(repeaterFor: typeof(SurveyChoiceModel), minimumRows: 2, maximumRows: 8)]
public List<SurveyChoiceModel>? Choices = null;
```

### Schema-to-Zod Conversion

The frontend converts JSON schemas to Zod validation schemas at runtime, with permissive handling for complex nested structures and automatic default generation for repeater min-items.

## License

AGPL-3.0 — See [LICENSE](LICENSE) for details.
