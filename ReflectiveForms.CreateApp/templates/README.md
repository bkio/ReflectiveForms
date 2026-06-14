# {{APP_NAME}}

Built with [ReflectiveForms](https://github.com/bkio/ReflectiveForms) — a schema-driven admin panel framework.

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/)

### Development

1. **Start the backend:**

```bash
cd backend
dotnet run
```

The API will be available at `http://localhost:{{BACKEND_PORT}}/rf/api`.

2. **Start the frontend:**

```bash
cd frontend
npm install
npm run dev
```

The app will be available at `http://localhost:{{FRONTEND_PORT}}`.

3. **Log in** with `admin@karasoftware.com` and password `123456`.

### Docker

```bash
cp .env.example .env
# Edit .env to set JWT_SECRET and other values
docker compose up --build
```

## Project Structure

```
{{PROJECT_NAME}}/
├── backend/          # .NET 8 ReflectiveForms API
│   ├── Program.cs
│   ├── RfBuilder.cs  # Entity configuration
│   └── Models/       # Entity models
├── frontend/         # React + Vite consumer app
│   └── src/
│       ├── main.tsx      # App entry point
│       └── rf.config.ts  # ReflectiveForms config
├── docker-compose.yml
└── .env.example
```

## Adding Entities

1. Create a model class in `backend/Models/`:

```csharp
using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Models;

public class TaskModel : EntityFieldsModel
{
    [JsonProperty("description")]
    [TextArea(label: "Description", instructions: "What needs to be done?", mandatory: true, placeholderText: "")]
    public string Description = "";

    [JsonProperty("is_done")]
    [Checkbox(label: "Done", instructions: "", defaultValue: false)]
    public bool IsDone;
}
```

2. Register it in `backend/RfBuilder.cs`:

```csharp
new EntityConfigurationBuilder<TaskModel>
{
    EntityName = "task",
    EntityReadableNameSingular = "Task",
    EntityReadableNamePlural = "Tasks",
    SupportsFrontendEdit = true,
    HasAuthor = false,
    HasTags = false,
    HasCategories = false,
    HasParentChildRelationship = false,
    RequireGlobalTitleUniqueness = false,
    OptionalTitleSanityCheck = null,
    ShowInNavigation = true,
}
```

3. Restart the backend — the entity appears automatically in the admin panel.

## Configuration

See `frontend/src/rf.config.ts` for frontend configuration (branding, colors, custom pages).

See `backend/RfBuilder.cs` for backend configuration (entities, auth, SSO).

{{INFRA_README_SECTION}}

{{AI_README_SECTION}}

## License

Private — powered by ReflectiveForms (AGPL-3.0).
