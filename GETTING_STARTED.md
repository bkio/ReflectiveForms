# Getting Started with ReflectiveForms

This guide walks you through building your first admin panel with ReflectiveForms. By the end, you'll have a working backend API and a React frontend that lets you create, edit, and manage entities — all generated from simple C# model classes.

## What You'll Build

A "Notes" admin panel where you can:
- Create and edit notes with a rich text editor
- Set priority levels (low / medium / high)
- Pin important notes
- Search, sort, and paginate through your notes

No frontend code needed — just define a C# class and ReflectiveForms handles the rest.

---

## Prerequisites

| Tool | Version | Check |
|------|---------|-------|
| .NET SDK | 8.0+ | `dotnet --version` |
| Node.js | 18+ | `node --version` |
| npm | 9+ | `npm --version` |

---

## Option A: Use the CLI Scaffolder (Fastest)

```bash
npx @reflectiveforms/create-app my-notes-app
```

Answer the prompts (or press Enter to use defaults):

```
Project name: my-notes-app
Display name: My Notes App
Primary color: #2563eb
Backend port: 9000
Frontend port: 3000
```

Then start it up:

```bash
cd my-notes-app

# Terminal 1 — start the backend
cd backend
dotnet run

# Terminal 2 — start the frontend
cd frontend
npm install
npm run dev
```

Open http://localhost:3000 and log in with `admin@karasoftware.com` / `123456`. You'll see a "Notes" section in the sidebar — that's your first entity, ready to use.

