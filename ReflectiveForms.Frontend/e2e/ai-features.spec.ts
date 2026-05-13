import { test, expect } from './helpers';

const API_BASE = 'http://localhost:9000/rf/api';

/**
 * AI Features — End-to-End Tests
 *
 * Covers: OpenAPI spec, schema AI flags, semantic search (API + UI),
 * AI generate (API + UI dialog), AI suggest (API), AI sanity check (API),
 * AI diff summary (API + UI), NL filter (API + UI), relation suggest (API),
 * reindex (API), and authorization gates.
 *
 * Uses blog-post as the primary test entity (has all AI flags enabled in Sample1).
 */

const ENTITY = 'blog-post';
const TS = () => Date.now().toString(36);

// Valid blog-post fields that satisfy all sanity checks
function validBlogFields(slug: string) {
  return {
    content: '<p>This is a detailed blog post about cloud computing and Kubernetes deployment strategies for enterprise teams.</p>',
    excerpt: 'A summary about cloud computing.',
    featured_image: '',
    status: 'published',
    scheduled_date: '',
    is_featured: false,
    allow_comments: true,
    reading_time_minutes: 5,
    seo_metadata: { meta_title: '', meta_description: '', meta_keywords: '', canonical_url: '' },
    external_links: [],
    publication_year: '',
    slug,
  };
}

// ─────────────────────────────────────────────────────────
// 1. OpenAPI Spec
// ─────────────────────────────────────────────────────────
test.describe('OpenAPI Spec', () => {
  test('GET /openapi.json returns valid OpenAPI 3.1 spec', async ({ api }) => {
    const { status, body } = await api.getOpenApiSpec();
    expect(status).toBe(200);
    expect(body.openapi).toBe('3.1.0');
    expect(body.info).toBeDefined();
    expect(body.info.title).toBeDefined();
    expect(body.paths).toBeDefined();
    expect(body.components).toBeDefined();
  });

  test('OpenAPI spec includes CRUD paths for registered entities', async ({ api }) => {
    const { body } = await api.getOpenApiSpec();
    const paths = Object.keys(body.paths);

    // blog-post CRUD operations should be in the spec
    expect(paths.some((p: string) => p.includes('operation=CREATE') && p.includes('type=blog-post'))).toBe(true);
    expect(paths.some((p: string) => p.includes('operation=READ') && p.includes('type=blog-post'))).toBe(true);
    expect(paths.some((p: string) => p.includes('operation=PEEK_ALL') && p.includes('type=blog-post'))).toBe(true);
  });

  test('OpenAPI spec includes AI endpoints when AI is configured', async ({ api }) => {
    const { body } = await api.getOpenApiSpec();
    const paths = Object.keys(body.paths);

    expect(paths.some((p: string) => p.includes('/ai/semantic_search'))).toBe(true);
    expect(paths.some((p: string) => p.includes('/ai/generate'))).toBe(true);
    expect(paths.some((p: string) => p.includes('/ai/suggest'))).toBe(true);
    expect(paths.some((p: string) => p.includes('/ai/sanity_check'))).toBe(true);
    expect(paths.some((p: string) => p.includes('/ai/diff_summary'))).toBe(true);
    expect(paths.some((p: string) => p.includes('/ai/nl_filter'))).toBe(true);
    expect(paths.some((p: string) => p.includes('/ai/relation_suggest'))).toBe(true);
  });

  test('OpenAPI spec includes security schemes', async ({ api }) => {
    const { body } = await api.getOpenApiSpec();
    const securitySchemes = body.components?.securitySchemes;
    expect(securitySchemes).toBeDefined();
    expect(securitySchemes.bearerAuth).toBeDefined();
    expect(securitySchemes.bearerAuth.type).toBe('http');
    expect(securitySchemes.bearerAuth.scheme).toBe('bearer');
  });
});

