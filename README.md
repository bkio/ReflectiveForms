# ReflectiveForms

A C# library that generates dynamic forms from entity configurations with a modern React frontend.

## Architecture

ReflectiveForms uses a **React SPA** approach with a TypeScript frontend consuming JSON schemas from a C# backend API.

## Project Structure

```
ReflectiveForms/
├── ReflectiveForms.Core/           # Core library
│   ├── Attributes/                 # Field attribute definitions
│   ├── Endpoints/                  # API endpoints
│   └── Schema/                     # JSON schema generation
├── ReflectiveForms.Core.Tests/     # Unit tests (xUnit)
├── ReflectiveForms.Frontend/       # React SPA frontend
│   └── src/
│       ├── components/             # React field components
│       ├── hooks/                  # React Query hooks
│       ├── lib/                    # Utilities (conditionParser, schemaToZod)
│       └── pages/                  # Page components
└── ReflectiveForms.Sample1/        # Sample ASP.NET application
```

## Getting Started

### Backend

```bash
cd ReflectiveForms.Sample1
dotnet restore
dotnet run
# Server runs at http://localhost:9000
```

### Frontend (React SPA)

```bash
cd ReflectiveForms.Frontend
npm install
npm run dev
# Server runs at http://localhost:5173
```

## API Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/rf/api/schema?type={name}` | GET | Fetch entity schema |
| `/rf/api/crud?operation={op}&type={name}` | POST | CRUD operations |
| `/rf/api/sanity_check?type={name}` | POST | Validate entity data |
| `/rf/api/entity_lock_control?type={name}&id={id}` | POST | Entity locking |
| `/rf/api/login` | POST | Authentication |
| `/rf/api/logout` | POST | Logout |

## Testing

### Backend Tests
```bash
cd ReflectiveForms.Core.Tests
dotnet test
```

### Frontend Tests
```bash
cd ReflectiveForms.Frontend
npm run test        # Watch mode
npm run test:run    # Single run
npm run test:coverage  # With coverage
```

### E2E Tests (Playwright)
```bash
cd ReflectiveForms.Frontend
npx playwright install  # First time only
npm run test:e2e        # Run all E2E tests
npm run test:e2e:ui     # Interactive UI mode
npm run test:e2e:headed # See browser
```

### Run All Tests
```bash
cd ReflectiveForms.Frontend
npm run test:all  # Unit + E2E tests
```

## Documentation

- [Frontend Architecture Proposal](FRONTEND_ARCHITECTURE_PROPOSAL.md) - Detailed architecture overview
- [Integration Testing](INTEGRATION_TESTING.md) - Manual testing guide
- [Frontend README](ReflectiveForms.Frontend/README.md) - React frontend documentation

## License

AGPL-3.0 - See [LICENSE](LICENSE) for details.
