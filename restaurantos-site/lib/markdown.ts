/**
 * Lecture du Markdown de la documentation développeurs (le sous-ensemble qu'elle utilise) : titres, paragraphes, listes
 * (cases à cocher comprises), tableaux, citations, blocs de code et code, gras, italique et liens dans le texte. Des blocs
 * de code qui se suivent dans des langages différents forment un groupe à onglets.
 */
export type Inline =
  | {type: 'text'; value: string}
  | {type: 'code'; value: string}
  | {type: 'strong'; children: Inline[]}
  | {type: 'em'; children: Inline[]}
  | {type: 'link'; href: string; children: Inline[]};

export interface CodeBlock {
  lang: string;
  code: string;
}

export type Block =
  | {type: 'heading'; level: number; id: string; children: Inline[]; text: string}
  | {type: 'paragraph'; children: Inline[]}
  | {type: 'list'; ordered: boolean; items: {checked: boolean | null; children: Inline[]}[]}
  | {type: 'table'; head: Inline[][]; rows: Inline[][][]}
  | {type: 'quote'; children: Inline[]}
  | {type: 'code'; block: CodeBlock}
  | {type: 'codegroup'; blocks: CodeBlock[]};

export function slugify(text: string): string {
  return text
    .toLowerCase()
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/[`*_]/g, '')
    .replace(/[^a-z0-9\s-]/g, '')
    .trim()
    .replace(/\s+/g, '-');
}

/** Le texte brut d'un titre (sans la mise en forme), pour son ancre et la table des matières. */
export function plain(inlines: Inline[]): string {
  return inlines.map(item => (item.type === 'text' || item.type === 'code' ? item.value : plain(item.children))).join('');
}

export function inline(source: string): Inline[] {
  const out: Inline[] = [];
  let text = '';
  const flush = () => {
    if (text) out.push({type: 'text', value: text});
    text = '';
  };
  for (let i = 0; i < source.length; ) {
    const rest = source.slice(i);
    if (rest[0] === '\\' && rest.length > 1) {
      text += rest[1];
      i += 2;
      continue;
    }
    if (rest[0] === '`') {
      const end = source.indexOf('`', i + 1);
      if (end > i) {
        flush();
        out.push({type: 'code', value: source.slice(i + 1, end)});
        i = end + 1;
        continue;
      }
    }
    if (rest.startsWith('**')) {
      const end = source.indexOf('**', i + 2);
      if (end > i + 2) {
        flush();
        out.push({type: 'strong', children: inline(source.slice(i + 2, end))});
        i = end + 2;
        continue;
      }
    }
    if (rest[0] === '*' && rest[1] !== ' ') {
      const end = source.indexOf('*', i + 1);
      if (end > i + 1) {
        flush();
        out.push({type: 'em', children: inline(source.slice(i + 1, end))});
        i = end + 1;
        continue;
      }
    }
    if (rest[0] === '[') {
      const link = /^\[([^\]]+)\]\(([^)\s]+)\)/.exec(rest);
      if (link) {
        flush();
        out.push({type: 'link', href: link[2], children: inline(link[1])});
        i += link[0].length;
        continue;
      }
    }
    text += rest[0];
    i += 1;
  }
  flush();
  return out;
}

/** Les cellules d'une ligne de tableau ; un « | » dans du code (`a \| b`) ou échappé ne coupe pas la cellule. */
function cells(line: string): string[] {
  const body = line.trim().replace(/^\|/, '').replace(/\|$/, '');
  const result: string[] = [];
  let current = '';
  let inCode = false;
  for (let i = 0; i < body.length; i++) {
    const char = body[i];
    if (char === '\\' && body[i + 1] === '|') {
      current += '|';
      i++;
    } else if (char === '`') {
      inCode = !inCode;
      current += char;
    } else if (char === '|' && !inCode) {
      result.push(current.trim());
      current = '';
    } else {
      current += char;
    }
  }
  result.push(current.trim());
  return result;
}

