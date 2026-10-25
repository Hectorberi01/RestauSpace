'use client';
import {useEffect,useState,type ReactNode} from 'react';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription,DialogFooter} from '@/components/ui/dialog';
import {Button} from '@/components/ui/button';
import {touches,useAdmin} from '../../hooks/use-admin';
import {describe} from '../../services/api/client';
import {addOns,subscriptions,tenants,type AddOnAfterOffer,type PlanDto,type TenantAddOnDto} from '../../services/api/admin-api';
import {money,date} from '../../utils/format';

const planLabel=(p:PlanDto)=>`${p.name} · ${money(p.price,p.currencyCode)} / ${p.billingInterval==='Yearly'?'an':'mois'}`;
const durations:[string,number][]=[['1 mois',1],['3 mois',3],['6 mois',6],['1 an',12]];
const renewals=[1,3,6,12];
const isoDay=(d:Date)=>`${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
const inMonths=(months:number)=>{const d=new Date();d.setMonth(d.getMonth()+months);return isoDay(d)};
// Fin de l'offre : le soir du jour choisi, heure locale de l'administrateur.
const endOfDay=(day:string)=>new Date(`${day}T23:59:59`).toISOString();

/** Durée d'une offre : jusqu'au jour choisi (avec renouvellement automatique possible) ou sans limite. */
interface Duration {unlimited:boolean;end:string;renew:boolean;months:number}

const initialDuration=(until?:string,renewal?:number|null):Duration=>({unlimited:false,end:until?until.slice(0,10):inMonths(3),renew:!!renewal,months:renewal??3});

/** Texte d'une offre en cours : « jusqu'au …, renouvelée tous les N mois » ou « sans limite de durée ». */
export function offerText(until?:string|null,renewal?:number|null){
  return until?`jusqu’au ${date(until)}${renewal?`, renouvelée tous les ${renewal} mois`:''}`:'sans limite de durée';
}

function DurationFields({value,onChange,unlimitedNote}:{value:Duration;onChange:(next:Duration)=>void;unlimitedNote?:string}){
  const today=isoDay(new Date());
  const set=(patch:Partial<Duration>)=>onChange({...value,...patch});
  return <fieldset className="choice-group">
    <legend>Durée</legend>
    <label className="choice"><input type="radio" name="duration" checked={!value.unlimited} onChange={()=>set({unlimited:false})}/>Jusqu’au</label>
    {!value.unlimited&&<div className="choice-detail">
      <input type="date" min={today} value={value.end} onChange={e=>set({end:e.target.value})} aria-label="Dernier jour offert" required/>
      <div className="chips">{durations.map(([label,months])=><button key={label} type="button" className={value.end===inMonths(months)?'chip active':'chip'} onClick={()=>set({end:inMonths(months),months})}>{label}</button>)}</div>
      <label className="choice"><input type="checkbox" checked={value.renew} onChange={e=>set({renew:e.target.checked})}/>Renouveler automatiquement tous les
        <select value={value.months} onChange={e=>set({months:Number(e.target.value),renew:true})} aria-label="Durée du renouvellement">{renewals.map(m=><option key={m} value={m}>{m}</option>)}</select>mois
      </label>
      {value.renew&&<p className="tiny-note">L’offre repart d’elle-même à chaque échéance, jusqu’à ce que vous y mettiez fin.</p>}
    </div>}
    <label className="choice"><input type="radio" name="duration" checked={value.unlimited} onChange={()=>set({unlimited:true,renew:false})}/>Sans limite de durée</label>
    {value.unlimited&&unlimitedNote&&<p className="tiny-note">{unlimitedNote}</p>}
  </fieldset>;
}

const durationValid=(d:Duration)=>d.unlimited||d.end>isoDay(new Date());
const untilOf=(d:Duration)=>d.unlimited?null:endOfDay(d.end);
const renewalOf=(d:Duration)=>!d.unlimited&&d.renew?d.months:null;

/**
 * Offrir un plan à un restaurant : pour une période (renouvelable) ou sans limite. Seuls les plans de base actifs de la
 * devise du restaurant sont proposés. L'abonnement en cours (même impayé ou suspendu) passe au plan offert ; un
 * restaurant sans abonnement en reçoit un. Le restaurant est prévenu (console et email).
 */
export function ComplimentaryDialog({tenantId,currentPlanId,until,renewal,children}:{tenantId:string;currentPlanId?:string;until?:string;renewal?:number|null;children:ReactNode}){
  const {raw,run}=useAdmin();
  const [open,setOpen]=useState(false);
  const [currency,setCurrency]=useState<string|null>(null);
  const [error,setError]=useState<string>();
  const [planId,setPlanId]=useState(currentPlanId??'');
  const [duration,setDuration]=useState(initialDuration(until,renewal));

  useEffect(()=>{
    if(!open)return;
    let active=true;
    setCurrency(null);setError(undefined);
    tenants.get(tenantId).then(t=>{if(active)setCurrency(t.defaultCurrencyCode)}).catch(e=>{if(active)setError(describe(e))});
    return()=>{active=false};
  },[open,tenantId]);

  const plans=currency?raw.plans.filter(p=>p.status==='Active'&&p.kind!=='AddOn'&&p.currencyCode===currency):[];
  const valid=!!planId&&durationValid(duration);
  const reset=(value:boolean)=>{setOpen(value);if(value){setPlanId(currentPlanId??'');setDuration(initialDuration(until,renewal))}};
  const submit=async()=>{
    if(!valid)return;
    const untilUtc=untilOf(duration);
    if(await run(`Plan offert ${offerText(untilUtc,renewalOf(duration))}`,()=>subscriptions.grantComplimentary(tenantId,planId,untilUtc,renewalOf(duration)),touches.subscriptions))reset(false);
  };

  return <>
    <span className="trigger-wrap" onClick={()=>reset(true)}>{children}</span>
    <Dialog open={open} onOpenChange={reset}>
      <DialogContent className="form-dialog">
        <DialogHeader><DialogTitle>Offrir un plan</DialogTitle><DialogDescription>Le restaurant n’a rien à payer pendant l’offre et en est prévenu (console et email). Sans renouvellement, il règle ensuite la période suivante (délai de grâce de 7 jours, puis suspension) ; il est prévenu 7 jours avant.</DialogDescription></DialogHeader>
        <form onSubmit={e=>{e.preventDefault();void submit()}}>
          <div className="form-fields">
            <label>Plan{currency?` (${currency})`:''}<select value={planId} onChange={e=>setPlanId(e.target.value)} required disabled={!currency||!plans.length}><option value="">{!currency&&!error?'Chargement de la devise…':plans.length?'Choisir un plan':'Aucun plan actif dans cette devise'}</option>{plans.map(p=><option key={p.id} value={p.id}>{planLabel(p)}</option>)}</select></label>
            <DurationFields value={duration} onChange={setDuration} unlimitedNote="Le restaurant ne pourra ni résilier ni ajouter d’options lui-même : passez par la plateforme."/>
          </div>
          {error&&<p className="login-error" role="alert">{error}</p>}
          <DialogFooter><Button type="button" variant="outline" onClick={()=>reset(false)}>Annuler</Button><Button type="submit" disabled={!valid}>Offrir</Button></DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </>;
}

/** Texte de l'offre d'une option : durée, renouvellement, puis ce qui suit. */
export function addOnOfferText(x:TenantAddOnDto){
  if(!x.isComplimentary)return null;
  const after=x.afterComplimentary==='Bill'?' · ensuite payante':' · ensuite arrêtée';
  return `Offerte ${offerText(x.complimentaryUntilUtc,x.complimentaryRenewalMonths)}${after}`;
}

/**
 * Offrir une option (vente en ligne, bornes, API…) à un restaurant abonné, pour une durée renouvelable ou sans limite.
 * Option déjà active : son offre est remplacée. À la fin, l'option s'arrête ou devient payante.
 */
export function AddOnOfferDialog({tenantId,currency,addOn,exclude=[],only,onDone,children}:{tenantId:string;currency?:string;addOn?:TenantAddOnDto;exclude?:string[];only?:(p:PlanDto)=>boolean;onDone:()=>void;children:ReactNode}){
  const {raw,run}=useAdmin();
  const [open,setOpen]=useState(false);
  const [planId,setPlanId]=useState(addOn?.addOn.id??'');
  const [quantity,setQuantity]=useState(addOn?.quantity??1);
  const [duration,setDuration]=useState(initialDuration(addOn?.complimentaryUntilUtc??undefined,addOn?.complimentaryRenewalMonths));
  const [then,setThen]=useState<AddOnAfterOffer>(addOn?.afterComplimentary??'Cancel');
  const options=raw.plans.filter(p=>p.kind==='AddOn'&&p.status==='Active'&&(!currency||p.currencyCode===currency)&&(p.id===addOn?.addOn.id||(!exclude.includes(p.id)&&(!only||only(p)))));
  const chosen=options.find(p=>p.id===planId);
  const counted=!!chosen?.entitlements.some(e=>e.enabled&&e.limit!==null);
  const valid=!!planId&&quantity>=1&&durationValid(duration);
  const reset=(value:boolean)=>{
    setOpen(value);
    if(value){setPlanId(addOn?.addOn.id??'');setQuantity(addOn?.quantity??1);setDuration(initialDuration(addOn?.complimentaryUntilUtc??undefined,addOn?.complimentaryRenewalMonths));setThen(addOn?.afterComplimentary??'Cancel')}
  };
  const submit=async()=>{
    if(!valid||!chosen)return;
    const untilUtc=untilOf(duration);
    const renewalMonths=renewalOf(duration);
    if(await run(`Option ${chosen.name} offerte ${offerText(untilUtc,renewalMonths)}`,()=>addOns.offer(tenantId,{planId,quantity:counted?quantity:1,untilUtc,renewalMonths,then}),touches.restaurants)){reset(false);onDone()}
  };
  return <>
    <span className="trigger-wrap" onClick={()=>reset(true)}>{children}</span>
    <Dialog open={open} onOpenChange={reset}>
      <DialogContent className="form-dialog">
        <DialogHeader><DialogTitle>{addOn?`Offre : ${addOn.addOn.name}`:'Offrir une option'}</DialogTitle><DialogDescription>L’option est active tout de suite, sans rien à payer pendant l’offre. Le restaurant en est prévenu (console et email). Un abonnement en cours est nécessaire.</DialogDescription></DialogHeader>
        <form onSubmit={e=>{e.preventDefault();void submit()}}>
          <div className="form-fields">
            {!addOn&&<label>Option{currency?` (${currency})`:''}<select value={planId} onChange={e=>setPlanId(e.target.value)} required disabled={!options.length}><option value="">{options.length?'Choisir une option':'Aucune option active dans cette devise'}</option>{options.map(p=><option key={p.id} value={p.id}>{planLabel(p)}</option>)}</select></label>}
            {counted&&<label>Quantité (ex. nombre de bornes)<input type="number" min={1} value={quantity} onChange={e=>setQuantity(Math.max(1,Number(e.target.value)||1))} required/></label>}
            <DurationFields value={duration} onChange={setDuration}/>
            {<fieldset className="choice-group">
              <legend>{duration.unlimited||duration.renew?'Si vous mettez fin à l’offre':'À la fin de l’offre'}</legend>
              <label className="choice"><input type="radio" name="then" checked={then==='Cancel'} onChange={()=>setThen('Cancel')}/>L’option s’arrête</label>
              <label className="choice"><input type="radio" name="then" checked={then==='Bill'} onChange={()=>setThen('Bill')}/>Elle devient payante (facturée avec la période suivante)</label>
            </fieldset>}
          </div>
          <DialogFooter><Button type="button" variant="outline" onClick={()=>reset(false)}>Annuler</Button><Button type="submit" disabled={!valid}>Offrir</Button></DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </>;
}
