'use client';
import {useEffect, useState} from 'react';
import {Check, Copy, KeyRound, Lock, Pencil, Plus, Printer as PrinterIcon, RotateCcw, ScrollText} from 'lucide-react';
import {
  createPrinter, deviceUrl, disablePrinter, enablePrinter, jobKindLabel, jobStatusLabel, listPrintJobs, listPrinters, printTestPage, protocolHint, protocolLabel,
  reprintJob, rotatePrinterToken, setKitchenRole, updatePrinter,
  type PrintJob, type Printer, type PrinterProtocol,
} from '@/lib/printing';
import {api} from '@/lib/api';
import {dateTime, time} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import type {Station} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, cx, useAction} from '@/components/ui';
import {Switch} from './shared';

const protocols: PrinterProtocol[] = ['StarCloudPrnt', 'EscPosRelay'];

function CopyLine({value, label}: {value: string; label: string}) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="flex items-center gap-2">
      <code className="min-w-0 flex-1 truncate rounded-lg bg-slate-100 px-2.5 py-2 text-xs" aria-label={label}>{value}</code>
      <Button size="sm" variant="secondary" onClick={() => void navigator.clipboard?.writeText(value).then(() => setCopied(true))}>
        {copied ? <Check className="size-4" aria-hidden="true" /> : <Copy className="size-4" aria-hidden="true" />}{copied ? 'Copiée' : 'Copier'}
      </Button>
    </div>
  );
}

/** L'adresse avec le jeton, affichée une seule fois, et comment la saisir selon le type d'imprimante. */
function TokenDialog({issued, onClose}: {issued: {name: string; protocol: PrinterProtocol; token: string} | null; onClose: () => void}) {
  return (
    <Modal open={!!issued} onClose={onClose} title={`Brancher ${issued?.name ?? ''}`} wide footer={<Button onClick={onClose}>J’ai copié l’adresse</Button>}>
      {issued && (
        <div className="space-y-4 text-sm">
          <p className="flex items-start gap-2 rounded-xl bg-amber-50 p-3 text-amber-900">
            <Lock className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
            Cette adresse contient le jeton de l’imprimante : elle ne sera plus affichée. Ne la partagez pas ; en cas de doute, générez-en une nouvelle.
          </p>
          <CopyLine value={deviceUrl(issued.protocol, issued.token)} label="Adresse de l’imprimante" />
          {issued.protocol === 'StarCloudPrnt' ? (
            <ol className="list-decimal space-y-1 pl-5 text-ink-700">
              <li>Ouvrez la page de configuration de l’imprimante (son adresse IP dans un navigateur).</li>
              <li>Dans « CloudPRNT », activez le service et collez cette adresse dans « Server URL ».</li>
              <li>Intervalle d’interrogation : 5 secondes. Enregistrez, puis redémarrez l’imprimante.</li>
              <li>Revenez ici et imprimez une page de test.</li>
            </ol>
          ) : (
            <ol className="list-decimal space-y-1 pl-5 text-ink-700">
              <li>Sur un ordinateur du restaurant toujours allumé, installez Node.js 20 ou plus récent.</li>
              <li>Copiez le relais <code className="text-xs">tools/print-relay/relay.mjs</code> fourni par RestauSpace.</li>
              <li>Lancez-le avec cette adresse et l’IP de l’imprimante : <code className="text-xs">PRINTER_HOST=192.168.1.50 RELAY_URL=… node relay.mjs</code></li>
              <li>Revenez ici et imprimez une page de test.</li>
            </ol>
          )}
        </div>
      )}
    </Modal>
  );
}