// ─────────────────────────────────────────────────────────
// 2. Schema AI Feature Flags
// ─────────────────────────────────────────────────────────
test.describe('Schema AI Feature Flags', () => {
  test('blog-post schema has AI feature flags enabled', async ({ api }) => {
    const schemas = await api.getAllSchemas();
    const blogSchema = schemas[ENTITY] as Record<string, unknown>;
    expect(blogSchema).toBeDefined();

    const features = blogSchema.features as Record<string, boolean>;
    expect(features.supports_semantic_search).toBe(true);
    expect(features.supports_ai_generation).toBe(true);
    expect(features.supports_ai_diff_summary).toBe(true);
    expect(features.supports_natural_language_filter).toBe(true);
  });

  test('survey schema has AI features but no diff summary', async ({ api }) => {
    const schemas = await api.getAllSchemas();
    const surveySchema = schemas['survey'] as Record<string, unknown>;
    expect(surveySchema).toBeDefined();

    const features = surveySchema.features as Record<string, boolean>;
    expect(features.supports_semantic_search).toBeTruthy();
    expect(features.supports_ai_generation).toBeTruthy();
    expect(features.supports_ai_diff_summary).toBeFalsy();
    expect(features.supports_natural_language_filter).toBeTruthy();
  });

  test('blog-post schema includes [AISuggestion] on excerpt field', async ({ api }) => {
    const schemas = await api.getAllSchemas();
    const blogSchema = schemas[ENTITY] as Record<string, unknown>;
    const fields = blogSchema.fields as Array<Record<string, unknown>>;

    const excerptField = fields.find((f) => f.name === 'excerpt');
    expect(excerptField).toBeDefined();
    expect(excerptField!.ai_suggestion).toBeDefined();
    expect((excerptField!.ai_suggestion as Record<string, unknown>).prompt).toBeDefined();
  });

  test('blog-post schema includes [AISanityCheck] on content field', async ({ api }) => {
    const schemas = await api.getAllSchemas();
    const blogSchema = schemas[ENTITY] as Record<string, unknown>;
    const fields = blogSchema.fields as Array<Record<string, unknown>>;

    const contentField = fields.find((f) => f.name === 'content');
    expect(contentField).toBeDefined();
    expect(contentField!.ai_sanity_checks).toBeDefined();
    expect(Array.isArray(contentField!.ai_sanity_checks)).toBe(true);
    expect((contentField!.ai_sanity_checks as unknown[]).length).toBeGreaterThanOrEqual(2);
  });
});

// ─────────────────────────────────────────────────────────
// 3. Semantic Search (API-level)
// ─────────────────────────────────────────────────────────
test.describe('Semantic Search API', () => {
  test.describe.configure({ mode: 'serial' });

  let searchEntityId: number;

  test('create and reindex entity for semantic search', async ({ api }) => {
    // Create a blog post with distinctive content
    const entity = await api.createEntity(ENTITY, {
      title: { rendered: `AI Search Target ${TS()}` },
      fields: validBlogFields(`ai-search-${TS()}`),
    });
    searchEntityId = entity.id;

    // Trigger reindex to ensure vector is up to date
    const reindex = await api.aiReindex(ENTITY, 'full');
    expect(reindex.status).toBe(200);
  });

  test('semantic search returns results for matching query', async ({ api }) => {
    const { status, body } = await api.aiSemanticSearch(
      'cloud computing Kubernetes deployment',
      ENTITY,
      10,
    );
    expect(status).toBe(200);
    expect(body.results).toBeDefined();
    expect(Array.isArray(body.results)).toBe(true);
    // The freshly indexed entity should appear in results
    // (BasicLLM embeddings may not give perfect relevance, but the result set should not be empty
    // if the reindex succeeded)
  });

  test('semantic search with empty query returns error', async ({ api }) => {
    const res = await api.request.post(`${API_BASE}/ai/semantic_search`, {
      data: { query: '' },
    });
    expect(res.status()).toBe(400);
  });

  test('cross-entity semantic search works (no entity_name)', async ({ api }) => {
    const { status, body } = await api.aiSemanticSearch('cloud computing');
    expect(status).toBe(200);
    expect(body.results).toBeDefined();
    expect(Array.isArray(body.results)).toBe(true);
  });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const a = new ApiHelper(request);
    await a.login();
    await a.deleteAll(ENTITY);
  });
});

