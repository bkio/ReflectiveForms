# @reflective-forms/create-app

CLI scaffolder for [ReflectiveForms](../README.md). Generates a new full-stack project with a .NET backend, React frontend, Docker configuration, and a sample entity — ready to run.

## Usage

```bash
npx @reflective-forms/create-app my-project
```

The CLI will prompt for:

| Prompt | Default | Description |
|--------|---------|-------------|
| Project name | `my-app` | Directory name and package name |
| Display name | Derived from project name | Shown in the admin panel sidebar |
| Primary color | `#2563eb` | Theme color for the UI |
| Backend port | `9000` | .NET dev server port |
| Frontend port | `3000` | Vite dev server port |
| Infrastructure stack | `1` (Local) | `1` Local (file-based), `2` AWS (DynamoDB/S3/SNS+SQS/Redis), `3` Google Cloud (Datastore/GCS/Pub-Sub/Redis), `4` MongoDB (MongoDB/MinIO/Redis) |
| Enable AI features | `N` | AI assistant, semantic search, field suggestions, sanity checks |
| Enable RF Sheets | `Y` | Built-in spreadsheet system; answer `n` to disable |

You can also pass the project name as an argument:

```bash
npx @reflective-forms/create-app my-project
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

## Infrastructure Stacks

The scaffolder supports four infrastructure stacks. Each generates the appropriate CrossCloudKit providers, NuGet packages, docker-compose services, and environment variables:

| Stack | Database | File Storage | Cache | PubSub | Docker Services |
|-------|----------|-------------|-------|--------|-----------------|
| **Local** (default) | Basic (file-based) | Basic (local) | Basic (memory-mapped) | Basic (file-based) | — |
| **AWS** | DynamoDB | S3 | Redis | SNS+SQS | Redis |
| **Google Cloud** | Datastore | Cloud Storage | Redis | Cloud Pub/Sub | Redis |
| **MongoDB** | MongoDB | MinIO (S3-compatible) | Redis | Redis | MongoDB, Redis, MinIO |

You can switch stacks later by editing `backend/RfBuilder.cs` and updating the NuGet packages in `backend/backend.csproj`.

## AI Features (Not Included by Default)

Answer **y** to the "Enable AI features?" prompt to include AI, or add them manually:

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
| `{{SHEETS_CONFIG}}` | RF Sheets prompt — empty when enabled, `SheetsEnabled = false,` when disabled |
| `{{INFRA_USING_STATEMENTS}}` | Infrastructure stack — C# using statements for chosen providers |
| `{{INFRA_SERVICE_INIT}}` | Infrastructure stack — service initialization code |
| `{{INFRA_CSPROJ_PACKAGES}}` | Infrastructure stack — NuGet PackageReferences |
| `{{INFRA_ENV_VARS}}` | Infrastructure stack — environment variable block for `.env.example` |
| `{{INFRA_DOCKER_SERVICES}}` | Infrastructure stack — docker-compose service definitions |
| `{{INFRA_DOCKER_ENV}}` | Infrastructure stack — backend environment vars in docker-compose |
| `{{INFRA_DOCKER_DEPENDS}}` | Infrastructure stack — `depends_on` entries for backend |
| `{{INFRA_README_SECTION}}` | Infrastructure stack — provider-specific documentation |

## Tests

```bash
node --test tests/scaffold.test.js   # 227 tests
```

The test suite verifies:
- All directories and files are created
- All placeholders are replaced in every template file
- Sample entity model is present with correct attribute constructors
- nginx SPA fallback and WebSocket upgrade headers are configured
- No unreplaced `{{...}}` tokens remain
- Per-stack rendered content (RfBuilder.cs, .env.example, README.md, docker-compose)
- Two-pass nested placeholder resolution
- Cross-file API endpoint and port consistency
- Full 16-combination feature-flag matrix (4 stacks × AI × sheets)
- Frontend template validation (package.json, vite, tailwind, nginx)
- NoteModel.cs field attributes and JsonProperty coverage
- Placeholder value edge cases (apostrophes, long names, dots, same ports)
- **Sync checks**: templates stay in sync with the actual framework:
  - RfBuilder.cs sets all required `EntityConfigurationBuilder` properties
  - .csproj includes all NuGet packages used in RfBuilder.cs (with matching versions)
  - NoteModel field attributes use correct constructor signatures
  - Program.cs doesn't duplicate CORS handling
  - rf.config.ts only uses valid `RfConfig` properties
  - Vite env variable name matches the real frontend
  - Vite proxy has WebSocket support
  - Tailwind config includes `@reflective-forms/frontend` content path
  - Template does not include AI config (intentionally minimal)
- **Build check**: scaffolds a project and runs `dotnet build` to verify the generated backend compiles
- **Run check**: scaffolds a project, starts the backend with `dotnet run` (verifies it listens on the configured port), and builds the frontend with `tsc + vite build`

## License

AGPL-3.0
