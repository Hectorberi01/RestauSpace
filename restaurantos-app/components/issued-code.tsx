'use client';
import {useState} from 'react';
import {Copy} from 'lucide-react';
import {money} from '@/lib/format';
import {storedValueKindLabel, type StoredValueKind} from '@/lib/marketing';
import {Button} from '@/components/ui';

/** Le code d'une carte qui vient d'être émise : affiché une seule fois. */
export function IssuedCode({code, amount, currency, kind}: {code: string; amount: number; currency: string; kind: StoredValueKind}) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="space-y-3 rounded-2xl border border-emerald-200 bg-emerald-50 p-4 text-center">
      <p className="text-sm font-medium text-emerald-900">{storedValueKindLabel[kind]} de {money(amount, currency)}</p>
      <p className="font-mono text-2xl font-bold tracking-widest text-emerald-950 select-all">{code}</p>
      <Button size="sm" variant="secondary" onClick={() => void navigator.clipboard?.writeText(code).then(() => setCopied(true))}>
        <Copy className="size-4" aria-hidden="true" />{copied ? 'Copié' : 'Copier le code'}
      </Button>
      <p className="text-xs text-emerald-900">Remettez ce code au client maintenant : il ne sera plus jamais affiché. Seuls ses 4 derniers caractères restent visibles.</p>
    </div>
  );
}
