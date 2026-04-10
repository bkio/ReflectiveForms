# ReflectiveForms

A schema-driven admin panel framework. Define entities with C# attributes, get a full CRUD admin panel with a modern React frontend — auto-save, display conditions, nested repeaters, entity relations, locking, SSO, and more.

## Packages

| Package | Description |
|---------|-------------|
| [`ReflectiveForms.Core`](ReflectiveForms.Core/) | .NET 8 NuGet library — entity configuration, schema generation, CRUD API, auth, SSO |
| [`@reflectiveforms/frontend`](ReflectiveForms.Frontend/) | React + TypeScript npm library — renders schemas as a full admin panel |
| [`@reflectiveforms/create-app`](ReflectiveForms.CreateApp/) | CLI scaffolder — generates a new project with backend, frontend, Docker, and a sample entity |

## Quick Start

### Option 1: Scaffold a new project (recommended)

```bash
npx @reflectiveforms/create-app my-project
cd my-project
# Start backend
cd backend && dotnet run
# In another terminal, start frontend
cd frontend && npm install && npm run dev
```

### Option 2: Add to an existing .NET app

**Backend:**

```bash
dotnet add package ReflectiveForms.Core
```

```csharp
// Program.cs
using Microsoft.Extensions.Logging;
using ReflectiveForms.Core;

var builder = WebApplication.CreateBuilder(args);
using var loggerFactory = LoggerFactory.Create(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
});
var rfLogger = loggerFactory.CreateLogger<Program>();

var app = builder.BuildWithReflectiveFields(RfBuilder.Build(rfLogger));
app.Run();
```

```csharp
// RfBuilder.cs
using CrossCloudKit.Database.Basic;
using CrossCloudKit.File.Basic;
using CrossCloudKit.Memory.Basic;
using CrossCloudKit.PubSub.Basic;
using Microsoft.Extensions.Logging;
using ReflectiveForms.Core;
using ReflectiveForms.Core.Endpoints;

public static class RfBuilder
{
    public static RfConfigurationBuilder Build(ILogger logger)
    {
        var pubSub = new PubSubServiceBasic();
        var memory = new MemoryServiceBasic(pubSub);
        var file = new FileServiceBasic(memory, pubSub);
        var db = new DatabaseServiceBasic("my-app-db", memory, Path.GetTempPath());

        return new RfConfigurationBuilder
        {
            Logger = logger,
            RootUserCredentials = new RootUserCredentials("admin@karasoftware.com", "123456"),
            RepositoryServiceConfiguration = new EntityRepositoryServiceConfiguration(
                db, memory, pubSub,
                new FileServiceConfiguration(file, "my-app-media")),
            EndpointConfiguration = new EndpointConfiguration
            {
                JwtSecret = "change-this-to-a-random-secret-at-least-32-chars",
                RootPath = "/rf",
                PublicUrlRootForApi = "http://localhost:9000/rf/api/",
                PublicFrontendBaseUrl = "http://localhost:3000",
            },
            EntityTypes =
            [
                new EntityConfigurationBuilder<NoteModel>
                {
                    EntityName = "note",
                    EntityReadableNameSingular = "Note",
                    EntityReadableNamePlural = "Notes",
                    SupportsFrontendEdit = true,
                    HasAuthor = false,
                    HasTags = false,
                    HasCategories = false,
                    HasParentChildRelationship = false,
                    RequireGlobalTitleUniqueness = false,
                    OptionalTitleSanityCheck = null,
                },
            ],
        };
    }
}
```

**Frontend:**

```bash
npm install @reflectiveforms/frontend react react-dom react-router-dom
```

```tsx
// main.tsx
import { createReflectiveFormsApp } from '@reflectiveforms/frontend';

createReflectiveFormsApp({
  apiBaseUrl: 'http://localhost:9000/rf/api',
  appName: 'My Admin',
  primaryColor: '#2563eb',
});
```

## Features

### Entity Configuration (Backend)

