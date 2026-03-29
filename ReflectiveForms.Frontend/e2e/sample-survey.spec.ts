import { test, expect } from './helpers';

/**
 * Survey Entity — Nested Repeaters & Display Conditions E2E Tests
 *
 * The survey entity has 3 levels of nesting:
 *   Survey → Sections[] → Questions[] → Choices[]
 *
 * Display conditions at each level:
 *   Root:    is_anonymous == false → Response Limit per Person
 *   Section: has_scoring == true  → Passing Score, Scoring Mode
 *            scoring_mode == weighted → Weighting Explanation
 *   Question: is_required == true      → Help Text
 *             question_type == rating   → Min Rating, Max Rating
 *             question_type == choice   → Choices[] repeater
 *
 * Tests cover:
 *   1. API: create complex nested data and read it back
 *   2. API: add/remove repeater elements and verify persistence
 *   3. UI:  display conditions at every nesting level
 *   4. UI:  add/remove repeater items (sections, questions, choices)
 *   5. UI:  full round-trip: fill nested form → save → reload → verify
 */

const ENTITY = 'survey';
const TS = () => Date.now().toString(36);

// ─────────────────────────────────────────────────────────
// Helper: build a full survey payload for API tests
// ─────────────────────────────────────────────────────────
function buildSurveyPayload(title: string) {
  return {
    title: { rendered: title },
    fields: {
      survey_description: 'E2E survey description',
      is_anonymous: false,
      response_limit: 3,
      due_date: '20260601',
      survey_status: 'draft',
      sections: [
        {
          section_title: 'Demographics',
          section_description: 'Basic info about the respondent.',
          has_scoring: false,
          passing_score: 0,
          scoring_mode: 'simple',
          score_explanation: '',
          questions: [
            {
              question_text: 'What is your name?',
              question_type: 'text',
              is_required: true,
              help_text: 'Please provide your full name.',
              min_rating: 0,
              max_rating: 1,
            },
            {
              question_text: 'What is your age group?',
              question_type: 'choice',
              is_required: false,
              help_text: '',
              min_rating: 0,
              max_rating: 1,
              choices: [
                { choice_label: 'Under 18', is_correct: false, choice_score: 0 },
                { choice_label: '18-34', is_correct: false, choice_score: 0 },
                { choice_label: '35-54', is_correct: false, choice_score: 0 },
                { choice_label: '55+', is_correct: false, choice_score: 0 },
              ],
            },
          ],
        },
        {
          section_title: 'Knowledge Quiz',
          section_description: 'Test your knowledge.',
          has_scoring: true,
          passing_score: 70,
          scoring_mode: 'weighted',
          score_explanation: 'Each question is weighted by difficulty.',
          questions: [
            {
              question_text: 'Rate your satisfaction',
              question_type: 'rating',
              is_required: true,
              help_text: 'Use 1-10 scale.',
              min_rating: 1,
              max_rating: 10,
            },
            {
              question_text: 'What is 2+2?',
              question_type: 'choice',
              is_required: true,
              help_text: 'Basic math question.',
              min_rating: 0,
              max_rating: 1,
              choices: [
                { choice_label: '3', is_correct: false, choice_score: 0 },
                { choice_label: '4', is_correct: true, choice_score: 10 },
                { choice_label: '5', is_correct: false, choice_score: 0 },
              ],
            },
          ],
        },
      ],
    },
  };
}

