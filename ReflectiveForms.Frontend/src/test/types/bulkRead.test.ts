import { describe, it, expect } from 'vitest';
import type { BulkReadSource, BulkReadResult, BulkReadResponse, BulkReadResultRow } from '../../types/schema';

describe('BulkRead Types', () => {
  it('BulkReadSource can have entity only', () => {
    const source: BulkReadSource = { entity: 'employee' };
    expect(source.entity).toBe('employee');
    expect(source.fields).toBeUndefined();
  });

  it('BulkReadSource can have entity and fields', () => {
    const source: BulkReadSource = { entity: 'employee', fields: ['name', 'email'] };
    expect(source.fields).toEqual(['name', 'email']);
  });

  it('BulkReadResultRow has id and fields', () => {
    const row: BulkReadResultRow = { id: 1, fields: { name: 'Alice', email: 'alice@co.com' } };
    expect(row.id).toBe(1);
    expect(row.fields.name).toBe('Alice');
  });

  it('BulkReadResult has entity, total_count, and rows', () => {
    const result: BulkReadResult = {
      entity: 'employee',
      total_count: 2,
      rows: [
        { id: 1, fields: { name: 'Alice' } },
        { id: 2, fields: { name: 'Bob' } },
      ],
    };
    expect(result.entity).toBe('employee');
    expect(result.total_count).toBe(2);
    expect(result.rows).toHaveLength(2);
  });

  it('BulkReadResponse has results and unauthorized arrays', () => {
    const response: BulkReadResponse = {
      results: [
        { entity: 'employee', total_count: 1, rows: [{ id: 1, fields: { name: 'Alice' } }] },
      ],
      unauthorized: ['salary_band'],
    };
    expect(response.results).toHaveLength(1);
    expect(response.unauthorized).toEqual(['salary_band']);
  });

  it('BulkReadResponse can have empty results and unauthorized', () => {
    const response: BulkReadResponse = { results: [], unauthorized: [] };
    expect(response.results).toEqual([]);
    expect(response.unauthorized).toEqual([]);
  });

  // ── Entity Added Scenario ────────────────────────────────────────────

  it('entity added increases result count', () => {
    const before: BulkReadResponse = {
      results: [{ entity: 'employee', total_count: 2, rows: [{ id: 1, fields: {} }, { id: 2, fields: {} }] }],
      unauthorized: [],
    };
    const after: BulkReadResponse = {
      results: [{ entity: 'employee', total_count: 3, rows: [{ id: 1, fields: {} }, { id: 2, fields: {} }, { id: 3, fields: {} }] }],
      unauthorized: [],
    };
    expect(after.results[0].total_count).toBe(before.results[0].total_count + 1);
    expect(after.results[0].rows).toHaveLength(3);
  });

  // ── Entity Removed Scenario ──────────────────────────────────────────

  it('entity removed decreases result count', () => {
    const before: BulkReadResponse = {
      results: [{ entity: 'employee', total_count: 3, rows: [{ id: 1, fields: {} }, { id: 2, fields: {} }, { id: 3, fields: {} }] }],
      unauthorized: [],
    };
    const after: BulkReadResponse = {
      results: [{ entity: 'employee', total_count: 2, rows: [{ id: 1, fields: {} }, { id: 3, fields: {} }] }],
      unauthorized: [],
    };
    expect(after.results[0].total_count).toBe(before.results[0].total_count - 1);
    // id=2 removed
    expect(after.results[0].rows.find(r => r.id === 2)).toBeUndefined();
  });

  // ── Entity Type Deleted Scenario ─────────────────────────────────────

  it('entity type deleted removes it from results entirely', () => {
    const before: BulkReadResponse = {
      results: [
        { entity: 'employee', total_count: 2, rows: [{ id: 1, fields: {} }, { id: 2, fields: {} }] },
        { entity: 'contractor', total_count: 1, rows: [{ id: 10, fields: {} }] },
      ],
      unauthorized: [],
    };
    const after: BulkReadResponse = {
      results: [
        { entity: 'employee', total_count: 2, rows: [{ id: 1, fields: {} }, { id: 2, fields: {} }] },
        // contractor no longer exists in the system
      ],
      unauthorized: [],
    };
    expect(before.results).toHaveLength(2);
    expect(after.results).toHaveLength(1);
    expect(after.results.find(r => r.entity === 'contractor')).toBeUndefined();
  });

  // ── Entity Field Removed Scenario ────────────────────────────────────

  it('entity field removed results in rows without that field', () => {
    const before: BulkReadResult = {
      entity: 'employee',
      total_count: 2,
      rows: [
        { id: 1, fields: { name: 'Alice', email: 'alice@co.com' } },
        { id: 2, fields: { name: 'Bob', email: 'bob@co.com' } },
      ],
    };
    const after: BulkReadResult = {
      entity: 'employee',
      total_count: 2,
      rows: [
        { id: 1, fields: { name: 'Alice' } },  // email field removed from schema
        { id: 2, fields: { name: 'Bob' } },
      ],
    };
    // Rows still exist, same count
    expect(after.total_count).toBe(before.total_count);
    expect(after.rows).toHaveLength(before.rows.length);
    // But email field is missing
    expect(after.rows[0].fields.email).toBeUndefined();
    expect(after.rows[0].fields.name).toBeDefined();
  });

  // ── Permission Changes Scenario ──────────────────────────────────────

  it('permission revoked moves entity from results to unauthorized', () => {
    const before: BulkReadResponse = {
      results: [
        { entity: 'employee', total_count: 2, rows: [{ id: 1, fields: {} }, { id: 2, fields: {} }] },
        { entity: 'salary', total_count: 1, rows: [{ id: 1, fields: {} }] },
      ],
      unauthorized: [],
    };
    const after: BulkReadResponse = {
      results: [
        { entity: 'employee', total_count: 2, rows: [{ id: 1, fields: {} }, { id: 2, fields: {} }] },
      ],
      unauthorized: ['salary'],
    };
    expect(before.results.find(r => r.entity === 'salary')).toBeDefined();
    expect(after.results.find(r => r.entity === 'salary')).toBeUndefined();
    expect(after.unauthorized).toContain('salary');
  });

  it('permission granted moves entity from unauthorized to results', () => {
    const before: BulkReadResponse = {
      results: [
        { entity: 'employee', total_count: 2, rows: [{ id: 1, fields: {} }, { id: 2, fields: {} }] },
      ],
      unauthorized: ['department'],
    };
    const after: BulkReadResponse = {
      results: [
        { entity: 'employee', total_count: 2, rows: [{ id: 1, fields: {} }, { id: 2, fields: {} }] },
        { entity: 'department', total_count: 3, rows: [{ id: 1, fields: {} }, { id: 2, fields: {} }, { id: 3, fields: {} }] },
      ],
      unauthorized: [],
    };
    expect(before.unauthorized).toContain('department');
    expect(after.unauthorized).not.toContain('department');
    expect(after.results.find(r => r.entity === 'department')).toBeDefined();
  });
});
