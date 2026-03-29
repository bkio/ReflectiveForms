import { test, expect } from './helpers';

/**
 * Full-Stack Data Persistence Integration Tests
 *
 * Verifies that data entered in the frontend UI is correctly persisted
 * through the backend API + database and can be read back with full fidelity.
 *
 * Each test creates an entity via UI, reads it back via the API,
 * then reloads the edit page and verifies all field values match.
 */

const TS = () => Date.now().toString(36);

// ══════════════════════════════════════════════════════════════
// Blog Post — round-trip every field type
// ══════════════════════════════════════════════════════════════
test.describe('Data Persistence: Blog Post round-trip', () => {
  let entityId: number;
  const slug = `persist-test-${TS()}`;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('blog-post');
  });

  test('create blog post via UI with every field populated', async ({ page, ui, api }) => {
    await api.deleteAll('blog-post');
    await ui.gotoNewEntity('blog-post');

    const title = `Persistence Blog ${TS()}`;
    await ui.fillTitle(title);
    await ui.fillWysiwyg('Post Content', '<h2>Hello</h2><p>World</p>');
    await ui.fillTextArea('Excerpt', 'An excerpt for persistence testing.');
    await ui.selectOption('Post Status', 'published');
    await ui.setCheckbox('Featured Post', true);
    await ui.setCheckbox('Allow Comments', false);
    await ui.fillNumber('Estimated Reading Time (minutes)', '12');

    // SEO Group
    await ui.fillTextField('Meta Title', 'Persistence SEO Title');
    await ui.fillTextArea('Meta Description', 'Persistence SEO Desc');
    await ui.fillTextField('Meta Keywords', 'persist, roundtrip');
    await ui.fillTextField('Canonical URL', 'https://example.com/canonical');

    // Repeater — add 1 link
    await ui.addRepeaterItem('External Links');
    await ui.fillTextField('Link Title', 'Round Trip Link');
    await ui.fillTextField('URL', 'https://roundtrip.dev');

    // Slug
    await ui.fillTextField('URL Slug', slug);

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll('blog-post');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('Persistence Blog'));
    expect(created).toBeDefined();
    entityId = created!.id;
  });

  test('API read-back matches every field', async ({ api }) => {
    const entity = await api.readEntity('blog-post', entityId);

    expect(entity.title.rendered).toContain('Persistence Blog');
    expect(entity.fields.content).toContain('Hello');
    expect(entity.fields.excerpt).toBe('An excerpt for persistence testing.');
    expect(entity.fields.status).toBe('published');
    expect(entity.fields.is_featured).toBe(true);
    expect(entity.fields.allow_comments).toBe(false);
    expect(Number(entity.fields.reading_time_minutes)).toBe(12);
    expect(entity.fields.seo_metadata.meta_title).toBe('Persistence SEO Title');
    expect(entity.fields.seo_metadata.meta_description).toBe('Persistence SEO Desc');
    expect(entity.fields.seo_metadata.meta_keywords).toBe('persist, roundtrip');
    expect(entity.fields.seo_metadata.canonical_url).toBe('https://example.com/canonical');
    expect(entity.fields.slug).toBe(slug);
    expect(entity.fields.external_links).toHaveLength(1);
    expect(entity.fields.external_links[0].link_title).toBe('Round Trip Link');
    expect(entity.fields.external_links[0].link_url).toBe('https://roundtrip.dev');
  });

  test('UI reload preserves all values in the form', async ({ page, ui }) => {
    await ui.gotoEditEntity('blog-post', entityId);

    // Title
    const titleValue = await ui.getTitle();
    expect(titleValue).toContain('Persistence Blog');

    // Select — status
    const statusField = page.locator('.field-wrapper, .bg-white.rounded-lg.shadow-sm')
      .filter({ has: page.locator('label', { hasText: 'Post Status' }) })
      .locator('button[aria-haspopup="listbox"]');
    await expect(statusField).toHaveAttribute('data-value', 'published');

    // Checkbox — Featured Post should be checked
    const featuredCb = page.locator('.field-wrapper, .bg-white.rounded-lg.shadow-sm')
      .filter({ has: page.locator('label', { hasText: 'Featured Post' }) })
      .locator('input[type="checkbox"]');
    await expect(featuredCb).toBeChecked();

    // Checkbox — Allow Comments should NOT be checked
    const commentCb = page.locator('.field-wrapper, .bg-white.rounded-lg.shadow-sm')
      .filter({ has: page.locator('label', { hasText: 'Allow Comments' }) })
      .locator('input[type="checkbox"]');
    await expect(commentCb).not.toBeChecked();

    // Number
    const readingTime = page.locator('.field-wrapper, .bg-white.rounded-lg.shadow-sm')
      .filter({ has: page.locator('label', { hasText: 'Estimated Reading Time' }) })
      .locator('input[type="number"]');
    await expect(readingTime).toHaveValue('12');

    // Group — SEO fields
    const metaTitle = page.locator('label', { hasText: 'Meta Title' }).first()
      .locator('xpath=ancestor::div[contains(@class,"field-wrapper")][1]')
      .locator('input[type="text"]');
    await expect(metaTitle).toHaveValue('Persistence SEO Title');

    // Slug
    const slugField = page.locator('.field-wrapper, .bg-white.rounded-lg.shadow-sm')
      .filter({ has: page.locator('label', { hasText: 'URL Slug' }) })
      .locator('input[type="text"]');
    await expect(slugField).toHaveValue(slug);

    // Repeater items
    const links = ui.repeaterItems('External Links');
    await expect(links).toHaveCount(1);
  });
});

