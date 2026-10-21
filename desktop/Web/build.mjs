import { spawnSync } from 'node:child_process';
import { mkdirSync, copyFileSync, readFileSync, writeFileSync } from 'node:fs';
mkdirSync('dist', { recursive: true });
const result = spawnSync(process.execPath, ['node_modules/@tailwindcss/cli/dist/index.mjs', '-i', 'styles.css', '-o', 'dist/styles.css', '--minify'], { stdio: 'inherit' });
if (result.status !== 0) process.exit(result.status || 1);
for (const name of ['index.html', 'app.js']) copyFileSync(name, `dist/${name}`);
copyFileSync('../Assets/logo-256.png', 'dist/logo.png');
writeFileSync('dist/THIRD-PARTY-NOTICES.txt', ['daisyUI 5\nhttps://daisyui.com\n' + readFileSync('node_modules/daisyui/LICENSE', 'utf8'), 'Tailwind CSS 4\nhttps://tailwindcss.com\n' + readFileSync('node_modules/tailwindcss/LICENSE', 'utf8')].join('\n\n'));
