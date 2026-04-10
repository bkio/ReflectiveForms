# ReflectiveForms Sample Project

A comprehensive sample application demonstrating every feature of the **ReflectiveForms** framework — an attribute-driven, reflection-based system that generates dynamic CRUD admin forms from C# entity definitions, served to a React SPA frontend.

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Entity Types in This Sample](#entity-types-in-this-sample)
- [Feature Coverage](#feature-coverage)
- [Backend (ASP.NET)](#backend-aspnet)
  - [Running the Backend](#running-the-backend)
  - [Project Structure](#project-structure)
  - [Configuration Deep Dive](#configuration-deep-dive)
  - [API Endpoints](#api-endpoints)
  - [Authentication](#authentication)
- [Frontend (React SPA)](#frontend-react-spa)
  - [Running the Frontend](#running-the-frontend)
  - [Available Scripts](#available-scripts)
  - [Connecting to the Backend](#connecting-to-the-backend)
  - [Extending the Frontend](#extending-the-frontend)
- [Extending the Sample](#extending-the-sample)
  - [Adding a New Entity Type](#adding-a-new-entity-type)
  - [Adding a New Field Type Usage](#adding-a-new-field-type-usage)
  - [Custom Validation with LogicSanityCheckAsync](#custom-validation-with-logicsanitycheckasync)
  - [Dynamic Dropdown Choices](#dynamic-dropdown-choices)
  - [Dynamic Default Values](#dynamic-default-values)
  - [Lifecycle Hooks](#lifecycle-hooks)
- [Reserved (Built-in) Entities](#reserved-built-in-entities)
- [Individual Sharing](#individual-sharing)
- [Troubleshooting](#troubleshooting)

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────┐
│  C# Entity Models                                            │
│  (Attributes: [Text], [Select], [Repeater], etc.)            │
└────────────────────────┬─────────────────────────────────────┘
                         │  Reflection at startup
                         ▼
┌──────────────────────────────────────────────────────────────┐
│  ReflectiveForms.Core                                        │
│  - EntitySchemaGenerator → JSON Schema                       │
│  - EntityRepositoryService → CRUD operations                 │
│  - EntitySanityChecker → Validation pipeline                 │
│  - Endpoint mapper → REST API routes                         │
└────────────────────────┬─────────────────────────────────────┘
                         │  HTTP JSON API (localhost:9000/rf/api)
                         ▼
┌──────────────────────────────────────────────────────────────┐
│  React Frontend (Vite + TypeScript)                          │
│  - Fetches schema → renders DynamicForm                      │
│  - React Hook Form + Zod for client validation               │
│  - TanStack Query for data fetching                          │
│  - Pessimistic entity locking (auto-save with 5s debounce)   │
│  - Dynamic default values from backend schema                │
│  - Read-only entity view with relation resolution            │
│  - Searchable selects and entity list search/sort/filter     │
│  - Depth-aware nested field rendering (no cards-in-cards)    │
│  - RF Sheets: spreadsheets with entity data + RF formulas    │
└──────────────────────────────────────────────────────────────┘
```

---

## Prerequisites

| Requirement          | Version   | Notes                                  |
|----------------------|-----------|----------------------------------------|
| .NET SDK             | 8.0+      | `dotnet --version` to verify           |
| Node.js              | 18+       | For the React frontend                 |
| npm                  | 9+        | Comes with Node.js                     |

---

## Quick Start

### 1. Start the backend

```bash
cd ReflectiveForms.Sample1
dotnet run
```

The backend starts on **http://localhost:9000**. It uses in-memory/file-based storage (no external database needed).

### 2. Start the frontend

```bash
cd ReflectiveForms.Frontend
npm install
npm run dev
```

The frontend starts on **http://localhost:3000** and automatically proxies API requests to the backend.

### 3. Log in

Open http://localhost:3000 in your browser and log in with:

| Field    | Value                     |
|----------|---------------------------|
| Email    | `admin@karasoftware.com`  |
| Password | `123456`                  |

---

## Entity Types in This Sample

This sample registers **6 custom entity types** plus the **6 built-in reserved entities**, covering every feature of the framework:

### 1. Objectives (OKR System)
> `EntityName: "objective"` — The original example model demonstrating core features.

| Feature | Details |
|---------|---------|
| Field types | TextArea, Select (static + dynamic), DatePicker, Url, Checkbox, Group, Repeater, Relation |
| Dynamic choices | Compile-time (year list) and Runtime (JavaScript-based, dependent on other fields) || Dynamic defaults | `DynamicDefaultValueAsync` — work start date defaults to today || Custom validation | `LogicSanityCheckAsync` — ensures root cause uniqueness across all objectives |
| Title sanity check | Rejects "Forbidden title example" |
| Entity hooks | PostCreate, PostUpdate, PostDelete (logging) |
| Config | HasAuthor ✓, HasTags ✓, HasCategories ✓, HasParent ✓, TitleUnique ✓ |

### 2. Blog Posts
> `EntityName: "blog-post"` — A content management model.

| Feature | Details |
|---------|---------|
| Field types | WysiwygEditor, MediaSourceBase64, TextArea, Text, Select, Checkbox ×2, DatePicker, Number (min/max), Url |
| Group | SEO Metadata with `Grid2ElementsInRow` layout |
| Repeater | External Links (min: 0, max: 10, no accordion) |
| DisplayCondition | `scheduled_date` only visible when `status == 'scheduled'` |
| Dynamic choices | `DynamicChoicesCompileTimeAsync` for publication year |
| Custom validation | `LogicSanityCheckAsync` — ensures URL slug uniqueness |
| Entity hooks | PostCreate, PostUpdate, PostDelete (logging) |
| Config | HasAuthor ✓, HasTags ✓, HasCategories ✓, HasParent ✗, TitleUnique ✓ |

### 3. Team Members
> `EntityName: "team-member"` — An HR/people directory model.

| Feature | Details |
|---------|---------|
| Field types | Email, MediaSourceBase64, Select (9 options), Text (with default value), Number (step 0.5, min/max), Range (1–10, step 0.5), Checkbox, WysiwygEditor, DatePicker |
| Group | Office Address with `Grid3ElementsInRow` layout |
| Repeater #1 | Social Links (with accordion) |
| Repeater #2 | Emergency Contacts (min: 1, max: 3, `Grid2ElementsInRow`, no accordion) |
| Relation | Links to `blog-post` entity |
| DisplayCondition | Office address only visible when `is_remote == false` |
| Config | SuperAdminOnly, HasAuthor ✗, HasTags ✗, HasCategories ✗, HasParent ✗, TitleUnique ✓ |

### 4. Products (E-Commerce)
> `EntityName: "product"` — A full product catalog model.

| Feature | Details |
|---------|---------|
| Field types | TextArea, WysiwygEditor, Select (static), MediaSourceBase64, Number (various configs), Range (discount %), Checkbox ×2, DatePicker, Url |
| DynamicChoicesRuntimeAsync | Subcategory dropdown changes based on selected product category (JavaScript) |
| Repeater #1 | Image Gallery (min: 0, max: 20, accordion) |
| Repeater #2 | Product Variants (min: 1, max: 50, `Grid2ElementsInRow`, accordion) — contains Number, Checkbox |
| Repeater #3 | Specifications (no min/max, `Grid2ElementsInRow`, no accordion) |
| DisplayCondition | `weight_kg` only visible when `is_digital == false` |
| Relation | Links to `team-member` entity |
| Entity hooks | PostCreate only |
| Config | HasAuthor ✓, HasTags ✓, HasCategories ✓, HasParent ✓, TitleUnique ✗ |

### 5. Events
> `EntityName: "event"` — A conference/event management model.

| Feature | Details |
|---------|---------|
| Field types | WysiwygEditor, Select (7 options), DatePicker ×2, Checkbox, Url ×2, Email, MediaSourceBase64, Number, Range (ticket pricing) |
| Deeply nested Groups | Event → Venue (Group) → Address (Group with `Grid3ElementsInRow`) |
| Repeater #1 | Sessions/Agenda (full layout, accordion) — contains Text, Email, DatePicker, Number, Select, TextArea |
| Repeater #2 | Sponsors (min: 0, max: 30, `Grid2ElementsInRow`, no accordion) — contains MediaSourceBase64 |
| DisplayCondition | `meeting_url` visible when `is_online == true`; `venue` visible when `is_online == false` || Dynamic defaults | `DynamicDefaultValueAsync` — start date defaults to today, end date to tomorrow || Relation | Links to `team-member` entity |
| Config | HasAuthor ✓, HasTags ✗, HasCategories ✓, HasParent ✗, TitleUnique ✗ |

### 6. Surveys
> `EntityName: "survey"` — A 3-level nested repeater model demonstrating deep nesting.

| Feature | Details |
|---------|---------|
| Field types | TextArea, Select, Checkbox, Number, DatePicker |
| 3-level Repeater nesting | Survey → Sections → Questions → Choices |
| DisplayCondition at every level | `passing_score` visible when `has_scoring == true`, `choices` visible when `question_type == choice`, `help_text` when `is_required == true` |
| Repeater constraints | Sections (1–10), Questions (1–20), Choices (2–8) |
| Config | HasAuthor ✓, HasTags ✗, HasCategories ✗, HasParent ✗, TitleUnique ✗ |

---

## Feature Coverage

Every ReflectiveForms feature is demonstrated at least once in this sample:

| Feature | Used In |
|---------|---------|
| **Text** field | Blog Post (slug), Team Member (job title with default), Product (spec name), Event (session title, sponsor name) |
| **TextArea** field | Objective (root cause, key results), Blog Post (excerpt), Product (short description), Event (session description) |
| **Email** field | Team Member (work email), Event (registration email, speaker email) |
| **Url** field | Objective (documentation), Blog Post (SEO canonical), Team Member (social links), Product (external page), Event (meeting, registration, sponsor, venue) |
| **Number** field (basic) | Blog Post (reading time), Event (max attendees, session duration) |
| **Number** field (min/max) | Blog Post (1–120), Product (price 0–999999, stock 0–1M), Event (session 5–480 min) |
| **Number** field (step size) | Team Member (0.5 step), Product (0.01 step for price, 1000 step for salary) |
| **Number** field (default value) | Team Member (0 years), Product (0 stock), Event (30 min sessions) |
| **Range** slider | Team Member (1–10 score), Product (0–90% discount), Event ($0–$5000 ticket) |
| **Select** (static choices) | Blog Post (status), Team Member (department), Product (category), Event (type), Objective (short/long term) |
| **Select** with `DynamicChoicesCompileTimeAsync` | Objective (initiation year), Blog Post (publication year) |
| **Select** with `DynamicChoicesRuntimeAsync` | Objective (year-based OKR type), Product (category → subcategory) |
| **Checkbox** | Blog Post (featured, allow comments), Team Member (remote), Product (published, digital), Event (online), Objective (achieved) |
| **DatePicker** | Objective (start date), Blog Post (scheduled date), Team Member (hire date), Product (launch date), Event (start + end dates) |
| **WysiwygEditor** | Blog Post (content), Team Member (bio), Product (long description), Event (description) |
| **MediaSourceBase64** | Blog Post (featured image), Team Member (avatar), Product (primary image, gallery), Event (banner, sponsor logo) |
| **Relation** | Objective (author → users), Team Member (→ blog-post), Product (→ team-member), Event (→ team-member) |
| **Group** (Full) | Objective (creator comment), Event (venue details) |
| **Group** (Grid2ElementsInRow) | Blog Post (SEO metadata) |
| **Group** (Grid3ElementsInRow) | Team Member (office address), Event (venue → address) |
| **Repeater** (basic) | Objective (key results, comments), Product (specifications) |
| **Repeater** (min/max rows) | Blog Post (external links 0–10), Team Member (emergency contacts 1–3), Product (variants 1–50, gallery 0–20), Event (sponsors 0–30) |
| **Repeater** (accordion) | Team Member (social links), Product (gallery, variants), Event (sessions) |
| **Repeater** (no accordion) | Blog Post (external links), Team Member (emergency contacts), Product (specifications), Event (sponsors) |
| **Repeater** (Grid layout) | Team Member (emergency contacts: Grid2), Product (variants: Grid2, specs: Grid2), Event (sponsors: Grid2) |
| **Nested Repeater** | Objective (key results → comments) |
| **Deeply nested Groups** | Event (→ Venue → Address) |
| **DisplayCondition** | Blog Post (scheduled date), Team Member (office address), Product (weight), Event (meeting URL / venue) |
| **LogicSanityCheckAsync** | Objective (root cause uniqueness), Blog Post (slug uniqueness) |
| **OptionalTitleSanityCheck** | Objective (rejects "Forbidden title example") |
| **RequireGlobalTitleUniqueness** | Objective ✓, Blog Post ✓, Team Member ✓, Product ✗, Event ✗ |
| **HasAuthor** | Objective ✓, Blog Post ✓, Product ✓, Event ✓, Team Member ✗ |
| **HasTags** | Objective ✓, Blog Post ✓, Product ✓, Team Member ✗, Event ✗ |
| **HasCategories** | Objective ✓, Blog Post ✓, Product ✓, Event ✓, Team Member ✗ |
| **HasParentChildRelationship** | Objective ✓, Product ✓, Blog Post ✗, Team Member ✗, Event ✗ |
| **HasIndividualSharing** | RF Sheets ✓ (built-in) — see [Individual Sharing](#individual-sharing) |
| **SupportsFrontendEdit** | `true` for all authorized (5 entities), `false` for some if needed (Team Member) |
| **PostCreateHook** | Objective, Blog Post, Product |
| **PostUpdateHook** | Objective, Blog Post |
| **PostDeleteHook** | Objective, Blog Post |
| **Default values** | Text (Team Member job title), Number (various), Select (various), Checkbox (various), Range (various) |
| **DynamicDefaultValueAsync** | Objective (work start date → today), Event (start date → today, end date → tomorrow) |

---

## Backend (ASP.NET)

### Running the Backend

```bash
cd ReflectiveForms.Sample1
dotnet run
```

The server starts on `http://localhost:9000` using Kestrel. Data is stored in the system temp directory using CrossCloudKit's basic (file-based) providers — no external database or message broker required.

### Project Structure

```
ReflectiveForms.Sample1/
├── Program.cs                        # ASP.NET host configuration, CORS, Kestrel
├── RfBuilder.cs                      # ReflectiveForms configuration (entities, hooks, services)
├── RfObjectiveExampleModel.cs        # Objective entity model (original example)
├── Models/
│   ├── BlogPostModel.cs              # Blog post entity + SEO metadata + external links
│   ├── TeamMemberModel.cs            # Team member entity + address + social links + contacts
│   ├── ProductModel.cs               # Product entity + variants + specs + gallery
│   ├── EventModel.cs                 # Event entity + sessions + sponsors + venue
│   └── SurveyModel.cs                # Survey entity + sections + questions + choices (3-level nesting)
├── Pages/                            # Razor Pages (login, index, error)
├── Properties/launchSettings.json
├── appsettings.json
└── wwwroot/                          # Static assets
```

### Configuration Deep Dive

The `RfBuilder.cs` file is the central configuration point. It demonstrates:

1. **Service setup** — CrossCloudKit basic providers (in-memory DB, file storage, pub/sub)
2. **Root user credentials** — Auto-created admin account
3. **Endpoint configuration** — JWT secret, root path, public API URL
4. **Entity type registration** — Each entity with its full configuration

Key configuration properties per entity:

```csharp
new EntityConfigurationBuilder<YourModel>
{
    EntityName = "your-entity",                           // URL-safe slug name
    EntityReadableNameSingular = "Your Entity",           // UI display (singular)
    EntityReadableNamePlural = "Your Entities",           // UI display (plural)
    SupportsFrontendEdit = true,                                      // or false for read-only
    HasAuthor = true,                                     // Adds author relationship
    HasTags = true,                                       // Adds tag taxonomy
    HasCategories = true,                                 // Adds category taxonomy
    HasParentChildRelationship = true,                    // Enables parent-child hierarchy
    RequireGlobalTitleUniqueness = true,                  // Enforces unique titles
    OptionalTitleSanityCheck = async title => ...,        // Custom title validation
    HasIndividualSharing = false,                         // Per-entity sharing (see below)
    CustomFrontendListRoute = null,                       // Custom sidebar route for sharing entities
    HooksSetup = new EntityOnChangedHooksSetup<YourModel> // Lifecycle hooks
    {
        PostCreateHook = (p, ct) => ...,
        PostUpdateHook = (p, ct) => ...,
        PostDeleteHook = (p, ct) => ...
    }
}
```

### API Endpoints

All endpoints are served under `/rf/api/`. The framework automatically generates:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/rf/api/schema` | GET | Returns JSON schema for all registered entities |
| `/rf/api/schema?type={entity}` | GET | Returns schema for a specific entity |
| `/rf/api/crud?operation=PEEK_ALL&type={entity}` | POST | List all entities of a type |
| `/rf/api/crud?operation=PEEK_ALL_PAGINATED&type={entity}&page_size={n}` | POST | Paginated entity list |
| `/rf/api/crud?operation=READ&type={entity}` | POST | Read entity (body: `{ id }`) |
| `/rf/api/crud?operation=CREATE&type={entity}` | POST | Create a new entity |
| `/rf/api/crud?operation=UPDATE&type={entity}` | POST | Update an entity (body includes `id`) |
| `/rf/api/crud?operation=DELETE&type={entity}` | POST | Delete an entity (body: `{ id }`) |
| `/rf/api/crud?operation=HISTORY&type={entity}` | POST | Revision history (body: `{ id }`) |
| `/rf/api/crud?operation=SHARING_CANDIDATES&type={entity}` | POST | Users/roles eligible for sharing |
| `/rf/api/entity_lock_control?type={entity}&id={id}&operation=try_lock` | POST | Acquire entity lock |
| `/rf/api/entity_lock_control?type={entity}&id={id}&operation=try_unlock` | POST | Release entity lock |
| `/rf/api/entity_lock_control?type={entity}&id={id}&operation=heartbeat` | POST | Refresh lock |
| `/rf/api/entity_lock_control?type={entity}&operation=all_locked` | GET | List all locked entities |
| `/rf/api/sanity_check?type={entity}` | POST | Validate entity data |
| `/rf/api/bulk_read` | POST | Fetch multiple entities with optional field filtering |
| `/rf/api/media` | POST | Upload media files |
| `/rf/api/auth_check` | POST | Verify authentication status |
| `/rf/api/capabilities` | POST | Get user capabilities per entity type |
| `/rf/api/login` | POST | Authenticate and receive JWT |
| `/rf/api/logout` | POST | Invalidate session |

### Authentication

The backend uses JWT + Cookie authentication:

- **JWT Secret**: Configured in `RfBuilder.cs` → `EndpointConfiguration.JwtSecret`
- **PublicFrontendBaseUrl**: Required — the frontend URL for CORS (e.g. `"http://localhost:3000"`)
- **Root user**: Auto-created on first startup with credentials from `RootUserCredentials`
- **IAM Roles**: Built-in role-based access control with capabilities per entity type
- **Frontend auth**: JWT token stored in browser cookie, automatically sent with requests
- **SSO**: Optional — configure via `EndpointConfiguration.SsoConfiguration` with OpenID Connect, Azure AD, or Google providers

---

## Frontend (React SPA)

The frontend is a React single-page application that dynamically renders forms based on the JSON schema provided by the backend. It includes:

- **Entity CRUD pages** — Dashboard, entity listing (with search, sort, filter & pagination), create/edit/clone forms
- **Read-only entity view** — Public view page at `/entities-view/:entityName` with grid layouts for groups, structured repeater headers, and relation fields resolved to clickable entity names
- **Revision diff** — Side-by-side revision comparison at `/entities-revisions/:entityName` with searchable revision selectors and field-level change highlighting
- **Dynamic default values** — Fields pre-filled with runtime-computed defaults from the backend
- **Searchable selects** — Filterable dropdowns for Relation and Select fields with large option sets
- **Entity locking** — Pessimistic locking with auto-refresh heartbeat
- **Auto-save** — Debounced auto-save with visual feedback and toast notifications
- **Depth-aware nesting** — Nested fields inside repeaters and groups render cleanly without redundant card wrappers
- **RF Sheets** — Built-in spreadsheet editor at `/sheets` with entity data sources, custom RF formulas (RF.FIELD, RF.SUM, RF.FILTER, etc.), sharing (user/role/public), and Excel export
- **Individual sharing** — Reusable sharing dialog and schema-driven navigation for entity types with `HasIndividualSharing` enabled

### Running the Frontend

```bash
cd ReflectiveForms.Frontend
npm install                  # Install dependencies (first time only)
npm run dev                  # Start development server on http://localhost:3000
```

> **Note:** The backend must be running on `http://localhost:9000` for the frontend to function. The Vite dev server automatically proxies `/rf/api/*` requests to the backend.

### Available Scripts

| Command | Description |
|---------|-------------|
| `npm run dev` | Start dev server with hot reload (port 3000) |
| `npm run build` | Type-check and build for production |
| `npm run preview` | Preview the production build locally |
| `npm run lint` | Run ESLint on all TypeScript/TSX files |
| `npm run type-check` | TypeScript type checking without emitting |
| `npm run test` | Run unit tests in watch mode (Vitest) |
| `npm run test:run` | Run unit tests once |
| `npm run test:coverage` | Run tests with code coverage report |
| `npm run test:e2e` | Run Playwright end-to-end tests |
| `npm run test:e2e:ui` | Run E2E tests with Playwright UI |
| `npm run test:e2e:headed` | Run E2E tests in headed browser mode |
| `npm run test:e2e:report` | Open Playwright HTML report |
| `npm run test:all` | Run both unit and E2E tests |

### Connecting to the Backend

The frontend connects to the backend API using these defaults:

- **Dev mode**: Vite proxies `/rf/api/*` → `http://localhost:9000` (configured in `vite.config.ts`)
- **Custom backend URL**: Set the environment variable `VITE_API_BASE_URL`:

```bash
VITE_API_BASE_URL=http://your-server:9000/rf/api npm run dev
```

### Extending the Frontend

The frontend is structured to automatically render any entity schema returned by the backend. Here's how to extend it:

#### Adding a Custom Field Component

1. Create a new component in `src/components/fields/`:

```tsx
// src/components/fields/MyCustomField.tsx
import { FieldComponentProps } from './types';

export function MyCustomField({ schema, path }: FieldComponentProps) {
  // Your custom field rendering logic
  return <div>...</div>;
}
```

2. Register it in `src/components/fields/FormField.tsx`:

```tsx
import { MyCustomField } from './MyCustomField';

function getFieldRegistry() {
  return {
    // ... existing fields
    MyCustomType: MyCustomField,
  };
}
```

#### Customizing Layout and Styling

- **Tailwind CSS**: All styling uses Tailwind utility classes (`tailwind.config.js`)
- **Layout**: `src/components/layout/AdminLayout.tsx` controls the admin panel shell
- **Theme**: Modify `tailwind.config.js` to change colors, fonts, spacing

#### Adding a New Page/Route

Routes are defined in `src/App.tsx` using React Router v6. Add new routes alongside the existing entity CRUD routes.

#### Key Frontend Libraries

| Library | Purpose |
|---------|---------|
| React Hook Form | Form state management and validation |
| Zod | Schema-based validation |
| TanStack Query | Server state management, caching, mutations |
| React Router v6 | Client-side routing |
| Tailwind CSS 3 | Utility-first styling |
| DOMPurify | HTML sanitization (for WYSIWYG content) |
| Lucide React | Icon library |
| Sonner | Toast notifications |

---

## Extending the Sample

### Adding a New Entity Type

1. **Create the field model** in `Models/`:

```csharp
using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Models;

namespace ReflectiveForms.Sample1.Models;

internal class FaqModel : EntityFieldsModel
{
    [JsonProperty("question"),
     Text(
         label: "Question",
         instructions: "The frequently asked question.",
         mandatory: true,
         placeholderText: "How do I...?")]
    public string Question = "";

    [JsonProperty("answer"),
     WysiwygEditor(
         label: "Answer",
         instructions: "Provide a detailed answer.",
         mandatory: true)]
    public string Answer = "";

    [JsonProperty("sort_order"),
     Number(
         label: "Sort Order",
         instructions: "Lower numbers appear first.",
         mandatory: false,
         placeholderText: "0",
         defaultValue: 0,
         minimumMaximumValues: [0, 1000])]
    public double SortOrder;
}
```

2. **Register it** in `RfBuilder.cs`:

```csharp
EntityTypes =
[
    // ... existing entities ...
    new EntityConfigurationBuilder<FaqModel>
    {
        EntityName = "faq",
        EntityReadableNamePlural = "FAQs",
        EntityReadableNameSingular = "FAQ",
        SupportsFrontendEdit = true,
        HasAuthor = false,
        HasTags = true,
        HasCategories = false,
        HasParentChildRelationship = false,
        RequireGlobalTitleUniqueness = true,
        OptionalTitleSanityCheck = null,
        HooksSetup = null
    }
]
```

3. **Restart the backend** — the frontend will automatically discover the new entity via the schema endpoint.

### Adding a New Field Type Usage

All 14 field types and their constructor parameters:

```csharp
// Text — single-line text input
[Text(label, instructions, mandatory, placeholderText)]
[Text(label, instructions, mandatory, defaultValue, placeholderText)]

// TextArea — multi-line text input
[TextArea(label, instructions, mandatory, placeholderText)]
[TextArea(label, instructions, mandatory, defaultValue, placeholderText)]

// Email — email input with validation
[Email(label, instructions, mandatory, placeholderText)]
[Email(label, instructions, mandatory, defaultValue, placeholderText)]

// Url — URL input with validation
[Url(label, instructions, mandatory, placeholderText)]
[Url(label, instructions, mandatory, defaultValue, placeholderText)]

// Number — numeric input with optional constraints
[Number(label, instructions, mandatory, placeholderText)]
[Number(label, instructions, mandatory, placeholderText, defaultValue)]
[Number(label, instructions, mandatory, placeholderText, minimumMaximumValues)]
[Number(label, instructions, mandatory, placeholderText, defaultValue, minimumMaximumValues)]
[Number(label, instructions, mandatory, placeholderText, minimumMaximumValues, stepSize)]
[Number(label, instructions, mandatory, placeholderText, defaultValue, minimumMaximumValues, stepSize)]

// Range — slider with min/max/step
[Range(label, instructions, mandatory, minimumValue, maximumValue, stepSize)]
[Range(label, instructions, mandatory, defaultValue, minimumValue, maximumValue, stepSize)]

// Select — dropdown (static or dynamic choices)
[Select(label, instructions, defaultValue, choices)]
// choices format: ["value : Display Label", ...]
// choices: null → use DynamicChoicesCompileTimeAsync or DynamicChoicesRuntimeAsync

// Checkbox — boolean toggle
[Checkbox(label, instructions, defaultValue)]

// DatePicker — date input
[DatePicker(label, instructions, mandatory, dateFormat)]
[DatePicker(label, instructions, mandatory, defaultValue, dateFormat)]

// WysiwygEditor — rich text editor
[WysiwygEditor(label, instructions, mandatory)]

// MediaSourceBase64 — file/image upload
[MediaSourceBase64(label, instructions, mandatory)]

// Relation — link to another entity
[Relation(label, instructions, mandatory, relationEntityName, isRelationEntityNotExistsOk)]
[Relation(label, instructions, mandatory, relationEntityName, isRelationEntityNotExistsOk, defaultValueId)]

// Group — nested object
[Group(label, instructions, groupFor, renderStyle)]
// renderStyle: Full, Grid2ElementsInRow, Grid3ElementsInRow, Grid4ElementsInRow, Grid6ElementsInRow

// Repeater — array of objects
[Repeater(label, instructions, repeaterFor, addButtonLabel, groupRenderStyle, useAccordion)]
[Repeater(label, instructions, repeaterFor, addButtonLabel, minimumRows, maximumRows, groupRenderStyle, useAccordion)]
// useAccordion: Yes, No

// DisplayCondition — conditional field visibility (separate attribute)
[DisplayCondition("field_name == 'value'")]
[DisplayCondition("field_name == true")]
```

### Custom Validation with LogicSanityCheckAsync

Add a method named `{FieldName}___LogicSanityCheckAsync` next to the field:

```csharp
[JsonProperty("my_field"),
 Text(label: "My Field", instructions: "", mandatory: true, placeholderText: "")]
public string MyField = "";

public async Task<string?> MyField___LogicSanityCheckAsync(
    int entityId,
    EntityOperationState operationState,
    JObject parentJObject,
    CancellationToken cancellationToken)
{
    // Access all entities of this type
    var allEntities = await operationState.GetAllEntitiesInOperationAsync("my-entity", cancellationToken);
    if (!allEntities.IsSuccessful)
        return allEntities.ErrorMessage;

    // Custom validation logic
    if (MyField.Length < 5)
        return "Field must be at least 5 characters.";

    return null; // null = passed validation
}
```

### Dynamic Dropdown Choices

#### Compile-Time Dynamic Choices

Generate options once at schema compilation (e.g., date ranges, enum values):

```csharp
[JsonProperty("year"),
 Select(label: "Year", instructions: "", defaultValue: "", choices: null)]
public string Year { get; init; } = "";

public static Task<string[]> Year___DynamicChoicesCompileTimeAsync(CancellationToken ct)
{
    var years = Enumerable.Range(2020, 10)
        .Select(y => $"{y} : {y}")
        .Prepend(" : Select Year")
        .ToArray();
    return Task.FromResult(years);
}
```

#### Runtime Dynamic Choices (JavaScript)

Generate options dynamically in the browser based on other field values:

```csharp
[JsonProperty("subcategory"),
 Select(label: "Subcategory", instructions: "", defaultValue: "", choices: null)]
public string Subcategory = "";

public Task<string> Subcategory___DynamicChoicesRuntimeAsync(CancellationToken ct)
{
    // Return JavaScript that will run in the browser
    // window.latest_dynamic_options_input contains current form values
    return Task.FromResult("""
        const input = window.latest_dynamic_options_input;
        if (input.category === 'a') return ['opt1 : Option 1', 'opt2 : Option 2'];
        return [' : Select a category first'];
    """);
}
```

### Dynamic Default Values

Compute field default values at runtime via async C# methods. The method naming convention is `{FieldName}___DynamicDefaultValueAsync`:

```csharp
[JsonProperty("start_date"),
 DatePicker(label: "Start Date", instructions: "When the event starts.", mandatory: true, dateFormat: "yyyyMMdd")]
public string StartDate = "";

public static Task<string> StartDate___DynamicDefaultValueAsync(CancellationToken ct)
{
    // Default to today's date in the field's date format
    return Task.FromResult(DateTime.UtcNow.ToString("yyyyMMdd"));
}

[JsonProperty("end_date"),
 DatePicker(label: "End Date", instructions: "When the event ends.", mandatory: true, dateFormat: "yyyyMMdd")]
public string EndDate = "";

public static Task<string> EndDate___DynamicDefaultValueAsync(CancellationToken ct)
{
    // Default to tomorrow
    return Task.FromResult(DateTime.UtcNow.AddDays(1).ToString("yyyyMMdd"));
}
```

The dynamic default value overrides the static `defaultValue` in the attribute. The schema generator invokes these methods at schema compilation time and includes the computed value in the JSON schema, which the frontend uses to pre-fill new entity forms.

### Lifecycle Hooks

React to entity CRUD operations with hooks:

```csharp
HooksSetup = new EntityOnChangedHooksSetup<YourModel>
{
    // Called after entity creation, before pub/sub notification
    PostCreateHook = async (p, cancellationToken) =>
    {
        // p.EntityName, p.NewId, p.FinalBody
        logger.LogInformation("Created {Name} #{Id}", p.EntityName, p.NewId);
    },

    // Called after entity update, before pub/sub notification
    PostUpdateHook = async (p, cancellationToken) =>
    {
        // p.EntityName, p.Id, p.OldBody, p.NewFinalBody
        var oldTitle = p.OldBody.Title;
        var newTitle = p.NewFinalBody.Title;
        if (oldTitle != newTitle)
            logger.LogInformation("Title changed: {Old} → {New}", oldTitle, newTitle);
    },

    // Called after entity deletion, before pub/sub notification
    PostDeleteHook = async (p, cancellationToken) =>
    {
        // p.EntityName, p.Id, p.LastBody
        logger.LogInformation("Deleted {Name} #{Id}", p.EntityName, p.Id);
    }
}
```

---

## Reserved (Built-in) Entities

ReflectiveForms automatically creates and manages these system entities:

| Entity | Name | Description |
|--------|------|-------------|
| **Users** | `users` | User accounts with email, password (SHA-256), role assignments |
| **IAM Roles** | `iam-role` | Role-based permissions with per-entity-type capabilities |
| **Tags** | `tags` | Flat taxonomy for tagging entities |
| **Categories** | `categories` | Flat taxonomy for categorizing entities |
| **Media** | `media` | Media library with automatic image resizing (150, 300, 600, 1024px) |
| **RF Sheets** | `rf-sheets` | Built-in spreadsheet editor with RF formulas, entity data sources, individual sharing (user/role/public), and Excel export. Uses `HasIndividualSharing` for per-entity access control. |

These are always available and do not need to be registered in `EntityTypes`.

---

## Individual Sharing

Entity types can opt into **per-entity access control** by setting `HasIndividualSharing = true`. The built-in RF Sheets entity uses this feature. Here's how to add it to your own entity types:

### 1. Inherit from `SharableEntityFieldsModel`

Instead of `EntityFieldsModel`, use `SharableEntityFieldsModel` as the base class. This adds `is_public`, `shared_users`, and `shared_roles` fields automatically:

```csharp
using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Models;

public class ProjectModel : SharableEntityFieldsModel
{
    [JsonProperty("description"),
     TextArea(label: "Description", instructions: "", mandatory: true,
        placeholderText: "Project description...")]
    public string Description = "";

    [JsonProperty("status"),
     Select(label: "Status", instructions: "",
        defaultValue: "planning",
        choices: new[] { "planning", "active", "completed" })]
    public string Status = "planning";
}
```

### 2. Enable sharing in the entity configuration

```csharp
new EntityConfigurationBuilder<ProjectModel>
{
    EntityName = "project",
    EntityReadableNameSingular = "Project",
    EntityReadableNamePlural = "Projects",
    SupportsFrontendEdit = true,
    HasAuthor = true,                       // Required — the author is the entity owner
    HasTags = false,
    HasCategories = false,
    HasParentChildRelationship = false,
    RequireGlobalTitleUniqueness = false,
    OptionalTitleSanityCheck = null,
    HasIndividualSharing = true,            // Enables per-entity access control
    CustomFrontendListRoute = "/projects",  // Route for the custom page in the sidebar
}
```

### 3. Build a custom frontend page

Shareable entities must have their own dedicated pages rather than using the generic entity list/edit pages, because the UX requires a sharing dialog, access-level banners, and filtered list views. The frontend automatically:
- Hides sharing entities from the generic entity sidebar section
- Adds a dedicated sidebar entry under the entity's readable name (plural) linking to `CustomFrontendListRoute`
- Redirects `/entities/{entityName}` to the custom route

See `RfSheetListPage` and `RfSheetPage` in the frontend source for a complete implementation example.

### What the framework handles automatically

- **Admin role creation** — A "Project Admin" IAM role is created at startup with full CRUD + user/role peek capabilities
- **Filtered list endpoints** — PEEK_ALL returns only entities the user owns, is shared with, or that are public
- **Per-entity access checks** — READ, UPDATE, DELETE, and entity locking verify per-entity access levels
- **Sharing protection** — Non-owners cannot modify `is_public`, `shared_users`, or `shared_roles` fields
- **Sharing candidates endpoint** — `SHARING_CANDIDATES` operation returns users and roles eligible for sharing, annotated with their maximum permission level (view or edit based on IAM capabilities)
- **Schema exposure** — `has_individual_sharing` and `custom_frontend_list_route` are included in the entity schema for frontend consumption

---

## Troubleshooting

### Backend won't start
- Ensure .NET 8.0+ SDK is installed: `dotnet --version`
- The backend uses the system temp directory for storage. Ensure write permissions on `$TMPDIR` or `%TEMP%`.

### Frontend can't connect to backend
- Confirm the backend is running on port 9000: `curl http://localhost:9000/rf/api/schema`
- The Vite proxy is configured for `localhost:9000` in `vite.config.ts`
- If using a different backend port, update both `vite.config.ts` and `VITE_API_BASE_URL`

### CORS errors
- The backend allows origin `http://localhost:3000` (the configured Vite dev server port)
- If your frontend runs on a different port, add it to the CORS policy in `Program.cs`

### "Forbidden title example" error
- The Objective entity has a title sanity check that rejects this exact string. This is intentional — demonstrates the `OptionalTitleSanityCheck` feature.

### Login fails
- Use the root credentials: `admin@karasoftware.com` / `123456`
- On first startup, the root user is automatically created
- Data persists in temp directory; delete it to reset: `rm -rf $TMPDIR/reflective-forms-tests-1`
