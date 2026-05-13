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
    // Two passes: first resolves top-level placeholders (e.g. {{INFRA_SERVICE_INIT}}),
    // second resolves any nested placeholders inside the injected values (e.g. {{PROJECT_NAME}}).
    for (let pass = 0; pass < 2; pass++) {
      for (const [placeholder, value] of Object.entries(replacements)) {
        content = content.replaceAll(`{{${placeholder}}}`, value);
      }
    }
    fs.writeFileSync(dest, content);
  }
}

const CCK_VERSION = '2026.4.22.71';

export function infraReplacements(stack) {
  if (stack === 'aws') {
    return {
      INFRA_USING_STATEMENTS: [
        'using CrossCloudKit.Database.AWS;',
        'using CrossCloudKit.File.AWS;',
        'using CrossCloudKit.Memory.Redis;',
        'using CrossCloudKit.PubSub.AWS;',
      ].join('\n'),
      INFRA_SERVICE_INIT: [
        '        var redisOpts = new RedisConnectionOptions',
        '        {',
        '            Host = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost",',
        '            Port = int.Parse(Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379"),',
        '            Password = Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? ""',
        '        };',
        '        var memoryService = new MemoryServiceRedis(redisOpts);',
        '        var pubSubService = new PubSubServiceAWS(',
        '            Environment.GetEnvironmentVariable("AWS_ACCESS_KEY") ?? "",',
        '            Environment.GetEnvironmentVariable("AWS_SECRET_KEY") ?? "",',
        '            Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1");',
        '        var fileService = new FileServiceAWS(',
        '            Environment.GetEnvironmentVariable("AWS_ACCESS_KEY") ?? "",',
        '            Environment.GetEnvironmentVariable("AWS_SECRET_KEY") ?? "",',
        '            Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1");',
        '        var databaseService = new DatabaseServiceAWS(',
        '            Environment.GetEnvironmentVariable("AWS_ACCESS_KEY") ?? "",',
        '            Environment.GetEnvironmentVariable("AWS_SECRET_KEY") ?? "",',
        '            Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",',
        '            memoryService);',
      ].join('\n'),
      INFRA_CSPROJ_PACKAGES: [
        `    <PackageReference Include="CrossCloudKit.Database.AWS" Version="${CCK_VERSION}" />`,
        `    <PackageReference Include="CrossCloudKit.File.AWS" Version="${CCK_VERSION}" />`,
        `    <PackageReference Include="CrossCloudKit.Memory.Redis" Version="${CCK_VERSION}" />`,
        `    <PackageReference Include="CrossCloudKit.PubSub.AWS" Version="${CCK_VERSION}" />`,
      ].join('\n'),
      INFRA_ENV_VARS: [
        '',
        '# AWS',
        'AWS_ACCESS_KEY=',
        'AWS_SECRET_KEY=',
        'AWS_REGION=us-east-1',
        '',
        '# Redis (cache & distributed locking)',
        'REDIS_HOST=localhost',
        'REDIS_PORT=6379',
        '# REDIS_PASSWORD=',
      ].join('\n'),
      INFRA_DOCKER_SERVICES: [
        '',
        '  redis:',
        '    image: redis:7-alpine',
        '    ports:',
        '      - "6379:6379"',
        '    restart: unless-stopped',
      ].join('\n'),
      INFRA_DOCKER_ENV: [
        '      - AWS_ACCESS_KEY=${AWS_ACCESS_KEY}',
        '      - AWS_SECRET_KEY=${AWS_SECRET_KEY}',
        '      - AWS_REGION=${AWS_REGION:-us-east-1}',
        '      - REDIS_HOST=redis',
        '      - REDIS_PORT=6379',
      ].join('\n'),
      INFRA_DOCKER_DEPENDS: '\n      - redis',
      INFRA_README_SECTION: `## Infrastructure

This project uses **AWS** services for production data:

| Service | Provider | Purpose |
|---------|----------|---------|
| Database | AWS DynamoDB | Document storage |
| File storage | AWS S3 | Media & file uploads |
| Cache | Redis | In-memory cache & distributed locking |
| PubSub | AWS SNS+SQS | Event messaging |

### Prerequisites

- AWS account with DynamoDB, S3, and SNS+SQS access
- Redis instance (included in docker-compose for local dev)
- Set \`AWS_ACCESS_KEY\`, \`AWS_SECRET_KEY\`, and \`AWS_REGION\` in \`.env\``,
    };
  }

  if (stack === 'gcp') {
    return {
      INFRA_USING_STATEMENTS: [
        'using CrossCloudKit.Database.GC;',
        'using CrossCloudKit.File.GC;',
        'using CrossCloudKit.Memory.Redis;',
        'using CrossCloudKit.PubSub.GC;',
      ].join('\n'),
      INFRA_SERVICE_INIT: [
        '        var redisOpts = new RedisConnectionOptions',
        '        {',
        '            Host = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost",',
        '            Port = int.Parse(Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379"),',
        '            Password = Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? ""',
        '        };',
        '        var memoryService = new MemoryServiceRedis(redisOpts);',
        '        var pubSubService = new PubSubServiceGC(',
        '            Environment.GetEnvironmentVariable("GCP_PROJECT_ID") ?? "",',
        '            Environment.GetEnvironmentVariable("GCP_SERVICE_ACCOUNT_JSON") ?? "",',
        '            isBase64Encoded: false);',
        '        var fileService = new FileServiceGC(',
        '            Environment.GetEnvironmentVariable("GCP_PROJECT_ID") ?? "",',
        '            Environment.GetEnvironmentVariable("GCP_SERVICE_ACCOUNT_KEY_PATH") ?? "");',
        '        var databaseService = new DatabaseServiceGC(',
        '            Environment.GetEnvironmentVariable("GCP_PROJECT_ID") ?? "",',
        '            Environment.GetEnvironmentVariable("GCP_SERVICE_ACCOUNT_KEY_PATH") ?? "",',
        '            memoryService);',
      ].join('\n'),
      INFRA_CSPROJ_PACKAGES: [
        `    <PackageReference Include="CrossCloudKit.Database.GC" Version="${CCK_VERSION}" />`,
        `    <PackageReference Include="CrossCloudKit.File.GC" Version="${CCK_VERSION}" />`,
        `    <PackageReference Include="CrossCloudKit.Memory.Redis" Version="${CCK_VERSION}" />`,
        `    <PackageReference Include="CrossCloudKit.PubSub.GC" Version="${CCK_VERSION}" />`,
      ].join('\n'),
      INFRA_ENV_VARS: [
        '',
        '# Google Cloud',
        'GCP_PROJECT_ID=',
        'GCP_SERVICE_ACCOUNT_KEY_PATH=',
        'GCP_SERVICE_ACCOUNT_JSON=',
        '',
        '# Redis (cache & distributed locking)',
        'REDIS_HOST=localhost',
        'REDIS_PORT=6379',
        '# REDIS_PASSWORD=',
      ].join('\n'),
      INFRA_DOCKER_SERVICES: [
        '',
        '  redis:',
        '    image: redis:7-alpine',
        '    ports:',
        '      - "6379:6379"',
        '    restart: unless-stopped',
      ].join('\n'),
      INFRA_DOCKER_ENV: [
        '      - GCP_PROJECT_ID=${GCP_PROJECT_ID}',
        '      - GCP_SERVICE_ACCOUNT_KEY_PATH=${GCP_SERVICE_ACCOUNT_KEY_PATH}',
        '      - GCP_SERVICE_ACCOUNT_JSON=${GCP_SERVICE_ACCOUNT_JSON}',
        '      - REDIS_HOST=redis',
        '      - REDIS_PORT=6379',
      ].join('\n'),
      INFRA_DOCKER_DEPENDS: '\n      - redis',
      INFRA_README_SECTION: `## Infrastructure

This project uses **Google Cloud** services for production data:

| Service | Provider | Purpose |
|---------|----------|---------|
| Database | Google Cloud Datastore | Document storage |
| File storage | Google Cloud Storage | Media & file uploads |
| Cache | Redis | In-memory cache & distributed locking |
| PubSub | Google Cloud Pub/Sub | Event messaging |

### Prerequisites

- Google Cloud project with Datastore, Cloud Storage, and Pub/Sub APIs enabled
- Service account key file with appropriate permissions
- Redis instance (included in docker-compose for local dev)
- Set \`GCP_PROJECT_ID\` and \`GCP_SERVICE_ACCOUNT_KEY_PATH\` in \`.env\``,
    };
  }

  if (stack === 'mongo') {
    return {
      INFRA_USING_STATEMENTS: [
        'using CrossCloudKit.Database.Mongo;',
        'using CrossCloudKit.File.S3Compatible;',
        'using CrossCloudKit.Memory.Redis;',
        'using CrossCloudKit.PubSub.Redis;',
      ].join('\n'),
      INFRA_SERVICE_INIT: [
        '        var redisOpts = new RedisConnectionOptions',
        '        {',
        '            Host = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost",',
        '            Port = int.Parse(Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379"),',
        '            Password = Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? ""',
        '        };',
        '        var memoryService = new MemoryServiceRedis(redisOpts);',
        '        var pubSubService = new PubSubServiceRedis(redisOpts);',
        '        var fileService = new FileServiceS3Compatible(',
        '            Environment.GetEnvironmentVariable("S3_SERVER") ?? "localhost:9000",',
        '            Environment.GetEnvironmentVariable("S3_ACCESS_KEY") ?? "minioadmin",',
        '            Environment.GetEnvironmentVariable("S3_SECRET_KEY") ?? "minioadmin",',
        '            Environment.GetEnvironmentVariable("S3_REGION") ?? "us-east-1");',
        '        var databaseService = new DatabaseServiceMongo(',
        '            Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING") ?? "mongodb://localhost:27017",',
        '            Environment.GetEnvironmentVariable("MONGODB_DATABASE") ?? "{{PROJECT_NAME}}",',
        '            memoryService);',
      ].join('\n'),
      INFRA_CSPROJ_PACKAGES: [
        `    <PackageReference Include="CrossCloudKit.Database.Mongo" Version="${CCK_VERSION}" />`,
        `    <PackageReference Include="CrossCloudKit.File.S3Compatible" Version="${CCK_VERSION}" />`,
        `    <PackageReference Include="CrossCloudKit.Memory.Redis" Version="${CCK_VERSION}" />`,
        `    <PackageReference Include="CrossCloudKit.PubSub.Redis" Version="${CCK_VERSION}" />`,
      ].join('\n'),
      INFRA_ENV_VARS: [
        '',
        '# MongoDB',
        'MONGODB_CONNECTION_STRING=mongodb://localhost:27017',
        'MONGODB_DATABASE={{PROJECT_NAME}}',
        '',
        '# Redis (cache, distributed locking & pub/sub)',
        'REDIS_HOST=localhost',
        'REDIS_PORT=6379',
        '# REDIS_PASSWORD=',
        '',
        '# MinIO (S3-compatible file storage)',
        'S3_SERVER=localhost:9000',
        'S3_ACCESS_KEY=minioadmin',
        'S3_SECRET_KEY=minioadmin',
        'S3_REGION=us-east-1',
      ].join('\n'),
      INFRA_DOCKER_SERVICES: [
        '',
        '  mongodb:',
        '    image: mongo:7',
        '    ports:',
        '      - "27017:27017"',
        '    volumes:',
        '      - mongodb_data:/data/db',
        '    restart: unless-stopped',
        '',
        '  redis:',
        '    image: redis:7-alpine',
        '    ports:',
        '      - "6379:6379"',
        '    restart: unless-stopped',
        '',
        '  minio:',
        '    image: minio/minio:latest',
        '    command: server /data --console-address ":9001"',
        '    ports:',
        '      - "9000:9000"',
        '      - "9001:9001"',
        '    environment:',
        '      - MINIO_ROOT_USER=minioadmin',
        '      - MINIO_ROOT_PASSWORD=minioadmin',
        '    volumes:',
        '      - minio_data:/data',
        '    restart: unless-stopped',
      ].join('\n'),
      INFRA_DOCKER_ENV: [
        '      - MONGODB_CONNECTION_STRING=mongodb://mongodb:27017',
        '      - MONGODB_DATABASE={{PROJECT_NAME}}',
        '      - REDIS_HOST=redis',
        '      - REDIS_PORT=6379',
        '      - S3_SERVER=minio:9000',
        '      - S3_ACCESS_KEY=minioadmin',
        '      - S3_SECRET_KEY=minioadmin',
        '      - S3_REGION=us-east-1',
      ].join('\n'),
      INFRA_DOCKER_DEPENDS: [
        '',
        '      - mongodb',
        '      - redis',
        '      - minio',
      ].join('\n'),
      INFRA_README_SECTION: `## Infrastructure

This project uses **MongoDB + Redis + MinIO** — fully self-hosted, no cloud account needed:

| Service | Provider | Purpose |
|---------|----------|---------|
| Database | MongoDB | Document storage |
| File storage | MinIO (S3-compatible) | Media & file uploads |
| Cache & PubSub | Redis | In-memory cache, distributed locking & event messaging |

### Local development

All services are included in \`docker-compose.yml\`:

\\\`\\\`\\\`bash
docker compose up -d mongodb redis minio
\\\`\\\`\\\`

- **MongoDB** — \`mongodb://localhost:27017\`
- **Redis** — \`localhost:6379\`
- **MinIO Console** — \`http://localhost:9001\` (login: minioadmin / minioadmin)`,
    };
  }

  // Default: local (Basic providers)
  return {
    INFRA_USING_STATEMENTS: [
      'using CrossCloudKit.Database.Basic;',
      'using CrossCloudKit.File.Basic;',
      'using CrossCloudKit.Memory.Basic;',
      'using CrossCloudKit.PubSub.Basic;',
    ].join('\n'),
    INFRA_SERVICE_INIT: [
      '        var pubSubService = new PubSubServiceBasic();',
      '        var memoryService = new MemoryServiceBasic(pubSubService);',
      '        var fileService = new FileServiceBasic(memoryService, pubSubService);',
      '        var databaseService = new DatabaseServiceBasic("{{PROJECT_NAME}}-db", memoryService, Path.GetTempPath());',
    ].join('\n'),
    INFRA_CSPROJ_PACKAGES: [
      `    <PackageReference Include="CrossCloudKit.Database.Basic" Version="${CCK_VERSION}" />`,
      `    <PackageReference Include="CrossCloudKit.File.Basic" Version="${CCK_VERSION}" />`,
      `    <PackageReference Include="CrossCloudKit.Memory.Basic" Version="${CCK_VERSION}" />`,
      `    <PackageReference Include="CrossCloudKit.PubSub.Basic" Version="${CCK_VERSION}" />`,
    ].join('\n'),
    INFRA_ENV_VARS: '',
    INFRA_DOCKER_SERVICES: '',
    INFRA_DOCKER_ENV: '',
    INFRA_DOCKER_DEPENDS: '',
    INFRA_README_SECTION: `## Infrastructure

This project uses **local file-based providers** — zero external dependencies. Data is
stored in temporary files and memory. Great for development and prototyping.

To switch to production infrastructure, re-scaffold with a different stack or manually
update \`backend/RfBuilder.cs\` to use cloud providers (AWS, Google Cloud, MongoDB, Redis).`,
  };
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

  console.log('\n  Infrastructure stack determines which database, file storage, cache,');
  console.log('  and messaging services your project uses. You can change this later by');
  console.log('  editing RfBuilder.cs and swapping the CrossCloudKit provider packages.\n');
  console.log('  1) Local         — file-based storage, zero dependencies (default)');
  console.log('  2) AWS           — DynamoDB, S3, SNS+SQS, Redis');
  console.log('  3) Google Cloud  — Datastore, Cloud Storage, Pub/Sub, Redis');
  console.log('  4) MongoDB       — MongoDB, MinIO (S3-compatible), Redis\n');
  const infraRaw = await ask(rl, 'Infrastructure stack (1-4)', '1');
  const infraMap = { '1': 'local', '2': 'aws', '3': 'gcp', '4': 'mongo' };
  const infraStack = infraMap[infraRaw] || 'local';

  console.log('\n  AI features include a centralized AI assistant with multi-turn chat,');
  console.log('  semantic search, AI-powered entity creation, field suggestions, sanity');
  console.log('  checks, NL filtering, and revision diff summaries. Ships with bundled');
  console.log('  local models (SmolLM2 + MiniLM) — no external services required.\n');
  const enableAiRaw = await ask(rl, 'Enable AI features? (y/N)', 'N');
  const enableAi = enableAiRaw.toLowerCase() === 'y' || enableAiRaw.toLowerCase() === 'yes';

  console.log('\n  RF Sheets is a built-in spreadsheet editor powered by Univer with 14');
  console.log('  custom RF formulas that pull live entity data, entity data sources,');
  console.log('  real-time collaborative viewing via WebSocket, per-sheet sharing');
  console.log('  (user/role/public), and Excel export.\n');
  const enableSheetsRaw = await ask(rl, 'Enable RF Sheets? (Y/n)', 'Y');
  const enableSheets = enableSheetsRaw.toLowerCase() !== 'n' && enableSheetsRaw.toLowerCase() !== 'no';

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
    SHEETS_CONFIG: enableSheets ? '' : '            SheetsEnabled = false,',
    ...infraReplacements(infraStack),
    ...aiReplacements(enableAi),
  };

  console.log(`\n📁 Creating project in ${projectDir}...\n`);
  copyTemplate(TEMPLATES_DIR, projectDir, replacements);

  console.log(`✅ Project "${projectName}" created successfully!`);
  const stackNames = { local: 'Local (file-based)', aws: 'AWS', gcp: 'Google Cloud', mongo: 'MongoDB + Redis' };
  console.log(`   Infrastructure: ${stackNames[infraStack]}.`);
  if (enableAi) {
    console.log(`   AI features enabled (bundled local models).`);
  }
  if (!enableSheets) {
    console.log(`   RF Sheets disabled.`);
  }
  console.log(`\nNext steps:\n`);
  console.log(`  cd ${projectName}`);
  console.log(`  # Start the backend:`);
  console.log(`  cd backend && dotnet run`);
  console.log(`  # In another terminal, start the frontend:`);
  console.log(`  cd frontend && npm install && npm run dev\n`);
}

const isDirectRun = process.argv[1] && (() => {
  try {
    return fs.realpathSync(path.resolve(process.argv[1])) === fs.realpathSync(fileURLToPath(import.meta.url));
  } catch {
    return false;
  }
})();
if (isDirectRun) {
  main().catch(console.error);
}