**Skip to [Understanding Your Entity Model](#understanding-your-entity-model) to learn how it works.**

---

## Option B: Set Up Manually

### Step 1: Create the Backend

Create a new .NET project:

```bash
mkdir my-notes-app && cd my-notes-app
mkdir backend && cd backend
dotnet new web
dotnet add package ReflectiveForms.Core
```

Replace `Program.cs` with:

```csharp
using System.Net;
using ReflectiveForms.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(System.Net.IPAddress.Loopback, 9000);
});

// Create rf logger
using var loggerFactory = LoggerFactory.Create(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
});
var rfLogger = loggerFactory.CreateLogger<Program>();

var app = builder.BuildWithReflectiveFields(RfBuilder.Build(rfLogger));
app.UseCors("Frontend");
app.Run();
```

Create `RfBuilder.cs`:

```csharp
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
        var db = new DatabaseServiceBasic("my-notes-db", memory, Path.GetTempPath());

        return new RfConfigurationBuilder
        {
            Logger = logger,
            RootUserCredentials = new RootUserCredentials("admin@karasoftware.com", "123456"),
            RepositoryServiceConfiguration = new EntityRepositoryServiceConfiguration(
                db, memory, pubSub,
                new FileServiceConfiguration(file, "my-notes-media")),
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

> **Tip:** You can also set `EditInactivityTimeoutMs` on the builder (default: `600000` = 10 minutes) to control how long a user can be idle before their edit lock is released.

Create `NoteModel.cs`:

```csharp
using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Models;

public class NoteModel : EntityFieldsModel
{
    [JsonProperty("content"),
     WysiwygEditor(label: "Content", instructions: "", mandatory: true)]
    public string Content = "";

    [JsonProperty("priority"),
     Select(label: "Priority", instructions: "",
        defaultValue: "medium",
        choices: new[] { "low", "medium", "high" })]
    public string Priority = "medium";

    [JsonProperty("is_pinned"),
     Checkbox(label: "Pinned", instructions: "", defaultValue: false)]
    public bool IsPinned;
}
```

Start the backend:

```bash
dotnet run
```

You should see `Now listening on: http://localhost:9000`. The schema endpoint is live at http://localhost:9000/rf/api/schema.

### Step 2: Create the Frontend

In a new terminal, go back to the project root:

```bash
cd my-notes-app
mkdir frontend && cd frontend
npm init -y
npm install @reflectiveforms/frontend react react-dom react-router-dom
npm install -D @vitejs/plugin-react vite typescript @types/react @types/react-dom tailwindcss postcss autoprefixer
npx tailwindcss init -p
```

Create `src/main.tsx`:

```tsx
import { createReflectiveFormsApp } from '@reflectiveforms/frontend';

createReflectiveFormsApp({
  apiBaseUrl: 'http://localhost:9000/rf/api',
  appName: 'My Notes App',
});
```

Create `index.html`:

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>My Notes App</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

Create `vite.config.ts`:

```ts
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: { port: 3000 },
});
```

Update `tailwind.config.js` to scan the library's components:

```js
export default {
  content: [
    './index.html',
    './src/**/*.{js,ts,jsx,tsx}',
    './node_modules/@reflectiveforms/frontend/dist/**/*.{js,mjs}',
  ],
  theme: { extend: {} },
  plugins: [],
};
```

Start the frontend:

```bash
npx vite
```

Open http://localhost:3000 and log in with `admin@karasoftware.com` / `123456`.

---

## Understanding Your Entity Model

Every entity in ReflectiveForms is a C# class that extends `EntityFieldsModel`. Fields are defined using attributes:

```csharp
public class NoteModel : EntityFieldsModel
{
    [JsonProperty("content"),           // JSON key in the API
     WysiwygEditor(label: "Content",    // Rich text editor
        instructions: "",
        mandatory: true)]               // Required field
    public string Content = "";

    [JsonProperty("priority"),
     Select(label: "Priority",          // Dropdown select
        instructions: "",
        defaultValue: "medium",
        choices: new[] { "low", "medium", "high" })]
    public string Priority = "medium";

    [JsonProperty("is_pinned"),
     Checkbox(label: "Pinned",          // Toggle checkbox
        instructions: "",
        defaultValue: false)]
    public bool IsPinned;
}
```

The backend reads these attributes at startup, generates a JSON schema, and exposes it at `/rf/api/schema`. The frontend fetches the schema and renders the correct form fields automatically.

---

## Adding a Second Entity

Let's add a "Task" entity. Create `TaskModel.cs`:

```csharp
using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Models;

public class TaskModel : EntityFieldsModel
{
    [JsonProperty("description"),
     TextArea(label: "Description", instructions: "", mandatory: true,
        placeholderText: "What needs to be done?")]
    public string Description = "";

    [JsonProperty("due_date"),
     DatePicker(label: "Due Date", instructions: "", mandatory: false)]
    public string DueDate = "";

    [JsonProperty("status"),
     Select(label: "Status", instructions: "",
        defaultValue: "todo",
        choices: new[] { "todo", "in-progress", "done" })]
    public string Status = "todo";

    [JsonProperty("effort_hours"),
     Number(label: "Estimated Hours", instructions: "", mandatory: false,
        placeholderText: "", minimumMaximumValues: new double[] { 0, 100 })]
    public double EffortHours;
}
```

Register it in `RfBuilder.cs` by adding to the `EntityTypes` array:

```csharp
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
    new EntityConfigurationBuilder<TaskModel>
    {
        EntityName = "task",
        EntityReadableNameSingular = "Task",
        EntityReadableNamePlural = "Tasks",
        SupportsFrontendEdit = true,
        HasAuthor = true,
        HasTags = false,
        HasCategories = false,
        HasParentChildRelationship = false,
        RequireGlobalTitleUniqueness = false,
        OptionalTitleSanityCheck = null,
    },
],
```

Restart the backend (`dotnet run`) and refresh the frontend. "Tasks" appears in the sidebar.

---

## Available Field Types

| Attribute | Renders As | Use For |
|-----------|-----------|---------|
| `Text` | Single-line input | Names, titles, short text |
| `TextArea` | Multi-line input | Descriptions, comments |
| `WysiwygEditor` | Rich text editor | Blog content, documentation |
| `Select` | Dropdown | Single choice from a list |
| `Checkbox` | Toggle | Boolean flags |
| `Number` | Number input | Quantities, scores (supports min/max/step) |
| `Range` | Slider | Visual numeric selection |
| `DatePicker` | Date input | Dates, deadlines |
| `Email` | Email input | Email addresses (with validation) |
| `Url` | URL input | Links (with validation) |
| `Relation` | Searchable dropdown | Link to another entity |
| `MediaSourceBase64` | Image upload | Photos, avatars |
| `Group` | Fieldset | Visually group related fields |
| `Repeater` | Add/remove rows | Lists (e.g. items, contacts) |

---

## Display Conditions

Show or hide fields based on other field values:

```csharp
[JsonProperty("is_urgent"),
 Checkbox(label: "Urgent", instructions: "", defaultValue: false)]
public bool IsUrgent;

[JsonProperty("urgency_reason"),
 DisplayCondition("is_urgent == true"),
 TextArea(label: "Why is this urgent?", instructions: "", mandatory: true, placeholderText: "")]
public string UrgencyReason = "";
```

The "Why is this urgent?" field only appears when the "Urgent" checkbox is checked. This works at any nesting depth, including inside repeaters.

---

## Entity Features

Enable features per entity in the configuration builder:

```csharp
new EntityConfigurationBuilder<TaskModel>
{
    EntityName = "task",
    EntityReadableNameSingular = "Task",
    EntityReadableNamePlural = "Tasks",
    SupportsFrontendEdit = true,

    // Metadata features
    HasAuthor = true,                    // Track author
    HasTags = true,                      // Multi-select tags
    HasCategories = true,                // Multi-select categories
    HasParentChildRelationship = true,   // Parent entity picker

    // Validation
    RequireGlobalTitleUniqueness = true, // No duplicate titles
    OptionalTitleSanityCheck = null,     // No custom title check

    // AI features (requires AiServiceConfiguration on the builder)
    SupportsSemanticSearch = false,          // Vector indexing on save
    SupportsAiGeneration = false,            // "Create with AI" endpoint
    SupportsAiDiffSummary = false,           // AI revision diff summaries
    SupportsNaturalLanguageFilter = false,   // NL → filter conditions
    EntityDescription = null,                // Required when any AI feature is enabled
}
```

---

## Enabling AI Features (Optional)

ReflectiveForms includes optional AI-powered features: semantic search, a centralized AI assistant (multi-turn chat with tool-calling for entity creation, updates, deletion, field suggestions, and navigation — all with user approval), AI sanity checks, revision diff summaries, NL filtering, and AI relation suggestions. All are off by default.

### Step 1: Add AI services to your builder

```csharp
// In RfBuilder.cs — add these alongside your existing services
using CrossCloudKit.LLM.Basic;
using CrossCloudKit.Vector.Basic;
using ReflectiveForms.Core.Ai;

// Bundled SmolLM2-135M + MiniLM for local dev (zero external dependencies)
var llmService = new LLMServiceBasic();
var vectorService = new VectorServiceBasic();

// For production, use OpenAI-compatible providers:
// var llm = new LLMServiceOpenAI("http://localhost:11434/v1", "", "gemma3:12b", "nomic-embed-text:v1.5");
// var vector = new VectorServiceQdrant(host: "localhost", grpcPort: 6334);

return new RfConfigurationBuilder
{
    // ... existing config ...
    AiServiceConfiguration = new AiServiceConfiguration(
        HeavyLlmService: llmService,    // Complex tasks: entity generation, diff summaries
        LightLlmService: llmService,    // Fast tasks: suggestions, sanity checks, embeddings
        VectorService: vectorService),
};
```

### Step 2: Enable features per entity type

```csharp
new EntityConfigurationBuilder<NoteModel>
{
    // ... existing config ...
    EntityDescription = "A simple note with rich-text content.",
    SupportsSemanticSearch = true,          // Index in vector DB on save
    SupportsAiGeneration = true,            // AI assistant can create entities of this type
    SupportsAiDiffSummary = true,           // AI summary on revision diffs
    SupportsNaturalLanguageFilter = true,   // NL → filter conditions
}
```

### Step 3: Add field-level AI attributes (optional)

```csharp
using ReflectiveForms.Core.Attributes;

public class NoteModel : EntityFieldsModel
{
    [JsonProperty("content"),
     WysiwygEditor(label: "Content", instructions: "", mandatory: true)]
    [AISanityCheck("Is the content well-written and professional?", AISanityCheckSeverity.Warning)]
    public string Content = "";

    [JsonProperty("summary"),
     TextArea(label: "Summary", instructions: "", mandatory: false, placeholderText: "")]
    [AISuggestion("Summarize the content in 2 sentences", "content")]
    public string Summary = "";
}
```

The frontend automatically shows AI buttons/badges for fields with these attributes, gated by user capabilities. Entity creation, updates, and field suggestions are routed through the centralized AI assistant with user approval.

---

## Enabling OpenAPI Spec Generation (Optional)

Add `OpenApi` to your endpoint configuration to serve an auto-generated OpenAPI 3.1 spec:

```csharp
EndpointConfiguration = new EndpointConfiguration
{
    // ... existing config ...
    OpenApi = new OpenApiConfiguration
    {
        Title = "My Notes API",
        Version = "1.0.0",
        Description = "Auto-generated from entity schemas",
    },
},
```

The spec is available at `GET /rf/api/openapi.json`. It includes all CRUD endpoints, field schemas, auth endpoints, and (if AI is enabled) AI endpoints. No AI services required — OpenAPI works independently.

---

## Creating a Shareable Entity Type

Standard entities use role-based access control — any user with the right IAM capabilities can read/write all entities of that type. **Shareable entities** add per-entity access control: each entity instance has its own owner, shared users, shared roles, and public visibility.

This is useful for entity types where users create personal content that should only be visible to others if explicitly shared — like documents, projects, dashboards, or reports.

### What Changes When You Enable Sharing

| Aspect | Standard Entity | Shareable Entity |
|--------|----------------|------------------|
| **Access control** | Role-based (per entity type) | Per-entity (owner, shared users/roles, public) |
| **List view** | Shows all entities | Shows only entities you own, are shared with you, or are public |
| **Edit access** | Anyone with UPDATE capability | Only owner + users shared with "edit" permission |
| **Delete access** | Anyone with DELETE capability | Only the entity owner |
| **Sharing settings** | N/A | Only the owner can change sharing |
| **Admin role** | N/A | Auto-generated "{Name} Admin" role with full access |
| **Frontend** | Generic entity list/edit pages | Custom pages (you provide the route) |

### Step-by-Step

**1. Create a fields model that inherits from `SharableEntityFieldsModel`** instead of `EntityFieldsModel`:

```csharp
using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Models;

public class ProjectModel : SharableEntityFieldsModel
{
    [JsonProperty("description"),
     TextArea(label: "Description", instructions: "", mandatory: true,
        placeholderText: "Describe the project...")]
    public string Description = "";

    [JsonProperty("status"),
     Select(label: "Status", instructions: "",
        defaultValue: "planning",
        choices: new[] { "planning", "active", "on-hold", "completed" })]
    public string Status = "planning";
}
```

`SharableEntityFieldsModel` automatically adds three fields to every entity:
- `is_public` (Checkbox) — When enabled, anyone with entity-type-level PEEK_ALL permission can view
- `shared_users` (Repeater) — List of user + permission (`view` or `edit`) entries
- `shared_roles` (Repeater) — List of role + permission (`view` or `edit`) entries

You don't define these fields yourself — they come from the base class.

**2. Register the entity with `HasIndividualSharing = true`:**

```csharp
new EntityConfigurationBuilder<ProjectModel>
{
    EntityName = "project",
    EntityReadableNameSingular = "Project",
    EntityReadableNamePlural = "Projects",
    SupportsFrontendEdit = true,
    HasAuthor = true,                       // Required — tracks entity ownership
    HasTags = false,
    HasCategories = false,
    HasParentChildRelationship = false,
    RequireGlobalTitleUniqueness = false,
    OptionalTitleSanityCheck = null,
    HasIndividualSharing = true,            // Enables per-entity sharing
    CustomFrontendListRoute = "/projects",  // Sidebar link & route redirect target
}
```

> **Requirements:** `HasAuthor` must be `true` (the author is the owner), and the fields model must inherit from `SharableEntityFieldsModel`. The framework validates these at startup and throws a clear error if misconfigured.

**3. Build a custom frontend page** for your entity type. Shareable entities need dedicated pages (they don't use the generic entity list/edit pages) because they have unique UX needs like the sharing dialog and access-level banners. See the built-in RF Sheets pages (`RfSheetListPage` and `RfSheetPage`) as a reference for building your own.

### What the Framework Does Automatically

Once configured, the backend:
- **Filters list results** — PEEK_ALL and PEEK_ALL_PAGINATED only return entities the user owns, is shared with, or that are public
- **Enforces access on read** — READ returns a `403` if the user has no access; adds `access_level` (owner/edit/view) to the response
- **Enforces access on update** — UPDATE requires at least `edit` access; non-owners cannot change sharing fields
- **Enforces access on delete** — DELETE requires `owner` access
- **Enforces access on lock** — Entity locking requires at least `edit` access
- **Creates an admin role** — A "Project Admin" role is auto-created at startup with full CRUD capabilities; users with this role (or the system Owner role) bypass per-entity checks
- **Exposes sharing candidates** — The `SHARING_CANDIDATES` operation returns users and roles eligible for sharing, annotated with their maximum permission level

The frontend schema includes `has_individual_sharing: true` and `custom_frontend_list_route: "/projects"`, which the sidebar and navigation system use to render the correct links and redirects.

---

## Branding & Theming

Customize the admin panel appearance:

```tsx
createReflectiveFormsApp({
  apiBaseUrl: 'http://localhost:9000/rf/api',
  appName: 'Acme Admin',
  logo: '/acme-logo.svg',        // or a React component
  primaryColor: '#7c3aed',       // purple theme
});
```

The primary color applies to buttons, active sidebar items, and links throughout the UI.

---

## Custom Sidebar Pages

Add your own pages alongside the auto-generated entity pages:

```tsx
import { BarChart3, Settings } from 'lucide-react';

createReflectiveFormsApp({
  apiBaseUrl: 'http://localhost:9000/rf/api',
  customPages: [
    {
      path: '/analytics',
      label: 'Analytics',
      icon: BarChart3,
      component: () => <div>Your analytics dashboard here</div>,
      section: 'Reports',
    },
    {
      path: '/settings',
      label: 'Settings',
      icon: Settings,
      component: () => <div>App settings here</div>,
      section: 'Admin',
    },
  ],
});
```

Pages are grouped by `section` in the sidebar. Pages without a section appear under "Custom".

---

## SSO Authentication

To use SSO instead of username/password login:

**Backend** — add SSO configuration:

```csharp
EndpointConfiguration = new EndpointConfiguration
{
    RootPath = "/rf",
    PublicUrlRootForApi = "http://localhost:9000/rf/api/",
    PublicFrontendBaseUrl = "http://localhost:3000",
    JwtSecret = "your-secret",
    SsoConfiguration = new SsoConfiguration
    {
        Provider = SsoProvider.AzureAd,
        Authority = "https://login.microsoftonline.com/{tenant}/v2.0",
        ClientId = "your-client-id",
        ClientSecret = "your-secret",
        AllowedDomains = ["company.com"],
    },
},
```

**Frontend** — switch to SSO mode:

```tsx
createReflectiveFormsApp({
  apiBaseUrl: 'http://localhost:9000/rf/api',
  auth: {
    mode: 'sso',
    ssoLoginUrl: '/auth/sso/login',
  },
});
```

---

## Docker Deployment

If you used the CLI scaffolder, Docker files are already included:

```bash
cd my-notes-app
cp .env.example .env
# Edit .env — set JWT_SECRET to something secure
docker compose up --build
```

This starts the backend on port 9000 and the frontend (via nginx) on port 3000.

---

## Next Steps

- Browse the [sample app](../ReflectiveForms.Sample1/) for advanced examples (nested repeaters, dynamic choices, sanity checks, AI features)
- Read the [frontend library docs](../ReflectiveForms.Frontend/README.md) for the full configuration reference
- See the [main README](../README.md) for the complete API endpoint reference