- **Declarative models** — C# attributes: `Text`, `TextArea`, `Select`, `Checkbox`, `DatePicker`, `Number`, `Range`, `Url`, `Email`, `Repeater`, `Group`, `Relation`, `WysiwygEditor`, `MediaSourceBase64`
- **Display conditions** — Show/hide fields based on sibling values, works inside repeaters at any depth
- **Nested repeaters** — 3+ levels (e.g. Survey → Sections → Questions → Choices) with min/max enforcement
- **Dynamic choices** — Select options from async C# methods (compile-time or runtime)
- **Dynamic defaults** — Runtime-computed default values via async methods
- **Sanity checks** — Server-side validation with custom async logic (e.g. uniqueness)
- **Entity metadata** — Tags, categories, parent-child hierarchy
- **Role-based access** — IAM with per-entity-type CRUD capabilities
- **Individual sharing** — Per-entity access control: share with specific users/roles, public toggle, auto-generated admin roles
- **SSO** — OpenID Connect, Azure AD, Google with auto-provisioning and domain filtering

### Admin Panel (Frontend)

- **Auto-save** — Debounced saves with toast notifications
- **Entity locking** — Pessimistic concurrent edit protection
- **Search, sort & filter** — Client-side search by title/author, sortable columns, pagination
- **Searchable selects** — Filterable dropdowns for relations and large choice sets
- **Read-only view** — Public entity view with metadata, grid layouts, resolved relations
- **View-only mode** — Entities flagged `SupportsFrontendEdit = false` redirect to view page
- **Depth-aware nesting** — Nested fields render without redundant card wrappers
- **Branding** — Configurable app name, logo, primary color via CSS variable
- **Custom pages** — Add sidebar pages grouped by section
- **Revision diff** — Side-by-side comparison of entity revisions with field-level change highlighting
- **SSO login** — Dedicated SSO login page with branding
- **Sharing dialog** — Reusable sharing UI for any entity type with individual sharing enabled
- **RF Sheets** — Built-in spreadsheet editor (Univer) with custom RF formulas, entity data sources, sharing (user/role/public), and Excel export

## Configuration Reference

### Backend (`EndpointConfiguration`)

| Property | Required | Default | Description |
|----------|----------|---------|-------------|
| `PublicFrontendBaseUrl` | Yes | — | Frontend URL for CORS |
| `JwtSecret` | Yes | — | JWT signing key |
| `RootPath` | Yes | `"/rf"` | API route prefix |
| `PublicUrlRootForApi` | Yes | — | Public API URL for schema links |
| `SsoConfiguration` | No | `null` | SSO settings (see below) |

### SSO Configuration

```csharp
config.Endpoints.SsoConfiguration = new SsoConfiguration {
    Provider = SsoProvider.AzureAd,
    Authority = "https://login.microsoftonline.com/{tenant}/v2.0",
    ClientId = "your-client-id",
    ClientSecret = "your-secret",
    AllowedDomains = new[] { "company.com" },
    AutoProvisionUsers = true,
    DefaultRole = "editor",
};
```

### Frontend (`RfConfig`)

| Property | Required | Default | Description |
|----------|----------|---------|-------------|
| `apiBaseUrl` | Yes | — | Backend API URL |
| `appName` | No | `"ReflectiveForms"` | Sidebar brand name |
| `logo` | No | — | URL string or React component |
| `primaryColor` | No | `"#2563eb"` | Theme color (sets `--rf-primary` CSS variable) |
| `basePath` | No | `"/"` | Router base path |
| `auth.mode` | No | `"local"` | `"local"` or `"sso"` |
| `auth.ssoLoginUrl` | No | — | SSO redirect endpoint (required when mode is `"sso"`) |
| `customPages` | No | `[]` | Extra sidebar pages with `path`, `label`, `icon`, `component`, `section` |

## Architecture

```
┌─────────────────────────────┐     ┌─────────────────────────────┐
│  @reflectiveforms/frontend  │     │  ReflectiveForms.Core       │
│  React SPA (Vite)           │────▶│  ASP.NET Core Backend       │
│                             │     │                             │
│  • React 18 + TypeScript    │     │  • JSON schema generation   │
│  • React Hook Form + Zod    │     │  • CRUD via CrossCloudKit   │
│  • TanStack Query v5        │     │  • JWT + Cookie auth + SSO  │
│  • Tailwind CSS 3           │     │  • Entity locking           │
│  • Configurable branding    │     │  • Sanity check pipeline    │
│  • RF Sheets (spreadsheets) │     │  • Bulk read endpoint       │
└─────────────────────────────┘     └─────────────────────────────┘
```

