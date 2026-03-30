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
Auth mode: local
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

Open http://localhost:3000 and log in with `admin` / `admin`. You'll see a "Notes" section in the sidebar — that's your first entity, ready to use.

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

var app = builder.BuildWithReflectiveFields(RfBuilder.Build());
app.UseCors("Frontend");
app.Run();
```

Create `RfBuilder.cs`:

```csharp
using CrossCloudKit.Database.Basic;
using CrossCloudKit.File.Basic;
using CrossCloudKit.Memory.Basic;
using CrossCloudKit.PubSub.Basic;
using ReflectiveForms.Core;
using ReflectiveForms.Core.Endpoints;

public static class RfBuilder
{
    public static RfConfigurationBuilder Build()
    {
        var pubSub = new PubSubServiceBasic();
        var memory = new MemoryServiceBasic(pubSub);
        var file = new FileServiceBasic(memory, pubSub);
        var db = new DatabaseServiceBasic("my-notes-db", memory, Path.GetTempPath());

        return new RfConfigurationBuilder
        {
            RootUserCredentials = new RootUserCredentials("admin@admin.com", "admin"),
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
                },
            ],
        };
    }
}
```

Create `NoteModel.cs`:

```csharp
using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Models;

public class NoteModel : EntityFieldsModel
{
    [JsonProperty("content")]
    [WysiwygEditor(label: "Content", mandatory: true)]
    public string Content = "";

    [JsonProperty("priority")]
    [Select(label: "Priority", mandatory: true,
        choices: ["low", "medium", "high"],
        defaultValue: "medium")]
    public string Priority = "medium";

    [JsonProperty("is_pinned")]
    [Checkbox(label: "Pinned", defaultValue: false)]
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

Open http://localhost:3000 and log in with `admin@admin.com` / `admin`.

---

## Understanding Your Entity Model

Every entity in ReflectiveForms is a C# class that extends `EntityFieldsModel`. Fields are defined using attributes:

```csharp
public class NoteModel : EntityFieldsModel
{
    [JsonProperty("content")]          // JSON key in the API
    [WysiwygEditor(label: "Content",   // Rich text editor
        mandatory: true)]              // Required field
    public string Content = "";

    [JsonProperty("priority")]
    [Select(label: "Priority",         // Dropdown select
        choices: ["low", "medium", "high"],
        defaultValue: "medium")]
    public string Priority = "medium";

    [JsonProperty("is_pinned")]
    [Checkbox(label: "Pinned",         // Toggle checkbox
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
    [JsonProperty("description")]
    [TextArea(label: "Description", mandatory: true, placeholder: "What needs to be done?")]
    public string Description = "";

    [JsonProperty("due_date")]
    [DatePicker(label: "Due Date")]
    public string DueDate = "";

    [JsonProperty("status")]
    [Select(label: "Status",
        choices: ["todo", "in-progress", "done"],
        defaultValue: "todo")]
    public string Status = "todo";

    [JsonProperty("effort_hours")]
    [Number(label: "Estimated Hours", mandatory: false, minValue: 0, maxValue: 100)]
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
    },
    new EntityConfigurationBuilder<TaskModel>
    {
        EntityName = "task",
        EntityReadableNameSingular = "Task",
        EntityReadableNamePlural = "Tasks",
        SupportsFrontendEdit = true,
        HasAuthor = true,      // track who created each task
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
[JsonProperty("is_urgent")]
[Checkbox(label: "Urgent", defaultValue: false)]
public bool IsUrgent;

[JsonProperty("urgency_reason")]
[DisplayCondition("is_urgent == true")]
[TextArea(label: "Why is this urgent?", mandatory: true)]
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

    // List page columns (besides title)
    ListColumns = ["status", "due_date"],
}
```

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
    JwtSecret = "your-secret",
    PublicFrontendBaseUrl = "http://localhost:3000",
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

- Browse the [sample app](../ReflectiveForms.Sample1/) for advanced examples (nested repeaters, dynamic choices, sanity checks)
- Read the [frontend library docs](../ReflectiveForms.Frontend/README.md) for the full configuration reference
- See the [main README](../README.md) for the complete API endpoint reference