export function parse(markdown: string): Block[] {
  const lines = markdown.replace(/\r\n/g, '\n').split('\n');
  const blocks: Block[] = [];
  const used = new Map<string, number>();
  const anchor = (text: string) => {
    const base = slugify(text) || 'section';
    const count = used.get(base) ?? 0;
    used.set(base, count + 1);
    return count === 0 ? base : `${base}-${count}`;
  };

  let codeEnd = -1;
  for (let i = 0; i < lines.length; ) {
    const line = lines[i];
    if (!line.trim()) {
      i++;
      continue;
    }

    const fence = /^```\s*([\w+-]*)\s*$/.exec(line);
    if (fence) {
      const start = i;
      const code: string[] = [];
      i++;
      while (i < lines.length && !/^```\s*$/.test(lines[i])) code.push(lines[i++]);
      i++;
      const block = {lang: fence[1] || 'text', code: code.join('\n')};
      const previous = blocks.at(-1);
      const separated = codeEnd < 0 || lines.slice(codeEnd, start).some(item => item.trim() !== '');
      codeEnd = i;
      if (previous?.type === 'code' && !separated && previous.block.lang !== block.lang) {
        blocks[blocks.length - 1] = {type: 'codegroup', blocks: [previous.block, block]};
      } else if (previous?.type === 'codegroup' && !separated && !previous.blocks.some(item => item.lang === block.lang)) {
        previous.blocks.push(block);
      } else {
        blocks.push({type: 'code', block});
      }
      continue;
    }

    const heading = /^(#{1,4})\s+(.*)$/.exec(line);
    if (heading) {
      const children = inline(heading[2].trim());
      const text = plain(children);
      blocks.push({type: 'heading', level: heading[1].length, id: anchor(text), children, text});
      i++;
      continue;
    }

    if (line.trim().startsWith('|') && i + 1 < lines.length && /^\s*\|?\s*:?-{3,}/.test(lines[i + 1])) {
      const head = cells(line).map(inline);
      i += 2;
      const rows: Inline[][][] = [];
      while (i < lines.length && lines[i].trim().startsWith('|')) rows.push(cells(lines[i++]).map(inline));
      blocks.push({type: 'table', head, rows});
      continue;
    }

    if (line.startsWith('>')) {
      const quote: string[] = [];
      while (i < lines.length && lines[i].startsWith('>')) quote.push(lines[i++].replace(/^>\s?/, ''));
      blocks.push({type: 'quote', children: inline(quote.join(' '))});
      continue;
    }

    const item = /^(\s*)([-*]|\d+\.)\s+(.*)$/.exec(line);
    if (item) {
      const ordered = /\d/.test(item[2]);
      const items: {checked: boolean | null; children: Inline[]}[] = [];
      while (i < lines.length) {
        const current = /^(\s*)([-*]|\d+\.)\s+(.*)$/.exec(lines[i]);
        if (current && current[1].length === 0) {
          let text = current[3];
          i++;
          while (i < lines.length && /^\s{2,}\S/.test(lines[i]) && !/^\s*([-*]|\d+\.)\s/.test(lines[i])) text += ' ' + lines[i++].trim();
          const task = /^\[( |x)\]\s+(.*)$/i.exec(text);
          items.push(task ? {checked: task[1].toLowerCase() === 'x', children: inline(task[2])} : {checked: null, children: inline(text)});
        } else if (current) {
          const last = items.at(-1);
          if (last) last.children.push({type: 'text', value: ' · '}, ...inline(current[3]));
          i++;
        } else if (lines[i].trim() && /^\s{2,}\S/.test(lines[i])) {
          const last = items.at(-1);
          if (last) last.children.push({type: 'text', value: ' '}, ...inline(lines[i].trim()));
          i++;
        } else {
          break;
        }
      }
      blocks.push({type: 'list', ordered, items});
      continue;
    }

    const paragraph: string[] = [];
    while (
      i < lines.length && lines[i].trim() && !/^(#{1,4}\s|```|>|\s*([-*]|\d+\.)\s)/.test(lines[i])
      && !(lines[i].trim().startsWith('|') && i + 1 < lines.length && /^\s*\|?\s*:?-{3,}/.test(lines[i + 1]))
    ) {
      paragraph.push(lines[i++].trim());
    }
    blocks.push({type: 'paragraph', children: inline(paragraph.join(' '))});
  }
  return blocks;
}