// ══════════════════════════════════════════════════════════════
// Team Member — verify Email, Number, Range, Group, Repeater,
// DisplayCondition, Select, Checkbox, DatePicker, Relation
// ══════════════════════════════════════════════════════════════
test.describe('Data Persistence: Team Member round-trip', () => {
  let entityId: number;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('team-member');
  });

  test('create team member via UI filling all fields', async ({ page, ui, api }) => {
    await api.deleteAll('team-member');
    await ui.gotoNewEntity('team-member');

    await ui.fillTitle(`Engineer ${TS()}`);

    // Email
    await ui.fillTextField('Work Email', 'engineer@test.com');

    // Select
    await ui.selectOption('Department', 'design');

    // Text (default value)
    await ui.fillTextField('Job Title', 'Lead Designer');

    // Number with step
    await ui.fillNumber('Years of Experience', '7.5');

    // Range
    await ui.setRange('Performance Score', '8');

    // Checkbox
    await ui.setCheckbox('Remote Worker', false);

    // Group (Office Address — visible because is_remote = false)
    await ui.fillTextField('Street Address', '100 Main St');
    await ui.fillTextField('City', 'Portland');
    await ui.fillTextField('State / Province', 'OR');
    await ui.fillTextField('Postal Code', '97201');
    await ui.selectOption('Country', 'US');

    // Repeater — Emergency Contacts (pre-populated from min_items=1)
    await ui.fillTextField('Contact Name', 'Jane Smith');
    await ui.selectOption('Relationship', 'spouse');
    await ui.fillTextField('Phone Number', '+1 555-1234');
    await ui.fillTextField('Email', 'jane@test.com');

    // DatePicker
    await ui.fillDate('Hire Date', '2022-03-15');

    // Number (salary)
    await ui.fillNumber('Annual Salary', '120000');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll('team-member');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('Engineer'));
    expect(created).toBeDefined();
    entityId = created!.id;
  });

  test('API read-back verifies all team member fields', async ({ api }) => {
    const entity = await api.readEntity('team-member', entityId);

    expect(entity.fields.email).toBe('engineer@test.com');
    expect(entity.fields.department).toBe('design');
    expect(entity.fields.job_title).toBe('Lead Designer');
    expect(Number(entity.fields.years_of_experience)).toBe(7.5);
    expect(Number(entity.fields.performance_score)).toBeGreaterThanOrEqual(7);
    expect(entity.fields.is_remote).toBe(false);

    // Group
    expect(entity.fields.office_address.street).toBe('100 Main St');
    expect(entity.fields.office_address.city).toBe('Portland');
    expect(entity.fields.office_address.state).toBe('OR');
    expect(entity.fields.office_address.postal_code).toBe('97201');
    expect(entity.fields.office_address.country).toBe('US');

    // Repeater
    expect(entity.fields.emergency_contacts.length).toBeGreaterThanOrEqual(1);
    expect(entity.fields.emergency_contacts[0].contact_name).toBe('Jane Smith');
    expect(entity.fields.emergency_contacts[0].phone).toBe('+1 555-1234');

    // Date
    expect(entity.fields.hire_date).toBe('20220315');
    expect(Number(entity.fields.salary)).toBe(120000);
  });

  test('UI reload preserves team member values', async ({ page, ui }) => {
    await ui.gotoEditEntity('team-member', entityId);

    const emailField = page.locator('.field-wrapper, .bg-white.rounded-lg.shadow-sm')
      .filter({ has: page.locator('label', { hasText: 'Work Email' }) })
      .locator('input[type="email"]');
    await expect(emailField).toHaveValue('engineer@test.com');

    const deptField = page.locator('.field-wrapper, .bg-white.rounded-lg.shadow-sm')
      .filter({ has: page.locator('label', { hasText: 'Department' }) })
      .locator('button[aria-haspopup="listbox"]');
    await expect(deptField).toHaveAttribute('data-value', 'design');

    // Remote checkbox should be unchecked
    const remoteCb = page.locator('.field-wrapper, .bg-white.rounded-lg.shadow-sm')
      .filter({ has: page.locator('label', { hasText: 'Remote Worker' }) })
      .locator('input[type="checkbox"]');
    await expect(remoteCb).not.toBeChecked();

    // Office Address group should be visible (not remote)
    const streetField = page.locator('label', { hasText: 'Street Address' }).first()
      .locator('xpath=ancestor::div[contains(@class,"field-wrapper")][1]')
      .locator('input[type="text"]');
    await expect(streetField).toHaveValue('100 Main St');

    // Emergency contacts repeater
    const contacts = ui.repeaterItems('Emergency Contacts');
    expect(await contacts.count()).toBeGreaterThanOrEqual(1);
  });
});