// ═════════════════════════════════════════════════════════════
// PART 1: Backend (API) Tests
// ═════════════════════════════════════════════════════════════
test.describe('Survey API: nested CRUD', () => {
  test.describe.configure({ mode: 'serial' });

  let createdId: number;

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY);
  });

  test('create survey with 3-level nested data', async ({ api }) => {
    await api.deleteAll(ENTITY);
    const payload = buildSurveyPayload(`API Nested Survey ${TS()}`);
    const result = await api.createEntity(ENTITY, payload);
    createdId = result.id ?? result.Id;
    expect(createdId).toBeGreaterThan(0);
  });

  test('read back preserves all nested structure', async ({ api }) => {
    const data = await api.readEntity(ENTITY, createdId);
    const fields = data.fields;

    // Root fields
    expect(fields.survey_description).toBe('E2E survey description');
    expect(fields.is_anonymous).toBe(false);
    expect(fields.response_limit).toBe(3);
    expect(fields.survey_status).toBe('draft');

    // Sections
    expect(fields.sections).toHaveLength(2);

    // Section 1: Demographics (no scoring)
    const s1 = fields.sections[0];
    expect(s1.section_title).toBe('Demographics');
    expect(s1.has_scoring).toBe(false);
    expect(s1.questions).toHaveLength(2);
    expect(s1.questions[0].question_text).toBe('What is your name?');
    expect(s1.questions[0].question_type).toBe('text');
    expect(s1.questions[0].is_required).toBe(true);

    // Level 3: Choices in age-group question
    const ageQ = s1.questions[1];
    expect(ageQ.question_type).toBe('choice');
    expect(ageQ.choices).toHaveLength(4);
    expect(ageQ.choices[0].choice_label).toBe('Under 18');
    expect(ageQ.choices[2].choice_label).toBe('35-54');

    // Section 2: Knowledge Quiz (scored, weighted)
    const s2 = fields.sections[1];
    expect(s2.has_scoring).toBe(true);
    expect(s2.passing_score).toBe(70);
    expect(s2.scoring_mode).toBe('weighted');
    expect(s2.score_explanation).toBe('Each question is weighted by difficulty.');

    // Rating question
    const ratingQ = s2.questions[0];
    expect(ratingQ.question_type).toBe('rating');
    expect(ratingQ.min_rating).toBe(1);
    expect(ratingQ.max_rating).toBe(10);

    // Choice question with correct answer
    const mathQ = s2.questions[1];
    expect(mathQ.choices).toHaveLength(3);
    expect(mathQ.choices[1].choice_label).toBe('4');
    expect(mathQ.choices[1].is_correct).toBe(true);
    expect(mathQ.choices[1].choice_score).toBe(10);
  });

  test('update: add a third section and a new question to section 1', async ({ api }) => {
    const data = await api.readEntity(ENTITY, createdId);
    const fields = data.fields;

    // Add new question to section 1
    fields.sections[0].questions.push({
      question_text: 'Where do you live?',
      question_type: 'text',
      is_required: false,
      help_text: '',
      min_rating: 0,
      max_rating: 1,
    });

    // Add new section 3
    fields.sections.push({
      section_title: 'Feedback',
      section_description: 'Final thoughts',
      has_scoring: false,
      passing_score: 0,
      scoring_mode: 'simple',
      score_explanation: '',
      questions: [
        {
          question_text: 'Any additional comments?',
          question_type: 'text',
          is_required: false,
          help_text: '',
          min_rating: 0,
          max_rating: 1,
        },
      ],
    });

    await api.updateEntity(ENTITY, {
      id: createdId,
      title: data.title,
      fields,
    });

    // Verify
    const updated = await api.readEntity(ENTITY, createdId);
    expect(updated.fields.sections).toHaveLength(3);
    expect(updated.fields.sections[0].questions).toHaveLength(3);
    expect(updated.fields.sections[0].questions[2].question_text).toBe('Where do you live?');
    expect(updated.fields.sections[2].section_title).toBe('Feedback');
  });

  test('update: remove a choice from a nested question', async ({ api }) => {
    const data = await api.readEntity(ENTITY, createdId);
    const fields = data.fields;

    // Section 1, question 2 (age group) has 4 choices: remove the "55+" choice
    const ageQ = fields.sections[0].questions[1];
    expect(ageQ.choices).toHaveLength(4);
    ageQ.choices.splice(3, 1); // remove last choice
    expect(ageQ.choices).toHaveLength(3);

    await api.updateEntity(ENTITY, {
      id: createdId,
      title: data.title,
      fields,
    });

    const updated = await api.readEntity(ENTITY, createdId);
    expect(updated.fields.sections[0].questions[1].choices).toHaveLength(3);
    expect(
      updated.fields.sections[0].questions[1].choices.map(
        (c: { choice_label: string }) => c.choice_label,
      ),
    ).toEqual(['Under 18', '18-34', '35-54']);
  });

  test('update: remove entire section 2 (Knowledge Quiz)', async ({ api }) => {
    const data = await api.readEntity(ENTITY, createdId);
    const fields = data.fields;

    // Before: 3 sections
    expect(fields.sections).toHaveLength(3);

    // Remove section at index 1 (Knowledge Quiz)
    fields.sections.splice(1, 1);

    await api.updateEntity(ENTITY, {
      id: createdId,
      title: data.title,
      fields,
    });

    const updated = await api.readEntity(ENTITY, createdId);
    expect(updated.fields.sections).toHaveLength(2);
    expect(updated.fields.sections[0].section_title).toBe('Demographics');
    expect(updated.fields.sections[1].section_title).toBe('Feedback');
  });

  test('update: add choices to a text question (change type to choice)', async ({ api }) => {
    const data = await api.readEntity(ENTITY, createdId);
    const fields = data.fields;

    // Convert "Where do you live?" (section 0, question 2) to choice type
    const q = fields.sections[0].questions[2];
    expect(q.question_text).toBe('Where do you live?');
    q.question_type = 'choice';
    q.choices = [
      { choice_label: 'Urban', is_correct: false, choice_score: 0 },
      { choice_label: 'Suburban', is_correct: false, choice_score: 0 },
      { choice_label: 'Rural', is_correct: false, choice_score: 0 },
    ];

    await api.updateEntity(ENTITY, {
      id: createdId,
      title: data.title,
      fields,
    });

    const updated = await api.readEntity(ENTITY, createdId);
    const updatedQ = updated.fields.sections[0].questions[2];
    expect(updatedQ.question_type).toBe('choice');
    expect(updatedQ.choices).toHaveLength(3);
    expect(updatedQ.choices[0].choice_label).toBe('Urban');
  });
});