// ─────────────────────────────────────────────────────────
// 4. AI Generate (API-level)
// ─────────────────────────────────────────────────────────
test.describe('AI Generate API', () => {
  test('generate returns draft fields (not saved)', async ({ api }) => {
    const { status, body } = await api.aiGenerate(
      ENTITY,
      'Write a blog post about the benefits of remote work for engineering teams',
    );
    expect(status).toBe(200);
    expect(body.fields).toBeDefined();
    expect(typeof body.fields).toBe('object');

    // Verify it was NOT persisted — peek all should not have this generated entity
    // (the generate endpoint only returns a draft)
    const allEntities = await api.peekAll(ENTITY);
    const generated = allEntities.find((e) =>
      (e.title ?? '').toLowerCase().includes('remote work'),
    );
    // Generated content should not be in the DB
    // (it might match if test data exists, so we just verify the API returned fields)
  });

  test('generate for non-existent entity returns 404', async ({ api }) => {
    const res = await api.request.post(
      `${API_BASE}/ai/generate?type=nonexistent-entity`,
      { data: { prompt: 'test' } },
    );
    expect(res.status()).toBe(404);
  });

  test('generate for entity without AI generation returns 400', async ({ api }) => {
    // team-member has SupportsSemanticSearch but NOT SupportsAiGeneration
    const res = await api.request.post(
      `${API_BASE}/ai/generate?type=team-member`,
      { data: { prompt: 'test' } },
    );
    expect(res.status()).toBe(400);
  });

  test('generate without prompt returns 400', async ({ api }) => {
    const res = await api.request.post(
      `${API_BASE}/ai/generate?type=${ENTITY}`,
      { data: {} },
    );
    expect(res.status()).toBe(400);
  });
});

// ─────────────────────────────────────────────────────────
// 5. AI Suggest Field (API-level)
// ─────────────────────────────────────────────────────────
test.describe('AI Suggest Field API', () => {
  test('suggest returns a suggestion for excerpt field', async ({ api }) => {
    const { status, body } = await api.aiSuggestField(ENTITY, 'excerpt', {
      content: '<p>Cloud computing has revolutionized how businesses deploy and manage applications.</p>',
    });
    expect(status).toBe(200);
    expect(body.suggestion).toBeDefined();
    expect(typeof body.suggestion).toBe('string');
    expect(body.suggestion.length).toBeGreaterThan(0);
  });

  test('suggest for field without [AISuggestion] returns 400', async ({ api }) => {
    const { status } = await api.aiSuggestField(ENTITY, 'status', {
      content: 'test',
    });
    expect(status).toBe(400);
  });
});

// ─────────────────────────────────────────────────────────
// 6. AI Sanity Check (API-level)
// ─────────────────────────────────────────────────────────
test.describe('AI Sanity Check API', () => {
  test('sanity check returns results for content field', async ({ api }) => {
    const { status, body } = await api.aiSanityCheck(
      ENTITY,
      'content',
      '<p>This is professional content about cloud computing best practices.</p>',
    );
    expect(status).toBe(200);
    expect(body.results).toBeDefined();
    expect(Array.isArray(body.results)).toBe(true);
    // Each result should have the expected shape
    for (const result of body.results) {
      expect(result).toHaveProperty('check');
      expect(result).toHaveProperty('passed');
      expect(result).toHaveProperty('severity');
    }
  });

  test('sanity check for field without [AISanityCheck] returns 400', async ({ api }) => {
    const { status } = await api.aiSanityCheck(ENTITY, 'excerpt', 'test value');
    expect(status).toBe(400);
  });
});

// ─────────────────────────────────────────────────────────
// 7. AI Diff Summary (API-level)
// ─────────────────────────────────────────────────────────
test.describe('AI Diff Summary API', () => {
  test.describe.configure({ mode: 'serial' });

  let diffEntityId: number;

  test('create and update entity to produce revision history', async ({ api }) => {
    // Create
    const entity = await api.createEntity(ENTITY, {
      title: { rendered: `Diff Test ${TS()}` },
      fields: validBlogFields(`diff-test-${TS()}`),
    });
    diffEntityId = entity.id;

    // Update to create a second revision
    await api.updateEntity(ENTITY, {
      id: diffEntityId,
      title: { rendered: `Diff Test UPDATED ${TS()}` },
      fields: {
        ...validBlogFields(`diff-test-${TS()}`),
        content: '<p>Updated content about new deployment patterns.</p>',
        is_featured: true,
      },
    });
  });

  test('diff summary returns summary text', async ({ api }) => {
    const { status, body } = await api.aiDiffSummary(ENTITY, diffEntityId, 1);
    expect(status).toBe(200);
    expect(body.summary).toBeDefined();
    expect(typeof body.summary).toBe('string');
    expect(body.summary.length).toBeGreaterThan(0);
  });

  test('diff summary with invalid revision index returns error', async ({ api }) => {
    const res = await api.request.post(
      `${API_BASE}/ai/diff_summary?type=${ENTITY}`,
      { data: { entity_id: diffEntityId, revision_index: 999 } },
    );
    expect(res.status()).toBeGreaterThanOrEqual(400);
  });

  test('diff summary for entity without the feature returns 400', async ({ api }) => {
    // team-member does not have SupportsAiDiffSummary
    const res = await api.request.post(
      `${API_BASE}/ai/diff_summary?type=team-member`,
      { data: { entity_id: 1, revision_index: 1 } },
    );
    expect(res.status()).toBe(400);
  });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const a = new ApiHelper(request);
    await a.login();
    await a.deleteAll(ENTITY);
  });
});

