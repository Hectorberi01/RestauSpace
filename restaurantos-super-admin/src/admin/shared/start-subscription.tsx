'use client';
import {useEffect,useState,type ReactNode} from 'react';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription,DialogFooter} from '@/components/ui/dialog';
import {Button} from '@/components/ui/button';
import {touches,useAdmin} from '../../hooks/use-admin';
import {describe} from '../../services/api/client';
import {subscriptions,tenants,type PlanDto,type PlatformTenantRow} from '../../services/api/admin-api';
import {shortId} from '../../services/api/mappers';
import {money} from '../../utils/format';

const planLabel=(p:PlanDto)=>`${p.name} · ${money(p.price,p.currencyCode)} / ${p.billingInterval==='Yearly'?'an':'mois'}`;

/**
 * Démarrer un abonnement depuis l'écran Abonnements : on choisit d'abord le restaurant, puis seulement les plans de base
 * actifs dans SA devise (l'API refuse les autres).
 */
export function StartSubscriptionDialog({candidates,children}:{candidates:PlatformTenantRow[];children:ReactNode}){
  const {raw,run}=useAdmin();
  const [open,setOpen]=useState(false);
  const [tenantId,setTenantId]=useState('');
  const [currency,setCurrency]=useState<string|null>(null);
  const [error,setError]=useState<string>();
  const [planId,setPlanId]=useState('');
  const [trialDays,setTrialDays]=useState('');

  useEffect(()=>{
    if(!tenantId){setCurrency(null);return}
    let active=true;
    setCurrency(null);setError(undefined);setPlanId('');
    tenants.get(tenantId).then(t=>{if(active)setCurrency(t.defaultCurrencyCode)}).catch(e=>{if(active)setError(describe(e))});
    return()=>{active=false};
  },[tenantId]);

  const plans=currency?raw.plans.filter(p=>p.status==='Active'&&p.kind!=='AddOn'&&p.currencyCode===currency):[];
  const reset=(value:boolean)=>{setOpen(value);if(!value){setTenantId('');setPlanId('');setTrialDays('');setError(undefined)}};
  const submit=async()=>{
    if(!tenantId||!planId)return;
    const days=Number(trialDays);
    if(await run('Abonnement démarré',()=>subscriptions.start(tenantId,planId,days>0?days:undefined),touches.subscriptions))reset(false);
  };

  return <>
    <span className="trigger-wrap" onClick={()=>reset(true)}>{children}</span>
    <Dialog open={open} onOpenChange={reset}>
      <DialogContent className="form-dialog">
        <DialogHeader><DialogTitle>Démarrer un abonnement</DialogTitle><DialogDescription>Seuls les plans de la devise du restaurant sont proposés.</DialogDescription></DialogHeader>
        <form onSubmit={e=>{e.preventDefault();void submit()}}>
          <div className="form-fields">
            <label>Restaurant<select value={tenantId} onChange={e=>setTenantId(e.target.value)} required><option value="">Choisir un restaurant</option>{candidates.map(t=><option key={t.tenantId} value={t.tenantId}>{t.name} · {shortId(t.tenantId)}</option>)}</select></label>
            <label>Plan{currency?` (${currency})`:''}<select value={planId} onChange={e=>setPlanId(e.target.value)} required disabled={!currency||!plans.length}><option value="">{!tenantId?'Choisissez d’abord le restaurant':!currency&&!error?'Chargement de la devise…':plans.length?'Choisir un plan':'Aucun plan actif dans cette devise'}</option>{plans.map(p=><option key={p.id} value={p.id}>{planLabel(p)}</option>)}</select></label>
            <label>Jours d’essai (vide : défaut du plan)<input type="number" min={0} step={1} value={trialDays} onChange={e=>setTrialDays(e.target.value)}/></label>
          </div>
          {error&&<p className="login-error" role="alert">{error}</p>}
          <DialogFooter><Button type="button" variant="outline" onClick={()=>reset(false)}>Annuler</Button><Button type="submit" disabled={!tenantId||!planId}>Démarrer</Button></DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </>;
}
