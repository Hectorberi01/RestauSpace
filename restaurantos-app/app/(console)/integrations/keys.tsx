'use client';
import {useEffect, useMemo, useState} from 'react';
import {KeyRound, Plus, RefreshCw, Trash2} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {issueKey, listKeys, revokeKey, rotateKey, type ApiCredential, type Catalog} from '@/lib/integrations';
import {groupOf, permissionLabel} from '@/lib/staff';
import {useConsole} from '@/components/console';
import {UpgradePanel, usePlanFeatures} from '@/components/plan';
import {Feature} from '@/lib/plan';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Spinner, cx, useAction} from '@/components/ui';
import {SecretOnce, day} from './shared';

function IssueDialog({open, catalog, onClose, onIssued}: {open: boolean; catalog: Catalog; onClose: () => void; onIssued: (key: string) => Promise<void>}) {
  const {busy, run} = useAction();
  const [name, setName] = useState('');
  const [scopes, setScopes] = useState<string[]>([]);
  const [expires, setExpires] = useState('');
  useEffect(() => {
    if (!open) return;
    setName('');
    setScopes([]);
    setExpires('');
  }, [open]);
  const groups = useMemo(() => {
    const map = new Map<string, string[]>();
    for (const scope of catalog.apiScopes) map.set(groupOf(scope), [...(map.get(groupOf(scope)) ?? []), scope]);
    return [...map.entries()];
  }, [catalog]);
  const toggle = (scope: string) => setScopes(current => (current.includes(scope) ? current.filter(item => item !== scope) : [...current, scope]));
  const tomorrow = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10);

  return (
    <Modal wide open={open} onClose={onClose} title="Nouvelle clé d’API" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="key-form" busy={busy === 'issue'} disabled={!name.trim() || scopes.length === 0}>Créer la clé</Button>
      </>
    }>
      <form id="key-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('issue', async () => {
          const issued = await issueKey(name.trim(), scopes, expires ? new Date(`${expires}T23:59:59`).toISOString() : null);
          onClose();
          await onIssued(issued.key);
        });
      }}>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-[1fr_12rem]">
          <Field label="Nom" hint="Pour reconnaître l’outil qui l’utilise : caisse, site, comptabilité…">
            <Input required maxLength={100} value={name} onChange={event => setName(event.target.value)} placeholder="Site de commande" />
          </Field>
          <Field label="Expiration (facultatif)"><Input type="date" min={tomorrow} value={expires} onChange={event => setExpires(event.target.value)} /></Field>
        </div>
        <fieldset>
          <legend className="mb-1 text-sm font-semibold">Ce que la clé peut faire</legend>
          <p className="mb-3 text-xs text-ink-500">Donnez le strict nécessaire. Le personnel, l’abonnement, le journal et les intégrations ne sont jamais accessibles par clé.</p>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            {groups.map(([group, items]) => (
              <div key={group} className="rounded-xl border border-slate-200 p-3">
                <p className="mb-1.5 text-sm font-semibold">{group}</p>
                {items.map(scope => (
                  <label key={scope} className="flex items-start gap-2 py-0.5 text-sm">
                    <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" checked={scopes.includes(scope)} onChange={() => toggle(scope)} />
                    <span>{permissionLabel[scope] ?? scope}<code className="ml-1 text-xs text-ink-400">{scope}</code></span>
                  </label>
                ))}
              </div>
            ))}
          </div>
        </fieldset>
      </form>
    </Modal>
  );
}