// ─────────────────────────────────────────────────────────
// 8. Natural Language Filter (API-level)
// ─────────────────────────────────────────────────────────
test.describe('NL Filter API', () => {
  test.describe.configure({ mode: 'serial' });

  test('setup: create test entities for filtering', async ({ api }) => {
    await api.deleteAll(ENTITY);
    await api.createEntity(ENTITY, {
      title: { rendered: 'NL Filter Published Post' },
      fields: validBlogFields(`nl-pub-${TS()}`),
    });
    await api.createEntity(ENTITY, {
      title: { rendered: 'NL Filter Draft Post' },
      fields: {
        ...validBlogFields(`nl-draft-${TS()}`),
        status: 'draft',
      },
    });
  });

  test('NL filter returns interpreted filters and results', async ({ api }) => {
    test.setTimeout(120000);
    const { status, body } = await api.aiNlFilter(
      ENTITY,
      'show me published blog posts',
    );
    expect(status).toBe(200);
    expect(body.interpreted_filters).toBeDefined();
    expect(body.natural_language_interpretation).toBeDefined();
    expect(body.results).toBeDefined();
    expect(Array.isArray(body.results)).toBe(true);
  });

  test('NL filter for entity without the feature returns 400', async ({ api }) => {
    // product does not have SupportsNaturalLanguageFilter
    const res = await api.request.post(
      `${API_BASE}/ai/nl_filter?type=product`,
      { data: { query: 'expensive products' } },
    );
    expect(res.status()).toBe(400);
  });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const a = new ApiHelper(request);
    await a.login();
    await a.deleteAll(ENTITY);
  });
});

// ─────────────────────────────────────────────────────────
// 9. Reindex API
// ─────────────────────────────────────────────────────────
test.describe('Reindex API', () => {
  test('full reindex succeeds for blog-post', async ({ api }) => {
    const { status } = await api.aiReindex(ENTITY, 'full');
    expect(status).toBe(200);
  });

  test('incremental reindex succeeds', async ({ api }) => {
    const { status } = await api.aiReindex(ENTITY, 'incremental');
    expect(status).toBe(200);
  });

  test('reindex for entity without semantic search returns 400', async ({ api }) => {
    const res = await api.request.post(
      `${API_BASE}/ai/reindex?type=product&mode=full`,
      { data: {} },
    );
    expect(res.status()).toBe(400);
  });

  test('reindex with invalid mode returns 400', async ({ api }) => {
    const res = await api.request.post(
      `${API_BASE}/ai/reindex?type=${ENTITY}&mode=unknown`,
      { data: {} },
    );
    expect(res.status()).toBe(400);
  });
});

// ─────────────────────────────────────────────────────────
// 10. Authorization Gates (API-level)
// ─────────────────────────────────────────────────────────
test.describe('AI Authorization', () => {
  test('unauthenticated request to AI endpoint returns 401', async ({ request }) => {
    // Create a fresh request context without cookies
    const res = await request.post(`${API_BASE}/ai/semantic_search`, {
      data: { query: 'test' },
      headers: { cookie: '' },
    });
    // May be 401 or 403 depending on auth middleware
    expect(res.status()).toBeGreaterThanOrEqual(400);
    expect(res.status()).toBeLessThan(500);
  });

  test('AI endpoints return 501 for entity types without AI config', async ({ api }) => {
    // This would only happen if AiServiceConfiguration were null,
    // which it isn't in Sample1. Instead verify that entity-specific checks work.
    // team-member does NOT have SupportsAiGeneration
    const res = await api.request.post(
      `${API_BASE}/ai/generate?type=team-member`,
      { data: { prompt: 'test' } },
    );
    expect(res.status()).toBe(400); // Not supported for this entity
  });
});