// ══════════════════════════════════════════════════════════════
// Product — verify DynamicChoicesRuntimeAsync, multiple Repeaters,
// Range (discount), DisplayCondition (digital vs. physical)
// ══════════════════════════════════════════════════════════════
test.describe('Data Persistence: Product round-trip', () => {
  let entityId: number;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('product');
  });

  test('create product via UI and verify fields persist', async ({ page, ui, api }) => {
    await api.deleteAll('product');
    await ui.gotoNewEntity('product');

    await ui.fillTitle(`Persist Product ${TS()}`);
    await ui.fillMedia('Primary Product Image');
    await ui.fillTextArea('Short Description', 'Short desc for persistence.');
    await ui.selectOption('Product Category', 'electronics');

    // Wait for dynamic subcategory to populate after category selection
    await page.waitForTimeout(1000);
    await ui.selectOption('Subcategory', 'laptops');

    await ui.fillNumber('Base Price (USD)', '599.99');
    await ui.setRange('Discount Percentage', '15');
    await ui.setCheckbox('Published', true);
    await ui.setCheckbox('Digital Product', false);

    // Weight visible because not digital
    await ui.fillNumber('Weight (kg)', '2.5');

    // Add variant (pre-populated from min_items=1)
    await ui.fillTextField('Variant Name', 'Standard');
    await ui.fillTextField('SKU', `PERSIST-${TS()}`);
    await ui.fillNumber('Price (USD)', '599.99');
    await ui.fillNumber('Stock Quantity', '50');

    // Add spec
    await ui.addRepeaterItem('Specifications');
    await ui.fillTextField('Specification', 'Screen Size');
    await ui.fillTextField('Value', '15.6 inches');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll('product');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('Persist Product'));
    expect(created).toBeDefined();
    entityId = created!.id;
  });

  test('API read-back verifies product data', async ({ api }) => {
    const entity = await api.readEntity('product', entityId);

    expect(entity.fields.short_description).toBe('Short desc for persistence.');
    expect(entity.fields.product_category).toBe('electronics');
    expect(entity.fields.subcategory).toBe('laptops');
    expect(Number(entity.fields.base_price)).toBe(599.99);
    expect(Number(entity.fields.discount_percentage)).toBeGreaterThanOrEqual(10);
    expect(entity.fields.is_published).toBe(true);
    expect(entity.fields.is_digital).toBe(false);
    expect(Number(entity.fields.weight_kg)).toBe(2.5);
    expect(entity.fields.variants.length).toBeGreaterThanOrEqual(1);
    expect(entity.fields.variants[0].variant_name).toBe('Standard');
    expect(entity.fields.specifications.length).toBeGreaterThanOrEqual(1);
    expect(entity.fields.specifications[0].spec_name).toBe('Screen Size');
    expect(entity.fields.specifications[0].spec_value).toBe('15.6 inches');
  });

  test('changing category updates subcategory choices dynamically', async ({ page, ui }) => {
    await ui.gotoEditEntity('product', entityId);

    // Change category to clothing
    await ui.selectOption('Product Category', 'clothing');
    await page.waitForTimeout(2000);

    // The subcategory should now show clothing options
    const subField = page.locator('label', { hasText: /^\s*Subcategory\s*\*?\s*$/ }).first()
      .locator('xpath=ancestor::div[contains(@class,"field-wrapper")][1]');
    const subTrigger = subField.locator('button[aria-haspopup="listbox"]');
    await subTrigger.click({ position: { x: 10, y: 10 } });

    const listbox = subField.locator('[role="listbox"]');
    const isVisible = await listbox.isVisible().catch(() => false);
    if (!isVisible) {
      await subTrigger.click({ position: { x: 10, y: 10 } });
    }
    await expect(listbox).toBeVisible({ timeout: 10000 });
    await expect(subField.locator('[role="option"]').first()).toBeVisible({ timeout: 5000 });

    const options = await subField.locator('[role="option"]').allTextContents();
    await page.keyboard.press('Escape');
    const hasClothingOption = options.some(o => /men|women|shoe|kid/i.test(o));
    expect(hasClothingOption).toBe(true);
  });
});

