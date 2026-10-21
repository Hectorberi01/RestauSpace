'use client';
import {useEffect, useState} from 'react';
import {FileUp} from 'lucide-react';
import {downloadCsv, parseCsv} from '@/lib/csv';
import {importCustomers, type Channel, type ImportResult, type ImportRow} from '@/lib/customers';
import {Alert, Button, Modal, useAction} from '@/components/ui';

const columns: {key: keyof ImportRow; names: string[]}[] = [
  {key: 'name', names: ['nom', 'name', 'client', 'nom complet']},
  {key: 'email', names: ['email', 'e-mail', 'mail', 'courriel']},
  {key: 'phone', names: ['telephone', 'téléphone', 'phone', 'tel', 'tél', 'mobile', 'whatsapp']},
  {key: 'tags', names: ['tags', 'tag', 'etiquettes', 'étiquettes']},
  {key: 'marketingChannels', names: ['consentements', 'consentement', 'marketing', 'consent']},
];

const channelOf = (value: string): Channel | null => {
  const key = value.trim().toLowerCase();
  if (key === 'email' || key === 'e-mail' || key === 'mail') return 'Email';
  if (key === 'sms') return 'Sms';
  if (key === 'whatsapp') return 'WhatsApp';
  return null;
};

const errorLabel: Record<string, string> = {
  'customers.customer.name_required': 'nom manquant',
  'customers.customer.name_too_long': 'nom trop long',
  'customers.customer.contact_required': 'coordonnée exigée manquante',
  'common.email.invalid': 'email invalide',
  'common.phone.invalid': 'téléphone invalide',
  'customers.tag.invalid': 'tag invalide',
};

/** Lignes du fichier : en-tête reconnu (nom, email, téléphone, tags, consentements) ou colonnes dans cet ordre. */
function toRows(table: string[][]): ImportRow[] {
  if (table.length === 0) return [];
  const header = table[0].map(cell => cell.trim().toLowerCase());
  const index = Object.fromEntries(columns.map(column => [column.key, header.findIndex(cell => column.names.includes(cell))])) as Record<keyof ImportRow, number>;
  const hasHeader = Object.values(index).some(position => position >= 0);
  const positions = hasHeader ? index : {name: 0, email: 1, phone: 2, tags: 3, marketingChannels: 4};
  return (hasHeader ? table.slice(1) : table).map(cells => {
    const read = (key: keyof ImportRow) => (positions[key] >= 0 ? (cells[positions[key]] ?? '').trim() : '');
    const list = (key: keyof ImportRow) => read(key).split(/[|,]/).map(item => item.trim()).filter(Boolean);
    return {
      name: read('name') || null,
      email: read('email') || null,
      phone: read('phone').replace(/[\s.\-()]/g, '') || null,
      tags: list('tags'),
      marketingChannels: list('marketingChannels').map(channelOf).filter((channel): channel is Channel => channel !== null),
    };
  });
}

