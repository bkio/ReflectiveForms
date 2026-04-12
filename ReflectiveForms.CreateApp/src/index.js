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

async function main() {
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });

  console.log('\n🔧 Create ReflectiveForms App\n');

  const projectName = process.argv[2] || await ask(rl, 'Project name', 'my-app');
  const appName = await ask(rl, 'Display name', projectName.replace(/[_-]/g, ' ').replace(/\b\w/g, c => c.toUpperCase()));
  const primaryColor = await ask(rl, 'Primary color (hex)', '#2563eb');
  const backendPort = await ask(rl, 'Backend port (dev)', '9000');
  const frontendPort = await ask(rl, 'Frontend port (dev)', '3000');

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
  };

  console.log(`\n📁 Creating project in ${projectDir}...\n`);
  copyTemplate(TEMPLATES_DIR, projectDir, replacements);

  console.log(`✅ Project "${projectName}" created successfully!\n`);
  console.log(`Next steps:\n`);
  console.log(`  cd ${projectName}`);
  console.log(`  # Start the backend:`);
  console.log(`  cd backend && dotnet run`);
  console.log(`  # In another terminal, start the frontend:`);
  console.log(`  cd frontend && npm install && npm run dev\n`);
}

main().catch(console.error);
