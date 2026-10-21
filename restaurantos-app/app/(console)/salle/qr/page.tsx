'use client';
import {useEffect, useState} from 'react';
import Link from 'next/link';
import QRCode from 'qrcode';
import {ArrowLeft, ExternalLink, Printer, QrCode} from 'lucide-react';
import {api, post} from '@/lib/api';
import {useResource} from '@/lib/hooks';
import type {DiningArea, LocationSummary} from '@/lib/types';
import {useConsole, WithLocation} from '@/components/console';
import {Alert, Button, Card, Empty, Field, Modal, Select, Spinner, useAction} from '@/components/ui';

/** Adresse du site public qui affiche le menu. En développement, le site tourne sur le port 3002. */
const siteUrl = (process.env.NEXT_PUBLIC_SITE_URL ?? (process.env.NODE_ENV === 'development' ? 'http://localhost:3002' : '')).replace(/\/+$/, '');

/** Le QR porte un jeton aléatoire de la table, jamais son nom : il ouvre la commande à cette table sur le site. */
const tableUrl = (token: string) => `${siteUrl}/table/${encodeURIComponent(token)}`;

interface TableCode {
  tableId: string;
  diningAreaId: string;
  areaName: string;
  label: string;
  token: string;
}

function TableQr({url, label, area, restaurant, onRenew}: {url: string; label: string; area: string; restaurant: string; onRenew?: () => void}) {
  const [src, setSrc] = useState<string | null>(null);
  useEffect(() => {
    let alive = true;
    void QRCode.toString(url, {type: 'svg', errorCorrectionLevel: 'M', margin: 1}).then(svg => {
      if (alive) setSrc(`data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`);
    });
    return () => {
      alive = false;
    };
  }, [url]);
  return (
    <figure className="flex break-inside-avoid flex-col items-center rounded-2xl border border-slate-300 bg-white p-4 text-center">
      <p className="text-sm font-semibold text-ink-600">{restaurant}</p>
      {src ? <img src={src} alt={`QR code de la table ${label}`} className="my-2 size-40" /> : <div className="my-2 size-40 animate-pulse rounded-lg bg-slate-100" />}
      <figcaption>
        <p className="text-xl font-bold">Table {label}</p>
        <p className="text-xs text-ink-500">{area} · Scannez pour commander</p>
      </figcaption>
      {onRenew && <button type="button" onClick={onRenew} className="mt-2 text-xs font-semibold text-ink-500 hover:text-brand-700 print:hidden">Nouveau QR</button>}
    </figure>
  );
}

function Sheet({location}: {location: LocationSummary}) {
  const {tenant, can} = useConsole();
  const {run} = useAction();
  const [areaId, setAreaId] = useState('');
  const [renewing, setRenewing] = useState<TableCode | null>(null);
  const {data, error} = useResource(signal => api<DiningArea[]>('/floor/dining-areas', {query: {locationId: location.id}, signal}), [location.id]);
  const codes = useResource(() => (can('floor.manage') ? post<TableCode[]>('/floor/qr-codes', {locationId: location.id}) : Promise.resolve([] as TableCode[])), [location.id]);
  const tokenOf = (tableId: string) => codes.data?.find(code => code.tableId === tableId)?.token;
  const restaurant = tenant.branding.displayName || tenant.name;
  const areas = (data ?? []).filter(area => !areaId || area.id === areaId);
  const count = areas.reduce((sum, area) => sum + area.tables.length, 0);

  return (
    <div className="space-y-5">
      <div className="print:hidden">
        <Link href="/salle" className="inline-flex items-center gap-1 text-sm font-semibold text-brand-700"><ArrowLeft className="size-4" aria-hidden="true" />Salle</Link>
        <h1 className="mt-2 text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">QR codes des tables</h1>
        <p className="mt-1 text-sm text-ink-500">Collez un QR code sur chaque table : le client scanne, commande depuis son téléphone et règle au serveur ou en ligne (Mobile Money). La commande arrive en cuisine rattachée à la table. Le QR ne contient pas le nom de la table.</p>
      </div>

      {!siteUrl ? (
        <Alert>L’adresse du site public n’est pas configurée (variable NEXT_PUBLIC_SITE_URL). Les QR codes ne peuvent pas être générés.</Alert>
      ) : !can('floor.manage') ? (
        <Alert tone="amber">Préparer les QR codes des tables demande la gestion du plan de salle.</Alert>
      ) : error || codes.error ? <Alert>{error ?? codes.error}</Alert> : !data || !codes.data ? <Spinner /> : data.length === 0 ? (
        <Card className="p-6"><Empty icon={<QrCode className="size-8" />} title="Aucune table">Créez d’abord vos salles et vos tables depuis le plan de salle.</Empty></Card>
      ) : (
        <>
          <div className="flex flex-wrap items-end gap-2 print:hidden">
            <Field label="Salle">
              <Select className="w-full! sm:w-auto!" value={areaId} onChange={event => setAreaId(event.target.value)}>
                <option value="">Toutes les salles</option>
                {data.map(area => <option key={area.id} value={area.id}>{area.name}</option>)}
              </Select>
            </Field>
            <a href={`${siteUrl}/commander/${location.id}`} target="_blank" rel="noopener noreferrer" className="inline-flex h-10 items-center gap-1.5 rounded-xl px-3 text-sm font-semibold text-brand-700 hover:bg-brand-50">
              <ExternalLink className="size-4" aria-hidden="true" />Voir la carte en ligne
            </a>
            <Button className="ml-auto" disabled={count === 0} onClick={() => window.print()}><Printer className="size-4" aria-hidden="true" />Imprimer ({count})</Button>
          </div>
          {location.status !== 'Active' && <div className="print:hidden"><Alert tone="amber">L’établissement n’est pas ouvert : le menu public s’affiche, mais indique qu’aucune commande n’est prise.</Alert></div>}
          <p className="text-sm text-ink-500 print:hidden">La commande à table suit les réglages de vente en ligne de l’établissement (mode « Sur place » activé, moyens de paiement). Un QR perdu ou recopié se remplace par « Nouveau QR » : l’ancien ne commande plus rien.</p>
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4 print:grid-cols-3 print:gap-3">
            {areas.flatMap(area => area.tables.map(table => {
              const token = tokenOf(table.id);
              return token ? (
                <TableQr key={table.id} url={tableUrl(token)} label={table.label} area={area.name} restaurant={restaurant}
                  onRenew={() => setRenewing({tableId: table.id, diningAreaId: area.id, areaName: area.name, label: table.label, token})} />
              ) : null;
            }))}
          </div>
          <Modal open={!!renewing} onClose={() => setRenewing(null)} title={`Nouveau QR pour la table ${renewing?.label ?? ''}`} footer={
            <>
              <Button variant="secondary" onClick={() => setRenewing(null)}>Annuler</Button>
              <Button variant="danger" onClick={() => renewing && void run('renew', async () => {
                await post(`/floor/tables/${renewing.tableId}/qr-token`);
                setRenewing(null);
                await codes.reload();
              }, 'Nouveau QR prêt : imprimez-le et remplacez l’ancien.')}>Remplacer le QR</Button>
            </>
          }>
            <p className="text-sm text-ink-600">L’ancien QR de cette table ne permettra plus de commander. Imprimez le nouveau et collez-le à sa place.</p>
          </Modal>
        </>
      )}
    </div>
  );
}

export default function TableQrPage() {
  return <WithLocation>{location => <Sheet location={location} />}</WithLocation>;
}
