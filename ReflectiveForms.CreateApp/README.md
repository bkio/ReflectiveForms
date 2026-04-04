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
| Auth mode | `local` | `local` (username/password) or `sso` |

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

## Template Placeholders

The scaffolder replaces `{{PLACEHOLDER}}` tokens in all template files:

| Placeholder | Source |
|-------------|--------|
| `{{PROJECT_NAME}}` | Project name prompt |
| `{{APP_NAME}}` | Display name prompt |
| `{{PRIMARY_COLOR}}` | Primary color prompt |
| `{{BACKEND_PORT}}` | Backend port prompt |
| `{{FRONTEND_PORT}}` | Frontend port prompt |
| `{{AUTH_MODE}}` | Auth mode prompt |
| `{{CSPROJ_NAME}}` | Derived from project name (dots replacing special chars) |

## Tests

```bash
node --test tests/scaffold.test.js   # 16 tests
```

The test suite verifies:
- All directories and files are created
- All placeholders are replaced in every template file
- Sample entity model is present
- nginx SPA fallback is configured
- No unreplaced `{{...}}` tokens remain

## License

AGPL-3.0
