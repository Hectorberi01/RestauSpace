'use client';
import {useEffect, useState} from 'react';
import {Copy, ExternalLink, MessageCircle, Send} from 'lucide-react';
import {api, post} from '@/lib/api';
import {loadCustomer} from '@/lib/customers';
import {fulfillmentOfOrder} from '@/lib/delivery';
import {normalizePhone} from '@/lib/reservations';
import {Alert, Button, Field, Input, Modal, Select, cx, useAction, useToast} from '@/components/ui';

export type ReceiptChannel = 'WhatsApp' | 'Sms';

interface ReceiptLink {
  url: string | null;
  text: string;
  canSendAutomatically: boolean;
}

interface ReceiptSendResult {
  status: 'Sent' | 'Skipped' | 'NoLink';
  reason: string | null;
  url: string | null;
  text: string;
}

const skipReason: Record<string, string> = {
  NoProvider: 'aucun compte WhatsApp ou SMS n’est relié à cet établissement (Paramètres > Notifications)',
  NoTemplate: 'aucun modèle « Reçu de la commande » n’est validé pour ce canal (Messages clients > Modèles)',
  TemplateIncomplete: 'le modèle « Reçu de la commande » est incomplet',
  QuotaExceeded: 'le quota d’envoi de messages est atteint',
  NoContact: 'le client n’a pas de numéro de téléphone',
  OptedOut: 'le client a refusé les messages sur ce canal',
  Duplicate: 'ce reçu vient déjà d’être envoyé à ce numéro',
  NotACustomer: 'ce destinataire n’est pas un client du restaurant',
  ConsentRequired: 'le consentement du client est requis',
  ConsentNotGranted: 'le client n’a pas donné son accord',
  FrequencyLimited: 'trop de messages récents pour ce client',
};

/** Lien wa.me : vers le numéro s'il est connu, sinon WhatsApp demande le contact. Rien ne passe par la plateforme. */
export function whatsAppLink(phone: string | null, text: string) {
  const digits = phone ? phone.replace(/[^0-9]/g, '') : '';
  return `https://wa.me/${digits}?text=${encodeURIComponent(text)}`;
}

/**
 * Envoi du reçu au client : par le compte WhatsApp (ou SMS) du restaurant, avec le lien signé vers la page du reçu ; ou,
 * à défaut, depuis le téléphone de l'équipier (lien wa.me avec le reçu en texte).
 */