export function ApiKeys({catalog}: {catalog: Catalog}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const {data, error, reload} = useResource(signal => listKeys(signal), []);
  const [issuing, setIssuing] = useState(false);
  const [secret, setSecret] = useState<string | null>(null);
  const [confirm, setConfirm] = useState<{key: ApiCredential; action: 'rotate' | 'revoke'} | null>(null);
  const manage = can('integrations.manage');
  // Sans le droit : clés visibles et révocables, plus de création ni de renouvellement.
  const locked = usePlanFeatures().blocked(Feature.IntegrationsApi);
  const keys = (data ?? []).slice().sort((a, b) => Number(a.status === 'Revoked') - Number(b.status === 'Revoked') || b.issuedAtUtc.localeCompare(a.issuedAtUtc));

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <p className="flex-1 text-sm text-ink-600">Une clé permet à un logiciel externe (caisse, site, comptabilité) d’utiliser l’API de votre restaurant, avec seulement les droits choisis.</p>
        {manage && !locked && <Button onClick={() => setIssuing(true)}><Plus className="size-4" aria-hidden="true" />Nouvelle clé</Button>}
      </div>
      {locked && <UpgradePanel feature={Feature.IntegrationsApi} title="Clés d’API">Connectez vos logiciels à RestauSpace.{keys.some(key => key.status === 'Active') ? ' Les clés existantes sont refusées par l’API.' : ''}</UpgradePanel>}
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : keys.length === 0 ? (
        <Card className="p-6"><Empty icon={<KeyRound className="size-8" />} title="Aucune clé d’API">Créez une clé pour connecter un logiciel externe.</Empty></Card>
      ) : (
        <div className="space-y-3">
          {keys.map(key => {
            const expired = key.expiresAtUtc !== null && new Date(key.expiresAtUtc) < new Date();
            return (
              <Card key={key.id} className={cx('p-4', key.status === 'Revoked' && 'opacity-60')}>
                <div className="flex flex-wrap items-start gap-3">
                  <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-slate-100 text-ink-600" aria-hidden="true"><KeyRound className="size-5" /></span>
                  <div className="min-w-0 flex-1">
                    <p className="flex flex-wrap items-center gap-2 font-semibold">
                      {key.name}
                      {key.status === 'Revoked' ? <Badge tone="red">Révoquée</Badge> : expired ? <Badge tone="red">Expirée</Badge> : <Badge tone="green">Active</Badge>}
                      {key.isDormant && key.status === 'Active' && <Badge tone="amber">Inutilisée depuis longtemps</Badge>}
                    </p>
                    <p className="mt-0.5 text-xs text-ink-500">
                      Créée le {day(key.issuedAtUtc)}
                      {key.rotatedAtUtc && ` · renouvelée le ${day(key.rotatedAtUtc)}`}
                      {` · ${key.lastUsedAtUtc ? `dernière utilisation le ${day(key.lastUsedAtUtc)}` : 'jamais utilisée'}`}
                      {key.expiresAtUtc && ` · expire le ${day(key.expiresAtUtc)}`}
                      {key.revokedAtUtc && ` · révoquée le ${day(key.revokedAtUtc)}`}
                    </p>
                    <div className="mt-2 flex flex-wrap gap-1">
                      {key.scopes.map(scope => <span key={scope} className="rounded-full bg-slate-100 px-2 py-0.5 text-xs text-ink-600" title={scope}>{permissionLabel[scope] ?? scope}</span>)}
                    </div>
                  </div>
                  {manage && key.status === 'Active' && (
                    <div className="flex gap-1">
                      {!locked && <Button size="sm" variant="secondary" onClick={() => setConfirm({key, action: 'rotate'})}><RefreshCw className="size-4" aria-hidden="true" />Renouveler</Button>}
                      <Button size="sm" variant="ghost" className="text-red-600" onClick={() => setConfirm({key, action: 'revoke'})}><Trash2 className="size-4" aria-hidden="true" />Révoquer</Button>
                    </div>
                  )}
                </div>
              </Card>
            );
          })}
        </div>
      )}
      <IssueDialog open={issuing} catalog={catalog} onClose={() => setIssuing(false)} onIssued={async key => {
        setSecret(key);
        await reload();
      }} />
      <SecretOnce title="Votre clé d’API" label="Clé" value={secret} onClose={() => setSecret(null)}>
        <p className="text-sm text-ink-600">À envoyer dans l’en-tête <code className="text-xs">X-Api-Key</code> de chaque appel.</p>
      </SecretOnce>
      <Modal open={confirm !== null} onClose={() => setConfirm(null)} title={confirm?.action === 'rotate' ? 'Renouveler la clé' : 'Révoquer la clé'} footer={
        <>
          <Button variant="secondary" onClick={() => setConfirm(null)}>Annuler</Button>
          <Button variant={confirm?.action === 'revoke' ? 'danger' : 'primary'} busy={busy === 'confirm'} onClick={() => confirm && run('confirm', async () => {
            if (confirm.action === 'rotate') {
              const issued = await rotateKey(confirm.key.id);
              setConfirm(null);
              setSecret(issued.key);
            } else {
              await revokeKey(confirm.key.id);
              setConfirm(null);
            }
            await reload();
          }, confirm?.action === 'revoke' ? 'Clé révoquée.' : undefined)}>{confirm?.action === 'rotate' ? 'Renouveler' : 'Révoquer définitivement'}</Button>
        </>
      }>
        {confirm && (
          <p className="text-sm text-ink-700">
            {confirm.action === 'rotate'
              ? <>L’ancienne clé de « {confirm.key.name} » cesse de fonctionner tout de suite. Mettez la nouvelle en place dans l’outil concerné aussitôt.</>
              : <>« {confirm.key.name} » cessera immédiatement de fonctionner. C’est irréversible.</>}
          </p>
        )}
      </Modal>
    </div>
  );
}
