# Frontend Architecture

## Overview

ReflectiveForms uses a modern **React SPA** architecture where the frontend consumes JSON schemas from the C# backend API.

> **Note:** Server-side HTML generation has been removed. This document describes the current React SPA architecture.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                        Browser                               │
│  ┌─────────────────────────────────────────────────────┐    │
│  │           React SPA (TypeScript)                     │    │
│  │  • Form Renderer (interprets JSON schema)            │    │
│  │  • State Management (React Query)                    │    │
│  │  • Built-in validation (Zod)                         │    │
│  └──────────────────────┬───────────────────────────────┘    │
│                         │                                    │
└─────────────────────────┼────────────────────────────────────┘
                          │ HTTP/REST
┌─────────────────────────┼────────────────────────────────────┐
│                         ▼                                    │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              ASP.NET Core Backend                     │   │
│  │  ┌────────────────┐  ┌────────────────────────────┐  │   │
│  │  │  Schema API    │  │     CRUD API               │  │   │
│  │  │  /rf/api/      │  │     /rf/api/crud           │  │   │
│  │  │  schema        │  │     /rf/api/sanity_check   │  │   │
│  │  └────────────────┘  └────────────────────────────┘  │   │
│  │           │                                           │   │
│  │           ▼                                           │   │
│  │  ┌────────────────────────────────────────────────┐  │   │
│  │  │     C# Entity Configurations                   │  │   │
│  │  │     EntityConfigurationBuilder<T>              │  │   │
│  │  │     Field Attributes                           │  │   │
│  │  └────────────────────────────────────────────────┘  │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

## Backend API

### Schema API
- `ReflectiveForms.Core/Schema/Models/EntitySchema.cs` - JSON schema models
- `ReflectiveForms.Core/Schema/EntitySchemaGenerator.cs` - Converts C# attributes to JSON
- `ReflectiveForms.Core/Endpoints/Mapped/Api/SchemaEndpoint.cs` - `/rf/api/schema` endpoint

### CRUD API
- `ReflectiveForms.Core/Endpoints/Mapped/Api/Crud.cs` - `/rf/api/crud` endpoint
- `ReflectiveForms.Core/Endpoints/Mapped/Api/SanityCheck.cs` - `/rf/api/sanity_check` endpoint
- `ReflectiveForms.Core/Endpoints/Mapped/Api/EntityLockControl.cs` - `/rf/api/entity_lock_control` endpoint

## React Frontend

Complete React + TypeScript frontend with:

### Components
- **Admin Layout** - Responsive sidebar navigation with entity type listing
- **Dashboard** - Entity type overview with quick actions
- **Entity List** - CRUD operations with search/delete
- **Entity Edit** - Dynamic form rendering from schema

### Field Types
- Text, TextArea, Select, Checkbox, Number, Date
- Relation (dropdown with entity search)
- Group (collapsible field groups)
- Repeater (dynamic lists)
- Media (file upload with preview)
- WYSIWYG (rich text editor)

### Features
- **Entity Locking** - Pessimistic locking with auto-refresh (`useEntityLock` hook)
- **Auto-save** - Debounced form auto-save
- **Error Boundaries** - Graceful error handling with retry
- **Zod Validation** - Schema-driven form validation
- **DOMPurify** - HTML sanitization for security

## Testing Infrastructure

### Unit Tests (Vitest + React Testing Library)
- API client tests
- Component tests for all field types
- Hook tests for entity operations and locking
- Library tests (conditionParser, schemaToZod, sanitize)

### E2E Tests (Playwright)
- CRUD operations
- All field types
- Repeater operations
- Entity locking scenarios
- Auto-save & validation
- Conditional field visibility
- API verification & CORS

See `INTEGRATION_TESTING.md` for detailed testing guide.

## Getting Started

### Backend
```bash
cd ReflectiveForms.Sample1
dotnet run
# Server runs at http://localhost:9000
```

### Frontend
```bash
cd ReflectiveForms.Frontend
npm install
npm run dev
# Server runs at http://localhost:5173
```

## Future Enhancements

1. **Performance** - Lazy loading for large forms, React.memo optimization, virtual scrolling
2. **Accessibility** - WCAG 2.1 compliance, aria-labels, screen reader testing
3. **Advanced Features** - WebSocket collaboration, offline support, field plugin system
4. **Testing** - Increase coverage to 90%+, visual regression tests
