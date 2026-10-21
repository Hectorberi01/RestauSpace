const cell = (value: string) => (/[";\n]/.test(value) ? `"${value.replaceAll('"', '""')}"` : value);

/** Fichier CSV (séparateur « ; », UTF-8 avec BOM) ouvert tel quel par Excel en français. */
export function downloadCsv(fileName: string, rows: string[][]) {
  const blob = new Blob(['﻿' + rows.map(row => row.map(cell).join(';')).join('\n')], {type: 'text/csv;charset=utf-8'});
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(link.href);
}

/** Lit un CSV (séparateur « ; » ou « , » détecté sur la première ligne, guillemets doublés), BOM compris. */
export function parseCsv(text: string): string[][] {
  const source = text.replace(/^\uFEFF/, '');
  const firstLine = source.split(/\r?\n/, 1)[0] ?? '';
  const separator = (firstLine.match(/;/g)?.length ?? 0) >= (firstLine.match(/,/g)?.length ?? 0) ? ';' : ',';
  const rows: string[][] = [];
  let row: string[] = [];
  let value = '';
  let quoted = false;
  for (let index = 0; index < source.length; index++) {
    const char = source[index];
    if (quoted) {
      if (char === '"' && source[index + 1] === '"') {
        value += '"';
        index++;
      } else if (char === '"') {
        quoted = false;
      } else {
        value += char;
      }
    } else if (char === '"') {
      quoted = true;
    } else if (char === separator) {
      row.push(value);
      value = '';
    } else if (char === '\n' || char === '\r') {
      if (char === '\r' && source[index + 1] === '\n') index++;
      row.push(value);
      if (row.some(cellValue => cellValue.trim())) rows.push(row);
      row = [];
      value = '';
    } else {
      value += char;
    }
  }
  row.push(value);
  if (row.some(cellValue => cellValue.trim())) rows.push(row);
  return rows;
}