## Project Structure

```
ReflectiveForms/
├── ReflectiveForms.Core/             # .NET NuGet library
│   ├── Attributes/Fields/            #   Field attribute definitions
│   ├── Endpoints/                    #   API endpoints, SSO, auth
│   ├── Models/                       #   Entity base models
│   ├── Operation/                    #   Locking, sanity checks, defaults
│   ├── Repositories/                 #   DB integration
│   └── Schema/                       #   JSON schema generator
│
├── ReflectiveForms.Core.Tests/       # Backend unit tests (xUnit, 260+)
│
├── ReflectiveForms.Frontend/         # React npm library
│   ├── src/
│   │   ├── api/                      #   API client
│   │   ├── components/               #   Fields, form, layout
│   │   ├── hooks/                    #   useEntity, useSchema, useAutoSave, useLock
│   │   ├── lib/                      #   createApp, RfConfigProvider, RF formulas, exports
│   │   └── pages/                    #   Dashboard, List, Edit, View, RevisionDiff, Sheets, Login, SSO
│   ├── e2e/                          #   Playwright E2E tests (30 suites)
│   └── vite.config.lib.ts           #   Library build config
│
├── ReflectiveForms.Sample1/          # Sample backend app (6 entity types)
│
└── ReflectiveForms.CreateApp/        # CLI scaffolder
    ├── src/index.js                  #   Interactive prompts + template engine
    ├── templates/                    #   Backend, frontend, Docker templates
    └── tests/                        #   Scaffold integration tests (16)
```

## API Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/rf/api/schema` | GET | All entity schemas |
| `/rf/api/schema?type={name}` | GET | Single entity schema |
| `/rf/api/crud?operation=CREATE&type={name}` | POST | Create entity |
| `/rf/api/crud?operation=READ&type={name}` | POST | Read entity |
| `/rf/api/crud?operation=UPDATE&type={name}` | POST | Update entity |
| `/rf/api/crud?operation=DELETE&type={name}` | POST | Delete entity |
| `/rf/api/crud?operation=PEEK_ALL&type={name}` | POST | List all |
| `/rf/api/crud?operation=PEEK_ALL_PAGINATED&type={name}&page_size={n}` | POST | Paginated list |
| `/rf/api/crud?operation=HISTORY&type={name}` | POST | Revision history |
| `/rf/api/crud?operation=SHARING_CANDIDATES&type={name}` | POST | Users/roles eligible for sharing |
| `/rf/api/sanity_check?type={name}` | POST | Validate |
| `/rf/api/entity_lock_control?type={name}&id={id}&operation=try_lock` | POST | Lock |
| `/rf/api/entity_lock_control?type={name}&id={id}&operation=try_unlock` | POST | Unlock |
| `/rf/api/entity_lock_control?type={name}&id={id}&operation=heartbeat` | POST | Heartbeat |
| `/rf/api/bulk_read` | POST | Fetch multiple entities with optional field filtering |
| `/rf/api/auth_check` | POST | Verify authentication status |
| `/rf/api/capabilities` | POST | Get user capabilities per entity type |
| `/rf/api/login` | POST | Authenticate |
| `/rf/api/logout` | POST | Logout |

## Testing

### Backend

```bash
cd ReflectiveForms.Core.Tests
dotnet test    # 260+ tests
```

### Frontend Unit Tests

```bash
cd ReflectiveForms.Frontend
npm run test:run       # 680+ tests (Vitest)
```

### E2E Tests

```bash
cd ReflectiveForms.Frontend
npx playwright install
npm run test:e2e       # 343 tests across 30+ suites (Playwright, Chromium)
```

### CLI Scaffolder Tests

```bash
cd ReflectiveForms.CreateApp
node --test tests/scaffold.test.js   # 16 tests
```

## Sample Entities (in ReflectiveForms.Sample1)

| Entity | Key Features |
|--------|-------------|
| **Objective** | Repeater (key results + comments), Group, Relation, Dynamic choices/defaults, Sanity check |
| **Blog Post** | WysiwygEditor, MediaSourceBase64, DisplayCondition, DynamicChoicesCompileTimeAsync |
| **Team Member** | DisplayCondition, Repeater (min 1/max 3), Relation, Range slider |
| **Product** | 3 nested Repeaters, DynamicChoicesRuntimeAsync (category → subcategory) |
| **Event** | Nested Groups, DisplayCondition, DynamicDefaultValueAsync (dates) |
| **Survey** | 3-level nesting (Sections → Questions → Choices), DisplayCondition at every level |

