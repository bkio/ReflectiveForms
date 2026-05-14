import { describe, it, before, after } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'fs';
import path from 'path';
import { execSync, spawn } from 'child_process';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const TEMPLATES_DIR = path.join(__dirname, '..', 'templates');
const REPO_ROOT = path.join(__dirname, '..', '..');

import { aiReplacements, infraReplacements } from '../src/index.js';

// Inline copyTemplate since the CLI mixes prompts with logic
function copyTemplate(src, dest, replacements) {
  if (fs.statSync(src).isDirectory()) {
    fs.mkdirSync(dest, { recursive: true });
    for (const entry of fs.readdirSync(src)) {
      copyTemplate(path.join(src, entry), path.join(dest, entry), replacements);
    }
  } else {
    let content = fs.readFileSync(src, 'utf-8');
    for (let pass = 0; pass < 2; pass++) {
      for (const [placeholder, value] of Object.entries(replacements)) {
        content = content.replaceAll(`{{${placeholder}}}`, value);
      }
    }
    fs.writeFileSync(dest, content);
  }
}

const TEST_DIR = path.join(__dirname, '..', '.test-output');

const REPLACEMENTS = {
  PROJECT_NAME: 'test-app',
  APP_NAME: 'Test App',
  PRIMARY_COLOR: '#ff6600',
  BACKEND_PORT: '4000',
  FRONTEND_PORT: '4001',
  CSPROJ_NAME: 'test.app',
  SHEETS_CONFIG: '',
  ...infraReplacements('local'),
  ...aiReplacements(false),
};

const AI_REPLACEMENTS = {
  PROJECT_NAME: 'test-app',
  APP_NAME: 'Test App',
  PRIMARY_COLOR: '#ff6600',
  BACKEND_PORT: '4000',
  FRONTEND_PORT: '4001',
  CSPROJ_NAME: 'test.app',
  SHEETS_CONFIG: '',
  ...infraReplacements('local'),
  ...aiReplacements(true),
};

// ─────────────────────────────────────────────────────────────────────────────
// Group 1: Unit tests for infraReplacements()
// ─────────────────────────────────────────────────────────────────────────────

const INFRA_REQUIRED_KEYS = [
  'INFRA_USING_STATEMENTS', 'INFRA_SERVICE_INIT', 'INFRA_CSPROJ_PACKAGES',
  'INFRA_ENV_VARS', 'INFRA_DOCKER_SERVICES', 'INFRA_DOCKER_ENV',
  'INFRA_DOCKER_DEPENDS', 'INFRA_README_SECTION',
];

