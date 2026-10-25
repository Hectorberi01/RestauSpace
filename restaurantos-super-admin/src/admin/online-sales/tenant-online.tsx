'use client';
import {useEffect,useState} from 'react';
import {Globe,Smartphone,MonitorSmartphone,PauseCircle,CalendarDays,ChevronDown,Check,Layers,ShoppingCart,Wallet,Store,Gift} from 'lucide-react';
import {Button} from '@/components/ui/button';
import {AddOnOfferDialog,addOnOfferText} from '../shared/complimentary';
import {DropdownMenu,DropdownMenuContent,DropdownMenuItem,DropdownMenuTrigger} from '@/components/ui/dropdown-menu';
import {StatusBadge,LoadingSkeleton} from '../../components/ui/shared';
import {describe} from '../../services/api/client';
import {addOns as addOnsApi,onlineSales,type OnlineChannelStats,type OnlineSalesTenantDetail,type OnlineServiceMode,type SalesAppKind,type TenantAddOnDto} from '../../services/api/admin-api';
import {shortId} from '../../services/api/mappers';
import {date,dateTime,isoDay,money,number} from '../../utils/format';

export const channelLabel:Record<SalesAppKind,string>={Web:'Site web',MobileApp:'Application',Kiosk:'Borne'};
export const channelIcon={Web:Globe,MobileApp:Smartphone,Kiosk:MonitorSmartphone} as const;
const modeLabel:Record<OnlineServiceMode,string>={Takeaway:'À emporter',DineIn:'Sur place',Delivery:'Livraison'};

export function ChannelTable({channels}:{channels:OnlineChannelStats[]}){
  const total=channels.reduce((t,c)=>t+c.placed,0);
  return <div className="simple-table"><table><thead><tr><th>Canal</th><th>Commandes</th><th>Prises en charge</th><th>Refusées</th><th>Annulées</th><th>Payées en ligne</th></tr></thead><tbody>
    {channels.map(c=>{const Icon=channelIcon[c.channel];return <tr key={c.channel}><td><span className="channel-cell"><Icon size={15}/>{channelLabel[c.channel]}</span></td><td>{number(c.placed)}</td><td>{number(c.handled)}{c.waiting?<small> · {number(c.waiting)} en attente</small>:null}</td><td>{number(c.expired)}</td><td>{number(c.cancelledByCustomer)}</td><td>{c.placed?`${Math.round(c.paidOnline/c.placed*100)} %`:'—'}</td></tr>})}
    {!total&&<tr><td colSpan={6}>Aucune commande en ligne sur la période.</td></tr>}
  </tbody></table></div>;
}

const periods:[string,number][]=[['7 derniers jours',7],['30 derniers jours',30],['90 derniers jours',90]];
const shiftDay=(day:string,days:number)=>{const d=new Date(`${day}T00:00:00`);d.setDate(d.getDate()+days);return isoDay(d)};
const total=(channels:OnlineChannelStats[],key:'placed'|'paidOnline')=>channels.reduce((t,c)=>t+c[key],0);

/** Écart avec la période précédente de même durée ; rien si elle n'a pas été chargée. */
function Delta({now,before}:{now:number;before:number|null}){
  if(before===null)return null;
  const diff=now-before;
  return <div className={`mini-stat-delta ${diff>0?'up':''}`}><b>{diff>0?`+${diff}`:diff}</b><small>vs période préc.</small></div>;
}

const optionIcon=(o:TenantAddOnDto)=>o.addOn.entitlements.some(e=>e.enabled&&e.featureCode.includes('kiosk'))?MonitorSmartphone:o.addOn.entitlements.some(e=>e.enabled&&e.featureCode.includes('app'))?Smartphone:Globe;

/** La vente en ligne d'un restaurant : période, aperçu, options, commandes par canal, applications, bornes, établissements. Aucun secret. */
/** Option de vente en ligne : elle donne un droit storefront.* (site via l'API, application, borne). */
const isOnline=(p:{entitlements:{enabled:boolean;featureCode:string}[]})=>p.entitlements.some(e=>e.enabled&&e.featureCode.startsWith('storefront.'));

