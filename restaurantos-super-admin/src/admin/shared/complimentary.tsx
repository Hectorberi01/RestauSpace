'use client';
import {useEffect,useState,type ReactNode} from 'react';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription,DialogFooter} from '@/components/ui/dialog';
import {Button} from '@/components/ui/button';
import {touches,useAdmin} from '../../hooks/use-admin';
import {describe} from '../../services/api/client';
import {subscriptions,tenants,type PlanDto} from '../../services/api/admin-api';
import {money,date} from '../../utils/format';

const planLabel=(p:PlanDto)=>`${p.name} · ${money(p.price,p.currencyCode)} / ${p.billingInterval==='Yearly'?'an':'mois'}`;
const durations:[string,number][]=[['1 mois',1],['3 mois',3],['6 mois',6],['1 an',12]];
const isoDay=(d:Date)=>`${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
const inMonths=(months:number)=>{const d=new Date();d.setMonth(d.getMonth()+months);return isoDay(d)};

/**
 * Offrir un plan à un restaurant : pour une période (jusqu'au soir du jour choisi) ou sans limite. Seuls les plans de base
 * actifs de la devise du restaurant sont proposés. L'abonnement en cours (même impayé ou suspendu) passe au plan offert ;
 * un restaurant sans abonnement en reçoit un.
 */
export function ComplimentaryDialog({tenantId,currentPlanId,until,children}:{tenantId:string;currentPlanId?:string;until?:string;children:ReactNode}){
  const {raw,run}=useAdmin();
  const [open,setOpen]=useState(false);
  const [currency,setCurrency]=useState<string|null>(null);
  const [error,setError]=useState<string>();
  const [planId,setPlanId]=useState(currentPlanId??'');
  const [unlimited,setUnlimited]=useState(false);
  const [end,setEnd]=useState(until?until.slice(0,10):inMonths(3));

  useEffect(()=>{
    if(!open)return;
    let active=true;
    setCurrency(null);setError(undefined);
    tenants.get(tenantId).then(t=>{if(active)setCurrency(t.defaultCurrencyCode)}).catch(e=>{if(active)setError(describe(e))});
    return()=>{active=false};
  },[open,tenantId]);

  const plans=currency?raw.plans.filter(p=>p.status==='Active'&&p.kind!=='AddOn'&&p.currencyCode===currency):[];
  const today=isoDay(new Date());
  const valid=!!planId&&(unlimited||end>today);
  const reset=(value:boolean)=>{setOpen(value);if(value){setPlanId(currentPlanId??'');setUnlimited(false);setEnd(until?until.slice(0,10):inMonths(3))}};
  const submit=async()=>{
    if(!valid)return;
    // Fin de l'offre : le soir du jour choisi, heure locale de l'administrateur.
    const untilUtc=unlimited?null:new Date(`${end}T23:59:59`).toISOString();
    if(await run(unlimited?'Plan offert sans limite':`Plan offert jusqu’au ${date(untilUtc!)}`,()=>subscriptions.grantComplimentary(tenantId,planId,untilUtc),touches.subscriptions))reset(false);
  };

  return <>
    <span className="trigger-wrap" onClick={()=>reset(true)}>{children}</span>
    <Dialog open={open} onOpenChange={reset}>
      <DialogContent className="form-dialog">
        <DialogHeader><DialogTitle>Offrir un plan</DialogTitle><DialogDescription>Le restaurant n’a rien à payer pendant l’offre. À la fin, il règle la période suivante (délai de grâce de 7 jours, puis suspension). Il est prévenu 7 jours avant.</DialogDescription></DialogHeader>
        <form onSubmit={e=>{e.preventDefault();void submit()}}>
          <div className="form-fields">
            <label>Plan{currency?` (${currency})`:''}<select value={planId} onChange={e=>setPlanId(e.target.value)} required disabled={!currency||!plans.length}><option value="">{!currency&&!error?'Chargement de la devise…':plans.length?'Choisir un plan':'Aucun plan actif dans cette devise'}</option>{plans.map(p=><option key={p.id} value={p.id}>{planLabel(p)}</option>)}</select></label>
            <fieldset className="choice-group">
              <legend>Durée</legend>
              <label className="choice"><input type="radio" name="duration" checked={!unlimited} onChange={()=>setUnlimited(false)}/>Jusqu’au</label>
              {!unlimited&&<div className="choice-detail">
                <input type="date" min={today} value={end} onChange={e=>setEnd(e.target.value)} aria-label="Dernier jour offert" required/>
                <div className="chips">{durations.map(([label,months])=><button key={label} type="button" className={end===inMonths(months)?'chip active':'chip'} onClick={()=>setEnd(inMonths(months))}>{label}</button>)}</div>
              </div>}
              <label className="choice"><input type="radio" name="duration" checked={unlimited} onChange={()=>setUnlimited(true)}/>Sans limite de durée</label>
              {unlimited&&<p className="tiny-note">Le restaurant ne pourra ni résilier ni ajouter d’options lui-même : passez par la plateforme.</p>}
            </fieldset>
          </div>
          {error&&<p className="login-error" role="alert">{error}</p>}
          <DialogFooter><Button type="button" variant="outline" onClick={()=>reset(false)}>Annuler</Button><Button type="submit" disabled={!valid}>Offrir</Button></DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </>;
}