function PrinterDialog({target, onClose, onSaved}: {
  target: {printer?: Printer} | null;
  onClose: () => void;
  onSaved: (issued?: {name: string; protocol: PrinterProtocol; token: string}) => Promise<void>;
}) {
  const {locations} = useConsole();
  const {busy, run} = useAction();
  const [name, setName] = useState('');
  const [locationId, setLocationId] = useState('');
  const [protocol, setProtocol] = useState<PrinterProtocol>('StarCloudPrnt');
  const [tickets, setTickets] = useState(true);
  const [kitchen, setKitchen] = useState(false);
  const [station, setStation] = useState('');
  const stations = useResource(
    signal => (locationId ? api<Station[]>('/kitchen/stations', {query: {locationId}, signal}).catch(() => [] as Station[]) : Promise.resolve([] as Station[])),
    [locationId],
  );
  useEffect(() => {
    if (!target) return;
    setName(target.printer?.name ?? '');
    setLocationId(target.printer?.locationId ?? locations[0]?.id ?? '');
    setProtocol(target.printer?.protocol ?? 'StarCloudPrnt');
    setTickets(target.printer?.printsCustomerTickets ?? true);
    setKitchen(target.printer?.printsKitchenTickets ?? false);
    setStation(target.printer?.kitchenStationId ?? '');
  }, [target, locations]);
  const editing = !!target?.printer;

  return (
    <Modal open={!!target} onClose={onClose} title={editing ? `Modifier ${target?.printer?.name ?? ''}` : 'Nouvelle imprimante'} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="printer" busy={busy === 'save'} disabled={!name.trim() || !locationId}>{editing ? 'Enregistrer' : 'Créer et afficher l’adresse'}</Button>
      </>
    }>
      {target && (
        <form id="printer" className="space-y-4" onSubmit={event => {
          event.preventDefault();
          void run('save', async () => {
            if (target.printer) {
              await updatePrinter(target.printer.id, {name: name.trim(), printsCustomerTickets: tickets});
              if (kitchen !== target.printer.printsKitchenTickets || (station || null) !== target.printer.kitchenStationId) {
                await setKitchenRole(target.printer.id, kitchen, station || null);
              }
              await onSaved();
            } else {
              const issued = await createPrinter({locationId, name: name.trim(), protocol, printsCustomerTickets: tickets});
              if (kitchen) await setKitchenRole(issued.id, true, station || null);
              await onSaved({name: name.trim(), protocol, token: issued.token});
            }
          }, editing ? 'Imprimante enregistrée.' : undefined);
        }}>
          <Field label="Nom" hint="Par exemple « Comptoir ».">
            <Input value={name} maxLength={80} onChange={event => setName(event.target.value)} />
          </Field>
          {!editing && (
            <>
              <Field label="Établissement">
                <Select value={locationId} onChange={event => setLocationId(event.target.value)}>
                  {locations.map(location => <option key={location.id} value={location.id}>{location.name}</option>)}
                </Select>
              </Field>
              <Field label="Type" hint={protocolHint[protocol]}>
                <Select value={protocol} onChange={event => setProtocol(event.target.value as PrinterProtocol)}>
                  {protocols.map(item => <option key={item} value={item}>{protocolLabel[item]}</option>)}
                </Select>
              </Field>
            </>
          )}
          <label className="flex items-start gap-2 text-sm">
            <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" checked={tickets} onChange={event => setTickets(event.target.checked)} />
            <span>Imprime les tickets des bornes et les reçus<span className="block text-xs text-ink-500">Numéro de commande, lignes, total et moyens de paiement.</span></span>
          </label>
          <label className="flex items-start gap-2 text-sm">
            <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" checked={kitchen} onChange={event => setKitchen(event.target.checked)} />
            <span>Imprime les bons de cuisine<span className="block text-xs text-ink-500">Dès qu’une commande est acceptée : lignes, options et notes, sans prix ni nom du client.</span></span>
          </label>
          {kitchen && (stations.data?.length ?? 0) > 0 && (
            <Field label="Poste servi" hint="Une imprimante du bar n’imprime que les boissons, par exemple.">
              <Select value={station} onChange={event => setStation(event.target.value)}>
                <option value="">Tous les postes</option>
                {stations.data?.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
              </Select>
            </Field>
          )}
        </form>
      )}
    </Modal>
  );
}

