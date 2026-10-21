'use client';
import {useState} from 'react';
import {Trash2} from 'lucide-react';
import {api, put} from '@/lib/api';
import {money, parseAmount} from '@/lib/format';
import type {PriceChannel, Product} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Button, Field, Input, Select, useAction} from '@/components/ui';

export const priceChannelLabel: Record<PriceChannel, string> = {
  InHouse: 'Salle et comptoir',
  Web: 'Site et QR de table',
  MobileApp: 'Application',
  Kiosk: 'Borne',
};

/**
 * Prix propres à un établissement, à un canal, ou aux deux. Le plus précis l'emporte ; les variantes gardent leur écart
 * avec ce prix.
 */
export function PriceOverrides({product, editable, onChanged}: {product: Product; editable: boolean; onChanged: () => Promise<void>}) {
  const {locations} = useConsole();
  const {busy, run} = useAction();
  const [locationId, setLocationId] = useState('');
  const [channel, setChannel] = useState<PriceChannel | ''>('');
  const [price, setPrice] = useState('');
  const overrides = product.priceOverrides ?? [];
  const locationName = (id: string | null) => (id ? locations.find(item => item.id === id)?.name ?? 'Établissement supprimé' : 'Tous les établissements');
  const parsed = parseAmount(price);
  const key = (item: {locationId: string | null; channel: PriceChannel | null}) => `${item.locationId ?? '*'}-${item.channel ?? '*'}`;

  return (
    <div className="space-y-4 pt-4">
      <p className="text-sm text-ink-500">
        Prix de base : <strong className="text-ink-800">{money(product.basePrice, product.currencyCode)}</strong>. Un prix propre remplace ce prix pour un
        établissement, un canal, ou les deux ; le plus précis l’emporte. Les variantes gardent leur écart.
      </p>
      {overrides.length === 0 ? (
        <p className="rounded-xl border border-dashed border-slate-300 px-4 py-3 text-sm text-ink-500">Même prix partout.</p>
      ) : (
        <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
          {overrides.map(item => (
            <li key={key(item)} className="flex items-center gap-3 px-4 py-2.5 text-sm">
              <span className="min-w-0 flex-1">
                <span className="font-medium">{locationName(item.locationId)}</span>
                <span className="text-ink-500"> · {item.channel ? priceChannelLabel[item.channel] : 'tous les canaux'}</span>
              </span>
              <span className="font-semibold">{money(item.basePrice, product.currencyCode)}</span>
              {editable && (
                <Button size="sm" variant="ghost" aria-label="Retirer ce prix" busy={busy === key(item)} onClick={() => run(key(item), async () => {
                  await api(`/catalog/products/${product.id}/price-overrides`, {method: 'DELETE', query: {locationId: item.locationId ?? undefined, channel: item.channel ?? undefined}});
                  await onChanged();
                }, 'Prix retiré.')}><Trash2 className="size-4" /></Button>
              )}
            </li>
          ))}
        </ul>
      )}
      {editable && (
        <form className="grid gap-2 sm:grid-cols-[1fr_1fr_8rem_auto] sm:items-end" onSubmit={event => {
          event.preventDefault();
          if (parsed === null || (!locationId && !channel)) return;
          void run('set', async () => {
            await put(`/catalog/products/${product.id}/price-overrides`, {locationId: locationId || null, channel: channel || null, basePrice: parsed});
            setPrice('');
            await onChanged();
          }, 'Prix enregistré.');
        }}>
          <Field label="Établissement">
            <Select value={locationId} onChange={event => setLocationId(event.target.value)}>
              <option value="">Tous</option>
              {locations.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
            </Select>
          </Field>
          <Field label="Canal">
            <Select value={channel} onChange={event => setChannel(event.target.value as PriceChannel | '')}>
              <option value="">Tous</option>
              {(Object.keys(priceChannelLabel) as PriceChannel[]).map(item => <option key={item} value={item}>{priceChannelLabel[item]}</option>)}
            </Select>
          </Field>
          <Field label="Prix">
            <Input inputMode="decimal" required value={price} onChange={event => setPrice(event.target.value)} placeholder={String(product.basePrice)} />
          </Field>
          <Button type="submit" busy={busy === 'set'} disabled={parsed === null || (!locationId && !channel)}>Enregistrer</Button>
        </form>
      )}
      {editable && !locationId && !channel && <p className="text-xs text-ink-500">Choisissez un établissement, un canal, ou les deux.</p>}
    </div>
  );
}