// ═════════════════════════════════════════════════════════════
// PART 2: Display Conditions — all nesting levels
// ═════════════════════════════════════════════════════════════
test.describe('Survey Display Conditions: root level', () => {
  test('response_limit hidden when is_anonymous is true', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);

    // Default is unchecked (is_anonymous = false), so response_limit should be visible
    const visibleBefore = await ui.fieldIsVisible('Response Limit per Person');
    expect(visibleBefore).toBe(true);

    // Check anonymous → response limit should hide
    await ui.setCheckbox('Anonymous Responses', true);
    await ui.page.waitForTimeout(500);

    const visibleAfter = await ui.fieldIsVisible('Response Limit per Person');
    expect(visibleAfter).toBe(false);
  });

  test('response_limit reappears when is_anonymous unchecked', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);

    await ui.setCheckbox('Anonymous Responses', true);
    await ui.page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Response Limit per Person')).toBe(false);

    await ui.setCheckbox('Anonymous Responses', false);
    await ui.page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Response Limit per Person')).toBe(true);
  });
});

test.describe('Survey Display Conditions: section level (scoring)', () => {
  test('scoring fields hidden by default, appear when Enable Scoring checked', async ({
    page,
    ui,
  }) => {
    await ui.gotoNewEntity(ENTITY);

    // The form pre-populates one section (min_items = 1).
    // Scoring fields should be hidden (has_scoring default = false)
    expect(await ui.fieldIsVisible('Passing Score')).toBe(false);
    expect(await ui.fieldIsVisible('Scoring Mode')).toBe(false);

    // Enable scoring in the first section
    await ui.setCheckbox('Enable Scoring', true);
    await page.waitForTimeout(500);

    expect(await ui.fieldIsVisible('Passing Score')).toBe(true);
    expect(await ui.fieldIsVisible('Scoring Mode')).toBe(true);
  });

  test('weighting explanation appears only when scoring_mode is weighted', async ({
    page,
    ui,
  }) => {
    await ui.gotoNewEntity(ENTITY);

    // Enable scoring first
    await ui.setCheckbox('Enable Scoring', true);
    await page.waitForTimeout(500);

    // Default scoring mode is "simple" → explanation hidden
    expect(await ui.fieldIsVisible('Weighting Explanation')).toBe(false);

    // Change to weighted → explanation appears
    await ui.selectOption('Scoring Mode', 'weighted');
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Weighting Explanation')).toBe(true);

    // Change back to simple → explanation hides
    await ui.selectOption('Scoring Mode', 'simple');
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Weighting Explanation')).toBe(false);
  });
});

