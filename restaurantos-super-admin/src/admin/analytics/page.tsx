'use client';
import {useEffect,useMemo,useState} from 'react';
import {Euro,Store,TrendingDown,TrendingUp,ShoppingBag,Wallet,Receipt,Ban,Download,RefreshCw,ChartNoAxesCombined,UserCheck,Hourglass,CreditCard,ArrowRight,ArrowUp,ArrowDown} from 'lucide-react';
import Link from 'next/link';
import type {ComponentType} from 'react';
import {moduleStyle,title as auditTitle} from '../shared/tenant-activity';
import {Button} from '@/components/ui/button';
import {useAdmin} from '../../hooks/use-admin';
import {PageHeader,DateRangePicker,LoadingSkeleton,MiniStat} from '../../components/ui/shared';
import {ChartCard,SeriesChart,DonutChart,DualSeriesChart,ComboChart,DONUT_PALETTE} from '../../components/charts/charts';
import {formatTotals} from '../shared/live-page';
import {exportCsv} from '../../services/admin-service';
import {describe} from '../../services/api/client';
import {reporting,type AmountByCurrency,type ReportRow} from '../../services/api/admin-api';
import {monthly} from '../../services/api/mappers';
import {money,number,date,dateTime,isoDay} from '../../utils/format';
import {bucketStart,buckets,period,periodLabel} from '../../utils/period';

const METRICS=['sales.revenue','sales.orders_completed','sales.orders_placed','sales.orders_cancelled','reservations.created'];
const percent=(v:number)=>`${(Math.round(v*1000)/10).toLocaleString('fr-FR')} %`;

