// Tickets ESC/POS produits dans le navigateur, pour une imprimante thermique reliée à l'appareil (Bluetooth ou USB).
// Même mise en page que le serveur (TicketRenderer) : 32 colonnes en 58 mm, 48 en 80 mm, moitié en double largeur.

export type TicketLine =
  | {kind: 'text'; text: string; right?: string; align?: 'left' | 'center' | 'right'; bold?: boolean; large?: boolean}
  | {kind: 'separator'}
  | {kind: 'feed'}
  | {kind: 'barcode'; text: string}
  | {kind: 'qr'; text: string};

export type PaperWidth = 58 | 80;

export const columnsOf = (paper: PaperWidth) => (paper === 58 ? 32 : 48);

const specials: Record<string, number> = {
  '€': 0x80, '‚': 0x82, '„': 0x84, '…': 0x85, '‘': 0x91, '’': 0x92, '“': 0x93, '”': 0x94, '•': 0x95, '–': 0x96, '—': 0x97,
  'Œ': 0x8c, 'œ': 0x9c, 'Š': 0x8a, 'š': 0x9a, 'Ÿ': 0x9f, ' ': 0x20, ' ': 0x20, ' ': 0x20, '−': 0x2d,
};

/** Windows-1252 (page 16 des imprimantes ESC/POS) : accents français, €, œ ; le reste devient « ? ». */
export function cp1252(text: string): number[] {
  const bytes: number[] = [];
  for (const char of text) {
    const code = char.codePointAt(0) ?? 0x3f;
    bytes.push(specials[char] ?? ((code >= 0x20 && code <= 0x7e) || (code >= 0xa1 && code <= 0xff) ? code : 0x3f));
  }
  return bytes;
}

function chunk(text: string, width: number): string[] {
  const out: string[] = [];
  let current = '';
  for (const word of text.split(' ').filter(Boolean)) {
    let piece = word;
    while (piece.length > width) {
      if (current) {
        out.push(current);
        current = '';
      }
      out.push(piece.slice(0, width));
      piece = piece.slice(width);
    }
    if (current && current.length + 1 + piece.length > width) {
      out.push(current);
      current = '';
    }
    current = current ? `${current} ${piece}` : piece;
  }
  if (current) out.push(current);
  return out;
}

/** Une ligne de texte découpée à la largeur du papier ; le texte de droite reste sur la dernière ligne. */
export function wrap(line: Extract<TicketLine, {kind: 'text'}>, columns: number): string[] {
  const width = line.large ? Math.floor(columns / 2) : columns;
  const indent = Math.min(line.text.length - line.text.trimStart().length, Math.floor(width / 4));
  const chunks = chunk(line.text, width - indent).map(part => ' '.repeat(indent) + part);
  if (chunks.length === 0) chunks.push('');
  const right = line.right ?? '';
  if (!right) return chunks;
  const last = chunks.pop() ?? '';
  if (last.length + 1 + right.length > width) return [...chunks, last, ' '.repeat(Math.max(0, width - right.length)) + right];
  return [...chunks, last + ' '.repeat(Math.max(1, width - last.length - right.length)) + right];
}

/** Aperçu texte (tests, impression de secours). */
export function plainText(lines: TicketLine[], paper: PaperWidth = 80): string {
  const columns = columnsOf(paper);
  return lines.flatMap(line => {
    if (line.kind === 'separator') return ['-'.repeat(columns)];
    if (line.kind === 'feed') return [''];
    if (line.kind !== 'text') return [line.text];
    const width = line.large ? Math.floor(columns / 2) : columns;
    return wrap(line, columns).map(text => (line.align === 'center' ? ' '.repeat(Math.max(0, Math.floor((width - text.length) / 2))) + text : text));
  }).join('\n');
}

const ESC = 0x1b;
const GS = 0x1d;
const LF = 0x0a;

/** Octets ESC/POS du ticket : initialisation, page 1252, lignes, avance et coupe. */
export function render(lines: TicketLine[], paper: PaperWidth = 80): Uint8Array<ArrayBuffer> {
  const columns = columnsOf(paper);
  const out: number[] = [ESC, 0x40, ESC, 0x74, 16];
  for (const line of lines) {
    if (line.kind === 'separator') {
      out.push(ESC, 0x61, 0, ...cp1252('-'.repeat(columns)), LF);
      continue;
    }
    if (line.kind === 'feed') {
      out.push(LF);
      continue;
    }
    if (line.kind === 'barcode') {
      const data = cp1252('{B' + line.text);
      out.push(ESC, 0x61, 1, GS, 0x68, 80, GS, 0x77, 2, GS, 0x48, 2, GS, 0x6b, 73, data.length, ...data, LF);
      continue;
    }
    if (line.kind === 'qr') {
      const data = cp1252(line.text);
      const size = data.length + 3;
      out.push(
        ESC, 0x61, 1,
        GS, 0x28, 0x6b, 4, 0, 0x31, 0x41, 0x32, 0,
        GS, 0x28, 0x6b, 3, 0, 0x31, 0x43, paper === 58 ? 5 : 6,
        GS, 0x28, 0x6b, 3, 0, 0x31, 0x45, 0x31,
        GS, 0x28, 0x6b, size & 0xff, size >> 8, 0x31, 0x50, 0x30, ...data,
        GS, 0x28, 0x6b, 3, 0, 0x31, 0x51, 0x30, LF,
      );
      continue;
    }
    out.push(ESC, 0x61, line.align === 'center' ? 1 : line.align === 'right' ? 2 : 0);
    out.push(ESC, 0x45, line.bold ? 1 : 0, GS, 0x21, line.large ? 0x11 : 0);
    for (const text of wrap(line, columns)) out.push(...cp1252(text), LF);
  }
  out.push(ESC, 0x45, 0, GS, 0x21, 0, ESC, 0x61, 0, ESC, 0x64, 3, GS, 0x56, 0x42, 3);
  return Uint8Array.from(out);
}