export function ImportDialog({open, onClose, onDone}: {open: boolean; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [rows, setRows] = useState<ImportRow[] | null>(null);
  const [fileName, setFileName] = useState('');
  const [certified, setCertified] = useState(false);
  const [result, setResult] = useState<ImportResult | null>(null);
  useEffect(() => {
    if (!open) return;
    setRows(null);
    setFileName('');
    setCertified(false);
    setResult(null);
  }, [open]);
  const withConsent = (rows ?? []).filter(row => row.marketingChannels.length > 0).length;

  const submit = () => run('import', async () => {
    const all = rows ?? [];
    const merged: ImportResult = {created: 0, updated: 0, skipped: 0, invalid: 0, lines: []};
    for (let start = 0; start < all.length; start += 500) {
      const part = await importCustomers(all.slice(start, start + 500), certified);
      merged.created += part.created;
      merged.updated += part.updated;
      merged.skipped += part.skipped;
      merged.invalid += part.invalid;
      merged.lines.push(...part.lines.map(line => ({...line, row: line.row + start})));
    }
    setResult(merged);
    await onDone();
  }, 'Import terminé.');

  return (
    <Modal wide open={open} onClose={onClose} title="Importer des clients" footer={result ? <Button onClick={onClose}>Fermer</Button> : (
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button busy={busy === 'import'} disabled={!rows?.length || rows.length > 5000} onClick={() => void submit()}>
          <FileUp className="size-4" aria-hidden="true" />Importer {rows?.length ? `${rows.length} ligne${rows.length > 1 ? 's' : ''}` : ''}
        </Button>
      </>
    )}>
      {result ? (
        <div className="space-y-3 text-sm">
          <p><strong>{result.created}</strong> créée(s), <strong>{result.updated}</strong> complétée(s), <strong>{result.skipped}</strong> déjà à jour, <strong>{result.invalid}</strong> refusée(s).</p>
          {result.invalid > 0 && (
            <ul className="max-h-48 overflow-y-auto rounded-xl bg-slate-50 p-3 text-xs">
              {result.lines.filter(line => line.outcome === 'Invalid').map(line => (
                <li key={line.row}>Ligne {line.row} : {errorLabel[line.error ?? ''] ?? line.error}</li>
              ))}
            </ul>
          )}
        </div>
      ) : (
        <div className="space-y-4 text-sm">
          <p className="text-ink-600">
            Fichier CSV avec les colonnes <strong>nom, email, téléphone, tags, consentements</strong> (tags et canaux séparés par « | » ; canaux : email, sms, whatsapp).
            Une fiche qui existe déjà (même email ou téléphone) n’est pas dupliquée : elle reçoit les tags manquants.
          </p>
          <div className="flex flex-wrap items-center gap-2">
            <label className="inline-flex h-10 cursor-pointer items-center gap-2 rounded-xl border border-slate-300 bg-white px-3 font-semibold hover:bg-slate-50">
              <FileUp className="size-4" aria-hidden="true" />{fileName || 'Choisir un fichier'}
              <input type="file" accept=".csv,text/csv" className="sr-only" onChange={async event => {
                const file = event.target.files?.[0];
                if (!file) return;
                setFileName(file.name);
                setRows(toRows(parseCsv(await file.text())));
              }} />
            </label>
            <Button variant="ghost" size="sm" onClick={() => downloadCsv('modele-clients.csv', [
              ['nom', 'email', 'téléphone', 'tags', 'consentements'],
              ['Awa Houngbédji', 'awa@exemple.com', '+22997000000', 'VIP|Habituée', 'whatsapp|email'],
            ])}>Télécharger un modèle</Button>
          </div>
          {rows && (rows.length === 0 ? <Alert>Le fichier ne contient aucune ligne.</Alert> : rows.length > 5000 ? <Alert>5 000 lignes au plus par import.</Alert> : (
            <>
              <div className="overflow-x-auto rounded-xl border border-slate-200">
                <table className="w-full text-left text-xs">
                  <thead className="bg-slate-50 text-ink-500"><tr><th className="px-2 py-1.5">Nom</th><th className="px-2 py-1.5">Email</th><th className="px-2 py-1.5">Téléphone</th><th className="px-2 py-1.5">Tags</th><th className="px-2 py-1.5">Consentements</th></tr></thead>
                  <tbody className="divide-y divide-slate-100">
                    {rows.slice(0, 5).map((row, position) => (
                      <tr key={position}><td className="px-2 py-1.5">{row.name}</td><td className="px-2 py-1.5">{row.email}</td><td className="px-2 py-1.5">{row.phone}</td><td className="px-2 py-1.5">{row.tags.join(', ')}</td><td className="px-2 py-1.5">{row.marketingChannels.join(', ')}</td></tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {rows.length > 5 && <p className="text-xs text-ink-500">… et {rows.length - 5} autre(s) ligne(s).</p>}
              {withConsent > 0 && (
                <label className="flex items-start gap-3 rounded-xl border border-amber-200 bg-amber-50 p-3">
                  <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" checked={certified} onChange={event => setCertified(event.target.checked)} />
                  <span>Je certifie que ces {withConsent} client(s) ont accepté de recevoir nos offres sur les canaux indiqués.
                    <span className="block text-xs text-ink-500">Sans cette case, les consentements du fichier sont ignorés (preuve « import » enregistrée sinon).</span></span>
                </label>
              )}
            </>
          ))}
        </div>
      )}
    </Modal>
  );
}
