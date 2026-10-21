'use client';
import {useEffect, useState} from 'react';
import {Ordering, type TableContext} from '@/app/commander/[locationId]/ordering';
import {Notice, Spinner} from '@/components/order-ui';
import {StorefrontError, errorMessage, storefront} from '@/lib/storefront';
import {tr, useLang} from '@/lib/i18n';

/** QR code d'une table : la table est reconnue par son jeton, puis la carte de son établissement s'ouvre. */
export function TableOrdering({token}: {token: string}) {
  useLang();
  const [table, setTable] = useState<(TableContext & {locationId: string}) | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    storefront<{locationId: string; label: string; areaName: string}>(`/tables/${encodeURIComponent(token)}`, {signal: controller.signal})
      .then(found => found && setTable({token, locationId: found.locationId, label: found.label, areaName: found.areaName}))
      .catch(reason => !controller.signal.aborted && setError(reason instanceof StorefrontError && reason.status === 404
        ? tr('Ce QR code n’est plus valable. Demandez au personnel de vous aider.')
        : errorMessage(reason)));
    return () => controller.abort();
  }, [token]);

  if (error) return <main className="mx-auto max-w-lg px-4 py-16"><Notice tone="error">{error}</Notice></main>;
  if (!table) return <main className="mx-auto max-w-lg px-4 py-16"><Spinner /></main>;
  return <Ordering locationId={table.locationId} table={table} />;
}
