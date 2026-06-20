import { test, expect } from './helpers';
import * as fs from 'fs';
import * as path from 'path';
import * as os from 'os';

/**
 * Field Migration Tests (Bug 1)
 *
 * When a C# entity model gains a new property, existing DB rows should
 * have the default value injected on read. These tests verify the API
 * read/update paths return complete entities with all model defaults.
 *
 * The critical test "stale entity gets defaults merged on read" simulates
 * the actual bug scenario: an entity created before a field was added to
 * the model, then read after the model has the new field.
 */

const TS = () => Date.now().toString(36);

const DB_BASE = path.join(os.tmpdir(), 'CrossCloudKit.Database.Basic', 'reflective-forms-tests-1');

function dbFilePath(entityName: string, id: number) {
  return path.join(DB_BASE, entityName, `id_${id}.json`);
}

test.describe('Field Default Merge on Read', () => {
  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('blog-post');
  });

  test('stale entity gets defaults merged on read — simulates field added after entity creation', async ({ api }) => {
    await api.deleteAll('blog-post');

    // 1. Create entity via API — gets all current defaults from PutOneAsync
    const created = await api.createEntity('blog-post', {
      title: { rendered: `Stale-${TS()}` },
      fields: {
        slug: `stale-${TS()}`,
        content: '<p>Content</p>',
        excerpt: '',
        reading_time_minutes: 5,
      },
    });

    // 2. Verify the fields exist initially (put by PutOneAsync)
    const fresh = await api.readEntity('blog-post', created.id);
    expect(fresh.fields).toHaveProperty('is_featured');
    expect(fresh.fields).toHaveProperty('allow_comments');

    // 3. Simulate "old entity": strip fields from DB file directly
    const filePath = dbFilePath('blog-post', created.id);
    expect(fs.existsSync(filePath), `DB file not found: ${filePath}`).toBe(true);

    const raw = JSON.parse(fs.readFileSync(filePath, 'utf-8'));
    delete raw.fields.is_featured;
    delete raw.fields.allow_comments;
    // Also strip some nested fields to test Group nesting
    if (raw.fields.seo_metadata) {
      delete raw.fields.seo_metadata.meta_keywords;
    }
    fs.writeFileSync(filePath, JSON.stringify(raw));

    // 4. Read via API — the merge should inject defaults for stripped fields
    const read = await api.readEntity('blog-post', created.id);

    // 5. Stripped top-level bools should be back with defaults
    expect(read.fields.is_featured).toBe(false);
    expect(read.fields.allow_comments).toBe(false);

    // 6. Stripped nested field should be back
    expect(read.fields.seo_metadata).toHaveProperty('meta_keywords');
    expect(read.fields.seo_metadata.meta_keywords).toBe('');

    // 7. Unstripped fields preserved
    expect(read.fields.content).toBe('<p>Content</p>');
  });

  test('UPDATE preserves defaults for fields not in body', async ({ api }) => {
    await api.deleteAll('blog-post');

    const created = await api.createEntity('blog-post', {
      title: { rendered: `Update-${TS()}` },
      fields: {
        slug: `upd-${TS()}`,
        content: '<p>Original body</p>',
        excerpt: 'Original summary',
        reading_time_minutes: 5,
      },
    });

    // Update only the content — omit is_featured and allow_comments
    await api.updateEntity('blog-post', {
      id: created.id,
      title: { rendered: `Update-${TS()}` },
      fields: {
        slug: `upd-${TS()}`,
        content: '<p>Updated body</p>',
        excerpt: 'Original summary',
        reading_time_minutes: 5,
      },
    });

    const read = await api.readEntity('blog-post', created.id);

    // Default bool fields still present
    expect(read.fields).toHaveProperty('is_featured');
    expect(read.fields).toHaveProperty('allow_comments');
    expect(read.fields.is_featured).toBe(false);
    expect(read.fields.allow_comments).toBe(false);  // C# default for bool is false

    // Updated value persisted
    expect(read.fields.content).toBe('<p>Updated body</p>');
    // Unchanged value preserved
    expect(read.fields.excerpt).toBe('Original summary');
  });
});