test.describe('Survey Display Conditions: question level', () => {
  test('help_text appears when is_required is checked', async ({ page, ui }) => {
    await ui.gotoNewEntity(ENTITY);

    // The first section has one pre-populated question (min_items = 1).
    // is_required is unchecked by default → help_text hidden
    expect(await ui.fieldIsVisible('Help Text')).toBe(false);

    await ui.setCheckbox('Required', true);
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Help Text')).toBe(true);

    await ui.setCheckbox('Required', false);
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Help Text')).toBe(false);
  });

  test('rating fields appear when question_type is rating', async ({ page, ui }) => {
    await ui.gotoNewEntity(ENTITY);

    // Default question type is "text" → rating fields hidden
    expect(await ui.fieldIsVisible('Min Rating')).toBe(false);
    expect(await ui.fieldIsVisible('Max Rating')).toBe(false);

    await ui.selectOption('Question Type', 'rating');
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Min Rating')).toBe(true);
    expect(await ui.fieldIsVisible('Max Rating')).toBe(true);

    // Switch back to text → rating fields hide
    await ui.selectOption('Question Type', 'text');
    await page.waitForTimeout(500);
    expect(await ui.fieldIsVisible('Min Rating')).toBe(false);
    expect(await ui.fieldIsVisible('Max Rating')).toBe(false);
  });

  test('choices repeater appears when question_type is choice', async ({ page, ui }) => {
    await ui.gotoNewEntity(ENTITY);

    // Default is "text" → choices repeater hidden
    expect(await ui.fieldIsVisible('Choices')).toBe(false);

    await ui.selectOption('Question Type', 'choice');
    await page.waitForTimeout(500);

    // Choices repeater should appear with 2 pre-populated items (min_items = 2)
    expect(await ui.fieldIsVisible('Choices')).toBe(true);
  });
});

// ═════════════════════════════════════════════════════════════
// PART 3: UI Repeater Add/Remove at every nesting level
// ═════════════════════════════════════════════════════════════
test.describe('Survey Repeater: add/remove sections', () => {
  test('form starts with 1 pre-populated section (min_items=1)', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);

    const items = ui.repeaterItems('Sections');
    await expect(items).toHaveCount(1);
  });

  test('can add a second section', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);

    await ui.addRepeaterItem('Sections');
    const items = ui.repeaterItems('Sections');
    await expect(items).toHaveCount(2);
  });

  test('can remove the second section back to 1', async ({ page, ui }) => {
    await ui.gotoNewEntity(ENTITY);

    // Start with 1, add to make 2
    await ui.addRepeaterItem('Sections');
    await expect(ui.repeaterItems('Sections')).toHaveCount(2);

    // Remove the second item (click the remove button in item 2)
    const secondItem = ui.repeaterItems('Sections').nth(1);
    const removeBtn = secondItem.locator('button[title="Remove"]');
    await removeBtn.click();

    await expect(ui.repeaterItems('Sections')).toHaveCount(1);
  });

  test('cannot remove last section (min_items=1)', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);
    await expect(ui.repeaterItems('Sections')).toHaveCount(1);

    // The remove button should not exist for the single remaining item
    const firstItem = ui.repeaterItems('Sections').nth(0);
    const removeBtn = firstItem.locator('button[title="Remove"]');
    await expect(removeBtn).toHaveCount(0);
  });
});

