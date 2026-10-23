'use client';
import {useEffect,useMemo,useState} from 'react';
import Link from 'next/link';
import {Users,Store,Euro,UserPlus,AlertTriangle,ShoppingBag,Wallet,RefreshCw,Globe,Repeat,TrendingUp,Gift,CreditCard,Ban} from 'lucide-react';
import {Button} from '@/components/ui/button';
import {useAdmin} from '../../hooks/use-admin';
import {PageHeader,StatCard,StatusBadge,UserAvatar,ActivityTimeline,DateRangePicker,LoadingSkeleton} from '../../components/ui/shared';
import {ChartCard,SeriesChart,DonutChart,BarSeries} from '../../components/charts/charts';
import {LiveDrawer} from '../shared/live-drawer';
import {formatTotals} from '../shared/live-page';
import {describe} from '../../services/api/client';
import {billingAdmin,reporting,type AttentionReason,type BillingOverview,type Granularity,type PlatformDashboard,type ReportRow,type TenantActivity} from '../../services/api/admin-api';
import {subscriptionStatus,tenantStatus} from '../../services/api/mappers';
import {activityLabel} from '../../services/api/profile';
import {money,number,date,dateTime,isoDay} from '../../utils/format';
import {bucketStart,buckets,period,periodLabel} from '../../utils/period';

type Selection={domain:'restaurants'|'users'|'subscriptions';id:string};

const sum=(rows:ReportRow[],metric:string)=>rows.filter(r=>r.metric===metric).reduce((t,r)=>t+r.value,0);
const byCurrency=(rows:ReportRow[],metric:string):[string,number][]=>{const m=new Map<string,number>();rows.filter(r=>r.metric===metric&&r.currency).forEach(r=>m.set(r.currency,(m.get(r.currency)??0)+r.value));return [...m.entries()].sort((a,b)=>b[1]-a[1])};
const totals=(list:{currencyCode:string;amount:number}[])=>list.length?formatTotals(list.map(a=>[a.currencyCode,a.amount])):'—';
/** La devise principale en grand ; les autres devises passent dans la légende de la tuile. */
const main=(list:{currencyCode:string;amount:number}[])=>list.length?money(list[0].amount,list[0].currencyCode):'—';
const others=(list:{currencyCode:string;amount:number}[])=>list.length>1?` · ${list.slice(1).map(a=>money(a.amount,a.currencyCode)).join(' · ')}`:'';
const percent=(part:number,whole:number)=>whole?`${(Math.round(part/whole*1000)/10).toLocaleString('fr-FR')} %`:'—';
const reasonLabels:Record<AttentionReason,string>={TrialEnding:'Fin d’essai',ComplimentaryEnding:'Fin d’offre',CancellationEnding:'Résiliation',PastDue:'Impayé'};
const reasonTone:Record<AttentionReason,string>={TrialEnding:'En essai',ComplimentaryEnding:'Offert',CancellationEnding:'En attente',PastDue:'En retard'};
const months=['janv.','févr.','mars','avr.','mai','juin','juil.','août','sept.','oct.','nov.','déc.'];
const monthLabel=(month:string)=>{const [y,m]=month.split('-').map(Number);return `${months[m-1]} ${String(y).slice(2)}`};
const daysUntil=(at:string)=>Math.ceil((new Date(at).getTime()-Date.now())/86400000);

/** Évolution d'un mois sur l'autre, dans la devise principale ; null sans base de comparaison. */
function growth(current:{currencyCode:string;amount:number}[],previous:{currencyCode:string;amount:number}[]){
  const main=current[0]??previous[0];
  if(!main)return null;
  const before=previous.find(a=>a.currencyCode===main.currencyCode)?.amount??0;
  const now=current.find(a=>a.currencyCode===main.currencyCode)?.amount??0;
  if(!before)return null;
  const value=Math.round((now-before)/before*1000)/10;
  return {text:`${value>0?'+':''}${value.toLocaleString('fr-FR')} %`,negative:value<0};
}

