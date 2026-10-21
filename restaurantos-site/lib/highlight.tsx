import {Fragment, type ReactNode} from 'react';

/**
 * Coloration légère des exemples de la documentation (aucune dépendance) : chaînes, commentaires, nombres, mots-clés,
 * clés JSON et variables d'environnement du shell.
 */
const keywords: Record<string, string[]> = {
  ts: ['const', 'let', 'await', 'async', 'function', 'return', 'if', 'else', 'new', 'import', 'from', 'export', 'class', 'interface', 'type', 'try', 'catch', 'throw', 'private', 'public', 'extends', 'implements', 'null', 'undefined', 'true', 'false', 'this', 'for', 'of'],
  dart: ['final', 'var', 'await', 'async', 'Future', 'return', 'if', 'else', 'class', 'import', 'throw', 'try', 'catch', 'null', 'true', 'false', 'this', 'const', 'void', 'extends', 'implements', 'for', 'in', 'late', 'required', 'override'],
  swift: ['let', 'var', 'func', 'await', 'async', 'throws', 'try', 'return', 'if', 'else', 'guard', 'struct', 'class', 'final', 'import', 'nil', 'true', 'false', 'self', 'extension', 'private', 'throw', 'do', 'catch', 'for', 'in'],
  kotlin: ['val', 'var', 'fun', 'return', 'if', 'else', 'class', 'import', 'null', 'true', 'false', 'this', 'private', 'throw', 'try', 'catch', 'when', 'object', 'for', 'in', 'suspend'],
  bash: ['curl', 'export', 'echo', 'openssl'],
};

const aliases: Record<string, string> = {js: 'ts', typescript: 'ts', sh: 'bash', shell: 'bash', kt: 'kotlin'};

function tokenPattern(lang: string): RegExp {
  if (lang === 'json') return /("(?:[^"\\]|\\.)*"(?=\s*:))|("(?:[^"\\]|\\.)*")|(-?\b\d+(?:\.\d+)?\b)|(\btrue\b|\bfalse\b|\bnull\b)/g;
  if (lang === 'bash') return /(#[^\n]*)|("(?:[^"\\]|\\.)*"|'[^']*')|(\$\{?[A-Za-z_][A-Za-z0-9_]*\}?)|(\s-{1,2}[A-Za-z][\w-]*)|(\b[a-z]+\b)/g;
  return /(\/\/[^\n]*|\/\*[\s\S]*?\*\/)|("(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|`(?:[^`\\]|\\.)*`)|(\b\d+(?:\.\d+)?\b)|(\b[A-Za-z_][A-Za-z0-9_]*\b)/g;
}

export function highlight(code: string, rawLang: string): ReactNode {
  const lang = aliases[rawLang] ?? rawLang;
  if (!['json', 'bash', 'ts', 'dart', 'swift', 'kotlin'].includes(lang)) return code;
  const words = new Set(keywords[lang] ?? []);
  const out: ReactNode[] = [];
  let last = 0;
  for (const match of code.matchAll(tokenPattern(lang))) {
    const index = match.index ?? 0;
    if (index > last) out.push(code.slice(last, index));
    const text = match[0];
    let className: string | null = null;
    if (lang === 'json') {
      className = match[1] ? 'text-pink-300' : match[2] ? 'text-emerald-300' : 'text-amber-300';
    } else if (lang === 'bash') {
      className = match[1] ? 'text-slate-500 italic' : match[2] ? 'text-emerald-300' : match[3] ? 'text-sky-300' : match[4] ? 'text-violet-300' : words.has(text) ? 'text-pink-300' : null;
    } else {
      className = match[1] ? 'text-slate-500 italic' : match[2] ? 'text-emerald-300' : match[3] ? 'text-amber-300'
        : words.has(text) ? 'text-pink-300' : /^[A-Z]/.test(text) ? 'text-sky-300' : null;
    }
    out.push(className ? <span key={index} className={className}>{text}</span> : <Fragment key={index}>{text}</Fragment>);
    last = index + text.length;
  }
  if (last < code.length) out.push(code.slice(last));
  return out;
}