export function TenantOnlineSales({tenantId,from,to,currency}:{tenantId:string;from?:string;to?:string;currency?:string}){
  const [days,setDays]=useState(30);
  const [data,setData]=useState<OnlineSalesTenantDetail|null>(null);
  const [previous,setPrevious]=useState<OnlineSalesTenantDetail|null>(null);
  const [error,setError]=useState<string>();
  const [options,setOptions]=useState<TenantAddOnDto[]|null>(null);
  const [version,setVersion]=useState(0);
  const fixed=!!from&&!!to;
  const end=to??isoDay(new Date());
  const start=from??shiftDay(end,-(days-1));
  useEffect(()=>{let active=true;addOnsApi.list(tenantId).then(list=>{if(active)setOptions(list.filter(x=>x.status==='Active'&&isOnline(x.addOn)))}).catch(()=>{if(active)setOptions([])});return()=>{active=false}},[tenantId,version]);
  useEffect(()=>{
    let active=true;setData(null);setPrevious(null);setError(undefined);
    const length=Math.round((new Date(`${end}T00:00:00`).getTime()-new Date(`${start}T00:00:00`).getTime())/86400000)+1;
    onlineSales.tenant(tenantId,start,end).then(d=>{if(active)setData(d)}).catch(e=>{if(active)setError(describe(e))});
    onlineSales.tenant(tenantId,shiftDay(start,-length),shiftDay(start,-1)).then(d=>{if(active)setPrevious(d)}).catch(()=>{});
    return()=>{active=false};
  },[tenantId,start,end]);
  if(error)return <p className="login-error">{error}</p>;
  if(!data)return <LoadingSkeleton/>;
  const placed=total(data.channels,'placed');
  const paid=total(data.channels,'paidOnline');
  const period=<div className="online-period">
    <span className="strip-icon"><CalendarDays size={17}/></span>
    <div>
      {fixed?<strong>Du {date(data.from)} au {date(data.to)}</strong>:<DropdownMenu><DropdownMenuTrigger asChild><button className="period-button">Du {date(data.from)} au {date(data.to)}<ChevronDown size={14}/></button></DropdownMenuTrigger><DropdownMenuContent align="start">{periods.map(([label,n])=><DropdownMenuItem key={n} onClick={()=>setDays(n)}>{days===n&&<Check size={14}/>}{label}</DropdownMenuItem>)}</DropdownMenuContent></DropdownMenu>}
      <small>Dernière commande : {data.lastOrderAtUtc?dateTime(data.lastOrderAtUtc):'aucune'} · {number(data.activeCustomerSessions)} client(s) connecté(s), {number(data.customerSignIns)} connexion(s) sur la période</small>
    </div>
  </div>;
  const reload=()=>setVersion(v=>v+1);
  const offer=<AddOnOfferDialog tenantId={tenantId} currency={currency} only={isOnline} exclude={(options??[]).map(o=>o.addOn.id)} onDone={reload}><Button variant="outline" size="sm"><Gift size={14}/>Offrir une option</Button></AddOnOfferDialog>;
  const subscribed=<section className="online-block"><div className="online-block-head"><h4><Layers size={16}/>Options de vente en ligne</h4>{offer}</div>{options===null?<LoadingSkeleton/>:options.length?<div className="online-list">{options.map(o=>{const Icon=optionIcon(o);return <div key={o.id} className="online-row"><span className="online-row-icon"><Icon size={16}/></span><strong>{o.addOn.name}{o.quantity>1?` × ${o.quantity}`:''}</strong><span className="online-row-meta">{o.isComplimentary?addOnOfferText(o):`${money(o.addOn.price*o.quantity,o.addOn.currencyCode)} / ${o.addOn.billingInterval==='Yearly'?'an':'mois'}`}</span><StatusBadge value={o.isComplimentary?'Offert':'Actif'}/>{!o.isComplimentary&&<AddOnOfferDialog tenantId={tenantId} currency={currency} addOn={o} onDone={reload}><button className="text-link"><Gift size={14}/> Offrir</button></AddOnOfferDialog>}</div>})}</div>:<p className="muted">Aucune option souscrite. Offrez-en une ici, ajoutez-la dans l’onglet Abonnement, ou laissez le restaurant la souscrire depuis sa console.</p>}</section>;
  if(!data.apps.length)return <div className="online-tab">{period}{subscribed}<section className="online-block"><h4><Globe size={16}/>Applications de vente</h4><p className="muted">Aucune application de vente : ce restaurant ne vend pas en ligne.</p></section></div>;
  return <div className="online-tab">
    {period}
    <section className="online-block">
      <h4>Aperçu des ventes en ligne</h4>
      <div className="online-kpis">
        <div className="mini-stat"><span className="mini-stat-icon purple"><ShoppingCart size={20}/></span><div className="mini-stat-body"><span>Commande(s)</span><strong>{number(placed)}</strong></div><Delta now={placed} before={previous?total(previous.channels,'placed'):null}/></div>
        <div className="mini-stat"><span className="mini-stat-icon green"><Wallet size={20}/></span><div className="mini-stat-body"><span>Payées en ligne</span><strong>{number(paid)}{placed?<small> · {Math.round(paid/placed*100)} %</small>:null}</strong></div><Delta now={paid} before={previous?total(previous.channels,'paidOnline'):null}/></div>
      </div>
    </section>
    {subscribed}
    <section className="online-block"><h4>Commandes en ligne</h4><div className="online-table"><ChannelTable channels={data.channels}/></div></section>
    <section className="online-block"><h4>Applications de vente</h4><div className="online-list">{data.apps.map(a=>{const Icon=channelIcon[a.kind];return <div key={a.id} className="online-row"><span className="online-row-icon"><Icon size={16}/></span><strong>{a.name}</strong><span className="online-row-meta">{channelLabel[a.kind]}</span><StatusBadge value={a.status==='Active'?'Actif':'Désactivé'}/></div>})}</div></section>
    <section className="online-block"><h4>Bornes</h4>{data.kiosks.length?<div className="online-list">{data.kiosks.map(k=><div key={k.id} className="online-row"><span className="online-row-icon"><MonitorSmartphone size={16}/></span><strong>{k.name}</strong><span className="online-row-meta">Établissement {shortId(k.locationId)} · {k.status==='Active'?`appairée le ${date(k.pairedAtUtc)}`:`révoquée le ${date(k.revokedAtUtc)}`}</span><StatusBadge value={k.status==='Active'?'Actif':'Désactivé'}/></div>)}</div>:<p className="muted">Aucune borne appairée.</p>}</section>
    <section className="online-block"><h4>Établissements</h4>{data.locations.length?<div className="online-list">{data.locations.map(l=><div key={l.locationId} className="online-row tall"><span className="online-row-icon"><Store size={16}/></span><span className="online-row-text"><strong>Établissement {shortId(l.locationId)}</strong><small>{l.serviceModes.map(m=>modeLabel[m]).join(', ')||'Aucun mode'}{l.slotCapacity?` · ${l.slotCapacity} commandes / créneau`:''}</small></span><span className={`status ${l.isPaused?'amber':'green'}`}>{l.isPaused?<><PauseCircle size={12}/> En pause</>:'Ouvert'}</span></div>)}</div>:<p className="muted">Aucun établissement réglé pour la vente en ligne.</p>}</section>
  </div>;
}