export function SendReceiptDialog({orderId, customerId, open, onClose}: {orderId: string; customerId: string | null; open: boolean; onClose: () => void}) {
  const toast = useToast();
  const {busy, run} = useAction();
  const [link, setLink] = useState<ReceiptLink | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [phone, setPhone] = useState('');
  const [customerPhone, setCustomerPhone] = useState<string | null>(null);
  const [channel, setChannel] = useState<ReceiptChannel>('WhatsApp');
  const [skipped, setSkipped] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setSkipped(null);
    setError(null);
    const controller = new AbortController();
    api<ReceiptLink>(`/online-sales/receipts/${orderId}`, {signal: controller.signal})
      .then(setLink)
      .catch(reason => !controller.signal.aborted && setError(reason instanceof Error ? reason.message : 'Reçu indisponible.'));
    // Numéro proposé : la fiche client, sinon le téléphone de la livraison ou du retrait.
    (customerId ? loadCustomer(customerId, controller.signal).then(customer => customer.phone) : Promise.resolve(null))
      .then(found => found ?? fulfillmentOfOrder(orderId, controller.signal).then(fulfillment => fulfillment?.customerPhone ?? null))
      .then(found => {
        setCustomerPhone(customerId ? found : null);
        setPhone(current => current || found || '');
      })
      .catch(() => undefined);
    return () => controller.abort();
  }, [open, orderId, customerId]);

  const normalized = normalizePhone(phone);
  const invalid = phone.trim() !== '' && normalized === undefined;
  const text = link?.text ?? '';
  const manual = whatsAppLink(normalized ?? null, text);

  return (
    <Modal open={open} onClose={onClose} title="Envoyer le reçu au client" footer={<Button variant="secondary" onClick={onClose}>Fermer</Button>}>
      <div className="space-y-4">
        {error && <Alert>{error}</Alert>}
        <Field label="Numéro du client" hint={customerId && !phone ? 'Laissez vide pour utiliser le numéro de la fiche client.' : 'Format international : +229…'}>
          <Input type="tel" autoComplete="off" placeholder="+229 01 90 00 00 00" value={phone} onChange={event => setPhone(event.target.value)} />
        </Field>
        {invalid && <Alert tone="amber">Numéro invalide : utilisez le format international (+229…).</Alert>}

        <section className="rounded-xl border border-slate-200 p-3" aria-labelledby="receipt-auto">
          <h3 id="receipt-auto" className="text-sm font-semibold">Par le compte du restaurant</h3>
          <p className="mt-0.5 text-xs text-ink-500">
            {link?.canSendAutomatically
              ? 'Le client reçoit un lien vers son reçu (page imprimable, mentions fiscales comprises).'
              : link ? 'Pas de site actif : le lien du reçu n’existe pas. Envoyez le reçu en texte depuis votre téléphone.' : 'Chargement…'}
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            <div className="w-36"><Select aria-label="Canal" value={channel} onChange={event => setChannel(event.target.value as ReceiptChannel)} disabled={!link?.canSendAutomatically}>
              <option value="WhatsApp">WhatsApp</option>
              <option value="Sms">SMS</option>
            </Select></div>
            <Button busy={busy === 'send'} disabled={!link?.canSendAutomatically || invalid || (!normalized && !customerId)}
              onClick={() => run('send', async () => {
                const result = await post<ReceiptSendResult>(`/online-sales/receipts/${orderId}/send`, {channel, phone: normalized ?? null});
                if (result.status === 'Sent') {
                  setSkipped(null);
                  toast('success', channel === 'WhatsApp' ? 'Reçu envoyé par WhatsApp.' : 'Reçu envoyé par SMS.');
                  onClose();
                } else {
                  setSkipped(result.status === 'NoLink' ? 'le restaurant n’a pas de site actif' : skipReason[result.reason ?? ''] ?? 'le message a été écarté');
                }
              })}>
              <Send className="size-4" aria-hidden="true" />Envoyer
            </Button>
          </div>
          {!normalized && customerId && !customerPhone && link?.canSendAutomatically && (
            <p className="mt-2 text-xs text-ink-500">Sans numéro saisi, le reçu part au numéro de la fiche client.</p>
          )}
        </section>
        {skipped && <Alert tone="amber">Rien n’est parti : {skipped}. Vous pouvez l’envoyer vous-même ci-dessous.</Alert>}

        <section className={cx('rounded-xl border p-3', skipped ? 'border-emerald-300 bg-emerald-50/50' : 'border-slate-200')} aria-labelledby="receipt-manual">
          <h3 id="receipt-manual" className="text-sm font-semibold">Depuis votre téléphone</h3>
          <p className="mt-0.5 text-xs text-ink-500">WhatsApp s’ouvre avec le reçu en texte{link?.url ? ' et son lien' : ''} ; vérifiez puis envoyez.</p>
          <div className="mt-3 flex flex-wrap gap-2">
            <a href={text ? manual : undefined} target="_blank" rel="noopener noreferrer" aria-disabled={!text || invalid}
              className={cx('inline-flex h-10 items-center gap-2 rounded-xl bg-emerald-600 px-4 text-sm font-semibold text-white shadow-sm hover:bg-emerald-700', (!text || invalid) && 'pointer-events-none opacity-50')}>
              <MessageCircle className="size-4" aria-hidden="true" />Ouvrir WhatsApp<ExternalLink className="size-3.5" aria-hidden="true" />
            </a>
            <Button variant="secondary" disabled={!text} onClick={() => run('copy', async () => {
              await navigator.clipboard.writeText(text);
            }, 'Reçu copié.')}>
              <Copy className="size-4" aria-hidden="true" />Copier le texte
            </Button>
          </div>
        </section>

        {text && (
          <details className="text-sm">
            <summary className="cursor-pointer font-medium text-ink-600">Aperçu du message</summary>
            <pre className="mt-2 max-h-64 overflow-auto rounded-xl bg-slate-50 p-3 font-sans text-xs whitespace-pre-wrap">{text}</pre>
          </details>
        )}
      </div>
    </Modal>
  );
}
