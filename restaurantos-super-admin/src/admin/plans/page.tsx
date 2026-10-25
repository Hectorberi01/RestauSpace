'use client';
import {useState} from 'react';
import {Plus,Check,X,Layers,Tag,ListChecks,Archive,RefreshCw} from 'lucide-react';
import {toast} from 'sonner';
import {touches,useAdmin} from '../../hooks/use-admin';
import {Button} from '@/components/ui/button';
import {PageHeader,StatusBadge,LoadingSkeleton,EmptyState} from '../../components/ui/shared';
import {ConfirmDialog,FormDialog} from '../../components/feedback/dialogs';
import {plans as plansApi,type Entitlement,type PlanDto} from '../../services/api/admin-api';
import {monthly,planStatus} from '../../services/api/mappers';
import {formatTotals} from '../shared/live-page';
import {currencyStep,money,number} from '../../utils/format';
import type {Field} from '../../types';

const createFields:Field[]=[
  {key:'name',label:'Nom du plan'},
  {key:'price',label:'Prix',type:'number'},
  {key:'currencyCode',label:'Devise (ISO 4217)'},
  {key:'billingInterval',label:'Facturation',type:'select',options:['Mensuelle','Annuelle']},
  {key:'kind',label:'Type',type:'select',options:['Plan de base','Option']},
  {key:'entitlements',label:'Droits, un par ligne : code ou code=limite (ex. orders.monthly=1000, storefront.api, storefront.kiosk=1)',type:'textarea',required:false},
];

const entitlementFields:Field[]=[
  {key:'featureCode',label:'Code (ex. reservations, orders.monthly)'},
  {key:'enabled',label:'État',type:'select',options:['Activé','Désactivé']},
  {key:'limit',label:'Limite (vide : illimité)',type:'number',required:false},
];

function parseEntitlements(text:string):Entitlement[]|string{
  const result:Entitlement[]=[];
  for(const line of text.split('\n').map(l=>l.trim()).filter(Boolean)){
    const [code,limit]=line.split('=').map(x=>x.trim());
    if(!/^[a-z][a-z0-9._-]{1,63}$/.test(code.toLowerCase()))return `Code invalide : « ${code} »`;
    if(limit!==undefined&&!/^\d+$/.test(limit))return `Limite invalide pour « ${code} »`;
    result.push({featureCode:code.toLowerCase(),enabled:true,limit:limit===undefined?null:Number(limit)});
  }
  return result;
}

const validPrice=(price:number,currency:string)=>{const step=currencyStep(currency);return price>=0&&Math.abs(Math.round(price/step)*step-price)<1e-9};
const priceMessage=(currency:string)=>currencyStep(currency)===1?`Les prix en ${currency} se saisissent sans décimale.`:`Les prix en ${currency} ont au plus ${String(currencyStep(currency)).split('.')[1]?.length??0} décimales.`;

const describeEntitlement=(e:Entitlement)=>!e.enabled?'Désactivé':e.limit===null?'Illimité':number(e.limit);

