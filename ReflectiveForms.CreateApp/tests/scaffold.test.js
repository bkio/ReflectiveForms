import { describe, it, before, after } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const TEMPLATES_DIR = path.join(__dirname, '..', 'templates');

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
  AUTH_MODE: 'local',
  CSPROJ_NAME: 'test.app',
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
