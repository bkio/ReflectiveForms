# @reflectiveforms/create-app

CLI scaffolder for [ReflectiveForms](../README.md). Generates a new full-stack project with a .NET backend, React frontend, Docker configuration, and a sample entity — ready to run.

## Usage

```bash
npx @reflectiveforms/create-app my-project
```

The CLI will prompt for:

| Prompt | Default | Description |
|--------|---------|-------------|
| Project name | `my-app` | Directory name and package name |
| Display name | Derived from project name | Shown in the admin panel sidebar |
| Primary color | `#2563eb` | Theme color for the UI |
| Backend port | `9000` | .NET dev server port |
| Frontend port | `3000` | Vite dev server port |

You can also pass the project name as an argument:

```bash
npx @reflectiveforms/create-app my-project
```

## Generated Structure

```
my-project/
├── backend/
│   ├── Program.cs             # .NET entry point with CORS + Kestrel
│   ├── backend.csproj         # .NET 8 project file
│   ├── RfBuilder.cs           # Entity configuration (includes sample Note)
│   ├── Models/
│   │   └── NoteModel.cs       # Sample entity (content, priority, pinned)
│   └── Dockerfile             # Multi-stage .NET build
├── frontend/
│   ├── src/
│   │   ├── main.tsx           # App entry (4 lines)
│   │   └── rf.config.ts       # ReflectiveForms config
│   ├── package.json
│   ├── vite.config.ts         # Dev proxy to backend
│   ├── tailwind.config.js     # Includes RF library content paths
│   ├── index.html
│   ├── tsconfig.json
│   ├── postcss.config.js
│   ├── Dockerfile             # Multi-stage Node → nginx
│   └── nginx.conf             # SPA fallback + API proxy
├── docker-compose.yml         # Backend + frontend services
├── .env.example               # Environment variables template
├── .gitignore
└── README.md                  # Project-specific getting started
```

## Running the Generated Project

### Development

```bash
cd my-project

# Terminal 1: Backend
cd backend
dotnet run

# Terminal 2: Frontend
cd frontend
npm install
npm run dev
```

Open `http://localhost:3000` and log in with `admin@karasoftware.com` / `123456`.

### Docker

```bash
cd my-project
cp .env.example .env
# Edit .env — at minimum set JWT_SECRET
docker compose up --build
```

## Sample Entity: Note

The generated project includes a `NoteModel` entity with:

- **Content** — WYSIWYG editor (required)
- **Priority** — Select field with low/medium/high choices
- **Is Pinned** — Checkbox

This demonstrates the basic ReflectiveForms workflow. Add your own entities by creating model classes in `backend/Models/` and registering them in `backend/RfBuilder.cs`.

## AI Features (Not Included in Scaffold)

The generated project does **not** include AI services or OpenAPI configuration — the template is intentionally minimal. To add AI features:

1. Add NuGet packages to `backend/backend.csproj`:
   ```xml
   <PackageReference Include="CrossCloudKit.LLM.Basic" Version="..." />
   <PackageReference Include="CrossCloudKit.Vector.Basic" Version="..." />
   ```

2. Configure AI in `backend/RfBuilder.cs`:
   ```csharp
   using CrossCloudKit.LLM.Basic;
   using CrossCloudKit.Vector.Basic;
   using ReflectiveForms.Core.Ai;

   var llmService = new LLMServiceBasic();
   var vectorService = new VectorServiceBasic();

   return new RfConfigurationBuilder
   {
       // ... existing config ...
       AiServiceConfiguration = new AiServiceConfiguration(
           HeavyLlmService: llmService,
           LightLlmService: llmService,
           VectorService: vectorService),
   };
   ```

3. Enable per entity: `SupportsSemanticSearch = true`, `SupportsAiGeneration = true`, etc.

4. Optionally add `OpenApi = new OpenApiConfiguration { ... }` to `EndpointConfiguration`.

See the [sample project](../ReflectiveForms.Sample1/) for a fully configured example.

## Template Placeholders

The scaffolder replaces `{{PLACEHOLDER}}` tokens in all template files:

| Placeholder | Source |
|-------------|--------|
| `{{PROJECT_NAME}}` | Project name prompt |
| `{{APP_NAME}}` | Display name prompt |
| `{{PRIMARY_COLOR}}` | Primary color prompt |
| `{{BACKEND_PORT}}` | Backend port prompt |
| `{{FRONTEND_PORT}}` | Frontend port prompt |
| `{{CSPROJ_NAME}}` | Derived from project name (dots replacing special chars) |

## Tests

```bash
node --test tests/scaffold.test.js   # 30 tests
```

The test suite verifies:
- All directories and files are created
- All placeholders are replaced in every template file
- Sample entity model is present with correct attribute constructors
- nginx SPA fallback and WebSocket upgrade headers are configured
- No unreplaced `{{...}}` tokens remain
- **Sync checks**: templates stay in sync with the actual framework:
  - RfBuilder.cs sets all required `EntityConfigurationBuilder` properties
  - .csproj includes all NuGet packages used in RfBuilder.cs (with matching versions)
  - NoteModel field attributes use correct constructor signatures
  - Program.cs doesn't duplicate CORS handling
  - rf.config.ts only uses valid `RfConfig` properties
  - Vite env variable name matches the real frontend
  - Vite proxy has WebSocket support
  - Tailwind config includes `@reflectiveforms/frontend` content path
  - Template does not include AI config (intentionally minimal)
- **Build check**: scaffolds a project and runs `dotnet build` to verify the generated backend compiles
- **Run check**: scaffolds a project, starts the backend with `dotnet run` (verifies it listens on the configured port), and builds the frontend with `tsc + vite build`

## License

AGPL-3.0