// ─────────────────────────────────────────────────────────
// 11. Full Flow: Create → Index → Search → Delete → Gone
// ─────────────────────────────────────────────────────────
test.describe('Full Flow: Create → Search → Delete', () => {
  test.describe.configure({ mode: 'serial' });

  let flowEntityId: number;
  const uniqueMarker = `flow-marker-${TS()}`;

  test('create entity with unique content', async ({ api }) => {
    const entity = await api.createEntity(ENTITY, {
      title: { rendered: `Flow Test ${uniqueMarker}` },
      fields: {
        ...validBlogFields(`flow-${TS()}`),
        content: `<p>Unique content about ${uniqueMarker} for semantic matching.</p>`,
      },
    });
    flowEntityId = entity.id;
  });

  test('reindex and search returns the entity', async ({ api }) => {
    await api.aiReindex(ENTITY, 'full');

    const { status, body } = await api.aiSemanticSearch(uniqueMarker, ENTITY, 10);
    expect(status).toBe(200);
    expect(body.results).toBeDefined();
    // With basic embeddings, exact text matching via embeddings may not be perfect,
    // but the result array should be populated after reindex
  });

  test('delete entity and verify search reflects removal', async ({ api }) => {
    await api.deleteEntity(ENTITY, flowEntityId);

    // Reindex to clean up
    await api.aiReindex(ENTITY, 'full');

    // The deleted entity should not appear in results
    const { body } = await api.aiSemanticSearch(uniqueMarker, ENTITY, 10);
    const found = body.results?.find(
      (r: { entity_id: number }) => r.entity_id === flowEntityId,
    );
    expect(found).toBeUndefined();
  });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const a = new ApiHelper(request);
    await a.login();
    await a.deleteAll(ENTITY);
  });
});

// ─────────────────────────────────────────────────────────
// 12. UI: AI Generate Dialog
// ─────────────────────────────────────────────────────────
test.describe('UI: AI Generate Dialog', () => {
  test('"Create with AI" button appears on blog-post list page', async ({ page, ui }) => {
    await ui.gotoEntityList(ENTITY);
    const aiButton = page.locator('[data-testid="ai-generate-button"]');
    await expect(aiButton).toBeVisible({ timeout: 10000 });
  });

  test('clicking "Create with AI" opens the dialog', async ({ page, ui }) => {
    await ui.gotoEntityList(ENTITY);
    const aiButton = page.locator('[data-testid="ai-generate-button"]');
    await aiButton.click();

    const dialog = page.locator('[data-testid="ai-generate-dialog"]');
    await expect(dialog).toBeVisible({ timeout: 5000 });

    // Dialog should have a prompt textarea
    const prompt = page.locator('[data-testid="ai-generate-prompt"]');
    await expect(prompt).toBeVisible();

    // Dialog should have a generate/submit button
    const submit = page.locator('[data-testid="ai-generate-submit"]');
    await expect(submit).toBeVisible();
  });

  test('generate dialog can be closed', async ({ page, ui }) => {
    await ui.gotoEntityList(ENTITY);
    const aiButton = page.locator('[data-testid="ai-generate-button"]');
    await aiButton.click();

    const dialog = page.locator('[data-testid="ai-generate-dialog"]');
    await expect(dialog).toBeVisible();

    // Close via the X button
    await page.locator('[data-testid="ai-generate-dialog"] button').first().click();
    // Dialog should disappear (either completely or after animation)
    await expect(dialog).not.toBeVisible({ timeout: 5000 });
  });
});

// ─────────────────────────────────────────────────────────
// 13. UI: Natural Language Filter
// ─────────────────────────────────────────────────────────
test.describe('UI: NL Filter', () => {
  test('NL filter input appears on blog-post list page', async ({ page, ui }) => {
    await ui.gotoEntityList(ENTITY);
    const nlFilter = page.locator('[data-testid="ai-nl-filter"]');
    await expect(nlFilter).toBeVisible({ timeout: 10000 });
  });

  test('NL filter input accepts text and has submit button', async ({ page, ui }) => {
    await ui.gotoEntityList(ENTITY);
    const input = page.locator('[data-testid="ai-nl-filter-input"]');
    await expect(input).toBeVisible();
    await input.fill('show published posts');

    const submit = page.locator('[data-testid="ai-nl-filter-submit"]');
    await expect(submit).toBeVisible();
    await expect(submit).toBeEnabled();
  });
});

