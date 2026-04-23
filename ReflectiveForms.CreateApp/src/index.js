#!/usr/bin/env node

import fs from 'fs';
import path from 'path';
import readline from 'readline';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const TEMPLATES_DIR = path.join(__dirname, '..', 'templates');

function ask(rl, question, defaultValue) {
  return new Promise((resolve) => {
    const suffix = defaultValue ? ` (${defaultValue})` : '';
    rl.question(`${question}${suffix}: `, (answer) => {
      resolve(answer.trim() || defaultValue || '');
    });
  });
}

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

export function aiReplacements(enableAi) {
  if (!enableAi) {
    return {
      AI_USING_STATEMENTS: '',
      AI_SERVICE_INIT: '',
      AI_BUILDER_CONFIG: '',
      AI_ENTITY_FLAGS: '',
      AI_NOTE_ATTRIBUTES: '',
      AI_CSPROJ_PACKAGES: '',
      AI_ENV_VARS: '',
      AI_DOCKER_ENV: '',
      AI_README_SECTION: `## AI Features (Optional)

This project does not include AI configuration by default. To re-scaffold with AI
features enabled, run \`create-reflectiveforms-app\` again and answer **y** to the
"Enable AI features?" prompt.`,
    };
  }

  return {
    AI_USING_STATEMENTS: [
      'using CrossCloudKit.LLM.Basic;',
      'using CrossCloudKit.Vector.Basic;',
      'using ReflectiveForms.Core.Ai;',
    ].join('\n'),
    AI_SERVICE_INIT: [
      '',
      '        // AI services — bundled SmolLM2 + MiniLM for local dev (zero external dependencies).',
      '        // For production, replace with LLMServiceOpenAI pointing to OpenAI, Ollama, Azure, etc.',
      '        var llmService = new LLMServiceBasic();',
      '        var vectorService = new VectorServiceBasic();',
    ].join('\n'),
    AI_BUILDER_CONFIG: [
      '            AiServiceConfiguration = new AiServiceConfiguration(',
      '                HeavyLlmService: llmService,',
      '                LightLlmService: llmService,',
      '                VectorService: vectorService),',
    ].join('\n'),
    AI_ENTITY_FLAGS: [
      '                    EntityDescription = "A simple note with rich-text content.",',
      '                    SupportsSemanticSearch = true,',
      '                    SupportsAiGeneration = true,',
      '                    SupportsAiDiffSummary = true,',
      '                    SupportsNaturalLanguageFilter = true,',
    ].join('\n'),
    AI_NOTE_ATTRIBUTES: [
      '',
      '    [AISanityCheck("Is this content well-written and free of obvious spelling or grammar errors?")]',
      '    [AISuggestion("Write a short note based on the title.", "title")]',
    ].join('\n'),
    AI_CSPROJ_PACKAGES: [
      '    <PackageReference Include="CrossCloudKit.LLM.Basic" Version="2026.4.22.71" ExcludeAssets="contentFiles" />',
      '    <PackageReference Include="CrossCloudKit.Vector.Basic" Version="2026.4.22.71" />',
    ].join('\n'),
    AI_ENV_VARS: [
      '',
      '# AI (bundled models work out-of-the-box; configure for external providers)',
      '# LLM_BASE_URL=http://localhost:11434/v1',
      '# LLM_API_KEY=',
      '# LLM_MODEL=gemma3:12b',
    ].join('\n'),
    AI_DOCKER_ENV: [
      '      # - LLM_BASE_URL=${LLM_BASE_URL}',
      '      # - LLM_API_KEY=${LLM_API_KEY}',
      '      # - LLM_MODEL=${LLM_MODEL}',
    ].join('\n'),
    AI_README_SECTION: `## AI Features

This project is configured with AI features enabled using the bundled local models
(SmolLM2 for completion, MiniLM for embeddings). No external dependencies required.

### What's included

| Feature | Description |
|---------|-------------|
| **Semantic Search** | Find notes by meaning, not just keywords |
| **AI Generation** | Generate new entities from a text prompt |
| **Field Suggestions** | AI-powered suggestions for individual fields |
| **Sanity Checks** | AI validates content quality on save |
| **Diff Summaries** | AI-generated summaries of what changed between revisions |
| **NL Filtering** | Filter entities using natural language queries |

### Switching to an external LLM provider

Edit \`backend/RfBuilder.cs\` and replace the bundled services:

\\\`\\\`\\\`csharp
// Replace these lines:
var llmService = new LLMServiceBasic();

// With an OpenAI-compatible provider (OpenAI, Ollama, Azure, Groq, etc.):
var llmService = new LLMServiceOpenAI(
    baseUrl: "http://localhost:11434/v1",
    apiKey: "",
    defaultModel: "gemma3:12b",
    embeddingModel: "nomic-embed-text:v1.5");
\\\`\\\`\\\`

Add the NuGet package: \`dotnet add package CrossCloudKit.LLM.OpenAI\`

### Adding AI attributes to your models

\\\`\\\`\\\`csharp
// AI sanity check — validates field content on save
[AISanityCheck("Is this description professional and clear?")]

// AI suggestion — adds a "Suggest" button next to the field
[AISuggestion("Summarize in 2 sentences based on the content.", "content")]
\\\`\\\`\\\``,
  };
}

async function main() {
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });

  console.log('\n🔧 Create ReflectiveForms App\n');

  const projectName = process.argv[2] || await ask(rl, 'Project name', 'my-app');
  const appName = await ask(rl, 'Display name', projectName.replace(/[_-]/g, ' ').replace(/\b\w/g, c => c.toUpperCase()));
  const primaryColor = await ask(rl, 'Primary color (hex)', '#2563eb');
  const backendPort = await ask(rl, 'Backend port (dev)', '9000');
  const frontendPort = await ask(rl, 'Frontend port (dev)', '3000');
  const enableAiRaw = await ask(rl, 'Enable AI features? (y/N)', 'N');
  const enableAi = enableAiRaw.toLowerCase() === 'y' || enableAiRaw.toLowerCase() === 'yes';

  rl.close();

  const projectDir = path.resolve(process.cwd(), projectName);
  if (fs.existsSync(projectDir)) {
    console.error(`\n❌ Directory "${projectName}" already exists.`);
    process.exit(1);
  }

  const replacements = {
    PROJECT_NAME: projectName,
    APP_NAME: appName,
    PRIMARY_COLOR: primaryColor,
    BACKEND_PORT: backendPort,
    FRONTEND_PORT: frontendPort,
    CSPROJ_NAME: projectName.replace(/[^a-zA-Z0-9]/g, '.'),
    ...aiReplacements(enableAi),
  };

  console.log(`\n📁 Creating project in ${projectDir}...\n`);
  copyTemplate(TEMPLATES_DIR, projectDir, replacements);

  console.log(`✅ Project "${projectName}" created successfully!`);
  if (enableAi) {
    console.log(`   AI features enabled (bundled local models).\n`);
  }
  console.log(`\nNext steps:\n`);
  console.log(`  cd ${projectName}`);
  console.log(`  # Start the backend:`);
  console.log(`  cd backend && dotnet run`);
  console.log(`  # In another terminal, start the frontend:`);
  console.log(`  cd frontend && npm install && npm run dev\n`);
}

const isDirectRun = process.argv[1] && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url));
if (isDirectRun) {
  main().catch(console.error);
}