// ══════════════════════════════════════════════════════════════
// Event — deeply nested groups, multiple date fields,
// DisplayCondition (online vs. in-person), Range (ticket)
// ══════════════════════════════════════════════════════════════
test.describe('Data Persistence: Event round-trip', () => {
  let entityId: number;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('event');
  });

  test('create in-person event and verify nested group persistence', async ({ page, ui, api }) => {
    await api.deleteAll('event');
    await ui.gotoNewEntity('event');

    await ui.fillTitle(`Persist Event ${TS()}`);
    await ui.fillWysiwyg('Event Description', '<p>Persistence test event.</p>');
    await ui.selectOption('Event Type', 'conference');
    await ui.fillDate('Start Date', '2025-06-15');
    await ui.fillDate('End Date', '2025-06-17');
    await ui.setCheckbox('Online Event', false);

    // Venue Details Group (visible because not online)
    await ui.fillTextField('Venue Name', 'Grand Convention Center');
    await ui.fillTextField('Street Address', '500 Event Blvd');
    await ui.fillTextField('City', 'Austin');
    await ui.fillTextField('State / Province', 'TX');
    await ui.fillTextField('Postal Code', '73301');
    await ui.selectOption('Country', 'US');
    await ui.fillNumber('Venue Capacity', '500');

    await ui.fillNumber('Maximum Attendees', '300');
    await ui.setRange('Ticket Price (USD)', '100');
    await ui.fillTextField('Registration Contact Email', 'events@test.com');

    // Session repeater
    await ui.addRepeaterItem('Sessions / Agenda');
    await ui.fillTextField('Session Title', 'Opening Keynote');
    await ui.fillTextField('Speaker Name', 'Dr. Tester');
    await ui.fillTextField('Speaker Email', 'keynote@test.com');
    await ui.fillDate('Session Date', '2025-06-15');
    await ui.fillNumber('Duration (minutes)', '60');
    await ui.selectOption('Session Type', 'keynote');

    // Sponsor repeater
    await ui.addRepeaterItem('Sponsors');
    await ui.fillTextField('Sponsor Name', 'Acme Corp');
    await ui.selectOption('Sponsor Tier', 'gold');
    await ui.fillTextField('Sponsor Website', 'https://acme.com');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll('event');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('Persist Event'));
    expect(created).toBeDefined();
    entityId = created!.id;
  });

  test('API read-back verifies nested group and repeater data', async ({ api }) => {
    const entity = await api.readEntity('event', entityId);

    expect(entity.fields.event_type).toBe('conference');
    expect(entity.fields.start_date).toBe('20250615');
    expect(entity.fields.end_date).toBe('20250617');
    expect(entity.fields.is_online).toBe(false);

    // Nested group: venue → address
    expect(entity.fields.venue.venue_name).toBe('Grand Convention Center');
    expect(entity.fields.venue.venue_address.street).toBe('500 Event Blvd');
    expect(entity.fields.venue.venue_address.city).toBe('Austin');
    expect(entity.fields.venue.venue_address.postal_code).toBe('73301');
    expect(Number(entity.fields.venue.capacity)).toBe(500);

    expect(Number(entity.fields.max_attendees)).toBe(300);
    expect(Number(entity.fields.ticket_price)).toBeGreaterThanOrEqual(75);

    // Session
    expect(entity.fields.sessions.length).toBeGreaterThanOrEqual(1);
    expect(entity.fields.sessions[0].session_title).toBe('Opening Keynote');
    expect(entity.fields.sessions[0].speaker_name).toBe('Dr. Tester');
    expect(entity.fields.sessions[0].session_type).toBe('keynote');

    // Sponsor
    expect(entity.fields.sponsors.length).toBeGreaterThanOrEqual(1);
    expect(entity.fields.sponsors[0].sponsor_name).toBe('Acme Corp');
    expect(entity.fields.sponsors[0].sponsor_tier).toBe('gold');
  });

  test('reload preserves nested values in the form', async ({ page, ui }) => {
    await ui.gotoEditEntity('event', entityId);

    // Verify nested venue name
    const venueName = page.locator('label', { hasText: 'Venue Name' }).first()
      .locator('xpath=ancestor::div[contains(@class,"field-wrapper")][1]')
      .locator('input[type="text"]');
    await expect(venueName).toHaveValue('Grand Convention Center');

    // Verify sessions repeater count
    const sessions = ui.repeaterItems('Sessions / Agenda');
    expect(await sessions.count()).toBeGreaterThanOrEqual(1);

    // Verify sponsors repeater count
    const sponsors = ui.repeaterItems('Sponsors');
    expect(await sponsors.count()).toBeGreaterThanOrEqual(1);
  });
});

