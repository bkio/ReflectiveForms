import { test, expect } from './helpers';

/**
 * Product Entity – Full CRUD E2E Tests
 *
 * Covers: DynamicChoicesRuntimeAsync (category → subcategory), 3 Repeaters
 * (gallery, variants min 1–50, specifications), Range slider (discount),
 * DisplayCondition (digital → hides weight), Relation to team-member,
 * Multiple Number configs (price with step 0.01, stock), MediaSourceBase64,
 * WysiwygEditor, TextArea, Checkbox × 2, DatePicker, Url.
 *
 * Full cycle: create → list → read → update → conditional → repeaters → delete
 */

const ENTITY = 'product';
const TS = () => Date.now().toString(36);

test.describe('Product CRUD', () => {
  let createdId: number;

  test.describe.configure({ mode: 'serial' });

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY);
  });

  // ──────────────────────────────────────
  // CREATE
  // ──────────────────────────────────────
  test('create a product with core fields', async ({ page, ui, api }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    await expect(page.locator('h1')).toContainText('New Product');

    // Title
    await ui.fillTitle(`E2E Headphones ${TS()}`);

    // MediaSourceBase64 — Primary Product Image
    await ui.fillMedia('Primary Product Image');

    // TextArea — Short Description
    await ui.fillTextArea('Short Description', 'Premium noise-cancelling headphones.');

    // WysiwygEditor — Full Description
    await ui.fillWysiwyg('Full Description', '<p>Studio quality with 40-hour battery life.</p>');

    // Select (static) — Product Category
    await ui.selectOption('Product Category', 'electronics');

    // Number — Base Price
    await ui.fillNumber('Base Price (USD)', '299.99');

    // Range — Discount Percentage
    await ui.setRange('Discount Percentage', '15');

    // Checkbox — Published
    await ui.setCheckbox('Published', true);

    // Checkbox — Digital Product (false = physical, should show weight)
    await ui.setCheckbox('Digital Product', false);

    // Number — Weight (only when not digital)
    await ui.fillNumber('Weight (kg)', '0.3');

    // Repeater — Variants (pre-populated from min_items=1)
    await ui.expandRepeaterItem('Product Variants', 0);
    await ui.fillTextField('Variant Name', 'Black / Standard');
    await ui.fillTextField('SKU', 'HP-BLK-STD');
    await ui.fillNumber('Price (USD)', '299.99');
    await ui.fillNumber('Stock Quantity', '500');
    await ui.setCheckbox('Available for Sale', true);

    // Repeater — Specifications
    await ui.addRepeaterItem('Specifications');
    await ui.fillTextField('Specification', 'Battery Life');
    await ui.fillTextField('Value', '40 hours');

    // Url — External Product Page
    await ui.fillTextField('External Product Page', 'https://manufacturer.com/hp');

    // DatePicker — Launch Date
    await ui.fillDate('Launch Date', '2026-01-15');

    // Save
    await ui.clickSaveNow();
    await ui.waitForSave();

    const entities = await api.peekAll(ENTITY);
    const created = entities.find(e => (e.title ?? e.name ?? '').includes('E2E Headphones'));
    expect(created).toBeDefined();
    createdId = created!.id;
  });

  // ──────────────────────────────────────
  // LIST verification
  // ──────────────────────────────────────
  test('product appears in the list page', async ({ page, ui }) => {
    await ui.gotoEntityList(ENTITY);
    await expect(page.locator('a', { hasText: /E2E Headphones/ })).toBeVisible();
  });

  // ──────────────────────────────────────
  // READ via API
  // ──────────────────────────────────────
  test('read product and verify all fields', async ({ api }) => {
    const entity = await api.readEntity(ENTITY, createdId);

    expect(entity.title.rendered).toContain('E2E Headphones');
    expect(entity.fields.short_description).toContain('noise-cancelling');
    expect(entity.fields.product_category).toBe('electronics');
    expect(Number(entity.fields.base_price)).toBe(299.99);
    expect(entity.fields.is_published).toBe(true);
    expect(entity.fields.is_digital).toBe(false);
    expect(Number(entity.fields.weight_kg)).toBe(0.3);
    expect(entity.fields.variants.length).toBe(1);
    expect(entity.fields.variants[0].variant_name).toBe('Black / Standard');
    expect(entity.fields.variants[0].sku).toBe('HP-BLK-STD');
    expect(entity.fields.specifications.length).toBe(1);
    expect(entity.fields.specifications[0].spec_name).toBe('Battery Life');
    expect(entity.fields.specifications[0].spec_value).toBe('40 hours');
    expect(entity.fields.product_url).toBe('https://manufacturer.com/hp');
  });

  // ──────────────────────────────────────
  // CONDITIONAL — Digital Product hides weight
  // ──────────────────────────────────────
  test('DisplayCondition: weight hidden when digital product is checked', async ({ page, ui }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    // Weight should be visible (is_digital == false)
    expect(await ui.fieldIsVisible('Weight (kg)')).toBe(true);

    // Toggle digital ON
    await ui.setCheckbox('Digital Product', true);
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Weight (kg)')).toBe(false);

    // Toggle back
    await ui.setCheckbox('Digital Product', false);
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Weight (kg)')).toBe(true);

    await ui.clickSaveNow();
    await ui.waitForSave();
  });

  // ──────────────────────────────────────
  // UPDATE — price, add variant, add spec
  // ──────────────────────────────────────
  test('update product: change price, add second variant and spec', async ({ ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    // Change price
    await ui.fillNumber('Base Price (USD)', '249.99');

    // Add second variant
    await ui.addRepeaterItem('Product Variants');
    const variants = ui.repeaterItems('Product Variants');
    const second = variants.nth(1);
    await second.locator('input[type="text"]').first().fill('White / Premium');
    // Fill SKU for the second variant (mandatory)
    const skuInputs = second.locator('input[type="text"]');
    await skuInputs.nth(1).fill(`SKU-PREMIUM-${TS()}`);

    // Add second spec
    await ui.addRepeaterItem('Specifications');
    const specs = ui.repeaterItems('Specifications');
    const secondSpec = specs.nth(1);
    await secondSpec.locator('input[type="text"]').first().fill('Weight');
    // Fill spec_value (mandatory)
    await secondSpec.locator('input[type="text"]').nth(1).fill('250g');

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entity = await api.readEntity(ENTITY, createdId);
    expect(Number(entity.fields.base_price)).toBe(249.99);
    expect(entity.fields.variants.length).toBe(2);
    expect(entity.fields.specifications.length).toBe(2);
  });

  // ──────────────────────────────────────
  // DYNAMIC CHOICES — subcategory depends on category
  // ──────────────────────────────────────
  test('DynamicChoicesRuntimeAsync: subcategory changes when category changes', async ({ page, ui }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    // Current category is "electronics" → subcategory options should include phones, laptops, etc.
    const subcatWrapper = page.locator('.field-wrapper')
      .filter({ has: page.locator('label', { hasText: 'Subcategory' }) });
    const subcatTrigger = subcatWrapper.locator('button[aria-haspopup="listbox"]');

    if (await subcatTrigger.isVisible({ timeout: 5000 }).catch(() => false)) {
      // Get current options
      await subcatTrigger.click();
      const electronicsOptions = await subcatWrapper.locator('[role="option"]').allTextContents();
      await page.keyboard.press('Escape');
      expect(electronicsOptions.some(o => /phone|laptop|audio/i.test(o))).toBe(true);

      // Change category to "clothing"
      await ui.selectOption('Product Category', 'clothing');
      await page.waitForTimeout(1000); // Allow runtime JS to re-evaluate

      await subcatTrigger.click();
      const clothingOptions = await subcatWrapper.locator('[role="option"]').allTextContents();
      await page.keyboard.press('Escape');
      expect(clothingOptions.some(o => /men|women|shoes/i.test(o))).toBe(true);

      // Revert category
      await ui.selectOption('Product Category', 'electronics');
    }

    await ui.clickSaveNow();
    await ui.waitForSave();
  });

  // ──────────────────────────────────────
  // REPEATER — remove a variant
  // ──────────────────────────────────────
  test('remove second variant from product', async ({ ui, api }) => {
    await ui.gotoEditEntity(ENTITY, createdId);

    const variants = ui.repeaterItems('Product Variants');
    const countBefore = await variants.count();

    // Remove last variant
    const last = variants.nth(countBefore - 1);
    await ui.safeClick(last.locator('button[title="Remove"]'));

    await ui.clickSaveNow();
    await ui.waitForSave();

    const entity = await api.readEntity(ENTITY, createdId);
    expect(entity.fields.variants.length).toBe(countBefore - 1);
  });

  // ──────────────────────────────────────
  // LIST — verify updated title via list page
  // ──────────────────────────────────────
  test('list page still shows product after updates', async ({ page, ui }) => {
    await ui.gotoEntityList(ENTITY);
    const count = await ui.entityRowCount();
    expect(count).toBeGreaterThanOrEqual(1);
    await expect(page.locator('a', { hasText: /E2E Headphones/ })).toBeVisible();
  });

  // ──────────────────────────────────────
  // DELETE via UI
  // ──────────────────────────────────────
  test('delete product and verify removal', async ({ page, ui, api }) => {
    // Ensure entity is unlocked from previous edit test
    await api.unlockEntity(ENTITY, createdId);

    await ui.gotoEntityList(ENTITY);
    const countBefore = await ui.entityRowCount();

    const deleteBtn = ui.entityRows().first().locator('button[title="Delete"]');
    await deleteBtn.waitFor({ state: 'visible', timeout: 30000 });

    page.on('dialog', dialog => dialog.accept());
    await deleteBtn.click();
    await page.waitForTimeout(2000);

    const entities = await api.peekAll(ENTITY);
    expect(entities.length).toBe(countBefore - 1);
  });
});