function PrinterRow({printer, locationName, editable, onEdit, onIssued, onChanged}: {
  printer: Printer;
  locationName: string;
  editable: boolean;
  onEdit: () => void;
  onIssued: (issued: {name: string; protocol: PrinterProtocol; token: string}) => void;
  onChanged: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const active = printer.status === 'Active';
  return (
    <li className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center">
      <span className={cx('grid size-10 shrink-0 place-items-center rounded-xl', printer.isOnline && active ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-ink-500')} aria-hidden="true">
        <PrinterIcon className="size-5" />
      </span>
      <div className="min-w-0 flex-1 space-y-1">
        <p className="flex flex-wrap items-center gap-2 font-medium">
          {printer.name}
          {!active ? <Badge>Coupée</Badge> : <Badge tone={printer.isOnline ? 'green' : 'amber'}>{printer.isOnline ? 'En ligne' : 'Hors ligne'}</Badge>}
          <Badge>{protocolLabel[printer.protocol]}</Badge>
          {printer.printsCustomerTickets && <Badge tone="blue">Tickets et reçus</Badge>}
          {printer.printsKitchenTickets && <Badge tone="amber">Bons de cuisine</Badge>}
        </p>
        <p className="text-sm text-ink-500">
          {locationName} · jeton …{printer.tokenHint} · {printer.lastSeenAtUtc ? `vue le ${dateTime(printer.lastSeenAtUtc)}` : 'jamais connectée'}
        </p>
      </div>
      {editable && (
        <div className="flex shrink-0 flex-wrap items-center gap-2 self-end sm:self-auto">
          <Button size="sm" variant="ghost" onClick={onEdit}><Pencil className="size-4" aria-hidden="true" />Modifier</Button>
          <Button size="sm" variant="secondary" disabled={!active} busy={busy === 'test'} onClick={() => void run('test', async () => {
            await printTestPage(printer.id);
            await onChanged();
          }, 'Page de test envoyée.')}><ScrollText className="size-4" aria-hidden="true" />Test</Button>
          <Button size="sm" variant="secondary" busy={busy === 'token'} onClick={() => void run('token', async () => {
            const issued = await rotatePrinterToken(printer.id);
            await onChanged();
            onIssued({name: printer.name, protocol: printer.protocol, token: issued.token});
          })}><KeyRound className="size-4" aria-hidden="true" />Nouvelle adresse</Button>
          <Switch on={active} label={`${active ? 'Couper' : 'Réactiver'} : ${printer.name}`} disabled={!!busy}
            onChange={() => void run('toggle', async () => {
              await (active ? disablePrinter(printer.id) : enablePrinter(printer.id));
              await onChanged();
            }, active ? `${printer.name} coupée.` : `${printer.name} réactivée.`)} />
        </div>
      )}
    </li>
  );
}

const jobTone: Record<PrintJob['status'], 'green' | 'amber' | 'red' | 'neutral' | 'blue'> = {
  Pending: 'blue', Sent: 'amber', Printed: 'green', Failed: 'red', Expired: 'neutral',
};

function JobRow({job, printerName, editable, onChanged}: {job: PrintJob; printerName: string; editable: boolean; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  return (
    <li className="flex items-center gap-3 py-2.5 text-sm">
      <span className="w-12 shrink-0 tabular-nums text-ink-500">{time(job.createdAtUtc)}</span>
      <span className="min-w-0 flex-1 truncate">{jobKindLabel[job.kind]} · {printerName}</span>
      <Badge tone={jobTone[job.status]}>{jobStatusLabel[job.status]}</Badge>
      {editable && job.kind !== 'TestPage' && (job.status === 'Printed' || job.status === 'Failed' || job.status === 'Expired') && (
        <Button size="sm" variant="ghost" busy={busy === 'reprint'} onClick={() => void run('reprint', async () => {
          await reprintJob(job.id);
          await onChanged();
        }, 'Ticket renvoyé à l’imprimante.')}><RotateCcw className="size-4" aria-hidden="true" />Réimprimer</Button>
      )}
    </li>
  );
}

export function Printers() {
  const {can, locations} = useConsole();
  const editable = can('printing.manage');
  const allowed = can('printing.view') || editable;
  const [dialog, setDialog] = useState<{printer?: Printer} | null>(null);
  const [issued, setIssued] = useState<{name: string; protocol: PrinterProtocol; token: string} | null>(null);
  const printers = useResource(signal => allowed ? listPrinters(signal) : Promise.resolve(null), [allowed]);
  const jobs = useResource(signal => allowed ? listPrintJobs(signal) : Promise.resolve(null), [allowed]);

  if (!allowed) return <Empty icon={<PrinterIcon className="size-8" />} title="Imprimantes non disponibles">Réservé aux membres qui gèrent les imprimantes.</Empty>;
  if (printers.error) return <Alert>{printers.error}</Alert>;
  if (!printers.data) return <Spinner label="Chargement des imprimantes…" />;
  const nameOfLocation = (id: string) => locations.find(location => location.id === id)?.name ?? 'Établissement';
  const nameOfPrinter = (id: string) => printers.data?.find(printer => printer.id === id)?.name ?? 'Imprimante';
  const reload = async () => {
    await Promise.all([printers.reload(), jobs.reload()]);
  };

  return (
    <div className="space-y-6">
      {!editable && <Alert tone="blue">Lecture seule : seuls les membres qui gèrent les imprimantes peuvent les modifier.</Alert>}
      <Card className="p-5">
        <div className="flex flex-wrap items-start gap-3">
          <div className="min-w-0 flex-1">
            <h2 className="font-semibold">Imprimantes de tickets</h2>
            <p className="text-sm text-ink-500">Star CloudPRNT directement par Internet, ou imprimante ESC/POS du réseau local par le relais.</p>
          </div>
          {editable && locations.length > 0 && (
            <Button size="sm" onClick={() => setDialog({})}><Plus className="size-4" aria-hidden="true" />Nouvelle imprimante</Button>
          )}
        </div>
        {printers.data.length === 0 ? (
          <p className="mt-4 text-sm text-ink-500">Aucune imprimante pour le moment.</p>
        ) : (
          <ul className="mt-2 divide-y divide-slate-100">
            {printers.data.map(printer => (
              <PrinterRow key={printer.id} printer={printer} locationName={nameOfLocation(printer.locationId)} editable={editable}
                onEdit={() => setDialog({printer})} onIssued={setIssued} onChanged={reload} />
            ))}
          </ul>
        )}
      </Card>

      <Card className="p-5">
        <h2 className="font-semibold">Derniers tickets</h2>
        <p className="text-sm text-ink-500">Un ticket non confirmé est renvoyé toutes les 2 minutes, 5 fois au plus ; il expire après 2 heures.</p>
        {jobs.error && <p className="mt-3 text-sm text-red-700">{jobs.error}</p>}
        {jobs.data && jobs.data.length === 0 && <p className="mt-3 text-sm text-ink-500">Aucun ticket imprimé pour le moment.</p>}
        {jobs.data && jobs.data.length > 0 && (
          <ul className="mt-2 divide-y divide-slate-100">
            {jobs.data.map(job => <JobRow key={job.id} job={job} printerName={nameOfPrinter(job.printerId)} editable={editable} onChanged={reload} />)}
          </ul>
        )}
      </Card>

      <PrinterDialog target={dialog} onClose={() => setDialog(null)} onSaved={async created => {
        setDialog(null);
        await reload();
        if (created) setIssued(created);
      }} />
      <TokenDialog issued={issued} onClose={() => setIssued(null)} />
    </div>
  );
}