// ══════════════════════════════════════════════════════════════
// Objective — nested repeater (key results → comments),
// DynamicChoicesCompileTimeAsync, DynamicChoicesRuntimeAsync
// ══════════════════════════════════════════════════════════════
test.describe('Data Persistence: Objective round-trip', () => {
  let entityId: number;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll('objective');
  });

  test('create objective with nested repeater data', async ({ page, ui, api }) => {
    await api.deleteAll('objective');
    await ui.gotoNewEntity('objective');

    await ui.fillTitle(`Persist OKR ${TS()}`);
    await ui.fillDate('Objective Work Planned Start Date', '2025-01-15');
    await ui.selectOption('Short-term or Long-term?', 'long_term');
    await ui.fillTextField('Objective Documentation URL', 'https://docs.example.com/okr');
    await ui.fillTextArea('Root Cause', `Unique cause ${TS()}`);

    // Entity-level Author (from has_author)
    await ui.selectSearchableOption('Author');

    // Creator Comment group — Group-level Author relation + Comment
    await ui.selectSearchableOption('Author', undefined, 1);
    await ui.fillTextArea('Comment', 'Initial creator comment');

    // Key Results repeater
    await ui.addRepeaterItem('Key Results');
    await ui.fillTextArea('Key Results', 'Achieve 95% uptime in Q1');

    // Objective Comments (uses SampleCommentModel with mandatory Author)
    await ui.addRepeaterItem('Objective Comments');
    const commentItem = ui.repeaterItems('Objective Comments').first();
    const commentAuthor = commentItem.locator('button[aria-haspopup="listbox"]');
    await commentAuthor.click();
    await expect(commentItem.locator('[role="listbox"]')).toBeVisible({ timeout: 10000 });
    const commentOption = commentItem.locator('[role="option"]').nth(1);
    await expect(commentOption).toBeVisible({ timeout: 10000 });
    await commentOption.click();
    await commentItem.locator('textarea').fill('Initial review complete');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll('objective');
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('Persist OKR'));
    expect(created).toBeDefined();
    entityId = created!.id;
  });

  test('API verifies objective with nested repeater', async ({ api }) => {
    const entity = await api.readEntity('objective', entityId);

    expect(entity.fields.objective_type).toBe('long_term');
    expect(entity.fields.documentation_url).toBe('https://docs.example.com/okr');
    expect(entity.fields.root_cause).toContain('Unique cause');
    expect(entity.fields.key_results.length).toBeGreaterThanOrEqual(1);
    expect(entity.fields.key_results[0].key_result).toBe('Achieve 95% uptime in Q1');
    expect(entity.fields.objective_comments.length).toBeGreaterThanOrEqual(1);
    expect(entity.fields.objective_comments[0].comment).toBe('Initial review complete');
  });
});