describe('infraReplacements() unit tests', () => {
  for (const stack of ['local', 'aws', 'gcp', 'mongo']) {
    it(`${stack} stack returns all required keys`, () => {
      const r = infraReplacements(stack);
      for (const key of INFRA_REQUIRED_KEYS) {
        assert.ok(key in r, `${stack} stack missing key "${key}"`);
        assert.equal(typeof r[key], 'string', `${stack} "${key}" should be a string`);
      }
    });
  }

  it('unknown stack falls back to local', () => {
    const unknown = infraReplacements('azure');
    const local = infraReplacements('local');
    for (const key of INFRA_REQUIRED_KEYS) {
      assert.equal(unknown[key], local[key], `fallback mismatch for "${key}"`);
    }
  });

  it('undefined stack falls back to local', () => {
    const undef = infraReplacements(undefined);
    const local = infraReplacements('local');
    for (const key of INFRA_REQUIRED_KEYS) {
      assert.equal(undef[key], local[key], `fallback mismatch for "${key}"`);
    }
  });

  it('all stacks use the same CCK_VERSION in csproj packages', () => {
    const versions = new Set();
    for (const stack of ['local', 'aws', 'gcp', 'mongo']) {
      const r = infraReplacements(stack);
      const found = [...r.INFRA_CSPROJ_PACKAGES.matchAll(/Version="([^"]+)"/g)].map(m => m[1]);
      assert.ok(found.length > 0, `${stack} should have version strings`);
      for (const v of found) versions.add(v);
    }
    assert.equal(versions.size, 1, `All stacks should use same CCK_VERSION, found: ${[...versions].join(', ')}`);
  });

  it('all stacks produce exactly 4 using statements', () => {
    for (const stack of ['local', 'aws', 'gcp', 'mongo']) {
      const r = infraReplacements(stack);
      const usings = r.INFRA_USING_STATEMENTS.split('\n').filter(l => l.startsWith('using '));
      assert.equal(usings.length, 4, `${stack} should have 4 using statements, got ${usings.length}`);
    }
  });

  it('all stacks produce exactly 4 csproj package references', () => {
    for (const stack of ['local', 'aws', 'gcp', 'mongo']) {
      const r = infraReplacements(stack);
      const refs = [...r.INFRA_CSPROJ_PACKAGES.matchAll(/PackageReference/g)];
      assert.equal(refs.length, 4, `${stack} should have 4 PackageReference entries, got ${refs.length}`);
    }
  });

  it('all stacks have matching usings and csproj packages', () => {
    for (const stack of ['local', 'aws', 'gcp', 'mongo']) {
      const r = infraReplacements(stack);
      const usings = [...r.INFRA_USING_STATEMENTS.matchAll(/using (CrossCloudKit(?:\.\w+)+);/g)]
        .map(m => m[1]);
      for (const ns of usings) {
        assert.ok(
          r.INFRA_CSPROJ_PACKAGES.includes(`"${ns}"`),
          `${stack}: using "${ns}" has no matching PackageReference`
        );
      }
    }
  });

  it('local stack has empty docker fields', () => {
    const r = infraReplacements('local');
    assert.equal(r.INFRA_DOCKER_SERVICES, '', 'local should have empty docker services');
    assert.equal(r.INFRA_DOCKER_ENV, '', 'local should have empty docker env');
    assert.equal(r.INFRA_DOCKER_DEPENDS, '', 'local should have empty docker depends');
  });

  it('non-local stacks have non-empty docker services', () => {
    for (const stack of ['aws', 'gcp', 'mongo']) {
      const r = infraReplacements(stack);
      assert.ok(r.INFRA_DOCKER_SERVICES.length > 0, `${stack} should have docker services`);
      assert.ok(r.INFRA_DOCKER_ENV.length > 0, `${stack} should have docker env`);
      assert.ok(r.INFRA_DOCKER_DEPENDS.length > 0, `${stack} should have docker depends`);
    }
  });

  it('each stack INFRA_README_SECTION is non-empty and mentions the stack', () => {
    const stackKeywords = { local: 'local', aws: 'AWS', gcp: 'Google Cloud', mongo: 'MongoDB' };
    for (const [stack, keyword] of Object.entries(stackKeywords)) {
      const r = infraReplacements(stack);
      assert.ok(r.INFRA_README_SECTION.length > 0, `${stack} readme should be non-empty`);
      assert.ok(
        r.INFRA_README_SECTION.toLowerCase().includes(keyword.toLowerCase()),
        `${stack} readme should mention "${keyword}"`
      );
    }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Group 2: Unit tests for aiReplacements()
// ─────────────────────────────────────────────────────────────────────────────

const AI_REQUIRED_KEYS = [
  'AI_USING_STATEMENTS', 'AI_SERVICE_INIT', 'AI_BUILDER_CONFIG',
  'AI_ENTITY_FLAGS', 'AI_NOTE_ATTRIBUTES', 'AI_CSPROJ_PACKAGES',
  'AI_ENV_VARS', 'AI_DOCKER_ENV', 'AI_README_SECTION',
];

describe('aiReplacements() unit tests', () => {
  it('aiReplacements(false) returns all required keys', () => {
    const r = aiReplacements(false);
    for (const key of AI_REQUIRED_KEYS) {
      assert.ok(key in r, `disabled AI missing key "${key}"`);
      assert.equal(typeof r[key], 'string');
    }
  });

  it('aiReplacements(true) returns all required keys', () => {
    const r = aiReplacements(true);
    for (const key of AI_REQUIRED_KEYS) {
      assert.ok(key in r, `enabled AI missing key "${key}"`);
      assert.equal(typeof r[key], 'string');
    }
  });

  it('aiReplacements(false) produces empty code-injection keys', () => {
    const r = aiReplacements(false);
    const codeKeys = [
      'AI_USING_STATEMENTS', 'AI_SERVICE_INIT', 'AI_BUILDER_CONFIG',
      'AI_ENTITY_FLAGS', 'AI_NOTE_ATTRIBUTES', 'AI_CSPROJ_PACKAGES',
    ];
    for (const key of codeKeys) {
      assert.equal(r[key], '', `disabled AI "${key}" should be empty`);
    }
  });

  it('aiReplacements(true) has matching usings and csproj packages', () => {
    const r = aiReplacements(true);
    const usings = [...r.AI_USING_STATEMENTS.matchAll(/using (CrossCloudKit(?:\.\w+)+);/g)]
      .map(m => m[1]);
    assert.ok(usings.length > 0, 'AI enabled should have CrossCloudKit usings');
    for (const ns of usings) {
      assert.ok(
        r.AI_CSPROJ_PACKAGES.includes(ns),
        `AI using "${ns}" has no matching PackageReference`
      );
    }
  });

  it('aiReplacements(true) AI_BUILDER_CONFIG references variables from AI_SERVICE_INIT', () => {
    const r = aiReplacements(true);
    assert.ok(r.AI_BUILDER_CONFIG.includes('llmService'), 'AI_BUILDER_CONFIG should reference llmService');
    assert.ok(r.AI_BUILDER_CONFIG.includes('vectorService'), 'AI_BUILDER_CONFIG should reference vectorService');
    assert.ok(r.AI_SERVICE_INIT.includes('llmService'), 'AI_SERVICE_INIT should define llmService');
    assert.ok(r.AI_SERVICE_INIT.includes('vectorService'), 'AI_SERVICE_INIT should define vectorService');
  });

  it('aiReplacements(true) AI_ENTITY_FLAGS includes EntityDescription', () => {
    const r = aiReplacements(true);
    assert.ok(r.AI_ENTITY_FLAGS.includes('EntityDescription'), 'AI entity flags should include EntityDescription');
  });

  it('aiReplacements(false) AI_README_SECTION mentions re-scaffold', () => {
    const r = aiReplacements(false);
    assert.ok(
      r.AI_README_SECTION.toLowerCase().includes('re-scaffold') || r.AI_README_SECTION.includes('create-reflectiveforms-app'),
      'Disabled AI readme should mention how to re-scaffold'
    );
  });

  it('aiReplacements(true) AI_README_SECTION mentions switching to external provider', () => {
    const r = aiReplacements(true);
    assert.ok(
      r.AI_README_SECTION.includes('LLMServiceOpenAI') || r.AI_README_SECTION.includes('external'),
      'Enabled AI readme should mention switching to external provider'
    );
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Group 5: CSPROJ_NAME transformation edge cases
// ─────────────────────────────────────────────────────────────────────────────

describe('CSPROJ_NAME edge cases', () => {
  const csprojTransform = (name) => name.replace(/[^a-zA-Z0-9]/g, '.');

  it('hyphens become dots', () => {
    assert.equal(csprojTransform('my-app'), 'my.app');
  });

  it('underscores become dots', () => {
    assert.equal(csprojTransform('my_app'), 'my.app');
  });

  it('multiple special chars', () => {
    assert.equal(csprojTransform('my-cool_app'), 'my.cool.app');
  });

  it('numeric start preserved', () => {
    assert.equal(csprojTransform('123app'), '123app');
  });

  it('already clean name unchanged', () => {
    assert.equal(csprojTransform('myapp'), 'myapp');
  });

  it('scaffold with special-char project name has no remaining placeholders', () => {
    const dir = path.join(__dirname, '..', '.test-special-name-output');
    const replacements = {
      ...REPLACEMENTS,
      PROJECT_NAME: 'my-cool_app',
      CSPROJ_NAME: csprojTransform('my-cool_app'),
    };
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, replacements);
      function checkDir(d) {
        for (const entry of fs.readdirSync(d)) {
          const full = path.join(d, entry);
          if (fs.statSync(full).isDirectory()) { checkDir(full); }
          else {
            const content = fs.readFileSync(full, 'utf-8');
            const match = content.match(/\{\{[A-Z_]+\}\}/);
            assert.equal(match, null, `Found unreplaced placeholder ${match?.[0]} in ${full}`);
          }
        }
      }
      checkDir(dir);
    } finally {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
    }
  });
});

describe('scaffold', () => {
  before(() => {
    if (fs.existsSync(TEST_DIR)) {
      fs.rmSync(TEST_DIR, { recursive: true });
    }
    copyTemplate(TEMPLATES_DIR, TEST_DIR, REPLACEMENTS);
  });

  after(() => {
    if (fs.existsSync(TEST_DIR)) {
      fs.rmSync(TEST_DIR, { recursive: true });
    }
  });

  it('creates backend directory', () => {
    assert.ok(fs.existsSync(path.join(TEST_DIR, 'backend')));
  });

  it('creates frontend directory', () => {
    assert.ok(fs.existsSync(path.join(TEST_DIR, 'frontend')));
  });

  it('creates docker-compose.yml', () => {
    assert.ok(fs.existsSync(path.join(TEST_DIR, 'docker-compose.yml')));
  });

  it('creates .env.example', () => {
    assert.ok(fs.existsSync(path.join(TEST_DIR, '.env.example')));
  });

  it('creates .gitignore', () => {
    assert.ok(fs.existsSync(path.join(TEST_DIR, '.gitignore')));
  });

  it('replaces PROJECT_NAME in README.md', () => {
    const readme = fs.readFileSync(path.join(TEST_DIR, 'README.md'), 'utf-8');
    assert.ok(readme.includes('test-app'));
    assert.ok(!readme.includes('{{PROJECT_NAME}}'));
  });

  it('replaces APP_NAME in frontend index.html', () => {
    const html = fs.readFileSync(path.join(TEST_DIR, 'frontend', 'index.html'), 'utf-8');
    assert.ok(html.includes('Test App'));
    assert.ok(!html.includes('{{APP_NAME}}'));
  });

  it('replaces PRIMARY_COLOR in frontend rf.config.ts', () => {
    const config = fs.readFileSync(path.join(TEST_DIR, 'frontend', 'src', 'rf.config.ts'), 'utf-8');
    assert.ok(config.includes('#ff6600'));
    assert.ok(!config.includes('{{PRIMARY_COLOR}}'));
    assert.ok(config.includes('VITE_API_BASE_URL'), 'should use correct env var name VITE_API_BASE_URL');
  });

  it('replaces BACKEND_PORT in docker-compose.yml', () => {
    const dc = fs.readFileSync(path.join(TEST_DIR, 'docker-compose.yml'), 'utf-8');
    assert.ok(dc.includes('4000'));
    assert.ok(!dc.includes('{{BACKEND_PORT}}'));
  });

  it('replaces FRONTEND_PORT in vite.config.ts', () => {
    const vite = fs.readFileSync(path.join(TEST_DIR, 'frontend', 'vite.config.ts'), 'utf-8');
    assert.ok(vite.includes('4001'));
    assert.ok(!vite.includes('{{FRONTEND_PORT}}'));
  });

  it('replaces CSPROJ_NAME in backend.csproj', () => {
    const csproj = fs.readFileSync(path.join(TEST_DIR, 'backend', 'backend.csproj'), 'utf-8');
    assert.ok(csproj.includes('test.app'));
    assert.ok(!csproj.includes('{{CSPROJ_NAME}}'));
  });

  it('creates sample NoteModel', () => {
    const note = fs.readFileSync(path.join(TEST_DIR, 'backend', 'Models', 'NoteModel.cs'), 'utf-8');
    assert.ok(note.includes('NoteModel'));
    assert.ok(note.includes('WysiwygEditor'));
    assert.ok(note.includes('instructions:'), 'NoteModel attributes must include instructions parameter');
  });

  it('backend Program.cs has correct port', () => {
    const prog = fs.readFileSync(path.join(TEST_DIR, 'backend', 'Program.cs'), 'utf-8');
    assert.ok(prog.includes('4000'));
    assert.ok(!prog.includes('{{BACKEND_PORT}}'));
  });

  it('frontend main.tsx imports from @reflectiveforms/frontend', () => {
    const main = fs.readFileSync(path.join(TEST_DIR, 'frontend', 'src', 'main.tsx'), 'utf-8');
    assert.ok(main.includes('@reflectiveforms/frontend'));
  });

  it('nginx.conf has SPA fallback', () => {
    const nginx = fs.readFileSync(path.join(TEST_DIR, 'frontend', 'nginx.conf'), 'utf-8');
    assert.ok(nginx.includes('try_files'));
    assert.ok(nginx.includes('/index.html'));
  });

  it('nginx.conf has WebSocket upgrade headers', () => {
    const nginx = fs.readFileSync(path.join(TEST_DIR, 'frontend', 'nginx.conf'), 'utf-8');
    assert.ok(nginx.includes('proxy_set_header Upgrade'), 'nginx should proxy WebSocket Upgrade header');
    assert.ok(nginx.includes('proxy_http_version 1.1'), 'nginx should use HTTP/1.1 for WebSocket');
  });

  it('no remaining placeholders in any file', () => {
    function checkDir(dir) {
      for (const entry of fs.readdirSync(dir)) {
        const full = path.join(dir, entry);
        if (fs.statSync(full).isDirectory()) {
          checkDir(full);
        } else {
          const content = fs.readFileSync(full, 'utf-8');
          const match = content.match(/\{\{[A-Z_]+\}\}/);
          assert.equal(match, null, `Found unreplaced placeholder ${match?.[0]} in ${full}`);
        }
      }
    }
    checkDir(TEST_DIR);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// AI-enabled scaffold: verify AI templates are applied correctly
// ─────────────────────────────────────────────────────────────────────────────

const AI_TEST_DIR = path.join(__dirname, '..', '.test-ai-output');

describe('scaffold (AI enabled)', () => {
  before(() => {
    if (fs.existsSync(AI_TEST_DIR)) {
      fs.rmSync(AI_TEST_DIR, { recursive: true });
    }
    copyTemplate(TEMPLATES_DIR, AI_TEST_DIR, AI_REPLACEMENTS);
  });

  after(() => {
    if (fs.existsSync(AI_TEST_DIR)) {
      fs.rmSync(AI_TEST_DIR, { recursive: true });
    }
  });

  it('RfBuilder.cs includes AI using statements', () => {
    const rfBuilder = fs.readFileSync(path.join(AI_TEST_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(rfBuilder.includes('using CrossCloudKit.LLM.Basic;'));
    assert.ok(rfBuilder.includes('using CrossCloudKit.Vector.Basic;'));
    assert.ok(rfBuilder.includes('using ReflectiveForms.Core.Ai;'));
  });

  it('RfBuilder.cs initialises AI services', () => {
    const rfBuilder = fs.readFileSync(path.join(AI_TEST_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(rfBuilder.includes('new LLMServiceBasic()'));
    assert.ok(rfBuilder.includes('new VectorServiceBasic()'));
  });

  it('RfBuilder.cs sets AiServiceConfiguration on builder', () => {
    const rfBuilder = fs.readFileSync(path.join(AI_TEST_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(rfBuilder.includes('AiServiceConfiguration = new AiServiceConfiguration('));
    assert.ok(rfBuilder.includes('HeavyLlmService: llmService'));
    assert.ok(rfBuilder.includes('LightLlmService: llmService'));
    assert.ok(rfBuilder.includes('VectorService: vectorService'));
  });

  it('RfBuilder.cs sets AI entity flags', () => {
    const rfBuilder = fs.readFileSync(path.join(AI_TEST_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(rfBuilder.includes('EntityDescription ='), 'AI entity should have EntityDescription');
    assert.ok(rfBuilder.includes('SupportsSemanticSearch = true'));
    assert.ok(rfBuilder.includes('SupportsAiGeneration = true'));
    assert.ok(rfBuilder.includes('SupportsAiDiffSummary = true'));
    assert.ok(rfBuilder.includes('SupportsNaturalLanguageFilter = true'));
  });

  it('NoteModel.cs includes AI attributes', () => {
    const noteModel = fs.readFileSync(path.join(AI_TEST_DIR, 'backend', 'Models', 'NoteModel.cs'), 'utf-8');
    assert.ok(noteModel.includes('[AISanityCheck('));
    assert.ok(noteModel.includes('[AISuggestion('));
    assert.ok(noteModel.includes('using ReflectiveForms.Core.Attributes;'),
      'NoteModel should import ReflectiveForms.Core.Attributes for AI attributes');
  });

  it('backend.csproj includes AI NuGet packages', () => {
    const csproj = fs.readFileSync(path.join(AI_TEST_DIR, 'backend', 'backend.csproj'), 'utf-8');
    assert.ok(csproj.includes('"CrossCloudKit.LLM.Basic"'));
    assert.ok(csproj.includes('"CrossCloudKit.Vector.Basic"'));
  });

  it('.env.example includes AI section', () => {
    const env = fs.readFileSync(path.join(AI_TEST_DIR, '.env.example'), 'utf-8');
    assert.ok(env.includes('LLM_BASE_URL'));
    assert.ok(env.includes('LLM_MODEL'));
  });

  it('README.md includes AI features documentation', () => {
    const readme = fs.readFileSync(path.join(AI_TEST_DIR, 'README.md'), 'utf-8');
    assert.ok(readme.includes('## AI Features'));
    assert.ok(readme.includes('Semantic Search'));
    assert.ok(readme.includes('AI Generation'));
    assert.ok(readme.includes('LLMServiceOpenAI'));
  });

  it('no remaining placeholders in any file', () => {
    function checkDir(dir) {
      for (const entry of fs.readdirSync(dir)) {
        const full = path.join(dir, entry);
        if (fs.statSync(full).isDirectory()) {
          checkDir(full);
        } else {
          const content = fs.readFileSync(full, 'utf-8');
          const match = content.match(/\{\{[A-Z_]+\}\}/);
          assert.equal(match, null, `Found unreplaced placeholder ${match?.[0]} in ${full}`);
        }
      }
    }
    checkDir(AI_TEST_DIR);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// AI-disabled scaffold: verify AI placeholders are cleaned up
// ─────────────────────────────────────────────────────────────────────────────

describe('scaffold (AI disabled)', () => {
  before(() => {
    if (fs.existsSync(TEST_DIR)) {
      fs.rmSync(TEST_DIR, { recursive: true });
    }
    copyTemplate(TEMPLATES_DIR, TEST_DIR, REPLACEMENTS);
  });

  after(() => {
    if (fs.existsSync(TEST_DIR)) {
      fs.rmSync(TEST_DIR, { recursive: true });
    }
  });

  it('RfBuilder.cs does NOT include AI using statements', () => {
    const rfBuilder = fs.readFileSync(path.join(TEST_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(!rfBuilder.includes('CrossCloudKit.LLM'));
    assert.ok(!rfBuilder.includes('CrossCloudKit.Vector'));
    assert.ok(!rfBuilder.includes('ReflectiveForms.Core.Ai'));
  });

  it('RfBuilder.cs does NOT set AiServiceConfiguration', () => {
    const rfBuilder = fs.readFileSync(path.join(TEST_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(!rfBuilder.includes('AiServiceConfiguration'));
    assert.ok(!rfBuilder.includes('LLMServiceBasic'));
    assert.ok(!rfBuilder.includes('VectorServiceBasic'));
  });

  it('RfBuilder.cs does NOT set AI entity flags', () => {
    const rfBuilder = fs.readFileSync(path.join(TEST_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(!rfBuilder.includes('SupportsSemanticSearch'));
    assert.ok(!rfBuilder.includes('SupportsAiGeneration'));
    assert.ok(!rfBuilder.includes('SupportsAiDiffSummary'));
    assert.ok(!rfBuilder.includes('SupportsNaturalLanguageFilter'));
  });

  it('NoteModel.cs does NOT include AI attributes', () => {
    const noteModel = fs.readFileSync(path.join(TEST_DIR, 'backend', 'Models', 'NoteModel.cs'), 'utf-8');
    assert.ok(!noteModel.includes('AISanityCheck'));
    assert.ok(!noteModel.includes('AISuggestion'));
  });

  it('backend.csproj does NOT include AI packages', () => {
    const csproj = fs.readFileSync(path.join(TEST_DIR, 'backend', 'backend.csproj'), 'utf-8');
    assert.ok(!csproj.includes('CrossCloudKit.LLM'));
    assert.ok(!csproj.includes('CrossCloudKit.Vector'));
  });

  it('README.md shows AI as optional', () => {
    const readme = fs.readFileSync(path.join(TEST_DIR, 'README.md'), 'utf-8');
    assert.ok(readme.includes('AI Features (Optional)'));
    assert.ok(!readme.includes('LLMServiceOpenAI'));
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Sync-check: verify CreateApp templates stay up-to-date with actual codebase
// Uses AI-enabled rendered output to check the "full-featured" template path.
// ─────────────────────────────────────────────────────────────────────────────

/** Read a template file with AI-enabled replacements applied */
function readRenderedTemplate(relPath) {
  let content = fs.readFileSync(path.join(TEMPLATES_DIR, relPath), 'utf-8');
  for (let pass = 0; pass < 2; pass++) {
    for (const [placeholder, value] of Object.entries(AI_REPLACEMENTS)) {
      content = content.replaceAll(`{{${placeholder}}}`, value);
    }
  }
  return content;
}

describe('sync-check: templates match framework', () => {

  // ── Backend: EntityConfigurationBuilder required properties ───────────────
  it('template RfBuilder.cs sets all required EntityConfigurationBuilder properties', () => {
    const ecbSource = fs.readFileSync(
      path.join(REPO_ROOT, 'ReflectiveForms.Core', 'EntityConfigurationBuilder.cs'), 'utf-8');
    const templateRfBuilder = readRenderedTemplate('backend/RfBuilder.cs');

    // Extract all "public required ... PropertyName { get; init; }" from the base class
    const requiredProps = [...ecbSource.matchAll(/public required \S+ (\w+)\s*\{/g)]
      .map(m => m[1]);
    assert.ok(requiredProps.length >= 9, `Expected at least 9 required properties, found ${requiredProps.length}`);

    for (const prop of requiredProps) {
      assert.ok(
        templateRfBuilder.includes(prop),
        `Template RfBuilder.cs is missing required property "${prop}" from EntityConfigurationBuilder`
      );
    }
  });

  // ── Backend: NuGet packages match using statements ───────────────────────
  it('template csproj includes all NuGet packages used by RfBuilder.cs', () => {
    const templateRfBuilder = readRenderedTemplate('backend/RfBuilder.cs');
    const templateCsproj = readRenderedTemplate('backend/backend.csproj');

    // Every "using CrossCloudKit.X.Y[.Z];" in RfBuilder means a package is needed
    // Package name = using namespace (e.g. CrossCloudKit.LLM.Basic)
    const crossCloudKitUsings = [...templateRfBuilder.matchAll(/using (CrossCloudKit(?:\.\w+)+);/g)]
      .map(m => m[1]);
    assert.ok(crossCloudKitUsings.length > 0, 'RfBuilder.cs should have CrossCloudKit using statements');

    for (const pkg of crossCloudKitUsings) {
      assert.ok(
        templateCsproj.includes(`"${pkg}"`),
        `Template backend.csproj is missing PackageReference for "${pkg}" used in RfBuilder.cs`
      );
    }

    // Also must reference ReflectiveForms.Core
    assert.ok(
      templateCsproj.includes('ReflectiveForms.Core'),
      'Template backend.csproj must reference ReflectiveForms.Core'
    );
  });

  // ── Backend: CrossCloudKit package versions match Sample1 ────────────────
  it('template CrossCloudKit package versions match Sample1', () => {
    const templateCsproj = readRenderedTemplate('backend/backend.csproj');
    const sample1Csproj = fs.readFileSync(
      path.join(REPO_ROOT, 'ReflectiveForms.Sample1', 'ReflectiveForms.Sample1.csproj'), 'utf-8');

    const extractVersions = (content) => {
      const versions = {};
      for (const m of content.matchAll(/Include="(CrossCloudKit\.[^"]+)"\s+Version="([^"]+)"/g)) {
        versions[m[1]] = m[2];
      }
      return versions;
    };

    const sample1Versions = extractVersions(sample1Csproj);
    const templateVersions = extractVersions(templateCsproj);

    for (const [pkg, version] of Object.entries(sample1Versions)) {
      if (templateVersions[pkg]) {
        assert.equal(
          templateVersions[pkg], version,
          `Template version of ${pkg} (${templateVersions[pkg]}) differs from Sample1 (${version})`
        );
      }
    }
  });

  // ── Backend: field attribute constructors include instructions ────────────
  it('template NoteModel uses correct field attribute constructor signatures', () => {
    const noteModel = readRenderedTemplate('backend/Models/NoteModel.cs');

    // Read all field attribute classes to extract required constructor params
    const fieldsDir = path.join(REPO_ROOT, 'ReflectiveForms.Core', 'Attributes', 'Fields');
    const attributeFiles = fs.readdirSync(fieldsDir).filter(f => f.endsWith('.cs'));

    // Every attribute used in NoteModel must have correct constructor call
    const usedAttributes = [...noteModel.matchAll(/\[(?:.*,\s*)?(\w+)\s*\(/g)].map(m => m[1]);
    for (const attr of usedAttributes) {
      const attrFile = attributeFiles.find(f => f === `${attr}.cs`);
      if (!attrFile) continue;

      const attrSource = fs.readFileSync(path.join(fieldsDir, attrFile), 'utf-8');
      // If any constructor has "string instructions" param, NoteModel must include it
      if (attrSource.includes('string instructions')) {
        assert.ok(
          noteModel.includes(`${attr}(`) && noteModel.includes('instructions:'),
          `NoteModel uses ${attr} but is missing required "instructions" parameter`
        );
      }
    }
  });

  // ── Backend: Program.cs does not add redundant CORS ──────────────────────
  it('template Program.cs does not add redundant CORS (framework handles it)', () => {
    const programCs = fs.readFileSync(
      path.join(TEMPLATES_DIR, 'backend', 'Program.cs'), 'utf-8');
    assert.ok(!programCs.includes('AddCors'), 'Program.cs should not call AddCors — BuildWithReflectiveFields handles CORS');
    assert.ok(!programCs.includes('UseCors'), 'Program.cs should not call UseCors — BuildWithReflectiveFields handles CORS');
  });

  // ── Frontend: rf.config.ts uses valid RfConfig properties ────────────────
  it('template rf.config.ts only uses valid RfConfig properties', () => {
    const rfConfigTypes = fs.readFileSync(
      path.join(REPO_ROOT, 'ReflectiveForms.Frontend', 'src', 'lib', 'types.ts'), 'utf-8');
    const templateConfig = fs.readFileSync(
      path.join(TEMPLATES_DIR, 'frontend', 'src', 'rf.config.ts'), 'utf-8');

    // Extract top-level property names from "export interface RfConfig { ... }"
    // We need to handle nested objects, so match the block between the interface
    // opening brace and its matching closing brace (track depth).
    const ifaceStart = rfConfigTypes.indexOf('interface RfConfig {');
    assert.ok(ifaceStart !== -1, 'RfConfig interface not found in types.ts');
    let depth = 0;
    let inBlock = false;
    let blockContent = '';
    for (let i = ifaceStart; i < rfConfigTypes.length; i++) {
      if (rfConfigTypes[i] === '{') { depth++; inBlock = true; }
      if (inBlock) blockContent += rfConfigTypes[i];
      if (rfConfigTypes[i] === '}') { depth--; if (depth === 0 && inBlock) break; }
    }

    // Extract only top-level properties (depth 1 — lines that start with a word at indent level 1)
    // Simple approach: extract "propertyName" from lines with exactly 2 spaces of indent
    const validProps = [...blockContent.matchAll(/^\s{2}(\w+)\??\s*:/gm)].map(m => m[1]);
    assert.ok(validProps.length >= 3, `Should find at least 3 RfConfig properties, found: ${validProps.join(', ')}`);

    // Extract assigned properties from template (non-commented lines)
    const activeLines = templateConfig.split('\n').filter(l => !l.trimStart().startsWith('//'));
    const assignedProps = [...activeLines.join('\n').matchAll(/^\s+(\w+)\s*:/gm)].map(m => m[1]);

    for (const prop of assignedProps) {
      assert.ok(
        validProps.includes(prop),
        `Template rf.config.ts uses property "${prop}" which is not in the RfConfig interface`
      );
    }
  });

  // ── Frontend: env variable name matches actual frontend ──────────────────
  it('template uses same VITE env variable name as real frontend', () => {
    const realMain = fs.readFileSync(
      path.join(REPO_ROOT, 'ReflectiveForms.Frontend', 'src', 'main.tsx'), 'utf-8');
    const templateConfig = fs.readFileSync(
      path.join(TEMPLATES_DIR, 'frontend', 'src', 'rf.config.ts'), 'utf-8');

    const realEnvVar = realMain.match(/import\.meta\.env\.(\w+)/)?.[1];
    assert.ok(realEnvVar, 'Real frontend main.tsx should use import.meta.env.VITE_*');

    assert.ok(
      templateConfig.includes(realEnvVar),
      `Template uses different env variable than real frontend (expected ${realEnvVar})`
    );
  });

  // ── Frontend: vite proxy has WebSocket support ───────────────────────────
  it('template vite.config.ts has WebSocket proxy support matching real config', () => {
    const realVite = fs.readFileSync(
      path.join(REPO_ROOT, 'ReflectiveForms.Frontend', 'vite.config.ts'), 'utf-8');
    const templateVite = fs.readFileSync(
      path.join(TEMPLATES_DIR, 'frontend', 'vite.config.ts'), 'utf-8');

    // If real config has ws:true, template must too
    if (realVite.includes('ws: true') || realVite.includes('ws:true')) {
      assert.ok(
        templateVite.includes('ws: true') || templateVite.includes('ws:true'),
        'Template vite.config.ts must have ws: true in proxy (real config has it for live updates)'
      );
    }
  });

  // ── Frontend: tailwind content includes RF library ───────────────────────
  it('template tailwind.config.js includes @reflectiveforms/frontend content path', () => {
    const templateTailwind = fs.readFileSync(
      path.join(TEMPLATES_DIR, 'frontend', 'tailwind.config.js'), 'utf-8');
    assert.ok(
      templateTailwind.includes('@reflectiveforms/frontend'),
      'Template tailwind.config.js must include @reflectiveforms/frontend in content paths for Tailwind to process library styles'
    );
  });

  // ── Frontend: nginx has WebSocket upgrade for live updates ───────────────
  it('template nginx.conf has WebSocket upgrade headers', () => {
    const nginx = fs.readFileSync(
      path.join(TEMPLATES_DIR, 'frontend', 'nginx.conf'), 'utf-8');
    assert.ok(nginx.includes('Upgrade'), 'nginx.conf must proxy WebSocket Upgrade header');
    assert.ok(nginx.includes('proxy_http_version 1.1'), 'nginx.conf must use HTTP/1.1 for WebSocket');
  });

  // ── Backend: template mentions optional EntityConfigurationBuilder props ─
  it('template RfBuilder.cs documents optional EntityConfigurationBuilder properties', () => {
    const templateRfBuilder = fs.readFileSync(
      path.join(TEMPLATES_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');

    const ecbSource = fs.readFileSync(
      path.join(REPO_ROOT, 'ReflectiveForms.Core', 'EntityConfigurationBuilder.cs'), 'utf-8');

    // Find optional (non-required) public properties with init setters
    const allProps = [...ecbSource.matchAll(/public (?!required)\S+\??\s+(\w+)\s*\{\s*get;\s*init;/g)]
      .map(m => m[1]);
    const optionalProps = allProps.filter(p =>
      ['HasIndividualSharing', 'CustomFrontendListRoute', 'HooksSetup'].includes(p));

    for (const prop of optionalProps) {
      assert.ok(
        templateRfBuilder.includes(prop),
        `Template RfBuilder.cs should document optional property "${prop}" (even as a comment)`
      );
    }
  });

  // ── Backend: template mentions optional EndpointConfiguration props ──────
  it('template RfBuilder.cs documents SsoConfiguration and OpenApi options', () => {
    const templateRfBuilder = fs.readFileSync(
      path.join(TEMPLATES_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(
      templateRfBuilder.includes('SsoConfiguration'),
      'Template should document SsoConfiguration (even as a comment)'
    );
    assert.ok(
      templateRfBuilder.includes('OpenApi'),
      'Template should document OpenApi / OpenApiConfiguration (even as a comment)'
    );
  });

  // ── Backend: template mentions EditInactivityTimeoutMs ───────────────────
  it('template RfBuilder.cs documents EditInactivityTimeoutMs', () => {
    const templateRfBuilder = fs.readFileSync(
      path.join(TEMPLATES_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(
      templateRfBuilder.includes('EditInactivityTimeoutMs'),
      'Template should document EditInactivityTimeoutMs (even as a comment)'
    );
  });

  // ── Backend: template mentions SheetsEnabled ────────────────────────────
  it('template RfBuilder.cs documents SheetsEnabled', () => {
    const templateRfBuilder = fs.readFileSync(
      path.join(TEMPLATES_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(
      templateRfBuilder.includes('SheetsEnabled') || templateRfBuilder.includes('SHEETS_CONFIG'),
      'Template should reference SheetsEnabled (via placeholder or comment)'
    );
  });

  // ── Frontend: template rf.config.ts documents AI config option ───────────
  it('template rf.config.ts documents AI display settings', () => {
    const templateConfig = fs.readFileSync(
      path.join(TEMPLATES_DIR, 'frontend', 'src', 'rf.config.ts'), 'utf-8');
    assert.ok(
      templateConfig.includes('disabled'),
      'Template rf.config.ts should document ai.disabled option (even as a comment)'
    );
    assert.ok(
      templateConfig.includes('aiEndpointBase'),
      'Template rf.config.ts should document ai.aiEndpointBase option (even as a comment)'
    );
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Sheets-disabled scaffold: verify SheetsEnabled = false is injected
// ─────────────────────────────────────────────────────────────────────────────

const SHEETS_DISABLED_DIR = path.join(__dirname, '..', '.test-sheets-disabled-output');
const SHEETS_DISABLED_REPLACEMENTS = {
  ...REPLACEMENTS,
  SHEETS_CONFIG: '            SheetsEnabled = false,',
};

describe('scaffold (sheets disabled)', () => {
  before(() => {
    if (fs.existsSync(SHEETS_DISABLED_DIR)) {
      fs.rmSync(SHEETS_DISABLED_DIR, { recursive: true });
    }
    copyTemplate(TEMPLATES_DIR, SHEETS_DISABLED_DIR, SHEETS_DISABLED_REPLACEMENTS);
  });

  after(() => {
    if (fs.existsSync(SHEETS_DISABLED_DIR)) {
      fs.rmSync(SHEETS_DISABLED_DIR, { recursive: true });
    }
  });

  it('RfBuilder.cs includes SheetsEnabled = false', () => {
    const rfBuilder = fs.readFileSync(path.join(SHEETS_DISABLED_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(rfBuilder.includes('SheetsEnabled = false'));
  });

  it('no remaining placeholders in any file', () => {
    function checkDir(dir) {
      for (const entry of fs.readdirSync(dir)) {
        const full = path.join(dir, entry);
        if (fs.statSync(full).isDirectory()) {
          checkDir(full);
        } else {
          const content = fs.readFileSync(full, 'utf-8');
          const match = content.match(/\{\{[A-Z_]+\}\}/);
          assert.equal(match, null, `Found unreplaced placeholder ${match?.[0]} in ${full}`);
        }
      }
    }
    checkDir(SHEETS_DISABLED_DIR);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Infrastructure stacks: AWS, GCP, MongoDB+Redis
// ─────────────────────────────────────────────────────────────────────────────

const AWS_DIR = path.join(__dirname, '..', '.test-aws-output');
const AWS_REPLACEMENTS = {
  ...REPLACEMENTS,
  ...infraReplacements('aws'),
};

describe('scaffold (AWS stack)', () => {
  before(() => {
    if (fs.existsSync(AWS_DIR)) fs.rmSync(AWS_DIR, { recursive: true });
    copyTemplate(TEMPLATES_DIR, AWS_DIR, AWS_REPLACEMENTS);
  });
  after(() => {
    if (fs.existsSync(AWS_DIR)) fs.rmSync(AWS_DIR, { recursive: true });
  });

  it('RfBuilder.cs uses AWS providers', () => {
    const rfBuilder = fs.readFileSync(path.join(AWS_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(rfBuilder.includes('using CrossCloudKit.Database.AWS;'));
    assert.ok(rfBuilder.includes('using CrossCloudKit.File.AWS;'));
    assert.ok(rfBuilder.includes('using CrossCloudKit.PubSub.AWS;'));
    assert.ok(rfBuilder.includes('using CrossCloudKit.Memory.Redis;'));
    assert.ok(rfBuilder.includes('DatabaseServiceAWS'));
    assert.ok(rfBuilder.includes('FileServiceAWS'));
    assert.ok(rfBuilder.includes('PubSubServiceAWS'));
    assert.ok(rfBuilder.includes('MemoryServiceRedis'));
    assert.ok(rfBuilder.includes('RedisConnectionOptions'));
  });

  it('RfBuilder.cs does NOT use Basic providers', () => {
    const rfBuilder = fs.readFileSync(path.join(AWS_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(!rfBuilder.includes('DatabaseServiceBasic'));
    assert.ok(!rfBuilder.includes('FileServiceBasic'));
    assert.ok(!rfBuilder.includes('PubSubServiceBasic'));
    assert.ok(!rfBuilder.includes('MemoryServiceBasic'));
  });

  it('backend.csproj references AWS packages', () => {
    const csproj = fs.readFileSync(path.join(AWS_DIR, 'backend', 'backend.csproj'), 'utf-8');
    assert.ok(csproj.includes('"CrossCloudKit.Database.AWS"'));
    assert.ok(csproj.includes('"CrossCloudKit.File.AWS"'));
    assert.ok(csproj.includes('"CrossCloudKit.PubSub.AWS"'));
    assert.ok(csproj.includes('"CrossCloudKit.Memory.Redis"'));
    assert.ok(!csproj.includes('"CrossCloudKit.Database.Basic"'));
  });

  it('.env.example includes AWS and Redis config', () => {
    const env = fs.readFileSync(path.join(AWS_DIR, '.env.example'), 'utf-8');
    assert.ok(env.includes('AWS_ACCESS_KEY'));
    assert.ok(env.includes('AWS_SECRET_KEY'));
    assert.ok(env.includes('AWS_REGION'));
    assert.ok(env.includes('REDIS_HOST'));
  });

  it('docker-compose.yml includes redis service', () => {
    const dc = fs.readFileSync(path.join(AWS_DIR, 'docker-compose.yml'), 'utf-8');
    assert.ok(dc.includes('redis:'));
    assert.ok(dc.includes('redis:7-alpine'));
    assert.ok(dc.includes('AWS_ACCESS_KEY'));
  });

  it('README.md documents AWS infrastructure', () => {
    const readme = fs.readFileSync(path.join(AWS_DIR, 'README.md'), 'utf-8');
    assert.ok(readme.includes('AWS'));
    assert.ok(readme.includes('DynamoDB'));
  });

  it('no remaining placeholders in any file', () => {
    function checkDir(dir) {
      for (const entry of fs.readdirSync(dir)) {
        const full = path.join(dir, entry);
        if (fs.statSync(full).isDirectory()) { checkDir(full); }
        else {
          const content = fs.readFileSync(full, 'utf-8');
          const match = content.match(/\{\{[A-Z_]+\}\}/);
          assert.equal(match, null, `Found unreplaced placeholder ${match?.[0]} in ${full}`);
        }
      }
    }
    checkDir(AWS_DIR);
  });
});

const GCP_DIR = path.join(__dirname, '..', '.test-gcp-output');
const GCP_REPLACEMENTS = {
  ...REPLACEMENTS,
  ...infraReplacements('gcp'),
};

describe('scaffold (Google Cloud stack)', () => {
  before(() => {
    if (fs.existsSync(GCP_DIR)) fs.rmSync(GCP_DIR, { recursive: true });
    copyTemplate(TEMPLATES_DIR, GCP_DIR, GCP_REPLACEMENTS);
  });
  after(() => {
    if (fs.existsSync(GCP_DIR)) fs.rmSync(GCP_DIR, { recursive: true });
  });

  it('RfBuilder.cs uses Google Cloud providers', () => {
    const rfBuilder = fs.readFileSync(path.join(GCP_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(rfBuilder.includes('using CrossCloudKit.Database.GC;'));
    assert.ok(rfBuilder.includes('using CrossCloudKit.File.GC;'));
    assert.ok(rfBuilder.includes('using CrossCloudKit.PubSub.GC;'));
    assert.ok(rfBuilder.includes('using CrossCloudKit.Memory.Redis;'));
    assert.ok(rfBuilder.includes('DatabaseServiceGC'));
    assert.ok(rfBuilder.includes('FileServiceGC'));
    assert.ok(rfBuilder.includes('PubSubServiceGC'));
    assert.ok(rfBuilder.includes('MemoryServiceRedis'));
  });

  it('backend.csproj references Google Cloud packages', () => {
    const csproj = fs.readFileSync(path.join(GCP_DIR, 'backend', 'backend.csproj'), 'utf-8');
    assert.ok(csproj.includes('"CrossCloudKit.Database.GC"'));
    assert.ok(csproj.includes('"CrossCloudKit.File.GC"'));
    assert.ok(csproj.includes('"CrossCloudKit.PubSub.GC"'));
    assert.ok(csproj.includes('"CrossCloudKit.Memory.Redis"'));
  });

  it('.env.example includes GCP config', () => {
    const env = fs.readFileSync(path.join(GCP_DIR, '.env.example'), 'utf-8');
    assert.ok(env.includes('GCP_PROJECT_ID'));
    assert.ok(env.includes('GCP_SERVICE_ACCOUNT_KEY_PATH'));
    assert.ok(env.includes('REDIS_HOST'));
  });

  it('no remaining placeholders in any file', () => {
    function checkDir(dir) {
      for (const entry of fs.readdirSync(dir)) {
        const full = path.join(dir, entry);
        if (fs.statSync(full).isDirectory()) { checkDir(full); }
        else {
          const content = fs.readFileSync(full, 'utf-8');
          const match = content.match(/\{\{[A-Z_]+\}\}/);
          assert.equal(match, null, `Found unreplaced placeholder ${match?.[0]} in ${full}`);
        }
      }
    }
    checkDir(GCP_DIR);
  });
});

const MONGO_DIR = path.join(__dirname, '..', '.test-mongo-output');
const MONGO_REPLACEMENTS = {
  ...REPLACEMENTS,
  ...infraReplacements('mongo'),
};

describe('scaffold (MongoDB + Redis stack)', () => {
  before(() => {
    if (fs.existsSync(MONGO_DIR)) fs.rmSync(MONGO_DIR, { recursive: true });
    copyTemplate(TEMPLATES_DIR, MONGO_DIR, MONGO_REPLACEMENTS);
  });
  after(() => {
    if (fs.existsSync(MONGO_DIR)) fs.rmSync(MONGO_DIR, { recursive: true });
  });

  it('RfBuilder.cs uses MongoDB + Redis + MinIO providers', () => {
    const rfBuilder = fs.readFileSync(path.join(MONGO_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(rfBuilder.includes('using CrossCloudKit.Database.Mongo;'));
    assert.ok(rfBuilder.includes('using CrossCloudKit.File.S3Compatible;'));
    assert.ok(rfBuilder.includes('using CrossCloudKit.Memory.Redis;'));
    assert.ok(rfBuilder.includes('using CrossCloudKit.PubSub.Redis;'));
    assert.ok(rfBuilder.includes('DatabaseServiceMongo'));
    assert.ok(rfBuilder.includes('FileServiceS3Compatible'));
    assert.ok(rfBuilder.includes('MemoryServiceRedis'));
    assert.ok(rfBuilder.includes('PubSubServiceRedis'));
  });

  it('backend.csproj references MongoDB + Redis + S3Compatible packages', () => {
    const csproj = fs.readFileSync(path.join(MONGO_DIR, 'backend', 'backend.csproj'), 'utf-8');
    assert.ok(csproj.includes('"CrossCloudKit.Database.Mongo"'));
    assert.ok(csproj.includes('"CrossCloudKit.File.S3Compatible"'));
    assert.ok(csproj.includes('"CrossCloudKit.Memory.Redis"'));
    assert.ok(csproj.includes('"CrossCloudKit.PubSub.Redis"'));
  });

  it('.env.example includes MongoDB, Redis, and MinIO config', () => {
    const env = fs.readFileSync(path.join(MONGO_DIR, '.env.example'), 'utf-8');
    assert.ok(env.includes('MONGODB_CONNECTION_STRING'));
    assert.ok(env.includes('MONGODB_DATABASE'));
    assert.ok(env.includes('REDIS_HOST'));
    assert.ok(env.includes('S3_SERVER'));
    assert.ok(env.includes('S3_ACCESS_KEY'));
  });

  it('docker-compose.yml includes mongodb, redis, and minio services', () => {
    const dc = fs.readFileSync(path.join(MONGO_DIR, 'docker-compose.yml'), 'utf-8');
    assert.ok(dc.includes('mongodb:'));
    assert.ok(dc.includes('mongo:7'));
    assert.ok(dc.includes('redis:'));
    assert.ok(dc.includes('minio:'));
    assert.ok(dc.includes('minio/minio:latest'));
    assert.ok(dc.includes('MONGODB_CONNECTION_STRING'));
  });

  it('README.md documents MongoDB infrastructure', () => {
    const readme = fs.readFileSync(path.join(MONGO_DIR, 'README.md'), 'utf-8');
    assert.ok(readme.includes('MongoDB'));
    assert.ok(readme.includes('MinIO'));
    assert.ok(readme.includes('Redis'));
  });

  it('no remaining placeholders in any file', () => {
    function checkDir(dir) {
      for (const entry of fs.readdirSync(dir)) {
        const full = path.join(dir, entry);
        if (fs.statSync(full).isDirectory()) { checkDir(full); }
        else {
          const content = fs.readFileSync(full, 'utf-8');
          const match = content.match(/\{\{[A-Z_]+\}\}/);
          assert.equal(match, null, `Found unreplaced placeholder ${match?.[0]} in ${full}`);
        }
      }
    }
    checkDir(MONGO_DIR);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Group 3: Combination scaffolds (feature-flag matrix)
// ─────────────────────────────────────────────────────────────────────────────

/** Helper: scaffold with specific flags and return a reader + cleanup */
function scaffoldCombo(stack, enableAi, sheetsDisabled) {
  const dir = path.join(__dirname, '..', `.test-combo-${stack}-ai${enableAi ? 1 : 0}-sh${sheetsDisabled ? 0 : 1}`);
  const replacements = {
    PROJECT_NAME: 'combo-app',
    APP_NAME: 'Combo App',
    PRIMARY_COLOR: '#123456',
    BACKEND_PORT: '5000',
    FRONTEND_PORT: '5001',
    CSPROJ_NAME: 'combo.app',
    SHEETS_CONFIG: sheetsDisabled ? '            SheetsEnabled = false,' : '',
    ...infraReplacements(stack),
    ...aiReplacements(enableAi),
  };
  if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
  copyTemplate(TEMPLATES_DIR, dir, replacements);
  const read = (relPath) => fs.readFileSync(path.join(dir, relPath), 'utf-8');
  const cleanup = () => { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); };
  return { dir, read, cleanup };
}

function assertNoPlaceholders(dir) {
  for (const entry of fs.readdirSync(dir)) {
    const full = path.join(dir, entry);
    if (fs.statSync(full).isDirectory()) { assertNoPlaceholders(full); }
    else {
      const content = fs.readFileSync(full, 'utf-8');
      const match = content.match(/\{\{[A-Z_]+\}\}/);
      assert.equal(match, null, `Found unreplaced placeholder ${match?.[0]} in ${full}`);
    }
  }
}

describe('combination scaffolds (feature-flag matrix)', () => {
  it('AWS + AI + sheets disabled: no remaining placeholders', () => {
    const { dir, cleanup } = scaffoldCombo('aws', true, true);
    try { assertNoPlaceholders(dir); } finally { cleanup(); }
  });

  it('AWS + AI + sheets disabled: RfBuilder has correct config', () => {
    const { read, cleanup } = scaffoldCombo('aws', true, true);
    try {
      const rb = read('backend/RfBuilder.cs');
      assert.ok(rb.includes('DatabaseServiceAWS'), 'should have AWS provider');
      assert.ok(rb.includes('AiServiceConfiguration'), 'should have AI config');
      assert.ok(rb.includes('SheetsEnabled = false'), 'should disable sheets');
    } finally { cleanup(); }
  });

  it('AWS + AI: csproj has both AWS and AI packages', () => {
    const { read, cleanup } = scaffoldCombo('aws', true, false);
    try {
      const csproj = read('backend/backend.csproj');
      assert.ok(csproj.includes('"CrossCloudKit.Database.AWS"'), 'csproj should have AWS DB');
      assert.ok(csproj.includes('"CrossCloudKit.LLM.Basic"'), 'csproj should have LLM');
    } finally { cleanup(); }
  });

  it('MongoDB + AI: no remaining placeholders', () => {
    const { dir, cleanup } = scaffoldCombo('mongo', true, false);
    try { assertNoPlaceholders(dir); } finally { cleanup(); }
  });

  it('MongoDB + AI: docker-compose has infra services AND AI env vars', () => {
    const { read, cleanup } = scaffoldCombo('mongo', true, false);
    try {
      const dc = read('docker-compose.yml');
      assert.ok(dc.includes('mongodb:'), 'docker-compose should have mongodb');
      assert.ok(dc.includes('redis:'), 'docker-compose should have redis');
      assert.ok(dc.includes('minio:'), 'docker-compose should have minio');
      assert.ok(dc.includes('LLM_BASE_URL') || dc.includes('LLM_MODEL'), 'docker-compose should have AI env vars');
    } finally { cleanup(); }
  });

  it('GCP + AI + sheets disabled: no remaining placeholders', () => {
    const { dir, cleanup } = scaffoldCombo('gcp', true, true);
    try { assertNoPlaceholders(dir); } finally { cleanup(); }
  });

  it('GCP + AI: RfBuilder has GCP providers AND AI config', () => {
    const { read, cleanup } = scaffoldCombo('gcp', true, false);
    try {
      const rb = read('backend/RfBuilder.cs');
      assert.ok(rb.includes('DatabaseServiceGC'), 'should have GCP provider');
      assert.ok(rb.includes('FileServiceGC'), 'should have GCP file service');
      assert.ok(rb.includes('AiServiceConfiguration'), 'should have AI config');
    } finally { cleanup(); }
  });

  it('Local + no AI + sheets disabled: minimal scaffold', () => {
    const { dir, read, cleanup } = scaffoldCombo('local', false, true);
    try {
      assertNoPlaceholders(dir);
      const rb = read('backend/RfBuilder.cs');
      assert.ok(rb.includes('DatabaseServiceBasic'), 'should have Basic provider');
      assert.ok(!rb.includes('AiServiceConfiguration'), 'should NOT have AI config');
      assert.ok(rb.includes('SheetsEnabled = false'), 'should disable sheets');
    } finally { cleanup(); }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Group 4: Cross-file consistency checks
// ─────────────────────────────────────────────────────────────────────────────

describe('cross-file consistency', () => {
  /** Extract GetEnvironmentVariable("X") calls from C# code */
  function extractEnvReads(csCode) {
    return [...csCode.matchAll(/GetEnvironmentVariable\("([^"]+)"\)/g)].map(m => m[1]);
  }
  /** Extract KEY= or KEY=value lines from .env file (excluding comments) */
  function extractEnvKeys(envContent) {
    return [...envContent.matchAll(/^([A-Z_][A-Z0-9_]*)=/gm)].map(m => m[1]);
  }

  for (const stack of ['local', 'aws', 'gcp', 'mongo']) {
    it(`${stack}: RfBuilder env var reads match .env.example entries`, () => {
      const replacements = {
        ...REPLACEMENTS,
        ...infraReplacements(stack),
        ...aiReplacements(false),
      };
      const dir = path.join(__dirname, '..', `.test-env-check-${stack}`);
      try {
        if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
        copyTemplate(TEMPLATES_DIR, dir, replacements);
        const rb = fs.readFileSync(path.join(dir, 'backend', 'RfBuilder.cs'), 'utf-8');
        const env = fs.readFileSync(path.join(dir, '.env.example'), 'utf-8');
        const envReads = extractEnvReads(rb);
        const envKeys = extractEnvKeys(env);
        for (const varName of envReads) {
          assert.ok(
            envKeys.includes(varName) || env.includes(varName),
            `${stack}: RfBuilder reads env var "${varName}" not found in .env.example`
          );
        }
      } finally {
        if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      }
    });
  }

  for (const stack of ['aws', 'gcp', 'mongo']) {
    it(`${stack}: docker-compose env vars match RfBuilder env reads`, () => {
      const replacements = {
        ...REPLACEMENTS,
        ...infraReplacements(stack),
        ...aiReplacements(false),
      };
      const dir = path.join(__dirname, '..', `.test-dc-check-${stack}`);
      try {
        if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
        copyTemplate(TEMPLATES_DIR, dir, replacements);
        const rb = fs.readFileSync(path.join(dir, 'backend', 'RfBuilder.cs'), 'utf-8');
        const dc = fs.readFileSync(path.join(dir, 'docker-compose.yml'), 'utf-8');
        const envReads = extractEnvReads(rb);
        // Extract "- VARNAME=" or "- VARNAME=${" from docker-compose
        const dcVars = [...dc.matchAll(/- ([A-Z_][A-Z0-9_]*)(?:=|\s)/gm)].map(m => m[1]);
        // Env vars with empty-string defaults (e.g. REDIS_PASSWORD ?? "") are truly optional
        // and don't need to be passed through docker-compose
        const optionalVars = [...rb.matchAll(/GetEnvironmentVariable\("([^"]+)"\)\s*\?\?\s*""/g)]
          .map(m => m[1]);
        for (const varName of envReads) {
          if (optionalVars.includes(varName)) continue;
          assert.ok(
            dcVars.includes(varName) || dc.includes(varName),
            `${stack}: RfBuilder reads "${varName}" but not found in docker-compose env`
          );
        }
      } finally {
        if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      }
    });
  }

  for (const stack of ['local', 'aws', 'gcp', 'mongo']) {
    it(`${stack}: csproj packages match RfBuilder using statements`, () => {
      const replacements = {
        ...REPLACEMENTS,
        ...infraReplacements(stack),
        ...aiReplacements(false),
      };
      const dir = path.join(__dirname, '..', `.test-csproj-check-${stack}`);
      try {
        if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
        copyTemplate(TEMPLATES_DIR, dir, replacements);
        const rb = fs.readFileSync(path.join(dir, 'backend', 'RfBuilder.cs'), 'utf-8');
        const csproj = fs.readFileSync(path.join(dir, 'backend', 'backend.csproj'), 'utf-8');
        const usings = [...rb.matchAll(/using (CrossCloudKit(?:\.\w+)+);/g)].map(m => m[1]);
        for (const ns of usings) {
          assert.ok(
            csproj.includes(ns),
            `${stack}: RfBuilder uses "${ns}" but csproj has no matching PackageReference`
          );
        }
      } finally {
        if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      }
    });
  }

  it('Program.cs port matches RfBuilder port', () => {
    const dir = path.join(__dirname, '..', '.test-port-check');
    const replacements = { ...REPLACEMENTS, BACKEND_PORT: '7777' };
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, replacements);
      const prog = fs.readFileSync(path.join(dir, 'backend', 'Program.cs'), 'utf-8');
      const rb = fs.readFileSync(path.join(dir, 'backend', 'RfBuilder.cs'), 'utf-8');
      assert.ok(prog.includes('7777'), 'Program.cs should have configured port');
      assert.ok(rb.includes('7777'), 'RfBuilder.cs should reference same port');
    } finally {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
    }
  });

  it('vite proxy target port matches Program.cs port', () => {
    const dir = path.join(__dirname, '..', '.test-vite-port-check');
    const replacements = { ...REPLACEMENTS, BACKEND_PORT: '8888', FRONTEND_PORT: '8889' };
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, replacements);
      const vite = fs.readFileSync(path.join(dir, 'frontend', 'vite.config.ts'), 'utf-8');
      assert.ok(vite.includes('8888'), 'vite proxy should target configured backend port');
      assert.ok(vite.includes('8889'), 'vite server should use configured frontend port');
    } finally {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
    }
  });

  it('docker-compose backend port maps to Dockerfile EXPOSE port', () => {
    const dc = fs.readFileSync(path.join(TEMPLATES_DIR, 'docker-compose.yml'), 'utf-8');
    const dockerfile = fs.readFileSync(path.join(TEMPLATES_DIR, 'backend', 'Dockerfile'), 'utf-8');
    // Dockerfile EXPOSE should match the internal port in docker-compose port mapping
    const exposeMatch = dockerfile.match(/EXPOSE (\d+)/);
    assert.ok(exposeMatch, 'Dockerfile should have EXPOSE');
    const exposedPort = exposeMatch[1];
    assert.ok(dc.includes(`:${exposedPort}`), `docker-compose should map to EXPOSE port ${exposedPort}`);
  });

  it('nginx.conf proxy_pass matches docker-compose backend service', () => {
    const nginx = fs.readFileSync(path.join(TEMPLATES_DIR, 'frontend', 'nginx.conf'), 'utf-8');
    assert.ok(
      nginx.includes('backend:8080'),
      'nginx proxy_pass should reference backend:8080 (matching docker-compose service and Dockerfile EXPOSE)'
    );
  });

  it('frontend package.json @reflectiveforms/frontend version is valid semver', () => {
    const pkg = JSON.parse(fs.readFileSync(path.join(TEMPLATES_DIR, 'frontend', 'package.json'), 'utf-8'));
    const version = pkg.dependencies?.['@reflectiveforms/frontend'];
    assert.ok(version, 'package.json should depend on @reflectiveforms/frontend');
    // Valid semver with optional ^ or ~ prefix, OR a dist-tag like "latest"
    const isSemver = /^[~^]?\d+\.\d+\.\d+/.test(version);
    const isDistTag = /^[a-z][a-z0-9._-]*$/.test(version);
    assert.ok(isSemver || isDistTag, `version "${version}" should be valid semver or a dist-tag`);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Group 6: Template completeness and structural integrity
// ─────────────────────────────────────────────────────────────────────────────

describe('template completeness and structural integrity', () => {
  const expectedTemplateFiles = [
    'backend/RfBuilder.cs',
    'backend/Program.cs',
    'backend/backend.csproj',
    'backend/Dockerfile',
    'backend/Models/NoteModel.cs',
    'frontend/index.html',
    'frontend/package.json',
    'frontend/Dockerfile',
    'frontend/nginx.conf',
    'frontend/vite.config.ts',
    'frontend/tailwind.config.js',
    'frontend/src/main.tsx',
    'frontend/src/rf.config.ts',
    'docker-compose.yml',
    '.env.example',
    '.gitignore',
    'README.md',
  ];

  it('all expected template files exist', () => {
    for (const f of expectedTemplateFiles) {
      assert.ok(
        fs.existsSync(path.join(TEMPLATES_DIR, f)),
        `Template file missing: ${f}`
      );
    }
  });

  it('no rendered file contains the literal string "undefined"', () => {
    const dir = path.join(__dirname, '..', '.test-undefined-check');
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, REPLACEMENTS);
      function check(d) {
        for (const entry of fs.readdirSync(d)) {
          const full = path.join(d, entry);
          if (fs.statSync(full).isDirectory()) { check(full); }
          else {
            const content = fs.readFileSync(full, 'utf-8');
            // Check for standalone "undefined" (not as part of a word like "isUndefined")
            const matches = content.match(/\bundefined\b/g);
            if (matches) {
              // Allow it in .js files or if it's in legitimate code context
              const ext = path.extname(full);
              if (!['.js', '.ts', '.tsx'].includes(ext)) {
                assert.fail(`Found literal "undefined" in ${full}`);
              }
            }
          }
        }
      }
      check(dir);
    } finally {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
    }
  });

  it('no rendered file contains standalone "null" from bad placeholder injection', () => {
    const dir = path.join(__dirname, '..', '.test-null-check');
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, REPLACEMENTS);
      function check(d) {
        for (const entry of fs.readdirSync(d)) {
          const full = path.join(d, entry);
          if (fs.statSync(full).isDirectory()) { check(full); }
          else {
            const content = fs.readFileSync(full, 'utf-8');
            // Check for "null" at start of a line (which would be injected placeholder)
            const lines = content.split('\n');
            for (const line of lines) {
              if (line.trim() === 'null') {
                assert.fail(`Found standalone "null" line in ${full}`);
              }
            }
          }
        }
      }
      check(dir);
    } finally {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
    }
  });

  it('docker-compose volumes has mongodb_data and minio_data for mongo stack', () => {
    const dir = path.join(__dirname, '..', '.test-volumes-check');
    const replacements = { ...REPLACEMENTS, ...infraReplacements('mongo') };
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, replacements);
      const dc = fs.readFileSync(path.join(dir, 'docker-compose.yml'), 'utf-8');
      assert.ok(dc.includes('mongodb_data'), 'docker-compose should declare mongodb_data volume');
      assert.ok(dc.includes('minio_data'), 'docker-compose should declare minio_data volume');
    } finally {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
    }
  });

  it('RfBuilder service variables are consistent between init and builder', () => {
    const dir = path.join(__dirname, '..', '.test-vars-check');
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, REPLACEMENTS);
      const rb = fs.readFileSync(path.join(dir, 'backend', 'RfBuilder.cs'), 'utf-8');
      // Verify the 4 service variables are created
      for (const varName of ['databaseService', 'memoryService', 'pubSubService', 'fileService']) {
        assert.ok(
          rb.includes(`var ${varName}`) || rb.includes(`new ${varName}`),
          `RfBuilder should create "${varName}" variable`
        );
      }
      // Verify they're passed to RepositoryServiceConfiguration
      assert.ok(rb.includes('databaseService'), 'builder should use databaseService');
      assert.ok(rb.includes('memoryService'), 'builder should use memoryService');
      assert.ok(rb.includes('pubSubService'), 'builder should use pubSubService');
      assert.ok(rb.includes('fileService'), 'builder should use fileService');
    } finally {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
    }
  });

  it('AI_NOTE_ATTRIBUTES injection produces valid NoteModel field declarations', () => {
    const dir = path.join(__dirname, '..', '.test-notemodel-check');
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, AI_REPLACEMENTS);
      const note = fs.readFileSync(path.join(dir, 'backend', 'Models', 'NoteModel.cs'), 'utf-8');
      // Verify attribute stacking: each "public" field declaration should be preceded by attributes
      const lines = note.split('\n');
      for (let i = 0; i < lines.length; i++) {
        if (lines[i].trim().startsWith('public ') && lines[i].includes('=')) {
          // This is a field declaration — there should be at least one attribute above it
          let hasAttr = false;
          for (let j = i - 1; j >= 0; j--) {
            const trimmed = lines[j].trim();
            if (trimmed.startsWith('[') || trimmed.endsWith(']') || trimmed.includes('Attribute')) {
              hasAttr = true;
              break;
            }
            if (trimmed === '' || trimmed.startsWith('//') || trimmed.startsWith('///')) continue;
            break;
          }
          assert.ok(hasAttr, `Field at line ${i + 1} ("${lines[i].trim()}") should have attribute(s) above it`);
        }
      }
    } finally {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
    }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Per-stack rendered RfBuilder.cs content & C# correctness
// ─────────────────────────────────────────────────────────────────────────────

describe('per-stack rendered RfBuilder.cs content', () => {
  function renderStack(stack) {
    const dir = path.join(__dirname, '..', `.test-render-${stack}`);
    const replacements = {
      PROJECT_NAME: 'myproject', APP_NAME: 'My Project', PRIMARY_COLOR: '#aabbcc',
      BACKEND_PORT: '9000', FRONTEND_PORT: '3000', CSPROJ_NAME: 'myproject',
      SHEETS_CONFIG: '', ...infraReplacements(stack), ...aiReplacements(false),
    };
    if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
    copyTemplate(TEMPLATES_DIR, dir, replacements);
    const read = (relPath) => fs.readFileSync(path.join(dir, relPath), 'utf-8');
    const cleanup = () => { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); };
    return { dir, read, cleanup };
  }

  it('local: variables declared in correct dependency order', () => {
    const { read, cleanup } = renderStack('local');
    try {
      const rb = read('backend/RfBuilder.cs');
      const pubSubPos = rb.indexOf('var pubSubService');
      const memoryPos = rb.indexOf('var memoryService');
      const filePos = rb.indexOf('var fileService');
      const dbPos = rb.indexOf('var databaseService');
      assert.ok(pubSubPos < memoryPos, 'pubSubService before memoryService');
      assert.ok(memoryPos < filePos, 'memoryService before fileService');
      assert.ok(filePos < dbPos, 'fileService before databaseService');
    } finally { cleanup(); }
  });

  it('aws: variables declared in correct dependency order', () => {
    const { read, cleanup } = renderStack('aws');
    try {
      const rb = read('backend/RfBuilder.cs');
      const redisPos = rb.indexOf('var redisOpts');
      const memoryPos = rb.indexOf('var memoryService');
      const pubSubPos = rb.indexOf('var pubSubService');
      const filePos = rb.indexOf('var fileService');
      const dbPos = rb.indexOf('var databaseService');
      assert.ok(redisPos < memoryPos, 'redisOpts before memoryService');
      assert.ok(memoryPos < dbPos, 'memoryService before databaseService');
      assert.ok(redisPos !== -1 && memoryPos !== -1 && pubSubPos !== -1 && filePos !== -1 && dbPos !== -1,
        'all 5 service variables must be present');
    } finally { cleanup(); }
  });

  it('gcp: variables declared in correct dependency order', () => {
    const { read, cleanup } = renderStack('gcp');
    try {
      const rb = read('backend/RfBuilder.cs');
      const redisPos = rb.indexOf('var redisOpts');
      const memoryPos = rb.indexOf('var memoryService');
      const dbPos = rb.indexOf('var databaseService');
      assert.ok(redisPos < memoryPos, 'redisOpts before memoryService');
      assert.ok(memoryPos < dbPos, 'memoryService before databaseService');
    } finally { cleanup(); }
  });

  it('mongo: variables declared in correct dependency order', () => {
    const { read, cleanup } = renderStack('mongo');
    try {
      const rb = read('backend/RfBuilder.cs');
      const redisPos = rb.indexOf('var redisOpts');
      const memoryPos = rb.indexOf('var memoryService');
      const pubSubPos = rb.indexOf('var pubSubService');
      const dbPos = rb.indexOf('var databaseService');
      assert.ok(redisPos < memoryPos, 'redisOpts before memoryService');
      assert.ok(redisPos < pubSubPos, 'redisOpts before pubSubService');
      assert.ok(memoryPos < dbPos, 'memoryService before databaseService');
    } finally { cleanup(); }
  });

  it('aws: correct constructor parameters', () => {
    const { read, cleanup } = renderStack('aws');
    try {
      const rb = read('backend/RfBuilder.cs');
      assert.ok(rb.includes('new RedisConnectionOptions'), 'RedisConnectionOptions');
      assert.ok(rb.includes('new MemoryServiceRedis(redisOpts)'), 'MemoryServiceRedis(redisOpts)');
      assert.ok(rb.includes('new PubSubServiceAWS('), 'PubSubServiceAWS');
      assert.ok(rb.includes('"AWS_ACCESS_KEY"'), 'AWS_ACCESS_KEY');
      assert.ok(rb.includes('"AWS_SECRET_KEY"'), 'AWS_SECRET_KEY');
      assert.ok(rb.includes('"AWS_REGION"'), 'AWS_REGION');
      assert.ok(rb.includes('new FileServiceAWS('), 'FileServiceAWS');
      assert.ok(rb.includes('new DatabaseServiceAWS('), 'DatabaseServiceAWS');
      const dbBlock = rb.substring(rb.indexOf('new DatabaseServiceAWS('), rb.indexOf('new DatabaseServiceAWS(') + 500);
      assert.ok(dbBlock.includes('memoryService'), 'DatabaseServiceAWS must receive memoryService');
    } finally { cleanup(); }
  });

  it('gcp: correct constructor parameters', () => {
    const { read, cleanup } = renderStack('gcp');
    try {
      const rb = read('backend/RfBuilder.cs');
      assert.ok(rb.includes('new PubSubServiceGC('), 'PubSubServiceGC');
      assert.ok(rb.includes('isBase64Encoded:'), 'isBase64Encoded param');
      assert.ok(rb.includes('"GCP_PROJECT_ID"'), 'GCP_PROJECT_ID');
      assert.ok(rb.includes('"GCP_SERVICE_ACCOUNT_JSON"'), 'GCP_SERVICE_ACCOUNT_JSON');
      assert.ok(rb.includes('new FileServiceGC('), 'FileServiceGC');
      assert.ok(rb.includes('"GCP_SERVICE_ACCOUNT_KEY_PATH"'), 'GCP_SERVICE_ACCOUNT_KEY_PATH');
      assert.ok(rb.includes('new DatabaseServiceGC('), 'DatabaseServiceGC');
      const dbBlock = rb.substring(rb.indexOf('new DatabaseServiceGC('), rb.indexOf('new DatabaseServiceGC(') + 500);
      assert.ok(dbBlock.includes('memoryService'), 'DatabaseServiceGC must receive memoryService');
    } finally { cleanup(); }
  });

  it('mongo: correct constructor parameters', () => {
    const { read, cleanup } = renderStack('mongo');
    try {
      const rb = read('backend/RfBuilder.cs');
      assert.ok(rb.includes('new PubSubServiceRedis(redisOpts)'), 'PubSubServiceRedis(redisOpts)');
      assert.ok(rb.includes('new FileServiceS3Compatible('), 'FileServiceS3Compatible');
      assert.ok(rb.includes('"S3_SERVER"'), 'S3_SERVER');
      assert.ok(rb.includes('"S3_ACCESS_KEY"'), 'S3_ACCESS_KEY');
      assert.ok(rb.includes('"S3_SECRET_KEY"'), 'S3_SECRET_KEY');
      assert.ok(rb.includes('"S3_REGION"'), 'S3_REGION');
      assert.ok(rb.includes('new DatabaseServiceMongo('), 'DatabaseServiceMongo');
      assert.ok(rb.includes('"MONGODB_CONNECTION_STRING"'), 'MONGODB_CONNECTION_STRING');
      assert.ok(rb.includes('"MONGODB_DATABASE"'), 'MONGODB_DATABASE');
      const dbBlock = rb.substring(rb.indexOf('new DatabaseServiceMongo('), rb.indexOf('new DatabaseServiceMongo(') + 500);
      assert.ok(dbBlock.includes('memoryService'), 'DatabaseServiceMongo must receive memoryService');
    } finally { cleanup(); }
  });

  it('local: correct constructor parameters', () => {
    const { read, cleanup } = renderStack('local');
    try {
      const rb = read('backend/RfBuilder.cs');
      assert.ok(rb.includes('new PubSubServiceBasic()'), 'PubSubServiceBasic()');
      assert.ok(rb.includes('new MemoryServiceBasic(pubSubService)'), 'MemoryServiceBasic(pubSubService)');
      assert.ok(rb.includes('new FileServiceBasic(memoryService, pubSubService)'), 'FileServiceBasic(memoryService, pubSubService)');
      assert.ok(rb.includes('new DatabaseServiceBasic("myproject-db", memoryService'), 'DatabaseServiceBasic with project name');
      assert.ok(rb.includes('Path.GetTempPath()'), 'should use temp path');
    } finally { cleanup(); }
  });

  for (const stack of ['local', 'aws', 'gcp', 'mongo']) {
    it(`${stack}: RepositoryServiceConfiguration params in correct order`, () => {
      const { read, cleanup } = renderStack(stack);
      try {
        const rb = read('backend/RfBuilder.cs');
        const repoConfig = rb.substring(rb.indexOf('new EntityRepositoryServiceConfiguration('));
        const repoBlock = repoConfig.substring(0, repoConfig.indexOf('),') + 1);
        const dbIdx = repoBlock.indexOf('databaseService');
        const memIdx = repoBlock.indexOf('memoryService');
        const psIdx = repoBlock.indexOf('pubSubService');
        const fsIdx = repoBlock.indexOf('FileServiceConfiguration');
        assert.ok(dbIdx < memIdx, `${stack}: databaseService before memoryService`);
        assert.ok(memIdx < psIdx, `${stack}: memoryService before pubSubService`);
        assert.ok(psIdx < fsIdx, `${stack}: pubSubService before FileServiceConfiguration`);
        assert.ok(repoBlock.includes('FileServiceConfiguration(fileService'), 'wraps fileService');
        assert.ok(repoBlock.includes('"myproject-media"'), 'bucket name');
      } finally { cleanup(); }
    });
  }

  it('local: no cloud provider references', () => {
    const { read, cleanup } = renderStack('local');
    try {
      const rb = read('backend/RfBuilder.cs');
      assert.ok(!rb.includes('ServiceAWS'), 'no AWS');
      assert.ok(!rb.includes('ServiceGC'), 'no GCP');
      assert.ok(!rb.includes('ServiceMongo'), 'no Mongo');
      assert.ok(!rb.includes('ServiceRedis'), 'no Redis');
      assert.ok(!rb.includes('S3Compatible'), 'no S3');
    } finally { cleanup(); }
  });

  it('aws: no Basic/GCP/Mongo providers', () => {
    const { read, cleanup } = renderStack('aws');
    try {
      const rb = read('backend/RfBuilder.cs');
      assert.ok(!rb.includes('DatabaseServiceBasic'), 'no Basic DB');
      assert.ok(!rb.includes('ServiceGC'), 'no GCP');
      assert.ok(!rb.includes('ServiceMongo'), 'no Mongo');
      assert.ok(!rb.includes('S3Compatible'), 'no S3');
    } finally { cleanup(); }
  });

  it('gcp: no Basic/AWS/Mongo providers', () => {
    const { read, cleanup } = renderStack('gcp');
    try {
      const rb = read('backend/RfBuilder.cs');
      assert.ok(!rb.includes('DatabaseServiceBasic'), 'no Basic DB');
      assert.ok(!rb.includes('ServiceAWS'), 'no AWS');
      assert.ok(!rb.includes('ServiceMongo'), 'no Mongo');
      assert.ok(!rb.includes('S3Compatible'), 'no S3');
    } finally { cleanup(); }
  });

  it('mongo: no Basic/AWS/GCP providers', () => {
    const { read, cleanup } = renderStack('mongo');
    try {
      const rb = read('backend/RfBuilder.cs');
      assert.ok(!rb.includes('DatabaseServiceBasic'), 'no Basic DB');
      assert.ok(!rb.includes('ServiceAWS'), 'no AWS');
      assert.ok(!rb.includes('ServiceGC'), 'no GCP');
    } finally { cleanup(); }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Two-pass nested placeholder resolution
// ─────────────────────────────────────────────────────────────────────────────

describe('two-pass nested placeholder resolution', () => {
  it('mongo: MONGODB_DATABASE default in RfBuilder.cs resolves PROJECT_NAME', () => {
    const dir = path.join(__dirname, '..', '.test-twopass-rb');
    const replacements = { ...REPLACEMENTS, PROJECT_NAME: 'my-cool-app', ...infraReplacements('mongo') };
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, replacements);
      const rb = fs.readFileSync(path.join(dir, 'backend', 'RfBuilder.cs'), 'utf-8');
      assert.ok(rb.includes('"my-cool-app"'), 'MONGODB_DATABASE default should resolve');
      assert.ok(!rb.includes('{{PROJECT_NAME}}'), 'no unreplaced placeholder');
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });

  it('mongo: docker-compose.yml resolves PROJECT_NAME in MONGODB_DATABASE', () => {
    const dir = path.join(__dirname, '..', '.test-twopass-dc');
    const replacements = { ...REPLACEMENTS, PROJECT_NAME: 'my-cool-app', ...infraReplacements('mongo') };
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, replacements);
      const dc = fs.readFileSync(path.join(dir, 'docker-compose.yml'), 'utf-8');
      assert.ok(dc.includes('MONGODB_DATABASE=my-cool-app'), 'docker-compose resolves');
      assert.ok(!dc.includes('{{PROJECT_NAME}}'), 'no unreplaced placeholder');
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });

  it('mongo: .env.example resolves PROJECT_NAME in MONGODB_DATABASE', () => {
    const dir = path.join(__dirname, '..', '.test-twopass-env');
    const replacements = { ...REPLACEMENTS, PROJECT_NAME: 'my-cool-app', ...infraReplacements('mongo') };
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, replacements);
      const env = fs.readFileSync(path.join(dir, '.env.example'), 'utf-8');
      assert.ok(env.includes('MONGODB_DATABASE=my-cool-app'), '.env resolves');
      assert.ok(!env.includes('{{PROJECT_NAME}}'), 'no unreplaced placeholder');
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });

  it('local: DatabaseServiceBasic name resolves PROJECT_NAME', () => {
    const dir = path.join(__dirname, '..', '.test-twopass-local');
    const replacements = { ...REPLACEMENTS, PROJECT_NAME: 'test-proj' };
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, replacements);
      const rb = fs.readFileSync(path.join(dir, 'backend', 'RfBuilder.cs'), 'utf-8');
      assert.ok(rb.includes('"test-proj-db"'), 'resolved project name');
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Docker-compose YAML structural validity
// ─────────────────────────────────────────────────────────────────────────────

describe('docker-compose YAML structure', () => {
  for (const stack of ['local', 'aws', 'gcp', 'mongo']) {
    it(`${stack}: depends_on lists only defined services`, () => {
      const dir = path.join(__dirname, '..', `.test-dc-struct-${stack}`);
      const replacements = { ...REPLACEMENTS, ...infraReplacements(stack) };
      try {
        if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
        copyTemplate(TEMPLATES_DIR, dir, replacements);
        const dc = fs.readFileSync(path.join(dir, 'docker-compose.yml'), 'utf-8');
        const serviceNames = [...dc.matchAll(/^  (\w+):\s*$/gm)].map(m => m[1]);
        const dependsOn = [...dc.matchAll(/^\s+- (\w+)\s*$/gm)].map(m => m[1]);
        for (const dep of dependsOn) {
          assert.ok(serviceNames.includes(dep),
            `${stack}: depends_on "${dep}" not in services: [${serviceNames.join(', ')}]`);
        }
      } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
    });
  }

  it('mongo: volume names in services match declared volumes', () => {
    const dir = path.join(__dirname, '..', '.test-dc-vols');
    const replacements = { ...REPLACEMENTS, ...infraReplacements('mongo') };
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, replacements);
      const dc = fs.readFileSync(path.join(dir, 'docker-compose.yml'), 'utf-8');
      const volSection = dc.substring(dc.lastIndexOf('volumes:'));
      const declaredVols = [...volSection.matchAll(/^  (\w+):\s*$/gm)].map(m => m[1]);
      const usedVols = [...dc.matchAll(/- (\w+):\/\S+/g)].map(m => m[1]);
      for (const vol of usedVols) {
        assert.ok(declaredVols.includes(vol),
          `Volume "${vol}" used but not declared: [${declaredVols.join(', ')}]`);
      }
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });

  it('backend Dockerfile ASPNETCORE_URLS port matches EXPOSE', () => {
    const df = fs.readFileSync(path.join(TEMPLATES_DIR, 'backend', 'Dockerfile'), 'utf-8');
    const exposePort = df.match(/EXPOSE (\d+)/)?.[1];
    const urlPort = df.match(/ASPNETCORE_URLS=http:\/\/\+:(\d+)/)?.[1];
    assert.ok(exposePort && urlPort, 'should have EXPOSE and ASPNETCORE_URLS');
    assert.equal(exposePort, urlPort, 'ports should match');
  });

  it('backend Dockerfile ENTRYPOINT uses backend.dll', () => {
    const df = fs.readFileSync(path.join(TEMPLATES_DIR, 'backend', 'Dockerfile'), 'utf-8');
    assert.ok(df.includes('"backend.dll"'), 'ENTRYPOINT should reference backend.dll');
  });

  it('frontend Dockerfile uses npm ci and copies nginx.conf', () => {
    const df = fs.readFileSync(path.join(TEMPLATES_DIR, 'frontend', 'Dockerfile'), 'utf-8');
    assert.ok(df.includes('npm ci'), 'should use npm ci');
    assert.ok(df.includes('nginx.conf'), 'should copy nginx.conf');
  });

  it('frontend Dockerfile EXPOSE matches nginx listen port', () => {
    const df = fs.readFileSync(path.join(TEMPLATES_DIR, 'frontend', 'Dockerfile'), 'utf-8');
    const nginx = fs.readFileSync(path.join(TEMPLATES_DIR, 'frontend', 'nginx.conf'), 'utf-8');
    assert.equal(df.match(/EXPOSE (\d+)/)?.[1], nginx.match(/listen (\d+)/)?.[1], 'ports should match');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Per-stack .env.example completeness
// ─────────────────────────────────────────────────────────────────────────────

describe('per-stack .env.example content', () => {
  function renderEnv(stack) {
    const dir = path.join(__dirname, '..', `.test-envfile-${stack}`);
    const replacements = { ...REPLACEMENTS, ...infraReplacements(stack), ...aiReplacements(false) };
    if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
    copyTemplate(TEMPLATES_DIR, dir, replacements);
    const env = fs.readFileSync(path.join(dir, '.env.example'), 'utf-8');
    const cleanup = () => { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); };
    return { env, cleanup };
  }

  it('all stacks: common env vars present', () => {
    for (const stack of ['local', 'aws', 'gcp', 'mongo']) {
      const { env, cleanup } = renderEnv(stack);
      try {
        for (const v of ['BACKEND_PORT=', 'JWT_SECRET=', 'FRONTEND_PORT=', 'VITE_API_BASE_URL='])
          assert.ok(env.includes(v), `${stack}: ${v}`);
      } finally { cleanup(); }
    }
  });

  it('aws: has AWS and Redis vars', () => {
    const { env, cleanup } = renderEnv('aws');
    try {
      for (const v of ['AWS_ACCESS_KEY=', 'AWS_SECRET_KEY=', 'AWS_REGION=', 'REDIS_HOST=', 'REDIS_PORT='])
        assert.ok(env.includes(v), `should have ${v}`);
    } finally { cleanup(); }
  });

  it('gcp: has GCP and Redis vars', () => {
    const { env, cleanup } = renderEnv('gcp');
    try {
      for (const v of ['GCP_PROJECT_ID=', 'GCP_SERVICE_ACCOUNT_KEY_PATH=', 'GCP_SERVICE_ACCOUNT_JSON=', 'REDIS_HOST='])
        assert.ok(env.includes(v), `should have ${v}`);
    } finally { cleanup(); }
  });

  it('mongo: has MongoDB, Redis, and S3 vars', () => {
    const { env, cleanup } = renderEnv('mongo');
    try {
      for (const v of ['MONGODB_CONNECTION_STRING=', 'MONGODB_DATABASE=', 'REDIS_HOST=',
                        'S3_SERVER=', 'S3_ACCESS_KEY=', 'S3_SECRET_KEY=', 'S3_REGION='])
        assert.ok(env.includes(v), `should have ${v}`);
    } finally { cleanup(); }
  });

  it('local: no infra-specific vars', () => {
    const { env, cleanup } = renderEnv('local');
    try {
      for (const prefix of ['AWS_', 'GCP_', 'MONGODB_', 'S3_', 'REDIS_'])
        assert.ok(!env.includes(prefix), `local should not have ${prefix}`);
    } finally { cleanup(); }
  });

  it('all stacks: port values rendered as numbers', () => {
    for (const stack of ['local', 'aws', 'gcp', 'mongo']) {
      const { env, cleanup } = renderEnv(stack);
      try {
        assert.ok(env.includes('BACKEND_PORT=4000'), `${stack}: backend port`);
        assert.ok(env.includes('FRONTEND_PORT=4001'), `${stack}: frontend port`);
      } finally { cleanup(); }
    }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Cross-file API endpoint & port consistency
// ─────────────────────────────────────────────────────────────────────────────

describe('cross-file API endpoint consistency', () => {
  const EP_DIR = path.join(__dirname, '..', '.test-endpoint-consistency');
  const EP_REPLACEMENTS = { ...REPLACEMENTS, BACKEND_PORT: '7777', FRONTEND_PORT: '7778' };

  before(() => {
    if (fs.existsSync(EP_DIR)) fs.rmSync(EP_DIR, { recursive: true });
    copyTemplate(TEMPLATES_DIR, EP_DIR, EP_REPLACEMENTS);
  });
  after(() => {
    if (fs.existsSync(EP_DIR)) fs.rmSync(EP_DIR, { recursive: true });
  });

  it('rf.config.ts uses correct port and /rf/api path', () => {
    const config = fs.readFileSync(path.join(EP_DIR, 'frontend', 'src', 'rf.config.ts'), 'utf-8');
    assert.ok(config.includes("'http://localhost:7777/rf/api'"), 'correct URL');
  });

  it('rf.config.ts API base URL has no trailing slash', () => {
    const config = fs.readFileSync(path.join(EP_DIR, 'frontend', 'src', 'rf.config.ts'), 'utf-8');
    const urlMatch = config.match(/localhost:\d+\/rf\/api([^']*)/);
    assert.ok(!urlMatch?.[1]?.startsWith('/'), 'no trailing slash');
  });

  it('.env.example VITE_API_BASE_URL matches rf.config.ts', () => {
    const env = fs.readFileSync(path.join(EP_DIR, '.env.example'), 'utf-8');
    assert.ok(env.includes('VITE_API_BASE_URL=http://localhost:7777/rf/api'), 'URL match');
  });

  it('RfBuilder PublicUrlRootForApi uses backend port', () => {
    const rb = fs.readFileSync(path.join(EP_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(rb.includes('http://localhost:7777/rf/api/'), 'backend port');
  });

  it('RfBuilder PublicFrontendBaseUrl uses frontend port', () => {
    const rb = fs.readFileSync(path.join(EP_DIR, 'backend', 'RfBuilder.cs'), 'utf-8');
    assert.ok(rb.includes('http://localhost:7778'), 'frontend port');
  });

  it('vite proxy path and target match', () => {
    const vite = fs.readFileSync(path.join(EP_DIR, 'frontend', 'vite.config.ts'), 'utf-8');
    assert.ok(vite.includes("'/rf/api'"), '/rf/api path');
    assert.ok(vite.includes('http://localhost:7777'), 'backend port');
  });

  it('nginx proxy location matches API path', () => {
    const nginx = fs.readFileSync(path.join(EP_DIR, 'frontend', 'nginx.conf'), 'utf-8');
    assert.ok(nginx.includes('location /rf/api'), '/rf/api');
  });

  it('docker-compose references both ports', () => {
    const dc = fs.readFileSync(path.join(EP_DIR, 'docker-compose.yml'), 'utf-8');
    assert.ok(dc.includes('7777'), 'backend port');
    assert.ok(dc.includes('7778'), 'frontend port');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Full 16-combination matrix (all stacks × AI × sheets)
// ─────────────────────────────────────────────────────────────────────────────

describe('full feature-flag matrix (16 combinations)', () => {
  for (const stack of ['local', 'aws', 'gcp', 'mongo']) {
    for (const ai of [true, false]) {
      for (const sheetsDisabled of [true, false]) {
        const label = `${stack}/AI=${ai ? 'on' : 'off'}/sheets=${sheetsDisabled ? 'off' : 'on'}`;

        it(`${label}: no remaining placeholders`, () => {
          const { dir, cleanup } = scaffoldCombo(stack, ai, sheetsDisabled);
          try { assertNoPlaceholders(dir); } finally { cleanup(); }
        });

        it(`${label}: correct feature flags`, () => {
          const { read, cleanup } = scaffoldCombo(stack, ai, sheetsDisabled);
          try {
            const rb = read('backend/RfBuilder.cs');
            if (ai) {
              assert.ok(rb.includes('AiServiceConfiguration'), `${label}: AI config`);
              assert.ok(rb.includes('new LLMServiceBasic()'), `${label}: LLM`);
              assert.ok(rb.includes('new VectorServiceBasic()'), `${label}: vector`);
            } else {
              assert.ok(!rb.includes('AiServiceConfiguration'), `${label}: no AI`);
              assert.ok(!rb.includes('LLMService'), `${label}: no LLM`);
              assert.ok(!rb.includes('VectorService'), `${label}: no vector`);
            }
            if (sheetsDisabled) {
              assert.ok(rb.includes('SheetsEnabled = false'), `${label}: sheets off`);
            } else {
              assert.ok(!rb.includes('SheetsEnabled'), `${label}: no sheets line`);
            }
          } finally { cleanup(); }
        });
      }
    }
  }
});

// ─────────────────────────────────────────────────────────────────────────────
// README.md per-stack content
// ─────────────────────────────────────────────────────────────────────────────

describe('README.md per-stack content', () => {
  function renderReadme(stack, ai) {
    const dir = path.join(__dirname, '..', `.test-readme-${stack}-ai${ai ? 1 : 0}`);
    const replacements = { ...REPLACEMENTS, ...infraReplacements(stack), ...aiReplacements(ai) };
    if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
    copyTemplate(TEMPLATES_DIR, dir, replacements);
    const readme = fs.readFileSync(path.join(dir, 'README.md'), 'utf-8');
    const cleanup = () => { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); };
    return { readme, cleanup };
  }

  it('aws: mentions DynamoDB, S3, SNS/SQS, Redis', () => {
    const { readme, cleanup } = renderReadme('aws', false);
    try {
      assert.ok(readme.includes('DynamoDB'), 'DynamoDB');
      assert.ok(readme.includes('S3'), 'S3');
      assert.ok(readme.includes('SNS') || readme.includes('SQS'), 'SNS/SQS');
      assert.ok(readme.includes('Redis'), 'Redis');
    } finally { cleanup(); }
  });

  it('gcp: mentions Datastore, Cloud Storage, Pub/Sub, Redis', () => {
    const { readme, cleanup } = renderReadme('gcp', false);
    try {
      assert.ok(readme.includes('Datastore'), 'Datastore');
      assert.ok(readme.includes('Cloud Storage'), 'Cloud Storage');
      assert.ok(readme.includes('Pub/Sub'), 'Pub/Sub');
      assert.ok(readme.includes('Redis'), 'Redis');
    } finally { cleanup(); }
  });

  it('mongo: mentions MongoDB, MinIO, Redis with docker compose', () => {
    const { readme, cleanup } = renderReadme('mongo', false);
    try {
      assert.ok(readme.includes('MongoDB'), 'MongoDB');
      assert.ok(readme.includes('MinIO'), 'MinIO');
      assert.ok(readme.includes('Redis'), 'Redis');
      assert.ok(readme.includes('docker compose'), 'docker compose');
      assert.ok(readme.includes('minioadmin'), 'MinIO credentials');
    } finally { cleanup(); }
  });

  it('local: mentions file-based', () => {
    const { readme, cleanup } = renderReadme('local', false);
    try {
      assert.ok(readme.toLowerCase().includes('local') || readme.toLowerCase().includes('file-based'), 'file-based');
    } finally { cleanup(); }
  });

  it('AI enabled: documents all 6 AI features + LLMServiceOpenAI migration', () => {
    const { readme, cleanup } = renderReadme('local', true);
    try {
      assert.ok(readme.includes('Semantic Search'), 'Semantic Search');
      assert.ok(readme.includes('AI Generation'), 'AI Generation');
      assert.ok(readme.includes('Suggestions') || readme.includes('Field Suggestions'), 'Suggestions');
      assert.ok(readme.includes('Sanity Check'), 'Sanity Checks');
      assert.ok(readme.includes('Diff Summar'), 'Diff Summaries');
      assert.ok(readme.includes('NL Filter'), 'NL Filtering');
      assert.ok(readme.includes('LLMServiceOpenAI'), 'LLMServiceOpenAI example');
      assert.ok(readme.includes('dotnet add package'), 'NuGet package');
    } finally { cleanup(); }
  });

  it('AI disabled: mentions re-scaffold', () => {
    const { readme, cleanup } = renderReadme('local', false);
    try {
      assert.ok(readme.includes('create-reflectiveforms-app') || readme.includes('re-scaffold'), 're-scaffold');
    } finally { cleanup(); }
  });

  it('all stacks: have Quick Start, Adding Entities, Configuration', () => {
    for (const stack of ['local', 'aws', 'gcp', 'mongo']) {
      const { readme, cleanup } = renderReadme(stack, false);
      try {
        assert.ok(readme.includes('## Quick Start'), `${stack}: Quick Start`);
        assert.ok(readme.includes('## Adding Entities'), `${stack}: Adding Entities`);
        assert.ok(readme.includes('## Configuration'), `${stack}: Configuration`);
      } finally { cleanup(); }
    }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Frontend template detailed validation
// ─────────────────────────────────────────────────────────────────────────────

describe('frontend template content validation', () => {
  const FE_DIR = path.join(__dirname, '..', '.test-frontend-validation');

  before(() => {
    if (fs.existsSync(FE_DIR)) fs.rmSync(FE_DIR, { recursive: true });
    copyTemplate(TEMPLATES_DIR, FE_DIR, REPLACEMENTS);
  });
  after(() => {
    if (fs.existsSync(FE_DIR)) fs.rmSync(FE_DIR, { recursive: true });
  });

  it('package.json is valid JSON with project name', () => {
    const pkg = JSON.parse(fs.readFileSync(path.join(FE_DIR, 'frontend', 'package.json'), 'utf-8'));
    assert.ok(pkg.name.includes('test-app'), 'name includes project');
  });

  it('package.json has build script with vite', () => {
    const pkg = JSON.parse(fs.readFileSync(path.join(FE_DIR, 'frontend', 'package.json'), 'utf-8'));
    assert.ok(pkg.scripts?.build?.includes('vite build'), 'vite build');
  });

  it('vite.config.ts port is numeric', () => {
    const vite = fs.readFileSync(path.join(FE_DIR, 'frontend', 'vite.config.ts'), 'utf-8');
    assert.ok(vite.includes('port: 4001'), 'numeric port');
  });

  it('vite.config.ts has changeOrigin', () => {
    const vite = fs.readFileSync(path.join(FE_DIR, 'frontend', 'vite.config.ts'), 'utf-8');
    assert.ok(vite.includes('changeOrigin: true'), 'changeOrigin');
  });

  it('index.html has correct title and root div', () => {
    const html = fs.readFileSync(path.join(FE_DIR, 'frontend', 'index.html'), 'utf-8');
    assert.ok(html.includes('<title>Test App</title>'), 'title');
    assert.ok(html.includes('id="root"'), 'root div');
  });

  it('main.tsx imports createReflectiveFormsApp', () => {
    const main = fs.readFileSync(path.join(FE_DIR, 'frontend', 'src', 'main.tsx'), 'utf-8');
    assert.ok(main.includes('createReflectiveFormsApp'), 'import');
  });

  it('tailwind.config.js includes RF library and primary color variable', () => {
    const tw = fs.readFileSync(path.join(FE_DIR, 'frontend', 'tailwind.config.js'), 'utf-8');
    assert.ok(tw.includes('@reflectiveforms/frontend/dist'), 'RF library');
    assert.ok(tw.includes('.{js,mjs}'), 'js+mjs');
    assert.ok(tw.includes('var(--rf-primary'), 'CSS variable');
  });

  it('nginx.conf SPA fallback + WebSocket + correct proxy target', () => {
    const nginx = fs.readFileSync(path.join(FE_DIR, 'frontend', 'nginx.conf'), 'utf-8');
    assert.ok(nginx.includes('try_files $uri $uri/ /index.html'), 'SPA fallback');
    assert.ok(nginx.includes('listen 80'), 'listen 80');
    assert.ok(nginx.includes('proxy_http_version 1.1'), 'HTTP/1.1');
    assert.ok(nginx.includes('proxy_set_header Upgrade $http_upgrade'), 'Upgrade');
    assert.ok(nginx.includes('proxy_set_header Connection "upgrade"'), 'Connection');
    assert.ok(nginx.includes('proxy_pass http://backend:'), 'proxy to backend service');
    assert.ok(!nginx.includes('proxy_pass http://localhost'), 'not to localhost');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// NoteModel.cs validation
// ─────────────────────────────────────────────────────────────────────────────

describe('NoteModel.cs content validation', () => {
  it('AI disabled: standard attributes, no AI attributes', () => {
    const dir = path.join(__dirname, '..', '.test-note-noai');
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, REPLACEMENTS);
      const note = fs.readFileSync(path.join(dir, 'backend', 'Models', 'NoteModel.cs'), 'utf-8');
      assert.ok(note.includes('WysiwygEditor'), 'WysiwygEditor');
      assert.ok(note.includes('Select'), 'Select');
      assert.ok(note.includes('Checkbox'), 'Checkbox');
      assert.ok(note.includes('EntityFieldsModel'), 'EntityFieldsModel');
      assert.ok(!note.includes('AISanityCheck'), 'no AISanityCheck');
      assert.ok(!note.includes('AISuggestion'), 'no AISuggestion');
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });

  it('AI enabled: AI attributes on adjacent lines', () => {
    const dir = path.join(__dirname, '..', '.test-note-ai');
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, AI_REPLACEMENTS);
      const note = fs.readFileSync(path.join(dir, 'backend', 'Models', 'NoteModel.cs'), 'utf-8');
      assert.ok(note.includes('AISanityCheck'), 'AISanityCheck');
      assert.ok(note.includes('AISuggestion'), 'AISuggestion');
      const lines = note.split('\n');
      const sanityLine = lines.findIndex(l => l.includes('AISanityCheck'));
      const suggestionLine = lines.findIndex(l => l.includes('AISuggestion'));
      assert.ok(Math.abs(sanityLine - suggestionLine) === 1, 'adjacent lines');
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });

  it('has required using statements', () => {
    const dir = path.join(__dirname, '..', '.test-note-usings');
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, REPLACEMENTS);
      const note = fs.readFileSync(path.join(dir, 'backend', 'Models', 'NoteModel.cs'), 'utf-8');
      assert.ok(note.includes('using Newtonsoft.Json'), 'Newtonsoft.Json');
      assert.ok(note.includes('using ReflectiveForms.Core.Attributes.Fields'), 'field attributes');
      assert.ok(note.includes('using ReflectiveForms.Core.Models'), 'models');
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });

  it('fields have JsonProperty attributes', () => {
    const dir = path.join(__dirname, '..', '.test-note-json');
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, REPLACEMENTS);
      const note = fs.readFileSync(path.join(dir, 'backend', 'Models', 'NoteModel.cs'), 'utf-8');
      const lines = note.split('\n');
      for (let i = 0; i < lines.length; i++) {
        if (lines[i].trim().startsWith('public ') && lines[i].includes('=')) {
          let hasJson = false;
          for (let j = i - 1; j >= Math.max(0, i - 5); j--) {
            if (lines[j].includes('JsonProperty')) { hasJson = true; break; }
          }
          assert.ok(hasJson, `Field at line ${i + 1} should have JsonProperty`);
        }
      }
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// SheetsEnabled injection detail
// ─────────────────────────────────────────────────────────────────────────────

describe('SheetsEnabled injection detail', () => {
  it('sheets disabled: SheetsEnabled inside builder block', () => {
    const dir = path.join(__dirname, '..', '.test-sheets-placement');
    const replacements = { ...REPLACEMENTS, SHEETS_CONFIG: '            SheetsEnabled = false,' };
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, replacements);
      const rb = fs.readFileSync(path.join(dir, 'backend', 'RfBuilder.cs'), 'utf-8');
      const builderStart = rb.indexOf('new RfConfigurationBuilder');
      const sheetsPos = rb.indexOf('SheetsEnabled = false');
      assert.ok(builderStart !== -1 && sheetsPos > builderStart, 'inside builder block');
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });

  it('sheets enabled: no SheetsEnabled line or artifacts', () => {
    const dir = path.join(__dirname, '..', '.test-sheets-enabled');
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, REPLACEMENTS);
      const rb = fs.readFileSync(path.join(dir, 'backend', 'RfBuilder.cs'), 'utf-8');
      assert.ok(!rb.includes('SheetsEnabled'), 'no SheetsEnabled');
      assert.ok(!rb.includes('SheetsEnabled = ,'), 'no syntax artifact');
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Placeholder value edge cases
// ─────────────────────────────────────────────────────────────────────────────

describe('placeholder value edge cases', () => {
  it('APP_NAME with apostrophe renders in HTML and config', () => {
    const dir = path.join(__dirname, '..', '.test-edge-appname');
    const replacements = { ...REPLACEMENTS, APP_NAME: "My App's Notes" };
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, replacements);
      const html = fs.readFileSync(path.join(dir, 'frontend', 'index.html'), 'utf-8');
      assert.ok(html.includes("My App's Notes"), 'HTML');
      const config = fs.readFileSync(path.join(dir, 'frontend', 'src', 'rf.config.ts'), 'utf-8');
      assert.ok(config.includes("My App's Notes"), 'config');
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });

  it('very long PROJECT_NAME renders without breaking', () => {
    const longName = 'a'.repeat(100);
    const dir = path.join(__dirname, '..', '.test-edge-longname');
    const replacements = { ...REPLACEMENTS, PROJECT_NAME: longName, CSPROJ_NAME: longName };
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, replacements);
      assertNoPlaceholders(dir);
      const rb = fs.readFileSync(path.join(dir, 'backend', 'RfBuilder.cs'), 'utf-8');
      assert.ok(rb.includes(longName), 'long name present');
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });

  it('PROJECT_NAME with dots renders in csproj', () => {
    const dir = path.join(__dirname, '..', '.test-edge-dots');
    const replacements = { ...REPLACEMENTS, PROJECT_NAME: 'my.project.name', CSPROJ_NAME: 'my.project.name' };
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, replacements);
      const csproj = fs.readFileSync(path.join(dir, 'backend', 'backend.csproj'), 'utf-8');
      assert.ok(csproj.includes('my.project.name'), 'dotted name');
      assertNoPlaceholders(dir);
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });

  it('same backend and frontend port still renders', () => {
    const dir = path.join(__dirname, '..', '.test-edge-sameport');
    const replacements = { ...REPLACEMENTS, BACKEND_PORT: '8080', FRONTEND_PORT: '8080' };
    try {
      if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true });
      copyTemplate(TEMPLATES_DIR, dir, replacements);
      assertNoPlaceholders(dir);
      const prog = fs.readFileSync(path.join(dir, 'backend', 'Program.cs'), 'utf-8');
      assert.ok(prog.includes('8080'), 'Program.cs port');
      const vite = fs.readFileSync(path.join(dir, 'frontend', 'vite.config.ts'), 'utf-8');
      assert.ok(vite.includes('port: 8080'), 'vite port');
    } finally { if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true }); }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Build-check: scaffold a project and verify the backend compiles
// ─────────────────────────────────────────────────────────────────────────────

const BUILD_DIR = path.join(__dirname, '..', '.test-build-output');
const CORE_CSPROJ = path.join(REPO_ROOT, 'ReflectiveForms.Core', 'ReflectiveForms.Core.csproj');

/**
 * Swap NuGet PackageReference for ReflectiveForms.Core with a local
 * ProjectReference so we can build/run without a published package.
 */
function patchCsproj(dir) {
  const csprojPath = path.join(dir, 'backend', 'backend.csproj');
  let csproj = fs.readFileSync(csprojPath, 'utf-8');
  csproj = csproj.replace(
    /\s*<PackageReference Include="ReflectiveForms\.Core"[^/]*\/>\s*/,
    ''
  );
  csproj = csproj.replace(
    '</Project>',
    `  <ItemGroup>\n    <ProjectReference Include="${CORE_CSPROJ.replace(/\\/g, '/')}" />\n  </ItemGroup>\n\n</Project>`
  );
  fs.writeFileSync(csprojPath, csproj);
}

describe('build-check: scaffolded backend compiles', () => {
  before(() => {
    if (fs.existsSync(BUILD_DIR)) {
      fs.rmSync(BUILD_DIR, { recursive: true });
    }
    copyTemplate(TEMPLATES_DIR, BUILD_DIR, REPLACEMENTS);
    patchCsproj(BUILD_DIR);
  });

  after(() => {
    if (fs.existsSync(BUILD_DIR)) {
      fs.rmSync(BUILD_DIR, { recursive: true });
    }
  });

  it('dotnet build succeeds on scaffolded backend', () => {
    const backendDir = path.join(BUILD_DIR, 'backend');
    try {
      execSync('dotnet build --nologo --verbosity quiet', {
        cwd: backendDir,
        stdio: 'pipe',
        timeout: 120_000,
      });
    } catch (err) {
      const stderr = err.stderr?.toString() || '';
      const stdout = err.stdout?.toString() || '';
      assert.fail(`dotnet build failed:\n${stderr}\n${stdout}`);
    }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Build-check (AI enabled): scaffold with AI and verify the backend compiles
// ─────────────────────────────────────────────────────────────────────────────

const AI_BUILD_DIR = path.join(__dirname, '..', '.test-ai-build-output');

describe('build-check: scaffolded AI-enabled backend compiles', () => {
  before(() => {
    if (fs.existsSync(AI_BUILD_DIR)) {
      fs.rmSync(AI_BUILD_DIR, { recursive: true });
    }
    copyTemplate(TEMPLATES_DIR, AI_BUILD_DIR, AI_REPLACEMENTS);
    patchCsproj(AI_BUILD_DIR);
  });

  after(() => {
    if (fs.existsSync(AI_BUILD_DIR)) {
      fs.rmSync(AI_BUILD_DIR, { recursive: true });
    }
  });

  it('dotnet build succeeds on AI-enabled scaffolded backend', () => {
    const backendDir = path.join(AI_BUILD_DIR, 'backend');
    try {
      execSync('dotnet build --nologo --verbosity quiet', {
        cwd: backendDir,
        stdio: 'pipe',
        timeout: 120_000,
      });
    } catch (err) {
      const stderr = err.stderr?.toString() || '';
      const stdout = err.stdout?.toString() || '';
      assert.fail(`dotnet build failed:\n${stderr}\n${stdout}`);
    }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Run-check: scaffold, start the backend, and build the frontend
// ─────────────────────────────────────────────────────────────────────────────

const RUN_DIR = path.join(__dirname, '..', '.test-run-output');
const RUN_REPLACEMENTS = {
  ...REPLACEMENTS,
  BACKEND_PORT: '19876',   // Use uncommon ports to avoid conflicts
  FRONTEND_PORT: '19877',
};

describe('run-check: scaffolded apps start and build', () => {
  before(() => {
    if (fs.existsSync(RUN_DIR)) fs.rmSync(RUN_DIR, { recursive: true });
    copyTemplate(TEMPLATES_DIR, RUN_DIR, RUN_REPLACEMENTS);
    patchCsproj(RUN_DIR);

    // Ensure the RF frontend library has been built
    const frontendLibDir = path.join(REPO_ROOT, 'ReflectiveForms.Frontend');
    if (!fs.existsSync(path.join(frontendLibDir, 'dist', 'index.es.js'))) {
      execSync('npm run build:lib', {
        cwd: frontendLibDir, stdio: 'pipe', timeout: 120_000,
      });
    }

    // Point the scaffolded frontend at the local library instead of npm
    const pkgPath = path.join(RUN_DIR, 'frontend', 'package.json');
    const pkg = JSON.parse(fs.readFileSync(pkgPath, 'utf-8'));
    pkg.dependencies['@reflectiveforms/frontend'] = `file:${frontendLibDir}`;
    fs.writeFileSync(pkgPath, JSON.stringify(pkg, null, 2));

    // Install frontend dependencies
    execSync('npm install', {
      cwd: path.join(RUN_DIR, 'frontend'),
      stdio: 'pipe',
      timeout: 120_000,
    });
  });

  after(() => {
    if (fs.existsSync(RUN_DIR)) fs.rmSync(RUN_DIR, { recursive: true });
  });

  it('backend starts and listens on configured port', { timeout: 90_000 }, async () => {
    const backendDir = path.join(RUN_DIR, 'backend');
    await new Promise((resolve, reject) => {
      const proc = spawn('dotnet', ['run'], {
        cwd: backendDir,
        stdio: ['ignore', 'pipe', 'pipe'],
      });
      let output = '';
      let settled = false;

      const timer = setTimeout(() => {
        if (!settled) {
          settled = true;
          proc.kill('SIGTERM');
          reject(new Error(`Backend did not start within 60s. Output:\n${output}`));
        }
      }, 60_000);

      const onData = (data) => {
        output += data.toString();
        if (!settled && output.includes('Now listening on')) {
          settled = true;
          clearTimeout(timer);
          proc.kill('SIGTERM');
          assert.ok(
            output.includes('19876'),
            `Should listen on configured port 19876, got:\n${output}`
          );
          resolve();
        }
      };

      proc.stdout.on('data', onData);
      proc.stderr.on('data', onData);

      proc.on('close', (code) => {
        if (!settled) {
          settled = true;
          clearTimeout(timer);
          reject(new Error(`Backend exited with code ${code} before listening:\n${output}`));
        }
      });
    });
  });

  it('frontend builds successfully (tsc + vite)', { timeout: 120_000 }, () => {
    try {
      execSync('npm run build', {
        cwd: path.join(RUN_DIR, 'frontend'),
        stdio: 'pipe',
        timeout: 120_000,
      });
    } catch (err) {
      const stderr = err.stderr?.toString() || '';
      const stdout = err.stdout?.toString() || '';
      assert.fail(`Frontend build failed:\n${stderr}\n${stdout}`);
    }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// CLI invocation check: verify the entry point runs correctly when called via
// a symlink (as npm/npx would), and that the isDirectRun guard triggers main()
// ─────────────────────────────────────────────────────────────────────────────
const CLI_TEST_DIR = path.join(__dirname, '..', '.test-cli-invoke');
const CLI_SCRIPT   = path.resolve(__dirname, '..', 'src', 'index.js');
const SYMLINK_PATH = path.join(CLI_TEST_DIR, 'create-app-bin');

describe('cli-check: invocation via symlink (simulates npx)', () => {
  before(() => {
    if (fs.existsSync(CLI_TEST_DIR)) fs.rmSync(CLI_TEST_DIR, { recursive: true });
    fs.mkdirSync(CLI_TEST_DIR, { recursive: true });
    fs.symlinkSync(CLI_SCRIPT, SYMLINK_PATH);
  });

  after(() => {
    if (fs.existsSync(CLI_TEST_DIR)) fs.rmSync(CLI_TEST_DIR, { recursive: true });
  });

  it('main() runs and scaffolds a project when called via symlink', { timeout: 30_000 }, async () => {
    const projectName = 'cli-symlink-test';
    const projectDir  = path.join(CLI_TEST_DIR, projectName);

    await new Promise((resolve, reject) => {
      const child = spawn('node', [SYMLINK_PATH, projectName], {
        cwd: CLI_TEST_DIR,
        stdio: ['pipe', 'pipe', 'pipe'],
      });

      let stdout = '';
      child.stdout.on('data', d => { stdout += d.toString(); });
      child.stderr.on('data', d => { stdout += d.toString(); });

      // Send one answer every 300 ms; keep stdin open until all are written
      // Answers: display-name, primary-color, backend-port, frontend-port,
      //          infra-stack (1=local), ai (n), sheets (y=default)
      const answers = ['\n', '\n', '\n', '\n', '1\n', 'n\n', '\n'];
      let idx = 0;
      const tick = setInterval(() => {
        if (idx < answers.length) {
          child.stdin.write(answers[idx++]);
        } else {
          clearInterval(tick);
          child.stdin.end();
        }
      }, 300);

      child.on('close', code => {
        if (code !== 0) return reject(new Error(`CLI exited ${code}:\n${stdout}`));
        resolve();
      });
    });

    assert.ok(fs.existsSync(projectDir),            'project directory was created');
    assert.ok(fs.existsSync(path.join(projectDir, 'backend')),  'backend/ exists');
    assert.ok(fs.existsSync(path.join(projectDir, 'frontend')), 'frontend/ exists');
    assert.ok(fs.existsSync(path.join(projectDir, 'docker-compose.yml')), 'docker-compose.yml exists');
    assert.ok(fs.existsSync(path.join(projectDir, 'README.md')), 'README.md exists');
  });
});
