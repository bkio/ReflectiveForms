import { describe, it, before, after } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'fs';
import path from 'path';
import { execSync, spawn } from 'child_process';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const TEMPLATES_DIR = path.join(__dirname, '..', 'templates');
const REPO_ROOT = path.join(__dirname, '..', '..');

import { aiReplacements } from '../src/index.js';

// Inline copyTemplate since the CLI mixes prompts with logic
function copyTemplate(src, dest, replacements) {
  if (fs.statSync(src).isDirectory()) {
    fs.mkdirSync(dest, { recursive: true });
    for (const entry of fs.readdirSync(src)) {
      copyTemplate(path.join(src, entry), path.join(dest, entry), replacements);
    }
  } else {
    let content = fs.readFileSync(src, 'utf-8');
    for (const [placeholder, value] of Object.entries(replacements)) {
      content = content.replaceAll(`{{${placeholder}}}`, value);
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
  ...aiReplacements(false),
};

const AI_REPLACEMENTS = {
  PROJECT_NAME: 'test-app',
  APP_NAME: 'Test App',
  PRIMARY_COLOR: '#ff6600',
  BACKEND_PORT: '4000',
  FRONTEND_PORT: '4001',
  CSPROJ_NAME: 'test.app',
  ...aiReplacements(true),
};

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
  for (const [placeholder, value] of Object.entries(AI_REPLACEMENTS)) {
    content = content.replaceAll(`{{${placeholder}}}`, value);
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
