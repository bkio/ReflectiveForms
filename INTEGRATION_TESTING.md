# Integration Testing Guide

This document describes how to perform integration testing between the ReflectiveForms backend and the React frontend.

## Prerequisites

1. **.NET 8.0 SDK** installed
2. **Node.js 18+** installed
3. Both projects cloned and dependencies installed

## Setup

### Backend Setup

```bash
cd ReflectiveForms.Sample1
dotnet restore
dotnet build
```

### Frontend Setup

```bash
cd ReflectiveForms.Frontend
npm install
```

## Running Tests

### Unit Tests

#### Backend (C#)
```bash
cd ReflectiveForms.Core.Tests
dotnet test
```

#### Frontend (React)
```bash
cd ReflectiveForms.Frontend
npm run test        # Watch mode
npm run test:run    # Single run
npm run test:coverage  # With coverage
```

### Integration Tests

1. **Start the backend server:**
   ```bash
   cd ReflectiveForms.Sample1
   dotnet run
   # Server runs at http://localhost:9000
   ```

2. **Start the frontend dev server:**
   ```bash
   cd ReflectiveForms.Frontend
   npm run dev
   # Server runs at http://localhost:5173
   ```

3. **Open browser** and navigate to http://localhost:5173

## Test Scenarios

### ✅ Entity CRUD Operations

| Test | Steps | Expected Result |
|------|-------|-----------------|
| List entities | Navigate to entity type | See list of existing entities |
| Create entity | Click "Add New", fill form, wait for auto-save | New entity created with ID |
| Edit entity | Click on existing entity, modify fields | Changes auto-saved |
| Delete entity | Click delete button, confirm | Entity removed from list |
| Clone entity | Click clone button | New entity with copied data |

### ✅ Form Field Types

| Field Type | Test |
|------------|------|
| Text | Enter text, verify saved |
| TextArea | Enter multiline text |
| Select | Choose option, verify saved |
| Checkbox | Toggle, verify saved |
| Number | Enter number, verify min/max |
| Date | Select date, verify format |
| Relation | Select related entity |
| Group | Verify nested fields work |
| Repeater | Add/remove/reorder items |
| Media | Upload image, verify base64 |
| WYSIWYG | Enter rich text |

### ✅ Repeater Operations

| Test | Steps | Expected Result |
|------|-------|-----------------|
| Add item | Click "Add" button | New item appears |
| Remove item | Click trash icon | Item marked as deleted (with undo) |
| Undo remove | Click undo button | Item restored |
| Move up | Click up arrow | Item moves up |
| Move down | Click down arrow | Item moves down |
| Nested repeater | Add item in nested repeater | Nested structure preserved |

### ✅ Auto-Save & Validation

| Test | Steps | Expected Result |
|------|-------|-----------------|
| Auto-save trigger | Modify field | "Your changes will be saved..." message |
| Validation error | Enter invalid data | Error toast displayed |
| Save success | Wait for save | "Changes saved" message |

### ✅ Entity Locking

| Test | Steps | Expected Result |
|------|-------|-----------------|
| Acquire lock | Open entity for editing | Lock acquired |
| Lock conflict | Open same entity in another tab | "Being edited by..." message |
| Lock release | Close edit page | Lock released |
| Inactivity timeout | Wait 10 minutes | Warning dialog |

### ✅ Conditional Fields

| Test | Steps | Expected Result |
|------|-------|-----------------|
| Show on condition | Set field value that matches condition | Dependent field appears |
| Hide on condition | Change field value | Dependent field disappears |

## API Endpoints Verification

Verify these endpoints return correct responses:

| Endpoint | Method | Test |
|----------|--------|------|
| `/rf/api/schema?type=<name>` | GET | Returns JSON schema |
| `/rf/api/schema` | GET | Returns all schemas |
| `/rf/api/crud?operation=READ&type=<name>` | POST | Returns entity data |
| `/rf/api/crud?operation=CREATE&type=<name>` | POST | Creates entity |
| `/rf/api/crud?operation=UPDATE&type=<name>` | POST | Updates entity |
| `/rf/api/crud?operation=DELETE&type=<name>` | POST | Deletes entity |
| `/rf/api/crud?operation=PEEK_ALL&type=<name>` | POST | Lists all entities |
| `/rf/api/sanity_check?type=<name>` | POST | Validates entity data |
| `/rf/api/entity_lock?type=<name>&id=<id>&action=lock` | POST | Acquires lock |
| `/rf/api/entity_lock?type=<name>&id=<id>&action=unlock` | POST | Releases lock |

## CORS Verification

Verify CORS is working:

```bash
# From terminal, test preflight request
curl -X OPTIONS http://localhost:9000/rf/api/schema \
  -H "Origin: http://localhost:5173" \
  -H "Access-Control-Request-Method: GET" \
  -v

# Should see:
# Access-Control-Allow-Origin: http://localhost:5173
# Access-Control-Allow-Credentials: true
```

## Browser DevTools Checks

1. **Network tab**: Verify no CORS errors
2. **Console tab**: No JavaScript errors
3. **Application tab**: Check cookies/storage

## Known Issues

- Nested repeaters with deep nesting (>3 levels) may have performance issues
- WYSIWYG editor currently renders as textarea (basic implementation)
- Media upload limited to 8MB

## Troubleshooting

### CORS Errors
Ensure the backend has CORS configured for `http://localhost:5173`:
```csharp
policy.WithOrigins("http://localhost:5173")
      .AllowAnyMethod()
      .AllowAnyHeader()
      .AllowCredentials();
```

## Automated E2E Tests (Playwright)

Playwright E2E tests have been added. To run:

```bash
cd ReflectiveForms.Frontend

# Install Playwright browsers (first time only)
npx playwright install

# Run all E2E tests
npm run test:e2e

# Run with visual UI
npm run test:e2e:ui

# Run in headed mode (see browser)
npm run test:e2e:headed

# View HTML report
npm run test:e2e:report
```

### E2E Test Files

| File | Description |
|------|-------------|
| `e2e/entity-crud.spec.ts` | Entity create, read, update, delete operations |
| `e2e/form-fields.spec.ts` | All field types (text, select, checkbox, etc.) |
| `e2e/repeater.spec.ts` | Repeater add, remove, reorder operations |
| `e2e/entity-lock.spec.ts` | Entity locking and concurrent edit prevention |
| `e2e/auto-save.spec.ts` | Auto-save triggers and validation errors |
| `e2e/conditional-fields.spec.ts` | Conditional field visibility |

### Running Tests Against Backend

1. Start the backend:
```bash
cd ReflectiveForms.Sample1
dotnet run
```

2. Run E2E tests:
```bash
cd ReflectiveForms.Frontend
npm run test:e2e
```

Example test file (`e2e/entity.spec.ts`):
```typescript
import { test, expect } from '@playwright/test';

test('can create a new entity', async ({ page }) => {
  await page.goto('/');
  await page.click('text=Add New');
  await page.fill('[placeholder="Enter Title:"]', 'Test Entity');
  await page.waitForSelector('text=Your changes will be saved');
  await page.waitForSelector('text=Changes have successfully been saved');
});
```
