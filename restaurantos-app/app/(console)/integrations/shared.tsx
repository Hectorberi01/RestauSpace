'use client';
import {useState} from 'react';
import {Check, Copy, ShieldAlert} from 'lucide-react';
import {Button, Modal} from '@/components/ui';

/**
 * Affiche UNE fois une valeur secrète (clé d'API, secret de signature). Elle ne vit que dans l'état du composant :
 * jamais stockée, jamais journalisée ; à la fermeture, elle est perdue.
 */
export function SecretOnce({title, label, value, children, onClose}: {title: string; label: string; value: string | null; children?: React.ReactNode; onClose: () => void}) {
  const [copied, setCopied] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const close = () => {
    setCopied(false);
    setAcknowledged(false);
    onClose();
  };
  return (
    <Modal open={value !== null} onClose={() => acknowledged && close()} title={title} footer={
      <Button disabled={!acknowledged} onClick={close}>Terminé</Button>
    }>
      <div className="space-y-4">
        <p className="flex items-start gap-2 rounded-xl bg-amber-50 p-3 text-sm text-amber-900">
          <ShieldAlert className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <span>Copiez-la maintenant et rangez-la dans un endroit sûr (gestionnaire de mots de passe, variables d’environnement). <strong>Elle ne sera plus jamais affichée.</strong></span>
        </p>
        <div>
          <p className="mb-1.5 text-sm font-medium">{label}</p>
          <div className="flex gap-2">
            <code className="min-w-0 flex-1 rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5 text-sm break-all select-all">{value}</code>
            <Button variant="secondary" aria-label="Copier" onClick={async () => {
              if (!value) return;
              await navigator.clipboard.writeText(value).catch(() => undefined);
              setCopied(true);
            }}>{copied ? <Check className="size-4" /> : <Copy className="size-4" />}</Button>
          </div>
        </div>
        {children}
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" className="size-4 accent-brand-600" checked={acknowledged} onChange={event => setAcknowledged(event.target.checked)} />
          Je l’ai copiée et mise en lieu sûr.
        </label>
      </div>
    </Modal>
  );
}

export const day = (iso: string) => new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', year: 'numeric'}).format(new Date(iso));

export const stamp = (iso: string) =>
  new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', second: '2-digit'}).format(new Date(iso));
