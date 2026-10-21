// Copie la documentation développeurs (docs/developpeurs) dans le site : les pages Markdown dans content/developpeurs,
// les messages d'erreur dans public/developpeurs (téléchargeables).
import fs from 'node:fs';
import path from 'node:path';

const source = path.resolve('../docs/developpeurs');
const pages = path.resolve('content/developpeurs');
const files = path.resolve('public/developpeurs');
fs.rmSync(pages, {recursive: true, force: true});
fs.mkdirSync(pages, {recursive: true});
fs.mkdirSync(files, {recursive: true});
for (const name of fs.readdirSync(source)) {
  if (name.endsWith('.md')) fs.copyFileSync(path.join(source, name), path.join(pages, name));
  if (name.endsWith('.json')) fs.copyFileSync(path.join(source, name), path.join(files, name));
}
console.log('Documentation copiée.');
