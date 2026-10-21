'use client';
import {useEffect,useMemo,useState} from 'react';
import Link from 'next/link';
import {Store,ShoppingBag,Wallet,Percent,Ban,MonitorSmartphone,UserCheck,PauseCircle,RefreshCw,ExternalLink} from 'lucide-react';
import {Button} from '@/components/ui/button';
import {useAdmin} from '../../hooks/use-admin';
import {PageHeader,StatCard,DateRangePicker,LoadingSkeleton,UserAvatar} from '../../components/ui/shared';
import {ChartCard,SeriesChart,DonutChart} from '../../components/charts/charts';
import {DataTable,type TableColumn} from '../../components/tables/data-table';
import {DetailDrawer} from '../../components/feedback/dialogs';
import {formatTotals} from '../shared/live-page';
import {describe} from '../../services/api/client';
import {addOns as addOnsApi,onlineSales,reporting,type OnlineSalesOverview,type OnlineSalesTenantRow,type ReportRow,type TenantAddOnDto} from '../../services/api/admin-api';
import {shortId} from '../../services/api/mappers';
import {date,dateTime,isoDay,money,number} from '../../utils/format';
import {bucketStart,buckets,period,periodLabel} from '../../utils/period';
import {ChannelTable,TenantOnlineSales,channelLabel} from './tenant-online';
import type {RecordItem} from '../../types';

const percent=(v:number)=>`${(Math.round(v*1000)/10).toLocaleString('fr-FR')} %`;
const byCurrency=(rows:ReportRow[],metric:string):[string,number][]=>{const m=new Map<string,number>();rows.filter(r=>r.metric===metric&&r.currency).forEach(r=>m.set(r.currency,(m.get(r.currency)??0)+r.value));return [...m.entries()].sort((a,b)=>b[1]-a[1])};

interface Loaded {overview:OnlineSalesOverview;tenants:OnlineSalesTenantRow[]&{truncated?:boolean};options:TenantAddOnDto[];activity:ReportRow[];granularity:'Day'|'Week'|'Month'}

/** Une option qui ouvre la vente en ligne (site, application, bornes). */
const isOnlineOption=(o:TenantAddOnDto)=>o.addOn.entitlements.some(e=>e.enabled&&e.featureCode.startsWith('storefront.'));
const optionLabel=(o:TenantAddOnDto)=>`${o.addOn.name}${o.quantity>1?` × ${o.quantity}`:''}`;
const emptyRow=(tenantId:string):OnlineSalesTenantRow=>({tenantId,webApps:0,mobileApps:0,kioskApps:0,disabledApps:0,activeKiosks:0,configuredLocations:0,pausedLocations:0,ordersPlaced:0,ordersExpired:0,lastOrderAtUtc:null,activeCustomerSessions:0});