export default function Dashboard(){
  const {state,raw,me,status}=useAdmin();
  const [choice,setChoice]=useState('30 jours');
  const [range,setRange]=useState({from:isoDay(new Date(Date.now()-29*86400000)),to:isoDay(new Date())});
  const [selection,setSelection]=useState<Selection|null>(null);
  const [data,setData]=useState<{dashboard:PlatformDashboard;series:ReportRow[];granularity:Granularity}|null>(null);
  const [billing,setBilling]=useState<BillingOverview|null>(null);
  const [activity,setActivity]=useState<TenantActivity|null>(null);
  const [error,setError]=useState<string>();
  const [billingError,setBillingError]=useState<string>();
  const [tick,setTick]=useState(0);
  const p=useMemo(()=>period(choice,range),[choice,range]);

  useEffect(()=>{
    let active=true;
    setError(undefined);
    Promise.all([reporting.dashboard(p.from,p.to),reporting.activity(p.from,p.to,p.granularity,['sales.revenue','sales.orders_completed','sales.online_revenue'])])
      .then(([dashboard,series])=>{if(active)setData({dashboard,series:series.rows,granularity:p.granularity})})
      .catch(e=>{if(active)setError(describe(e))});
    reporting.tenantActivity(p.from,p.to).then(a=>{if(active)setActivity(a)}).catch(()=>{if(active)setActivity(null)});
    return()=>{active=false};
  },[p,tick]);

  useEffect(()=>{
    let active=true;
    setBillingError(undefined);
    billingAdmin.overview().then(o=>{if(active)setBilling(o)}).catch(e=>{if(active)setBillingError(describe(e))});
    return()=>{active=false};
  },[tick]);

  const d=data?.dashboard;
  const b=billing;
  const count=<T extends string>(list:{status:T;count:number}[]|undefined,s:T)=>list?.find(x=>x.status===s)?.count??0;
  const revenue=d?byCurrency(d.activity,'sales.revenue'):[];
  const mainCurrency=revenue[0]?.[0]??byCurrency(data?.series??[],'sales.revenue')[0]?.[0];
  // Toutes les périodes de l'intervalle, à zéro quand rien n'a été vendu : la courbe ne relie pas des points éloignés.
  const filled=(metric:string,currency?:string)=>data?buckets({...p,granularity:data.granularity}).map(x=>({name:periodLabel(x,data.granularity),value:data.series.filter(r=>r.metric===metric&&(!currency||r.currency===currency)&&bucketStart(r.periodStart+'T00:00:00Z',data.granularity)===x).reduce((t,r)=>t+r.value,0)})):[];
  const series=filled('sales.revenue',mainCurrency);
  const onlineRevenue=d?byCurrency(d.activity,'sales.online_revenue'):[];
  const onlineOrders=d?sum(d.activity,'sales.online_orders_completed'):0;
  const onlineShare=mainCurrency&&revenue[0]?.[1]?(onlineRevenue.find(([c])=>c===mainCurrency)?.[1]??0)/revenue[0][1]:null;
  const tenantName=(id:string)=>raw.tenants.find(t=>t.tenantId===id)?.name??`Restaurant ${id.slice(0,8)}`;
  const recentTenants=[...state.restaurants].sort((x,y)=>String(y.date).localeCompare(String(x.date))).slice(0,5);

  // Encaissements d'abonnement des six derniers mois, dans la devise principale (mois sans paiement à zéro).
  const collectionCurrency=b?.collectedThisMonth[0]?.currencyCode??b?.monthlyRecurringRevenue[0]?.currencyCode??b?.collections.at(-1)?.currencyCode;
  const collectionSeries=useMemo(()=>{
    if(!b)return [];
    const now=new Date(b.atUtc);
    return Array.from({length:6},(_,i)=>{const m=new Date(Date.UTC(now.getUTCFullYear(),now.getUTCMonth()-5+i,1));const key=`${m.getUTCFullYear()}-${String(m.getUTCMonth()+1).padStart(2,'0')}`;return {name:monthLabel(key),value:b.collections.filter(c=>c.month===key&&c.currencyCode===collectionCurrency).reduce((t,c)=>t+c.amount,0)}});
  },[b,collectionCurrency]);
  const collectedGrowth=b?growth(b.collectedThisMonth,b.collectedLastMonth):null;
  const live=b?b.paying+b.trialing+b.pastDue+b.suspended+b.complimentary:0;

  const revenueStats:[string,string,typeof Users,string,{text:string;negative:boolean}|null][]=b?[
    ['Revenu récurrent (MRR)',main(b.monthlyRecurringRevenue),Repeat,`Plan et options${others(b.monthlyRecurringRevenue)}`,null],
    ['Encaissé ce mois-ci',main(b.collectedThisMonth),CreditCard,`Mois précédent : ${totals(b.collectedLastMonth)}${others(b.collectedThisMonth)}`,collectedGrowth],
    ['Revenu moyen',main(b.averageRevenuePerSubscription),TrendingUp,`Par abonnement et par mois${others(b.averageRevenuePerSubscription)}`,null],
    ['Abonnements payants',number(b.paying),Euro,`${number(b.complimentary)} offert(s) · ${number(b.trialing)} en essai`,null],
  ]:[];
  const healthStats:[string,string,typeof Users,string][]=b?[
    ['Conversion des essais',percent(b.trialsConverted,b.trialsStarted),TrendingUp,`${number(b.trialsConverted)} payant(s) sur ${number(b.trialsStarted)} essai(s), 90 jours`],
    ['Impayés',number(b.pastDue),AlertTriangle,`${number(b.suspended)} suspendu(s)`],
    ['Résiliations du mois',number(b.cancelledThisMonth),Ban,`${number(b.cancellationsScheduled)} programmée(s)`],
    ['Plans offerts',number(b.complimentary),Gift,`dont ${number(b.complimentaryUnlimited)} sans limite`],
  ]:[];
  const activityStats:[string,string,typeof Users,string][]=[
    ['Restaurants actifs',number(count(d?.tenantsByStatus,'Active')),Store,`${number(count(d?.tenantsByStatus,'Trial'))} en essai`],
    ['Nouveaux restaurants',number(d?.newTenants??0),UserPlus,`Sur la période (${choice.toLowerCase()})`],
    ['Commandes terminées',number(d?sum(d.activity,'sales.orders_completed'):0),ShoppingBag,'Tous restaurants'],
    ['Chiffre d’affaires',d?formatTotals(revenue):'—',Wallet,'Commandes terminées, par devise'],
  ];

  const open=(subscriptionId:string,tenantId:string)=>setSelection(state.subscriptions.some(s=>s.id===subscriptionId)?{domain:'subscriptions',id:subscriptionId}:{domain:'restaurants',id:tenantId});

  return <>
    <PageHeader title="Tableau de bord" subtitle={`Bonjour, voici l’état de la plateforme (${me.email}).`}>
      <DateRangePicker value={choice} onChange={v=>{if(v==='Personnalisé')setRange({from:p.from,to:p.to});setChoice(v)}} onRange={setRange} range={range}/>
      <Button variant="outline" onClick={()=>setTick(t=>t+1)}><RefreshCw size={16}/>Actualiser</Button>
    </PageHeader>

    {b&&b.attention.length>0&&<div className="attention-strip"><div><span className="attention-icon"><AlertTriangle size={21}/></span><div><strong>À traiter</strong><p>{number(b.attention.filter(a=>a.reason==='PastDue').length)} impayé(s) · {number(b.attention.filter(a=>a.reason!=='PastDue').length)} échéance(s) sous 7 jours · {number(b.failedCheckouts)} paiement(s) en ligne échoué(s) sur 30 jours</p></div></div><a href="#a-traiter">Voir la liste</a></div>}

    <h2 className="dashboard-section">Revenus récurrents</h2>
    {billingError&&<div className="notice warn">Indicateurs d’abonnement indisponibles : {billingError}</div>}
    {!b&&!billingError?<LoadingSkeleton/>:b&&<>
      <div className="stats-grid four">{revenueStats.map(([title,value,Icon,caption,trend])=><StatCard key={title} label={title} value={value} caption={trend?undefined:caption} trend={trend?.text} negative={trend?.negative} icon={Icon}/>)}</div>
      <div className="charts-grid">
        <ChartCard title="Encaissements d’abonnement" subtitle={`Six derniers mois${collectionCurrency?` · ${collectionCurrency}`:''}`}><BarSeries data={collectionSeries} label="Encaissé" format={v=>money(v,collectionCurrency??'EUR')}/></ChartCard>
        <ChartCard title="Abonnements par statut" subtitle="État actuel"><DonutChart data={[{name:'Payants',value:b.paying-b.pastDue},{name:'Offerts',value:b.complimentary},{name:subscriptionStatus.Trialing,value:b.trialing},{name:subscriptionStatus.PastDue,value:b.pastDue},{name:subscriptionStatus.Suspended,value:b.suspended}]} center={number(live)} caption="abonnements vivants"/></ChartCard>
      </div>
      <h2 className="dashboard-section">Santé des abonnements</h2>
      <div className="stats-grid four">{healthStats.map(([title,value,Icon,caption])=><StatCard key={title} label={title} value={value} caption={caption} icon={Icon}/>)}</div>

      <div className="two-columns" id="a-traiter">
        <section className="panel"><div className="panel-heading"><div><h2>À traiter</h2><p>Impayés et échéances des 7 prochains jours</p></div><Link href="/admin/subscriptions">Abonnements</Link></div>
          {b.attention.length?<div className="simple-table"><table><thead><tr><th>Restaurant</th><th>Motif</th><th>Échéance</th><th>Montant / mois</th></tr></thead><tbody>{b.attention.slice(0,8).map(a=>{const days=daysUntil(a.atUtc);return <tr key={`${a.subscriptionId}-${a.reason}`}>
            <td><button className="identity" onClick={()=>open(a.subscriptionId,a.tenantId)}><UserAvatar name={tenantName(a.tenantId)}/><span><strong>{tenantName(a.tenantId)}</strong><small>{a.planName}</small></span></button></td>
            <td><StatusBadge value={reasonTone[a.reason]}/> <small>{reasonLabels[a.reason]}</small></td>
            <td>{date(a.atUtc)}<br/><small>{a.reason==='PastDue'?(days>0?`suspension dans ${days} j`:'suspension imminente'):days<=1?'demain':`dans ${days} j`}</small></td>
            <td>{a.monthlyAmount===null?'—':money(a.monthlyAmount,a.currencyCode)}</td>
          </tr>})}</tbody></table></div>:<p className="muted">Rien à traiter : aucun impayé ni échéance dans les 7 jours.</p>}
        </section>
        <section className="panel"><div className="panel-heading"><div><h2>Paiements en ligne échoués</h2><p>Refusés ou expirés, 30 derniers jours ({number(b.failedCheckouts)})</p></div><Link href="/admin/billing">Facturation</Link></div>
          {b.recentFailedCheckouts.length?b.recentFailedCheckouts.map(c=><p key={`${c.tenantId}-${c.atUtc}`} className="entitlement-row"><span><strong>{tenantName(c.tenantId)}</strong><small> · {c.provider}</small></span><span>{money(c.amount,c.currencyCode)} · {c.status==='Expired'?'expiré':'refusé'} · {dateTime(c.atUtc)}</span></p>):<p className="muted">Aucun paiement en ligne échoué.</p>}
        </section>
      </div>
    </>}

    <h2 className="dashboard-section">Activité des restaurants</h2>
    {error&&<div className="notice warn">Indicateurs indisponibles : {error}</div>}
    {!d&&!error?<LoadingSkeleton/>:<>
      <div className="stats-grid four">{activityStats.map(([title,value,Icon,caption])=><StatCard key={title} label={title} value={value} caption={caption} icon={Icon}/>)}</div>
      <div className="charts-grid">
        <ChartCard title="Chiffre d’affaires des restaurants" subtitle={`${date(p.from)} → ${date(p.to)}${mainCurrency?` · ${mainCurrency}`:''}`}><SeriesChart data={series} label="Chiffre d’affaires" format={v=>money(v,mainCurrency??'EUR')}/></ChartCard>
        <ChartCard title="Restaurants par statut" subtitle="État actuel"><DonutChart data={(['Active','Trial','Suspended','Closed'] as const).map(s=>({name:tenantStatus[s],value:count(d?.tenantsByStatus,s)}))} center={number(d?.tenantsByStatus.reduce((t,x)=>t+x.count,0)??0)} caption="restaurants"/></ChartCard>
      </div>
      <section className="panel online-strip"><div className="panel-heading"><div><h2><Globe size={18}/> Vente en ligne</h2><p>Commandes terminées venues d’un site, d’une application ou d’une borne</p></div><Link href="/admin/online-sales">Détails</Link></div><div className="online-strip-stats"><div><strong>{number(onlineOrders)}</strong><span>commandes en ligne</span></div><div><strong>{onlineRevenue.length?formatTotals(onlineRevenue):money(0,mainCurrency??'EUR')}</strong><span>chiffre d’affaires en ligne</span></div><div><strong>{onlineShare===null?'—':`${(Math.round(onlineShare*1000)/10).toLocaleString('fr-FR')} %`}</strong><span>du chiffre d’affaires{mainCurrency?` (${mainCurrency})`:''}</span></div></div></section>
      <div className="two-columns">
        <section className="panel"><div className="panel-heading"><div><h2>Restaurants les plus actifs</h2><p>Commandes terminées sur la période</p></div><Link href="/admin/restaurants">Restaurants</Link></div>
          {activity?.mostActive.length?<div className="simple-table"><table><thead><tr><th>#</th><th>Restaurant</th><th>Commandes</th><th>Chiffre d’affaires</th></tr></thead><tbody>{activity.mostActive.map((t,i)=><tr key={t.tenantId}>
            <td><span className="rank">{i+1}</span></td>
            <td><button className="identity" onClick={()=>setSelection({domain:'restaurants',id:t.tenantId})}><UserAvatar name={t.name}/><span><strong>{t.name}</strong><small>{tenantStatus[t.status]}</small></span></button></td>
            <td>{number(t.orders)}</td><td>{t.currency?money(t.revenue,t.currency):'—'}</td>
          </tr>)}</tbody></table></div>:<p className="muted">{activity?'Aucune commande terminée sur la période.':'Classement indisponible.'}</p>}
        </section>
        <section className="panel"><div className="panel-heading"><div><h2>Restaurants inactifs</h2><p>Ouverts, sans commande terminée depuis 14 jours</p></div></div>
          {activity?.inactive.length?activity.inactive.slice(0,8).map(t=><button key={t.tenantId} className="user-list-row" onClick={()=>setSelection({domain:'restaurants',id:t.tenantId})}><UserAvatar name={t.name}/><span><strong>{t.name}</strong><small>{t.lastOrderDay?`Dernière commande le ${date(t.lastOrderDay)}`:'Aucune commande récente'}</small></span><StatusBadge value={tenantStatus[t.status]}/></button>):<p className="muted">{activity?'Tous les restaurants ouverts ont vendu ces 14 derniers jours.':'Liste indisponible.'}</p>}
        </section>
      </div>
    </>}

    {status!=='loading'&&<div className="two-columns">
      <section className="panel"><div className="panel-heading"><div><h2>Derniers restaurants</h2><p>Les établissements créés récemment</p></div><Link href="/admin/restaurants">Tout voir</Link></div><div className="simple-table"><table><thead><tr><th>Restaurant</th><th>Plan</th><th>Statut</th><th>Création</th></tr></thead><tbody>{recentTenants.map(r=><tr key={r.id}><td><button className="identity" onClick={()=>setSelection({domain:'restaurants',id:r.id})}><UserAvatar name={r.name}/><span><strong>{r.name}</strong><small>{String(r.subscription)}</small></span></button></td><td><span className="plan-tag">{String(r.plan)}</span></td><td><StatusBadge value={r.status}/></td><td>{date(r.date)}</td></tr>)}{!recentTenants.length&&<tr><td colSpan={4}>Aucun restaurant.</td></tr>}</tbody></table></div></section>
      <section className="panel"><div className="panel-heading"><div><h2>Activité récente</h2><p>Journal d’audit de la plateforme</p></div><Link href="/admin/audit-logs">Journal</Link></div><ActivityTimeline items={state['audit-logs'].slice(0,6).map(a=>({name:activityLabel(String(a.action)),detail:`${a.user} · ${a.tenant}`,date:dateTime(a.date)}))}/></section>
    </div>}
    {selection&&<LiveDrawer key={selection.id} {...selection} onClose={()=>setSelection(null)}/>}
  </>;
}