export default function Analytics(){
  const {raw,status,truncated}=useAdmin();
  const [choice,setChoice]=useState('12 mois');
  const [range,setRange]=useState({from:isoDay(new Date(Date.now()-364*86400000)),to:isoDay(new Date())});
  const [rows,setRows]=useState<ReportRow[]|null>(null);
  const [previousRows,setPreviousRows]=useState<ReportRow[]|null>(null);
  const [recurring,setRecurring]=useState<AmountByCurrency[]|null>(null);
  const [error,setError]=useState<string>();
  const [tick,setTick]=useState(0);
  const p=useMemo(()=>period(choice,range),[choice,range]);

  useEffect(()=>{
    let active=true;
    setError(undefined);
    Promise.all([reporting.activity(p.from,p.to,p.granularity,METRICS),reporting.dashboard(p.from,p.to)])
      .then(([activity,dashboard])=>{if(active){setRows(activity.rows);setRecurring(dashboard.monthlyRecurringRevenue)}})
      .catch(e=>{if(active)setError(describe(e))});
    // Période précédente de même durée, pour les écarts des chiffres clés.
    const length=Math.round((new Date(p.to).getTime()-new Date(p.from).getTime())/86400000)+1;
    const shift=(day:string,days:number)=>{const d=new Date(day+'T00:00:00Z');d.setUTCDate(d.getUTCDate()+days);return isoDay(d)};
    setPreviousRows(null);
    reporting.activity(shift(p.from,-length),shift(p.from,-1),p.granularity,METRICS).then(r=>{if(active)setPreviousRows(r.rows)}).catch(()=>{});
    return()=>{active=false};
  },[p,tick]);

  const from=new Date(p.from+'T00:00:00Z').getTime();
  const to=new Date(p.to+'T00:00:00Z').getTime()+86400000;
  const inPeriod=(iso:string|null)=>{if(!iso)return false;const t=new Date(iso).getTime();return t>=from&&t<to};
  const plans=new Map(raw.plans.map(x=>[x.id,x]));
  const billed=raw.subscriptions.filter(s=>s.status==='Active'||s.status==='PastDue');
  // MRR : la même source que le tableau de bord (montants recopiés par la facturation à chaque changement).
  const mrr:[string,number][]=(recurring??[]).map(m=>[m.currencyCode,m.amount] as [string,number]).sort((a,b)=>b[1]-a[1]);
  const [mainCurrency,mainMrr]=mrr[0]??['EUR',0];
  const billedMain=billed.filter(s=>plans.get(s.planId)?.currencyCode===mainCurrency).length;
  // Churn : parmi les abonnements vivants au début de la période, la part résiliée pendant la période (jamais plus de 100 %).
  const alive=raw.subscriptions.filter(s=>new Date(s.startedAtUtc).getTime()<from&&(!s.cancelledAtUtc||new Date(s.cancelledAtUtc).getTime()>=from));
  const liveAtStart=alive.length;
  const cancelled=alive.filter(s=>inPeriod(s.cancelledAtUtc)).length;
  const cancelledAll=raw.subscriptions.filter(s=>inPeriod(s.cancelledAtUtc)).length;
  const churn=liveAtStart?cancelled/liveAtStart:0;
  const newTenants=raw.tenants.filter(t=>inPeriod(t.createdAtUtc)).length;
  const collected=new Map<string,number>();
  raw.payments.filter(x=>inPeriod(x.recordedAtUtc)).forEach(x=>collected.set(x.currencyCode,(collected.get(x.currencyCode)??0)+x.amount));

  const data=rows??[];
  const sum=(metric:string)=>data.filter(r=>r.metric===metric).reduce((t,r)=>t+r.value,0);
  const revenueByCurrency=new Map<string,number>();
  data.filter(r=>r.metric==='sales.revenue'&&r.currency).forEach(r=>revenueByCurrency.set(r.currency,(revenueByCurrency.get(r.currency)??0)+r.value));
  const revenue=[...revenueByCurrency.entries()].sort((a,b)=>b[1]-a[1]);
  const revenueCurrency=revenue[0]?.[0];
  const completed=sum('sales.orders_completed');
  const placed=sum('sales.orders_placed');
  const cancelledOrders=sum('sales.orders_cancelled');
  const series=(metric:string,currency?:string)=>buckets(p).map(b=>({name:periodLabel(b,p.granularity),value:data.filter(r=>r.metric===metric&&(!currency||r.currency===currency)&&bucketStart(r.periodStart+'T00:00:00Z',p.granularity)===b).reduce((t,r)=>t+r.value,0)}));

  const growth=buckets(p).map(b=>{const end=new Date(b+'T00:00:00Z');return {b,end}}).map(({b},i,all)=>{const next=all[i+1]?new Date(all[i+1].b+'T00:00:00Z').getTime():to;return {name:periodLabel(b,p.granularity),value:raw.tenants.filter(t=>new Date(t.createdAtUtc).getTime()<next).length}});
  const movements=buckets(p).map(b=>({name:periodLabel(b,p.granularity),a:raw.subscriptions.filter(s=>bucketStart(s.startedAtUtc,p.granularity)===b&&inPeriod(s.startedAtUtc)).length,b:raw.subscriptions.filter(s=>s.cancelledAtUtc&&bucketStart(s.cancelledAtUtc,p.granularity)===b&&inPeriod(s.cancelledAtUtc)).length}));
  const byPlan=raw.plans.map(plan=>{const live=raw.subscriptions.filter(s=>s.planId===plan.id&&s.status!=='Cancelled');const paying=live.filter(s=>s.status==='Active'||s.status==='PastDue').length;return {plan,live:live.length,paying,mrr:paying*monthly(plan)}}).filter(x=>x.live>0||x.plan.status==='Active');

  const before=previousRows??[];
  const sumBefore=(metric:string)=>before.filter(r=>r.metric===metric).reduce((t,r)=>t+r.value,0);
  const revenueBefore=revenueCurrency?before.filter(r=>r.metric==='sales.revenue'&&r.currency===revenueCurrency).reduce((t,r)=>t+r.value,0):0;
  const revenueNow=revenueCurrency?revenueByCurrency.get(revenueCurrency)??0:0;
  const completedBefore=sumBefore('sales.orders_completed');
  const placedBefore=sumBefore('sales.orders_placed');
  const basket=completed&&revenueCurrency?revenueNow/completed:null;
  const basketBefore=completedBefore&&revenueCurrency?revenueBefore/completedBefore:null;
  const cancelRate=placed?cancelledOrders/placed:null;
  const cancelRateBefore=placedBefore?sumBefore('sales.orders_cancelled')/placedBefore:null;
  /** Évolution relative ; rien sans base de comparaison. */
  const change=(now:number|null,then:number|null):Trend|undefined=>previousRows&&now!==null&&then?{value:(now-then)/then,label:'vs période préc.'}:undefined;
  const points=(now:number|null,then:number|null,invert=false):Trend|undefined=>previousRows&&now!==null&&then!==null?{value:now-then,label:'vs période préc.',points:true,invert}:undefined;

  const monthStart=new Date(new Date().getFullYear(),new Date().getMonth(),1).getTime();
  const createdThisMonth=(list:{createdAtUtc:string}[])=>list.filter(t=>new Date(t.createdAtUtc).getTime()>=monthStart).length;
  const liveSubscriptions=raw.subscriptions.filter(s=>s.status!=='Cancelled');
  const startedThisMonth=raw.subscriptions.filter(s=>new Date(s.startedAtUtc).getTime()>=monthStart).length;

  const keyFigures:KeyFigure[]=[
    ['Chiffre d’affaires',formatTotals(revenue),Receipt,'purple',change(revenueNow,revenueBefore)],
    ['Commandes terminées',number(completed),ShoppingBag,'blue',change(completed,completedBefore)],
    ['Abonnements en cours',number(liveSubscriptions.length),CreditCard,'purple',{value:startedThisMonth,label:'démarrés ce mois',count:true}],
    ['Panier moyen',basket!==null&&revenueCurrency?money(basket,revenueCurrency):'—',Wallet,'green',change(basket,basketBefore)],
    ['Taux d’annulation',cancelRate!==null?percent(cancelRate):'—',Ban,'red',points(cancelRate,cancelRateBefore,true)],
    ['Churn',liveAtStart?percent(churn):'—',TrendingDown,'amber',undefined,`${cancelled} sur ${liveAtStart} en début de période`],
    ['ARR',recurring?formatTotals(mrr.map(([c,v])=>[c,v*12])):'—',TrendingUp,'blue',undefined,'MRR × 12'],
    ['Abonnements encaissés',formatTotals([...collected.entries()]),Euro,'green',undefined,'Paiements enregistrés sur la période'],
  ];

  const main=buckets(p).map((b,i,all)=>{const next=all[i+1]?new Date(all[i+1]+'T00:00:00Z').getTime():to;return {name:periodLabel(b,p.granularity),line:data.filter(r=>r.metric==='sales.revenue'&&(!revenueCurrency||r.currency===revenueCurrency)&&bucketStart(r.periodStart+'T00:00:00Z',p.granularity)===b).reduce((t,r)=>t+r.value,0),bar:raw.tenants.filter(t=>new Date(t.createdAtUtc).getTime()<next).length}});
  const liveTotal=byPlan.reduce((t,x)=>t+x.live,0);
  const latest=[...raw.audit].sort((a,b)=>b.occurredAtUtc.localeCompare(a.occurredAtUtc)).slice(0,6);
  const tenantNames=new Map(raw.tenants.map(t=>[t.tenantId,t.name]));

  const exportRows=()=>exportCsv(byPlan.map(x=>({id:x.plan.id,name:x.plan.name,status:x.plan.status,price:x.plan.price,currency:x.plan.currencyCode,abonnements:x.live,payants:x.paying,mrr:x.mrr})),`statistiques-plans-${p.from}-${p.to}`);

  return <>
    <PageHeader title="Statistiques" icon={ChartNoAxesCombined} subtitle={`Croissance et activité de la plateforme · ${date(p.from)} → ${date(p.to)}`}>
      <DateRangePicker value={choice} onChange={v=>{if(v==='Personnalisé')setRange({from:p.from,to:p.to});setChoice(v)}} onRange={setRange} range={range}/>
      <Button variant="outline" onClick={()=>setTick(t=>t+1)}><RefreshCw size={16}/>Actualiser</Button>
      <Button variant="outline" onClick={exportRows}><Download size={16}/>Exporter</Button>
    </PageHeader>
    {error&&<div className="notice warn">Activité indisponible : {error}</div>}
    {status==='loading'?<LoadingSkeleton/>:<>
      <div className="mini-stats">
        <MiniStat label="Restaurants" value={number(raw.tenants.length)} icon={Store} tone="purple" delta={createdThisMonth(raw.tenants)}/>
        <MiniStat label="Actifs" value={number(raw.tenants.filter(t=>t.status==='Active').length)} icon={UserCheck} tone="blue" delta={createdThisMonth(raw.tenants.filter(t=>t.status==='Active'))}/>
        <MiniStat label="En essai" value={number(raw.tenants.filter(t=>t.status==='Trial').length)} icon={Hourglass} tone="amber" delta={createdThisMonth(raw.tenants.filter(t=>t.status==='Trial'))}/>
        <MiniStat label="MRR" value={recurring?formatTotals(mrr):'—'} icon={Euro} tone="green"/>
      </div>
      <div className="stats-layout">
        <div className="stats-main">
          <ChartCard title="Chiffre d’affaires et croissance" subtitle={`Ventes des restaurants${revenueCurrency?` (${revenueCurrency})`:''} et nombre cumulé de restaurants`}>{rows?<ComboChart data={main} lineLabel={`Chiffre d’affaires${revenueCurrency?` (${revenueCurrency})`:''}`} barLabel="Restaurants" format={v=>money(v,revenueCurrency??'EUR')}/>:<LoadingSkeleton/>}</ChartCard>
          <div className="stats-split">
            <ChartCard title="Abonnements par plan" subtitle="Répartition des abonnements non résiliés">
              <div className="plan-donut">
                <DonutChart data={byPlan.map(x=>({name:`${x.plan.name} · ${x.plan.currencyCode}`,value:x.live}))} center={number(liveTotal)} caption="abonnements" legend={false}/>
                <ul className="plan-legend">{byPlan.map((x,i)=><li key={x.plan.id}><i style={{background:DONUT_PALETTE[i%DONUT_PALETTE.length]}}/><span>{x.plan.name} · {x.plan.currencyCode}</span><strong>{number(x.live)}</strong><small>{liveTotal?Math.round(x.live/liveTotal*100):0} %</small></li>)}{!byPlan.length&&<li className="muted">Aucun plan.</li>}</ul>
              </div>
            </ChartCard>
            <div className="stats-stack">
              <ChartCard title="Croissance des restaurants" subtitle="Nombre cumulé de restaurants créés"><div className="chart-small"><SeriesChart data={growth} label="Restaurants"/></div></ChartCard>
              <ChartCard title="Abonnements démarrés et résiliés" subtitle="Par période"><div className="chart-small"><DualSeriesChart data={movements} labels={['Démarrés','Résiliés']}/></div></ChartCard>
            </div>
          </div>
        </div>
        <aside className="stats-side">
          <section className="panel">
            <div className="panel-heading"><div><h2>Détail des chiffres clés</h2><p>Sur la période choisie</p></div></div>
            <div className="key-grid">{keyFigures.map(([label,value,Icon,tone,trend,caption])=><div key={label} className="key-card">
              <span className={`key-icon ${tone}`}><Icon size={17}/></span>
              <span className="key-label">{label}</span>
              <strong className="key-value">{value}</strong>
              {trend?<TrendLine trend={trend}/>:caption?<small className="key-caption">{caption}</small>:null}
            </div>)}</div>
          </section>
          <section className="panel">
            <div className="panel-heading"><div><h2>Dernières activités</h2><p>Journal d’audit de la plateforme</p></div><Link href="/admin/audit-logs" className="see-all">Voir tout <ArrowRight size={13}/></Link></div>
            {latest.length?<ul className="latest-list">{latest.map(e=>{const module=e.action.split('.')[0];const [Icon,tone,tag]=moduleStyle[module]??[Receipt,'gray',module];return <li key={e.id}><span className={`activity-icon ${tone}`}><Icon size={16}/></span><span className="latest-text"><strong>{auditTitle(e.action)}</strong><small>{e.tenantId?tenantNames.get(e.tenantId)??'Restaurant':'Plateforme'}</small></span><span className="latest-side"><time>{dateTime(e.occurredAtUtc)}</time><span className="latest-tag">{tag}</span></span></li>})}</ul>:<p className="muted">Aucune activité récente.</p>}
          </section>
        </aside>
      </div>
      {(truncated.subscriptions||truncated.restaurants||truncated.billing)&&<div className="notice warn" role="status">Certaines listes ont atteint la limite de chargement : churn, croissance et encaissements sont partiels.</div>}
      <p className="table-note">MRR, ARR et activité des restaurants : API de reporting (devises séparées). Écarts : période précédente de même durée. Churn, croissance et répartition par plan : abonnements et restaurants chargés depuis l’API.</p>
    </>}
  </>;
}

type Trend={value:number;label:string;points?:boolean;count?:boolean;invert?:boolean};
type KeyFigure=[string,string,ComponentType<{size?:number}>,'purple'|'blue'|'green'|'amber'|'red',Trend|undefined,string?];

/** Évolution : en %, en points (taux) ou en nombre ; une hausse d'un taux d'annulation est mauvaise. */
function TrendLine({trend}:{trend:Trend}){
  const up=trend.value>0;
  const good=trend.invert?trend.value<0:trend.value>0;
  const text=trend.count?`${up?'+':''}${trend.value}`:trend.points?`${up?'+':''}${(Math.round(trend.value*1000)/10).toLocaleString('fr-FR')} pt`:`${up?'+':''}${Math.round(trend.value*100)} %`;
  return <span className={`key-trend ${trend.value===0?'flat':good?'good':'bad'}`}>{trend.value!==0&&(up?<ArrowUp size={12}/>:<ArrowDown size={12}/>)}{text}<small>{trend.label}</small></span>;
}