export default function OnlineSales(){
  const {raw}=useAdmin();
  const [choice,setChoice]=useState('30 jours');
  const [range,setRange]=useState({from:isoDay(new Date(Date.now()-29*86400000)),to:isoDay(new Date())});
  const [data,setData]=useState<Loaded|null>(null);
  const [error,setError]=useState<string>();
  const [tick,setTick]=useState(0);
  const [selected,setSelected]=useState<string|null>(null);
  const p=useMemo(()=>period(choice,range),[choice,range]);

  useEffect(()=>{
    let active=true;
    setError(undefined);
    Promise.all([
      onlineSales.overview(p.from,p.to),
      onlineSales.tenants(p.from,p.to),
      addOnsApi.active(),
      reporting.activity(p.from,p.to,p.granularity,['sales.online_revenue','sales.online_orders_completed','sales.revenue']),
    ]).then(([overview,tenants,options,activity])=>{if(active)setData({overview,tenants,options:options.filter(isOnlineOption),activity:activity.rows,granularity:p.granularity})})
      .catch(e=>{if(active)setError(describe(e))});
    return()=>{active=false};
  },[p,tick]);

  const o=data?.overview;
  const placed=o?o.channels.reduce((t,c)=>t+c.placed,0):0;
  const expired=o?o.channels.reduce((t,c)=>t+c.expired,0):0;
  const paidOnline=o?o.channels.reduce((t,c)=>t+c.paidOnline,0):0;
  const onlineRevenue=data?byCurrency(data.activity,'sales.online_revenue'):[];
  const revenue=data?byCurrency(data.activity,'sales.revenue'):[];
  const mainCurrency=onlineRevenue[0]?.[0]??revenue[0]?.[0];
  const share=mainCurrency&&(revenue.find(([c])=>c===mainCurrency)?.[1]??0)>0?(onlineRevenue.find(([c])=>c===mainCurrency)?.[1]??0)/(revenue.find(([c])=>c===mainCurrency)?.[1]??1):null;
  const series=data?buckets({...p,granularity:data.granularity}).map(b=>({name:periodLabel(b,data.granularity),value:data.activity.filter(r=>r.metric==='sales.online_revenue'&&r.currency===mainCurrency&&bucketStart(r.periodStart+'T00:00:00Z',data.granularity)===b).reduce((t,r)=>t+r.value,0)})):[];

  const tenantById=new Map(raw.tenants.map(t=>[t.tenantId,t]));
  // Les restaurants qui ont une application de vente, et ceux qui ont souscrit une option sans encore en créer.
  const optionsByTenant=new Map<string,TenantAddOnDto[]>();
  (data?.options??[]).forEach(o=>optionsByTenant.set(o.tenantId,[...(optionsByTenant.get(o.tenantId)??[]),o]));
  const known=new Set((data?.tenants??[]).map(t=>t.tenantId));
  const allTenants=[...(data?.tenants??[]),...[...optionsByTenant.keys()].filter(id=>!known.has(id)).map(emptyRow)];
  const rows:RecordItem[]=allTenants.map(t=>{const tenant=tenantById.get(t.tenantId);const apps=t.webApps+t.mobileApps+t.kioskApps;return {
    id:t.tenantId,
    name:tenant?.name??`Restaurant ${shortId(t.tenantId)}`,
    plan:tenant?.planName??'Sans abonnement',
    options:(optionsByTenant.get(t.tenantId)??[]).map(optionLabel).join(' · ')||'Aucune',
    status:t.ordersPlaced>0?'Actif':apps===0?'Sans application':'Sans commande',
    channels:[t.webApps?`${t.webApps} site${t.webApps>1?'s':''}`:'',t.mobileApps?`${t.mobileApps} appli.`:'',t.kioskApps?`bornes`:''].filter(Boolean).join(' · ')||'Aucune application',
    kiosks:t.activeKiosks,
    locations:t.configuredLocations?`${t.configuredLocations}${t.pausedLocations?` (${t.pausedLocations} en pause)`:''}`:'—',
    orders:t.ordersPlaced,
    expired:t.ordersExpired,
    last:t.lastOrderAtUtc??'',
    customers:t.activeCustomerSessions,
  }});
  const columns:TableColumn[]=[
    {key:'name',label:'Restaurant',render:r=><div className="identity"><UserAvatar name={r.name}/><div><strong>{r.name}</strong><small>{String(r.plan)}</small></div></div>},
    {key:'options',label:'Options souscrites'},
    {key:'channels',label:'Applications'},
    {key:'kiosks',label:'Bornes',render:r=>number(Number(r.kiosks))},
    {key:'locations',label:'Établissements'},
    {key:'orders',label:'Commandes',render:r=><span>{number(Number(r.orders))}{Number(r.expired)?<small className="text-muted"> · {number(Number(r.expired))} refusée(s)</small>:null}</span>},
    {key:'status',label:'Activité'},
    {key:'last',label:'Dernière commande',render:r=>r.last?dateTime(r.last):'Jamais'},
    {key:'customers',label:'Clients connectés',render:r=>number(Number(r.customers))},
  ];
  const selectedRow=rows.find(r=>r.id===selected);

  const stats:[string,string,typeof Store,string][]=o?[
    ['Restaurants avec une option',number(optionsByTenant.size),Store,`${number(o.tenantsWithApps)} avec une application active · ${number(o.tenantsWithOrders)} avec des commandes`],
    ['Commandes en ligne',number(placed),ShoppingBag,placed?`${percent(paidOnline/placed)} payées en ligne`:'Site, application et bornes'],
    ['Chiffre d’affaires en ligne',onlineRevenue.length?formatTotals(onlineRevenue):money(0,mainCurrency??'EUR'),Wallet,'Commandes en ligne terminées'],
    ['Part du chiffre d’affaires',share===null?'—':percent(share),Percent,mainCurrency?`En ${mainCurrency}, toutes commandes terminées`:'Aucune vente'],
    ['Taux de refus',placed?percent(expired/placed):'—',Ban,`${number(expired)} non acceptée(s) à temps`],
    ['Bornes actives',number(o.activeKiosks),MonitorSmartphone,`${number(o.revokedKiosks)} révoquée(s)`],
    ['Clients connectés',number(o.activeCustomerSessions),UserCheck,`${number(o.customerSignIns)} connexion(s) sur la période`],
    ['Établissements en pause',number(o.pausedLocations),PauseCircle,`sur ${number(o.configuredLocations)} réglé(s) pour la vente en ligne`],
  ]:[];

  return <>
    <PageHeader title="Vente en ligne" subtitle={`Sites, applications et bornes des restaurants · ${date(p.from)} → ${date(p.to)}`}>
      <DateRangePicker value={choice} onChange={v=>{if(v==='Personnalisé')setRange({from:p.from,to:p.to});setChoice(v)}} onRange={setRange} range={range}/>
      <Button variant="outline" onClick={()=>setTick(t=>t+1)}><RefreshCw size={16}/>Actualiser</Button>
    </PageHeader>
    {error&&<div className="notice warn">Vente en ligne indisponible : {error}</div>}
    {!data&&!error?<LoadingSkeleton/>:data&&o&&<>
      <div className="stats-grid four">{stats.map(([label,value,Icon,caption])=><StatCard key={label} label={label} value={value} caption={caption} icon={Icon}/>)}</div>
      <div className="charts-grid">
        <ChartCard title="Chiffre d’affaires en ligne" subtitle={mainCurrency?`Commandes en ligne terminées · ${mainCurrency}`:'Commandes en ligne terminées'}><SeriesChart data={series} label="Chiffre d’affaires en ligne" format={v=>money(v,mainCurrency??'EUR')}/></ChartCard>
        <ChartCard title="Répartition par canal" subtitle="Commandes passées sur la période"><DonutChart data={o.channels.map(c=>({name:channelLabel[c.channel],value:c.placed}))} center={number(placed)} caption="commandes"/></ChartCard>
      </div>
      <div className="two-columns">
        <section className="panel"><div className="panel-heading"><div><h2>Commandes par canal</h2><p>Prise en charge, refus faute d’acceptation, annulations par le client</p></div></div><ChannelTable channels={o.channels}/></section>
        <ChartCard title="Applications de vente" subtitle="Applications actives, par canal"><DonutChart data={o.apps.map(a=>({name:channelLabel[a.kind],value:a.active}))} center={number(o.apps.reduce((t,a)=>t+a.active,0))} caption="actives"/></ChartCard>
      </div>
      <DataTable key={`${p.from}-${p.to}`} rows={rows} columns={columns} tabs={['Tous','Actif','Sans commande','Sans application']} filters={[{key:'plan',label:'Plan'}]} onOpen={r=>setSelected(r.id)} filename={`vente-en-ligne-${p.from}-${p.to}`}/>
      {data.tenants.truncated&&<div className="notice warn" role="status">Liste incomplète : seuls les {number(rows.length)} restaurants les plus actifs sont chargés.</div>}
      <p className="table-note">Restaurants ayant une option de vente en ligne ou une application de vente, du plus actif au moins actif ; « Sans application » : option souscrite, aucune application créée. Le chiffre d’affaires en ligne compte les commandes terminées depuis la mise à jour de la plateforme qui l’enregistre. Aucune clé, aucun jeton ni aucune donnée de client n’est affiché ici.</p>
    </>}
    {selected&&<DetailDrawer open onClose={()=>setSelected(null)} title={selectedRow?.name??shortId(selected)} subtitle="Vente en ligne">
      <TenantOnlineSales tenantId={selected} from={p.from} to={p.to}/>
      <div className="drawer-actions"><Button variant="outline" asChild><Link href={`/admin/restaurants?record=${selected}`}><ExternalLink size={15}/>Fiche du restaurant</Link></Button></div>
    </DetailDrawer>}
  </>;
}