## Technical Details

### Display Conditions

```csharp
[JsonProperty("is_digital"),
 Checkbox(label: "Digital Product", instructions: "", defaultValue: false)]
public bool IsDigital;

[JsonProperty("weight_kg"),
 DisplayCondition("is_digital == false"),
 Number(label: "Weight (kg)", instructions: "", mandatory: false)]
public double WeightKg;
```

Conditions scope to the current repeater item when nested.

### Nested Repeaters (3 levels)

```csharp
[Repeater(repeaterFor: typeof(SurveySectionModel), minimumRows: 1, maximumRows: 10)]
public List<SurveySectionModel> Sections = [];

// Inside SectionModel:
[Repeater(repeaterFor: typeof(SurveyQuestionModel), minimumRows: 1, maximumRows: 20)]
public List<SurveyQuestionModel> Questions = [];

// Inside QuestionModel:
[DisplayCondition("question_type == choice"),
 Repeater(repeaterFor: typeof(SurveyChoiceModel), minimumRows: 2, maximumRows: 8)]
public List<SurveyChoiceModel>? Choices = null;
```

### Individual Sharing (Shareable Entity Types)

Entity types can opt into per-entity access control by setting `HasIndividualSharing = true`. This enables:

- **Per-entity sharing** — Each entity instance can be shared with specific users and/or roles at `view` or `edit` permission levels
- **Public toggle** — Entities can be marked public so anyone with entity-type-level access can view them
- **Owner-based access** — The entity author is always the owner with full control
- **Auto-generated admin role** — A "{ReadableName} Admin" role is automatically created and maintained at startup, granting full access to all entities of that type
- **Entity locking** — Lock checks respect sharing permissions (only users with edit access can lock)
- **Dedicated frontend pages** — Sharing entities use custom pages (set via `CustomFrontendListRoute`) instead of the generic entity list/edit pages

```csharp
// 1. Create a fields model inheriting from SharableEntityFieldsModel
public class ProjectModel : SharableEntityFieldsModel
{
    [JsonProperty("description"),
     TextArea(label: "Description", instructions: "", mandatory: true,
        placeholderText: "Describe the project...")]
    public string Description = "";

    [JsonProperty("status"),
     Select(label: "Status", instructions: "",
        defaultValue: "active",
        choices: new[] { "active", "archived" })]
    public string Status = "active";
}

// 2. Register with HasIndividualSharing = true
new EntityConfigurationBuilder<ProjectModel>
{
    EntityName = "project",
    EntityReadableNameSingular = "Project",
    EntityReadableNamePlural = "Projects",
    SupportsFrontendEdit = true,
    HasAuthor = true,  // Required for sharing
    HasTags = false,
    HasCategories = false,
    HasParentChildRelationship = false,
    RequireGlobalTitleUniqueness = false,
    OptionalTitleSanityCheck = null,
    HasIndividualSharing = true,
    CustomFrontendListRoute = "/projects",
}
```

The framework then automatically:
- Adds `is_public`, `shared_users`, and `shared_roles` fields (inherited from `SharableEntityFieldsModel`)
- Creates a "Project Admin" IAM role with full CRUD capabilities on the entity type
- Filters PEEK_ALL results to only entities the user can access (owned, shared, or public)
- Enforces per-entity READ/UPDATE/DELETE access checks
- Strips sharing fields from UPDATE requests for non-owners
- Exposes `SHARING_CANDIDATES` operation returning eligible users and roles
- Exposes `has_individual_sharing` and `custom_frontend_list_route` in the schema for frontend navigation

## Development (this repo)

### Running the sample app

```bash
# Backend
cd ReflectiveForms.Sample1 && dotnet run   # http://localhost:9000

# Frontend
cd ReflectiveForms.Frontend && npm install && npm run dev   # http://localhost:3000/

# Login: admin@karasoftware.com / 123456
```

### Building the frontend library

```bash
cd ReflectiveForms.Frontend
npm run build:lib   # Outputs to dist/
```

## License

AGPL-3.0 — See [LICENSE](LICENSE) for details.