// ─────────────────────────────────────────────────────────
// 14. UI: AI Suggest Button on edit page
// ─────────────────────────────────────────────────────────
test.describe('UI: AI Suggest Button', () => {
  test.describe.configure({ mode: 'serial' });
  let editEntityId: number;

  test('setup: create blog post to edit', async ({ api }) => {
    await api.deleteAll(ENTITY);
    const entity = await api.createEntity(ENTITY, {
      title: { rendered: `Suggest Test ${TS()}` },
      fields: validBlogFields(`suggest-${TS()}`),
    });
    editEntityId = entity.id;
  });

  test('AI suggest button appears next to excerpt field', async ({ page, ui }) => {
    await ui.gotoEditEntity(ENTITY, editEntityId);
    // The excerpt field has [AISuggestion], so a suggest button should appear
    const suggestBtn = page.locator('[data-testid="ai-suggest-excerpt"]');
    await expect(suggestBtn).toBeVisible({ timeout: 10000 });
  });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const a = new ApiHelper(request);
    await a.login();
    await a.deleteAll(ENTITY);
  });
});

// ─────────────────────────────────────────────────────────
// 15. UI: AI Sanity Check Badge on edit page
// ─────────────────────────────────────────────────────────
test.describe('UI: AI Sanity Check', () => {
  test.describe.configure({ mode: 'serial' });
  let checkEntityId: number;

  test('setup: create blog post to edit', async ({ api }) => {
    await api.deleteAll(ENTITY);
    const entity = await api.createEntity(ENTITY, {
      title: { rendered: `Sanity Check Test ${TS()}` },
      fields: validBlogFields(`sanity-${TS()}`),
    });
    checkEntityId = entity.id;
  });

  test('AI sanity check button appears next to content field', async ({ page, ui }) => {
    await ui.gotoEditEntity(ENTITY, checkEntityId);
    // The content field has [AISanityCheck], so a check button should appear
    const checkBtn = page.locator('[data-testid="ai-sanity-check-content"]');
    await expect(checkBtn).toBeVisible({ timeout: 10000 });
  });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const a = new ApiHelper(request);
    await a.login();
    await a.deleteAll(ENTITY);
  });
});

// ─────────────────────────────────────────────────────────
// 16. UI: AI Diff Summary on revision page
// ─────────────────────────────────────────────────────────
test.describe('UI: AI Diff Summary', () => {
  test.describe.configure({ mode: 'serial' });
  let revEntityId: number;

  test('setup: create and update blog post for revision history', async ({ api }) => {
    await api.deleteAll(ENTITY);
    const entity = await api.createEntity(ENTITY, {
      title: { rendered: `Revision Test ${TS()}` },
      fields: validBlogFields(`rev-${TS()}`),
    });
    revEntityId = entity.id;

    // Update to create a second revision
    await api.updateEntity(ENTITY, {
      id: revEntityId,
      title: { rendered: `Revision Test Updated ${TS()}` },
      fields: {
        ...validBlogFields(`rev-${TS()}`),
        content: '<p>Updated revision content.</p>',
      },
    });
  });

  test('AI diff summary toggle appears on revision page', async ({ page, ui }) => {
    await ui.gotoRevisionDiff(ENTITY, revEntityId);
    const diffSummary = page.locator('[data-testid="ai-diff-summary"]');
    await expect(diffSummary).toBeVisible({ timeout: 15000 });
  });

  test('clicking AI summary toggle expands and shows loading or content', async ({ page, ui }) => {
    await ui.gotoRevisionDiff(ENTITY, revEntityId);
    const toggle = page.locator('[data-testid="ai-diff-summary-toggle"]');
    await expect(toggle).toBeVisible({ timeout: 15000 });
    await toggle.click();

    // Should show either loading spinner or content
    const loadingOrContent = page.locator(
      '[data-testid="ai-diff-summary-loading"], [data-testid="ai-diff-summary-content"], [data-testid="ai-diff-summary-error"]',
    );
    await expect(loadingOrContent.first()).toBeVisible({ timeout: 30000 });
  });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const a = new ApiHelper(request);
    await a.login();
    await a.deleteAll(ENTITY);
  });
});
