import { describe, it, expect } from 'vitest';

// Test that all expected exports are available from the library entry point
describe('Library Exports', () => {
  it('exports createReflectiveFormsApp', async () => {
    const mod = await import('../../lib/index');
    expect(typeof mod.createReflectiveFormsApp).toBe('function');
  });

  it('exports RfConfigProvider and useRfConfig', async () => {
    const mod = await import('../../lib/index');
    expect(typeof mod.RfConfigProvider).toBe('function');
    expect(typeof mod.useRfConfig).toBe('function');
  });

  it('exports RfRoutes', async () => {
    const mod = await import('../../lib/index');
    expect(typeof mod.RfRoutes).toBe('function');
  });

  it('exports AdminLayout', async () => {
    const mod = await import('../../lib/index');
    expect(typeof mod.AdminLayout).toBe('function');
  });

  it('exports all page components', async () => {
    const mod = await import('../../lib/index');
    expect(typeof mod.DashboardPage).toBe('function');
    expect(typeof mod.EntityListPage).toBe('function');
    expect(typeof mod.EntityEditPage).toBe('function');
    expect(typeof mod.EntityViewPage).toBe('function');
    expect(typeof mod.LoginPage).toBe('function');
    expect(typeof mod.SsoLoginPage).toBe('function');
  });

  it('exports all hooks', async () => {
    const mod = await import('../../lib/index');
    expect(typeof mod.useSchema).toBe('function');
    expect(typeof mod.useAllSchemas).toBe('function');
    expect(typeof mod.useEntity).toBe('function');
    expect(typeof mod.useEntityList).toBe('function');
    expect(typeof mod.useCreateEntity).toBe('function');
    expect(typeof mod.useUpdateEntity).toBe('function');
    expect(typeof mod.useDeleteEntity).toBe('function');
    expect(typeof mod.useSanityCheck).toBe('function');
    expect(typeof mod.useEntityLock).toBe('function');
    expect(typeof mod.useAutoSave).toBe('function');
    expect(typeof mod.useAuth).toBe('function');
    expect(typeof mod.AuthProvider).toBe('function');
  });

  it('exports utility functions', async () => {
    const mod = await import('../../lib/index');
    expect(typeof mod.schemaToZod).toBe('function');
    expect(typeof mod.generateDefaults).toBe('function');
    expect(typeof mod.evaluateCondition).toBe('function');
    expect(typeof mod.evaluateCompoundCondition).toBe('function');
    expect(typeof mod.sanitizeHtml).toBe('function');
    expect(typeof mod.getNestedError).toBe('function');
    expect(typeof mod.getFieldTypes).toBe('function');
  });
});