test.describe('Survey Repeater: add/remove questions (level 2)', () => {
  test('section starts with 1 pre-populated question (min_items=1)', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);

    const items = ui.repeaterItems('Questions');
    await expect(items).toHaveCount(1);
  });

  test('can add multiple questions and remove them', async ({ ui }) => {
    await ui.gotoNewEntity(ENTITY);

    // Add 2 more questions (total 3)
    await ui.addRepeaterItem('Questions');
    await ui.addRepeaterItem('Questions');
    await expect(ui.repeaterItems('Questions')).toHaveCount(3);

    // Remove the middle one (index 1)
    const secondQuestion = ui.repeaterItems('Questions').nth(1);
    await secondQuestion.locator('button[title="Remove"]').click();
    await expect(ui.repeaterItems('Questions')).toHaveCount(2);
  });
});

test.describe('Survey Repeater: add/remove choices (level 3)', () => {
  test('choices repeater starts with 2 items (min_items=2) when type=choice', async ({
    page,
    ui,
  }) => {
    await ui.gotoNewEntity(ENTITY);

    // Switch question type to "choice" to reveal choices
    await ui.selectOption('Question Type', 'choice');
    await page.waitForTimeout(500);

    const items = ui.repeaterItems('Choices');
    await expect(items).toHaveCount(2);
  });

  test('can add choices up to max (8) and add button disappears', async ({ page, ui }) => {
    await ui.gotoNewEntity(ENTITY);

    await ui.selectOption('Question Type', 'choice');
    await page.waitForTimeout(500);

    // Start with 2, add 6 more to reach 8
    for (let i = 0; i < 6; i++) {
      await ui.addRepeaterItem('Choices');
    }
    await expect(ui.repeaterItems('Choices')).toHaveCount(8);

    // The add button should disappear at max_items
    const choicesWrapper = page
      .locator('label', { hasText: /^\s*Choices\s*\*?\s*$/ })
      .first()
      .locator('xpath=ancestor::div[contains(@class,"field-wrapper")][1]');
    const addBtn = choicesWrapper.locator('button').filter({ hasText: /add/i }).last();
    await expect(addBtn).toHaveCount(0);
  });

  test('can remove choices but not below min (2)', async ({ page, ui }) => {
    await ui.gotoNewEntity(ENTITY);

    await ui.selectOption('Question Type', 'choice');
    await page.waitForTimeout(500);

    // Add one more → 3 total
    await ui.addRepeaterItem('Choices');
    await expect(ui.repeaterItems('Choices')).toHaveCount(3);

    // Remove the third
    const thirdItem = ui.repeaterItems('Choices').nth(2);
    await thirdItem.locator('button[title="Remove"]').click();
    await expect(ui.repeaterItems('Choices')).toHaveCount(2);

    // At min_items=2, remove buttons should not be present
    const firstItem = ui.repeaterItems('Choices').nth(0);
    const removeBtn = firstItem.locator('button[title="Remove"]');
    await expect(removeBtn).toHaveCount(0);
  });
});