export default function Plans(){
  const {raw,status,run,reload}=useAdmin();
  const usage=(plan:PlanDto)=>{const rows=raw.subscriptions.filter(x=>x.planId===plan.id&&(x.status==='Active'||x.status==='PastDue'||x.status==='Trialing'));const billed=rows.filter(x=>x.status!=='Trialing').length;return {count:rows.length,mrr:billed?money(billed*monthly(plan),plan.currencyCode):formatTotals([])}};
  const currencies=[...new Set(raw.plans.map(p=>p.currencyCode))].sort();
  const [currency,setCurrency]=useState('Toutes');
  const shown=raw.plans.filter(p=>currency==='Toutes'||p.currencyCode===currency).sort((a,b)=>a.currencyCode.localeCompare(b.currencyCode)||(a.status===b.status?0:a.status==='Active'?-1:1)||monthly(a)-monthly(b));
  const features=[...new Set(shown.flatMap(p=>p.entitlements.map(e=>e.featureCode)))].sort();
  const create=(v:Record<string,string|number>)=>{
    const entitlements=parseEntitlements(String(v.entitlements??''));
    if(typeof entitlements==='string'){toast.error(entitlements);return}
    const currencyCode=String(v.currencyCode).trim().toUpperCase();
    if(!validPrice(Number(v.price),currencyCode)){toast.error(priceMessage(currencyCode));return}
    void run('Plan créé',()=>plansApi.create({name:String(v.name).trim(),price:Number(v.price),currencyCode,billingInterval:v.billingInterval==='Annuelle'?'Yearly':'Monthly',entitlements,kind:v.kind==='Option'?'AddOn':'Base'}),touches.plans);
  };
  return <>
    <PageHeader title="Plans & Fonctionnalités" subtitle="Le catalogue des offres et des droits associés.">
      <Button variant="outline" onClick={()=>void reload(['plans','subscriptions'])}><RefreshCw size={16}/>Actualiser</Button>
      <FormDialog title="Créer un plan" fields={createFields} initial={{currencyCode:'EUR',billingInterval:'Mensuelle',kind:'Plan de base'}} onSave={create}><Button><Plus size={16}/>Créer un plan</Button></FormDialog>
    </PageHeader>
    {status==='loading'?<LoadingSkeleton/>:!raw.plans.length?<EmptyState title="Aucun plan" description="Créez un premier plan pour pouvoir démarrer des abonnements."/>:<>
      {currencies.length>1&&<div className="table-tabs page-tabs">{['Toutes',...currencies].map(c=><button key={c} className={c===currency?'active':''} onClick={()=>setCurrency(c)}>{c==='Toutes'?'Toutes les devises':c}<span>{c==='Toutes'?raw.plans.length:raw.plans.filter(p=>p.currencyCode===c).length}</span></button>)}</div>}
      <p className="table-note">Un restaurant ne voit et ne peut choisir que les plans de sa devise par défaut. Une option s’ajoute à l’abonnement d’un restaurant, depuis sa fiche (onglet Abonnement).</p>
      <div className="plans-grid">{shown.map(p=>{const u=usage(p);return <section className="plan-card" key={p.id}>
        <div className="plan-head"><span className="plan-icon"><Layers size={22}/></span>{p.kind==='AddOn'&&<StatusBadge value="Option"/>}<StatusBadge value={planStatus[p.status]}/></div>
        <h2>{p.name}</h2>
        <p className="muted">{p.billingInterval==='Yearly'?`Facturation annuelle · ${money(monthly(p),p.currencyCode)} / mois`:'Facturation mensuelle'}</p>
        <div className="plan-price">{money(p.price,p.currencyCode)}<span>/ {p.billingInterval==='Yearly'?'an':'mois'}</span></div>
        <div className="plan-metrics"><div><strong>{number(u.count)}</strong><span>restaurants</span></div><div><strong>{u.mrr}</strong><span>MRR</span></div></div>
        <div>{p.entitlements.length?p.entitlements.map(e=><p className="entitlement-row" key={e.featureCode}><span>{e.enabled?<Check size={14}/>:<X size={14}/>} {e.featureCode}</span><span>{describeEntitlement(e)}</span></p>):<p className="muted">Aucun droit défini.</p>}</div>
        {p.status==='Active'&&<div className="plan-actions">
          <FormDialog title={`Prix du plan ${p.name}`} fields={[{key:'price',label:`Prix (${p.currencyCode})`,type:'number',step:currencyStep(p.currencyCode)}]} initial={{price:p.price}} description="Le nouveau prix s’applique aux prochaines périodes." onSave={v=>{if(!validPrice(Number(v.price),p.currencyCode)){toast.error(priceMessage(p.currencyCode));return}void run('Prix modifié',()=>plansApi.changePrice(p.id,Number(v.price)),touches.plans)}}><Button variant="outline" className="full-button"><Tag size={16}/>Modifier le prix</Button></FormDialog>
          <FormDialog title={`Droit du plan ${p.name}`} fields={entitlementFields} initial={{enabled:'Activé'}} description="Un code existant est remplacé, un nouveau code est ajouté." onSave={v=>{const code=String(v.featureCode).trim().toLowerCase();if(!/^[a-z][a-z0-9._-]{1,63}$/.test(code)){toast.error('Code invalide : a-z, 0-9, « . », « _ », « - », commençant par une lettre.');return}void run('Droit enregistré',()=>plansApi.setEntitlement(p.id,{featureCode:code,enabled:v.enabled==='Activé',limit:Number(v.limit)>0?Number(v.limit):null}),touches.plans)}}><Button variant="outline" className="full-button"><ListChecks size={16}/>Ajouter ou modifier un droit</Button></FormDialog>
          <ConfirmDialog title={`Archiver le plan ${p.name} ?`} description="Le plan ne pourra plus être choisi ; les abonnements existants continuent. L’archivage est définitif." confirmText={p.name} onConfirm={()=>void run('Plan archivé',()=>plansApi.archive(p.id),touches.plans)}><button className="text-link plan-disable"><Archive size={14}/> Archiver</button></ConfirmDialog>
        </div>}
      </section>})}</div>
      {features.length>0&&<section className="panel"><div className="panel-heading"><div><h2>Comparez les droits</h2><p>Les fonctionnalités et limites de chaque offre.</p></div></div><div className="table-scroll"><table><thead><tr><th>Droit</th>{shown.map(p=><th key={p.id}>{p.name} <small>{p.currencyCode}</small></th>)}</tr></thead><tbody>{features.map(f=><tr key={f}><td><code>{f}</code></td>{shown.map(p=>{const e=p.entitlements.find(x=>x.featureCode===f);return <td key={p.id}>{!e||!e.enabled?<span className="text-muted">—</span>:e.limit===null?<Check size={18} className="text-purple"/>:number(e.limit)}</td>})}</tr>)}</tbody></table></div></section>}
    </>}
  </>;
}
