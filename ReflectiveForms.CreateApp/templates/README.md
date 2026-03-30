# {{APP_NAME}}

Built with [ReflectiveForms](https://github.com/nicenemo/ReflectiveForms) — a schema-driven admin panel framework.

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/)

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

3. **Log in** with username `admin` and password `admin`.

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
using ReflectiveForms.Core.Attributes;

public class TaskModel
{
    [Field(FieldType.TextLine, Required = true)]
    public string Title { get; set; } = "";

    [Field(FieldType.Checkbox)]
    public bool Done { get; set; }
}
```

2. Register it in `backend/RfBuilder.cs`:

```csharp
config.Entity<TaskModel>(e => {
    e.PluralName = "Tasks";
    e.ListColumns = new[] { "title", "done" };
});
```

3. Restart the backend — the entity appears automatically in the admin panel.

## Configuration

See `frontend/src/rf.config.ts` for frontend configuration (branding, colors, custom pages).

See `backend/RfBuilder.cs` for backend configuration (entities, auth, SSO).

## License

Private — powered by ReflectiveForms (AGPL-3.0).