// ═════════════════════════════════════════════════════════════
// PART 4: Full UI Round-Trip — fill, save, reload, verify
// ═════════════════════════════════════════════════════════════
test.describe('Survey Full Round-Trip', () => {
  test.describe.configure({ mode: 'serial' });

  let createdId: number;

  test.afterAll(async ({ request }) => {
    const { ApiHelper } = await import('./helpers');
    const api = new ApiHelper(request);
    await api.login();
    await api.deleteAll(ENTITY);
  });

  test('create survey via UI with nested sections, questions, and choices', async ({
    page,
    ui,
    api,
  }) => {
    await api.deleteAll(ENTITY);
    await ui.gotoNewEntity(ENTITY);

    const title = `UI Survey ${TS()}`;
    await ui.fillTitle(title);
    await ui.fillTextArea('Survey Description', 'Created from UI test.');
    await ui.fillDate('Due Date', '2026-12-31');
    await ui.selectOption('Survey Status', 'active');

    // ── Section 1 is pre-populated ──
    await ui.fillTextField('Section Title', 'General');
    await ui.fillTextArea('Section Description', 'General questions.');

    // Enable scoring on section 1
    await ui.setCheckbox('Enable Scoring', true);
    await page.waitForTimeout(500);
    await ui.fillNumber('Passing Score', '50');
    await ui.selectOption('Scoring Mode', 'simple');

    // Question 1 in section 1 is pre-populated
    await ui.fillTextArea('Question Text', 'How did you hear about us?');
    await ui.selectOption('Question Type', 'choice');
    await page.waitForTimeout(500);

    // Choice items are pre-populated (min_items=2)
    // Fill in the two pre-populated choices
    const choiceLabels = page.locator('input[type="text"]').filter({
      has: page.locator('xpath=ancestor::div[contains(@class,"field-wrapper")]//label[contains(text(),"Choice Label")]'),
    });

    // Use a more robust approach: fill the Choice Label fields by their form path
    const firstChoice = ui.repeaterItems('Choices').nth(0);
    await firstChoice.locator('input[type="text"]').fill('Social Media');

    const secondChoice = ui.repeaterItems('Choices').nth(1);
    await secondChoice.locator('input[type="text"]').fill('Word of Mouth');

    // Save
    await ui.clickSaveNow();
    await ui.waitForSave();

    // Extract the created ID from the URL
    const url = page.url();
    const match = url.match(/[?&]id=(\d+)/);
    expect(match).toBeTruthy();
    createdId = parseInt(match![1], 10);
    expect(createdId).toBeGreaterThan(0);
  });

  test('reload saved survey and verify nested data persisted', async ({ api }) => {
    const data = await api.readEntity(ENTITY, createdId);

    expect(data.fields.survey_description).toBe('Created from UI test.');
    expect(data.fields.survey_status).toBe('active');

    // Section 1
    const s1 = data.fields.sections[0];
    expect(s1.section_title).toBe('General');
    expect(s1.section_description).toBe('General questions.');
    expect(s1.has_scoring).toBe(true);
    expect(s1.passing_score).toBe(50);

    // Question 1
    const q1 = s1.questions[0];
    expect(q1.question_text).toBe('How did you hear about us?');
    expect(q1.question_type).toBe('choice');

    // Choices
    expect(q1.choices.length).toBeGreaterThanOrEqual(2);
    expect(q1.choices[0].choice_label).toBe('Social Media');
    expect(q1.choices[1].choice_label).toBe('Word of Mouth');
  });

  test('update: add second section via API and verify in UI', async ({ page, ui, api }) => {
    const data = await api.readEntity(ENTITY, createdId);
    const fields = data.fields;

    // Add a second section via API
    fields.sections.push({
      section_title: 'Satisfaction',
      section_description: 'Rate your experience.',
      has_scoring: false,
      passing_score: 0,
      scoring_mode: 'simple',
      score_explanation: '',
      questions: [
        {
          question_text: 'Overall satisfaction?',
          question_type: 'rating',
          is_required: true,
          help_text: 'Rate from 1 to 5.',
          min_rating: 1,
          max_rating: 5,
        },
      ],
    });

    await api.updateEntity(ENTITY, {
      id: createdId,
      title: data.title,
      fields,
    });

    // Verify in the UI
    await ui.gotoEditEntity(ENTITY, createdId);
    await expect(ui.repeaterItems('Sections')).toHaveCount(2);

    // The second section should have "Satisfaction" as its title
    const secondSection = ui.repeaterItems('Sections').nth(1);
    const titleInput = secondSection.locator('input[type="text"]').first();
    await expect(titleInput).toHaveValue('Satisfaction');
  });
});
