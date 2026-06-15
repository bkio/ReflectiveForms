# ReflectiveForms
![Tests](https://img.shields.io/badge/Tests-1711%2F1712%20passing-brightgreen)

A schema-driven admin panel framework. Define entities with C# attributes, get a full CRUD admin panel with a modern React frontend — auto-save, display conditions, nested repeaters, entity relations, locking, SSO, AI-powered features (centralized AI assistant with tool-calling, semantic search, sanity checks, NL filtering), OpenAPI spec generation, and more.
## Test Results

**Last Updated:** 2026-06-15 19:47:09 UTC

| Metric | Count |
|--------|-------|
| **Tests Passed** | **1711** |
| **Tests Failed** | **1** |
| **Total Tests** | **1712** |

## Preview

Annotate your C# models — ReflectiveForms generates the full admin UI automatically.

```csharp
public class BlogPostModel : EntityFieldsModel
{
    [JsonProperty("title"),
     Text(label: "Title", instructions: "", mandatory: true)]
    public string Title = "";

    [JsonProperty("author"),
     Relation(label: "Author", instructions: "", relatedEntity: "user")]
    public string Author = "";

    [JsonProperty("content"),
     WysiwygEditor(label: "Post Content", instructions: "")]
    public string Content = "";

    [JsonProperty("excerpt"),
     TextArea(label: "Excerpt", instructions: "", mandatory: false),
     AISuggestion("Write a 1-sentence summary", "title", "content")]
    public string Excerpt = "";
}
```

That's it — register it, and the dashboard, list view, and full create/edit form appear automatically.

![Dashboard](https://raw.githubusercontent.com/bkio/ReflectiveForms/main/docs/images/dashboard.png)
*Dashboard showing all registered entity types with View All / Create New actions*

![Entity List](https://raw.githubusercontent.com/bkio/ReflectiveForms/main/docs/images/entity-list.png)
*Searchable, sortable entity list with NL filter, AI search, and per-row actions*

![Create Form](https://raw.githubusercontent.com/bkio/ReflectiveForms/main/docs/images/blog-post-form-top.png)
*Auto-generated create form with rich field types: title, relation dropdowns, tags, WYSIWYG editor*

## Packages

| Package | Description |
|---------|-------------|
| [`ReflectiveForms.Core`](ReflectiveForms.Core/) | .NET 8 NuGet library — entity configuration, schema generation, CRUD API, auth, SSO |
| [`@reflective-forms/frontend`](ReflectiveForms.Frontend/) | React + TypeScript npm library — renders schemas as a full admin panel |
| [`@reflective-forms/create-app`](ReflectiveForms.CreateApp/) | CLI scaffolder — generates a new project with backend, frontend, Docker, and a sample entity |

## Quick Start

### Option 1: Scaffold a new project (recommended)

```bash
npx @reflective-forms/create-app my-project
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
                    ShowInNavigation = true, // Set false to hide from sidebar & dashboard
                },
            ],
        };
    }
}
```

**Frontend:**

```bash
npm install @reflective-forms/frontend react react-dom react-router-dom
```

```tsx
// main.tsx
import { createReflectiveFormsApp } from '@reflective-forms/frontend';

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
- **System-managed entities** — Root user, Owner role, and sharing admin roles are immutable; backend rejects UPDATE/DELETE with 403, frontend shows read-only view with "System" badge
- **Individual sharing** — Per-entity access control: share with specific users/roles, public toggle, auto-generated admin roles
- **SSO** — OpenID Connect, Azure AD, Google with auto-provisioning and domain filtering
- **AI features (optional)** — Centralized AI assistant with multi-turn chat and tool-calling (entity creation, updates, deletion, field suggestions, navigation — all with user approval), semantic search (vector indexing), AI sanity checks (`[AISanityCheck]`), AI field suggestions (`[AISuggestion]`), revision diff AI summaries, natural language filtering, AI relation suggestions (`[AIRelationSuggestion]`). All off by default — enable per entity type
- **OpenAPI spec generation** — Auto-generated OpenAPI 3.1 spec at `/openapi.json`, independent of AI features

#### Field Convention Methods (`___` Naming Pattern)

ReflectiveForms uses a **naming convention** to discover four kinds of field-level customization methods. These are methods you define **directly on your entity model class**, named `{FieldName}___{Suffix}`. The framework discovers them via reflection — no registration needed.

> ⚠️ **These are NOT lifecycle hooks.** Do not confuse these with `PostCreateHook` / `PostUpdateHook` / `PostDeleteHook`, which are fire-and-forget callbacks configured separately via `EntityOnChangedHooksSetup` in `RfBuilder.cs`.

| Suffix | Purpose | Method Signature | Runs |
|--------|---------|-----------------|------|
| `___DynamicChoicesCompileTimeAsync` | Populate `Select` choices at schema generation time | `static Task<string[]>` | Schema generation, sanity checks, view building |
| `___DynamicChoicesRuntimeAsync` | Populate `Select` choices in the browser based on form state | `Task<string>` returning JavaScript | In browser, on field interaction |
| `___DynamicDefaultValueAsync` | Compute a default value at runtime | `Task<object?>` (instance or static) | Schema generation and new-entity form pre-fill |
| `___LogicSanityCheckAsync` | Server-side validation before save | `Task<string?>` (null = pass, string = error) | On create/update, before the entity is written |

```csharp
public class ProductModel : EntityFieldsModel
{
    // 1. Compile-time dynamic choices: called once at schema generation
    [JsonProperty("category"),
     Select(label: "Category", instructions: "", defaultValue: "", choices: null)]
    public string Category = "";
    public static Task<string[]> Category___DynamicChoicesCompileTimeAsync(CancellationToken ct)
        => Task.FromResult(new[] { "a : Category A", "b : Category B" });

    // 2. Runtime dynamic choices: returns JS that runs in the browser
    [JsonProperty("subcategory"),
     Select(label: "Subcategory", instructions: "", defaultValue: "", choices: null)]
    public string Subcategory = "";
    public Task<string> Subcategory___DynamicChoicesRuntimeAsync(CancellationToken ct)
        => Task.FromResult("""
            const input = window.latest_dynamic_options_input;
            if (input.category === 'a') return ['a1 : Sub A1', 'a2 : Sub A2'];
            return [': Select a category first'];
            """);

    // 3. Dynamic default value: computed when a new entity form opens
    [JsonProperty("created_date"),
     DatePicker(label: "Created", instructions: "", mandatory: true, dateFormat: "yyyyMMdd")]
    public string CreatedDate = "";
    public Task<object?> CreatedDate___DynamicDefaultValueAsync(CancellationToken ct)
        => Task.FromResult<object?>(DateTime.UtcNow.ToString("yyyyMMdd"));

    // 4. Sanity check: server-side validation — return null if valid, error string if not
    [JsonProperty("sku"),
     Text(label: "SKU", instructions: "", mandatory: true, placeholderText: "")]
    public string Sku = "";
    public async Task<string?> Sku___LogicSanityCheckAsync(
        int entityId, EntityOperationState operationState, JObject parentJObject, CancellationToken ct)
    {
        var all = await operationState.GetAllEntitiesInOperationAsync("product", ct);
        if (!all.IsSuccessful)
            return all.ErrorMessage;
        foreach (var entity in all.Data)
        {
            var casted = entity.ToObjectWithPolymorphism<EntityModel<ProductModel>>().NotNull();
            if (casted.Fields.Sku == Sku && casted.Id != entityId)
                return "SKU must be unique.";
        }
        return null; // pass
    }
}
```

> ⚠️ **Always use `ToObjectWithPolymorphism` when iterating entities.** Two reasons:
>
> 1. **JObject structure** — `GetAllEntitiesInOperationAsync` returns raw `JObject` values. User-defined fields are nested under a `fields` key, not at the JObject root:
>    ```json
>    { "id": 42, "slug": "...", "title": {...}, "fields": { "sku": "ABC-123" } }
>    ```
>    `e["sku"]` would return `null` — the field is at `e["fields"]["sku"]`.
>
> 2. **Polymorphism** — The framework serializes entities with `TypeNameHandling.All`, embedding `$type` metadata so nested sub-models (Groups, Repeater items, etc.) retain their concrete type information. Plain `ToObject<T>()` ignores this metadata and deserializes everything to the **declared base type**, silently dropping all custom fields on nested objects.
>
> `ToObjectWithPolymorphism` preserves the type graph: every nested `BaseModel` subclass deserializes to its actual concrete type with all custom properties intact.
>
> **Correct pattern:**
> ```csharp
> var casted = entity.ToObjectWithPolymorphism<EntityModel<ProductModel>>().NotNull();
> // casted.Fields.Sku, casted.Id, etc.
> ```

**Key distinction from lifecycle hooks:**

| Pattern | Where defined | Purpose |
|---------|--------------|---------|
| `___` convention methods | On the **entity model class** itself | Field-level: choices, defaults, validation |
| `PostCreateHook` / `PostUpdateHook` / `PostDeleteHook` | In `RfBuilder.cs` via `EntityOnChangedHooksSetup` | Entity-level: fire-and-forget side effects after save (logging, recalculating related entities). These run **after** the entity is persisted and sanity checks have passed |

### Admin Panel (Frontend)

- **Auto-save** — Debounced saves with toast notifications
- **Entity locking** — Pessimistic concurrent edit protection
- **Search, sort & filter** — Client-side search by title/author, sortable columns, pagination
- **Searchable selects** — Filterable dropdowns for relations and large choice sets
- **Read-only view** — Public entity view with metadata, grid layouts, resolved relations
- **View-only mode** — Entities flagged `SupportsFrontendEdit = false` redirect to view page
- **Hidden from navigation** — Entities flagged `ShowInNavigation = false` are hidden from the sidebar and dashboard; still fully accessible via direct URL and API
- **Hide reserved entity types** — Set `ReservedEntityTypesToHideInNavigation` on `RfConfigurationBuilder` to hide built-in types (Tags, Categories, Media, Users, IamRoles) from the sidebar and dashboard. All visible by default. Example: `ReservedEntityTypesToHideInNavigation = [ReservedEntityType.Tags, ReservedEntityType.Categories]`
- **System-managed entity protection** — System-managed entities (root user, Owner role, sharing admin roles) display a "System" badge, hide edit/delete/clone actions, and redirect edit URLs to the view page
- **Depth-aware nesting** — Nested fields render without redundant card wrappers
- **Branding** — Configurable app name, logo, primary color via CSS variable
- **Custom pages** — Add sidebar pages grouped by section
- **Revision diff** — Side-by-side comparison of entity revisions with field-level change highlighting
- **SSO login** — Dedicated SSO login page with branding
- **Sharing dialog** — Reusable sharing UI for any entity type with individual sharing enabled
- **RF Sheets** — Built-in spreadsheet editor (Univer) with 14 custom RF formulas, entity data sources, live collaborative viewing via WebSocket, sharing (user/role/public), and Excel export
- **Live updates** — WebSocket-based real-time broadcasting: editors push snapshots, viewers see changes instantly with scroll preservation
- **AI components** — Centralized AI assistant panel (multi-turn chat with tool-calling, entity creation/update/deletion proposals with user approval, field suggestions, navigation), semantic search overlay, AI sanity check badges, NL filter bar, AI diff summary, relation suggestions — all conditionally rendered based on backend feature flags and user capabilities

## Configuration Reference

### Builder (`RfConfigurationBuilder`)

| Property | Required | Default | Description |
|----------|----------|---------|-------------|
| `Logger` | Yes | — | `ILogger` for diagnostics |
| `RootUserCredentials` | Yes | — | Auto-created admin account |
| `RepositoryServiceConfiguration` | Yes | — | Database, memory, pubsub, file services |
| `EndpointConfiguration` | Yes | — | JWT, CORS, root path (see below) |
| `AiServiceConfiguration` | No | `null` | AI services (see below) |
| `SheetsEnabled` | No | `true` | Enable/disable the built-in RF Sheets spreadsheet system. When `false`, the `rf-sheets` entity is not registered, sheet routes are hidden in the frontend, and AI sheet tools are disabled. |
| `EntityTypes` | Yes | — | Entity type registrations |
| `EditInactivityTimeoutMs` | No | `600000` | Inactivity timeout (ms) before a user's edit lock is released and they are redirected to the view page. Also controls the backend lock TTL safety net. |
| `ReservedEntityTypesToHideInNavigation` | No | `null` | List of built-in reserved entity types to hide from the sidebar and dashboard. Accepts `ReservedEntityType` enum values: `Tags`, `Categories`, `Media`, `Users`, `IamRoles`. All visible when `null`. Example: `[ReservedEntityType.Tags, ReservedEntityType.Categories]` |

### Backend (`EndpointConfiguration`)

| Property | Required | Default | Description |
|----------|----------|---------|-------------|
| `PublicFrontendBaseUrl` | Yes | — | Frontend URL for CORS |
| `JwtSecret` | Yes | — | JWT signing key |
| `RootPath` | Yes | `"/rf"` | API route prefix |
| `PublicUrlRootForApi` | Yes | — | Public API URL for schema links |
| `SsoConfiguration` | No | `null` | SSO settings (see below) |
| `OpenApi` | No | `null` | OpenAPI 3.1 spec generation settings (see below) |

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

### OpenAPI Configuration

```csharp
config.Endpoints.OpenApi = new OpenApiConfiguration
{
    Title = "My API",
    Version = "1.0.0",
    Description = "My ReflectiveForms API",
    ContactEmail = "admin@example.com",
    IncludeAuthEndpoints = true,     // Include login/logout/auth_check
    IncludeSchemaEndpoints = true,   // Include /schema
    IncludeMediaEndpoints = true,    // Include /media
    IncludeRfExtensions = true,      // Include x-rf-* properties
    IncludeAiEndpoints = true,       // Include AI endpoints (requires AiServiceConfiguration)
    RequireAuthentication = false,   // true = requires JWT/Cookie to access spec
};
```

### AI Configuration (`AiServiceConfiguration`)

AI is fully optional. Set `AiServiceConfiguration` on `RfConfigurationBuilder` to enable AI features:

```csharp
// Uses CrossCloudKit ILLMService + IVectorService
var llm = new LLMServiceOpenAI("http://localhost:11434/v1", "", "gemma3:12b", "nomic-embed-text:v1.5");
var vector = new VectorServiceBasic();

return new RfConfigurationBuilder
{
    // ... existing config ...
    AiServiceConfiguration = new AiServiceConfiguration(
        HeavyLlmService: llm,    // Complex tasks: entity generation, diff summaries
        LightLlmService: llm,    // Fast tasks: suggestions, sanity checks, NL filters, embeddings
        VectorService: vector),
};
```

Then enable per entity type:

```csharp
new EntityConfigurationBuilder<BlogPostModel>
{
    // ... existing config ...
    EntityDescription = "A blog article with rich-text content, SEO metadata, and publication workflow.",
    SupportsSemanticSearch = true,          // Vector indexing on save
    SupportsAiGeneration = true,            // "Create with AI" endpoint
    SupportsAiDiffSummary = true,           // AI revision diff summaries
    SupportsNaturalLanguageFilter = true,   // NL → filter conditions
}
```

Field-level AI attributes:

```csharp
[AISuggestion("Summarize in 2 sentences", "title", "content")]
public string Summary = "";

[AISanityCheck("Is this professional and free of spelling errors?", AISanityCheckSeverity.Warning)]
public string Description = "";

[AIRelationSuggestion(topK: 5)]  // On Relation fields — requires target entity to have SupportsSemanticSearch
public int RelatedAuthor;
```

### Disabling RF Sheets

RF Sheets is enabled by default. To disable the built-in spreadsheet system:

```csharp
return new RfConfigurationBuilder
{
    // ... existing config ...
    SheetsEnabled = false,
};
```

When disabled:
- The `rf-sheets` entity type is not registered (no sheet CRUD endpoints)
- Frontend sheet routes (`/sheets`, `/sheets/:id`) are hidden
- AI sheet tools (`list_sheets`, `suggest_formula`, etc.) are not available
- The entity name `rf-sheets` remains reserved and cannot be used for custom entities

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
│  @reflective-forms/frontend  │     │  ReflectiveForms.Core       │
│  React SPA (Vite)           │────▶│  ASP.NET Core Backend       │
│                             │     │                             │
│  • React 18 + TypeScript    │     │  • JSON schema generation   │
│  • React Hook Form + Zod    │     │  • CRUD via CrossCloudKit   │
│  • TanStack Query v5        │     │  • JWT + Cookie auth + SSO  │
│  • Tailwind CSS 3           │     │  • Entity locking           │
│  • Configurable branding    │     │  • Sanity check pipeline    │
│  • RF Sheets (spreadsheets) │     │  • Bulk read endpoint       │
│  • WebSocket live updates   │     │  • WebSocket live updates   │
│  • AI components (optional) │     │  • AI endpoints (optional)  │
│                             │     │  • OpenAPI 3.1 generation   │
└─────────────────────────────┘     └─────────────────────────────┘
```

## Project Structure

```
ReflectiveForms/
├── ReflectiveForms.Core/             # .NET NuGet library
│   ├── Ai/                           #   AI services, vector sync, handlers, config
│   ├── Attributes/Fields/            #   Field attribute definitions (+ AI attributes)
│   ├── Endpoints/                    #   API endpoints, SSO, auth, AI, OpenAPI
│   ├── Models/                       #   Entity base models
│   ├── Operation/                    #   Locking, sanity checks, defaults
│   ├── Repositories/                 #   DB integration
│   └── Schema/                       #   JSON schema + OpenAPI generator
│
├── ReflectiveForms.Core.Tests/       # Backend unit tests (xUnit, 264)
│
├── ReflectiveForms.Frontend/         # React npm library
│   ├── src/
│   │   ├── api/                      #   API client
│   │   ├── components/               #   Fields, form, layout
│   ├── hooks/                    #   useEntity, useSchema, useAutoSave, useEntityLock, useLiveUpdates, useAi
│   │   ├── lib/                      #   createApp, RfConfigProvider, RF formulas, exports
│   │   └── components/ai/            #   AI components (assistant chat, search, suggest, sanity, etc.)
│   └── pages/                    #   Dashboard, List, Edit, View, RevisionDiff, Sheets, Login, SSO
│   ├── e2e/                          #   Playwright E2E tests (34 suites)
│   └── vite.config.lib.ts           #   Library build config
│
├── ReflectiveForms.Sample1/          # Sample backend app (6 entity types)
│
└── ReflectiveForms.CreateApp/        # CLI scaffolder
    ├── src/index.js                  #   Interactive prompts + template engine
    ├── templates/                    #   Backend, frontend, Docker templates
    └── tests/                        #   Scaffold integration tests (30)
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
| `/rf/api/live_updates?type={name}&id={id}` | WebSocket | Real-time entity change broadcasting |
| `/rf/api/auth_check` | POST | Verify authentication status |
| `/rf/api/capabilities` | POST | Get user capabilities per entity type |
| `/rf/api/frontend_settings` | POST | Frontend configuration (inactivity timeout, etc.) |
| `/rf/api/login` | POST | Authenticate |
| `/rf/api/logout` | POST | Logout |
| `/rf/api/openapi.json` | GET | OpenAPI 3.1 spec (requires `OpenApi` config) |
| `/rf/api/ai/semantic_search` | POST | Semantic search across entity types |
| `/rf/api/ai/generate` | POST | NL → entity draft (requires `CREATE`) |
| `/rf/api/ai/suggest` | POST | AI field suggestion (requires `UPDATE`) |
| `/rf/api/ai/sanity_check` | POST | AI field validation (requires `UPDATE`) |
| `/rf/api/ai/diff_summary` | POST | AI revision diff summary (requires `READ`) |
| `/rf/api/ai/nl_filter` | POST | NL → filter + results (requires `PEEK_ALL`) |
| `/rf/api/ai/relation_suggest` | POST | AI relation suggestions (requires `READ` + `PEEK_ALL`) |
| `/rf/api/ai/chat` | POST | AI assistant multi-turn chat with tool-calling (requires auth) |
| `/rf/api/ai/reindex` | POST | Rebuild vector index (root user only) |

## Testing

### Backend

```bash
cd ReflectiveForms.Core.Tests
dotnet test    # 553 tests
```

### Frontend Unit Tests

```bash
cd ReflectiveForms.Frontend
npm run test:run       # 895 tests (Vitest)
```

### E2E Tests

```bash
cd ReflectiveForms.Frontend
npx playwright install
npm run test:e2e       # 360 tests across 34 suites (Playwright, 3 browsers)
```

### CLI Scaffolder Tests

```bash
cd ReflectiveForms.CreateApp
node --test tests/scaffold.test.js   # 30 tests
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

Entity types in the sample with AI enabled: Objective (semantic search, generation, diff summary, NL filter), Blog Post (semantic search, generation, diff summary, NL filter), Survey (diff summary), Team Member (semantic search), Product (semantic search).

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
    ShowInNavigation = true, // Set false to hide from sidebar & dashboard
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
