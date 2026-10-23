'use client';
import {CollectionOverview} from './collection';
import {useEffect,useState} from 'react';
import {useSearchParams} from 'next/navigation';
import {Plus,Users,Store,CreditCard,Clock,UserCheck,UserX,Euro,TriangleAlert,MoreHorizontal,Eye,RefreshCw,Hourglass,Receipt,Wallet,Pause} from 'lucide-react';
import {toast} from 'sonner';
import {Button} from '@/components/ui/button';
import {DropdownMenu,DropdownMenuContent,DropdownMenuItem,DropdownMenuTrigger} from '@/components/ui/dropdown-menu';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription,DialogFooter} from '@/components/ui/dialog';
import {PageHeader,StatCard,StatusBadge,UserAvatar,LoadingSkeleton,MiniStat,type Tone} from '../../components/ui/shared';
import {DataTable,type TableColumn} from '../../components/tables/data-table';
import {FormDialog} from '../../components/feedback/dialogs';
import {AUDIT_LIMIT,touches,useAdmin,type LiveDomain} from '../../hooks/use-admin';
import {StartSubscriptionDialog} from './start-subscription';
import {navigation,summaries} from '../../constants/navigation';
import {describe} from '../../services/api/client';
import {shortId} from '../../services/api/mappers';
import {money,number,date,dateTime} from '../../utils/format';
import {configs} from './config';
import {LiveDrawer} from './live-drawer';
import {runAll,statusAction,subscriptions,tenants,users} from './live-actions';
import type {RecordItem} from '../../types';
import type {ComponentType} from 'react';

type PageDomain=Exclude<LiveDomain,'plans'>;
type Stat=[string,string,ComponentType<{size?:number}>,string?];

export function totalsByCurrency(rows:RecordItem[],key:string,filter:(r:RecordItem)=>boolean=()=>true){
  const totals=new Map<string,number>();
  rows.filter(filter).forEach(r=>{const currency=String(r.currency||'');const value=Number(r[key])||0;if(currency&&value)totals.set(currency,(totals.get(currency)??0)+value)});
  return [...totals.entries()].sort((a,b)=>b[1]-a[1]);
}

export function formatTotals(totals:[string,number][]){
  return totals.length?totals.map(([currency,amount])=>money(amount,currency)).join(' · '):money(0);
}

export function LivePage({domain}:{domain:PageDomain}){
  const {state,raw,status,run,reload,truncated}=useAdmin();
  const [selected,setSelected]=useState<string|null>(null);
  const [pending,setPending]=useState<string[]|null>(null);
  const query=useSearchParams();
  useEffect(()=>{const id=query.get('record');if(id)setSelected(id)},[query]);
  const rows=state[domain];
  const config=configs[domain]!;
  const label=navigation.find(n=>n.key===domain)!.label;
  const count=(...values:string[])=>rows.filter(r=>values.includes(r.status)).length;

  const monthStart=new Date(new Date().getFullYear(),new Date().getMonth(),1).getTime();
  const recent=(...values:string[])=>rows.filter(r=>new Date(String(r.date)).getTime()>=monthStart&&(!values.length||values.includes(r.status))).length;
  const minis:[string,number,ComponentType<{size?:number}>,Tone,number][]=domain==='users'?[
    ['Comptes',rows.length,Users,'purple',recent()],
    ['Actifs',count('Actif'),UserCheck,'blue',recent('Actif')],
    ['En attente de vérification',count('En attente'),Clock,'amber',recent('En attente')],
    ['Suspendus ou désactivés',count('Suspendu','Désactivé'),UserX,'red',recent('Suspendu','Désactivé')],
  ]:domain==='restaurants'?[
    ['Restaurants',rows.length,Store,'purple',recent()],
    ['Actifs',count('Actif'),UserCheck,'blue',recent('Actif')],
    ['En essai',count('En essai'),Hourglass,'amber',recent('En essai')],
    ['Suspendus',count('Suspendu'),Pause,'red',recent('Suspendu')],
  ]:[];
  const stats:Stat[]=domain==='users'?[
    ['Comptes',number(rows.length),Users],
    ['Actifs',number(count('Actif')),UserCheck],
    ['En attente de vérification',number(count('En attente')),Clock],
    ['Suspendus ou désactivés',number(count('Suspendu','Désactivé')),UserX],
  ]:domain==='restaurants'?[
    ['Restaurants',number(rows.length),Store],
    ['Actifs',number(count('Actif')),UserCheck],
    ['En essai',number(count('En essai')),Hourglass],
    ['Suspendus',number(count('Suspendu')),UserX],
  ]:domain==='subscriptions'?[
    ['MRR',formatTotals(totalsByCurrency(rows,'amount',r=>r.apiStatus==='Active'||r.apiStatus==='PastDue')),Euro,'Actifs et en retard, par devise'],
    ['Actifs',number(count('Actif')),CreditCard],
    ['En essai',number(count('En essai')),Hourglass],
    ['En retard de paiement',number(count('En retard')),TriangleAlert],
  ]:domain==='billing'?[
    ['Encaissé sur 30 jours',formatTotals(totalsByCurrency(rows,'amount',r=>Date.now()-new Date(String(r.date)).getTime()<30*86400000)),Wallet,'Paiements d’abonnement enregistrés'],
    ['Encaissé au total',formatTotals(totalsByCurrency(rows,'amount')),Euro,'Depuis le premier paiement'],
    ['Paiements',number(rows.length),Receipt],
    ['Restaurants payants',number(new Set(rows.map(r=>String(r.tenantId))).size),Store],
  ]:[];

  const applyStatus=async(ids:string[],target:'Actif'|'Suspendu')=>{
    const actions=ids.map(id=>rows.find(r=>r.id===id)).filter((r):r is RecordItem=>!!r).map(r=>statusAction(domain,r,target)).filter((a):a is ()=>Promise<unknown>=>!!a);
    if(!actions.length){toast.info('Aucun élément sélectionné ne peut prendre ce statut.');return}
    const {done,failed}=await runAll(actions);
    if(done)toast.success(`${done} élément(s) mis à jour`);
    if(failed.length)toast.error(`${failed.length} échec(s) : ${describe(failed[0])}`);
    await reload(domain==='users'?touches.users:touches.restaurants);
  };
  const change=(ids:string[],target:string)=>{if(target==='Suspendu'){setPending(ids);return}void applyStatus(ids,target as 'Actif')};

  const identity=(r:RecordItem)=>{
    const detail=domain==='users'?(r.canStartSession?'Peut se connecter':'Connexion impossible'):domain==='restaurants'?`${r.currency||'—'} · ${shortId(r.id)}`:domain==='billing'?`Abonnement ${shortId(String(r.subscriptionId))}`:`Restaurant ${shortId(String(r.tenantId))}`;
    return <div className="identity"><UserAvatar name={r.name}/><div><strong>{r.name}</strong><small>{detail}</small></div></div>;
  };
  const amount=(key:string)=>(r:RecordItem)=>(Number(r[key])||r.currency)?money(Number(r[key])||0,String(r.currency||'EUR')):'—';
  const columns:TableColumn[]=config.columns.map(c=>
    c.key==='name'&&domain!=='audit-logs'?{...c,render:identity}:
    c.key==='amount'||c.key==='mrr'?{...c,render:amount(c.key)}:
    c.key==='renewal'?{...c,render:(r:RecordItem)=><span className={r.renewal?'text-green':'text-muted'}>{r.renewal?'Automatique':'Fin de période'}</span>}:
    c.key==='subscription'||c.key==='verified'?{...c,render:(r:RecordItem)=><StatusBadge value={r[c.key]}/>}:
    c.key==='date'&&(domain==='audit-logs'||domain==='billing')?{...c,render:(r:RecordItem)=>dateTime(r.date)}:
    c.key==='periodStart'?{...c,render:(r:RecordItem)=>`${date(r.periodStart)} → ${date(r.end)}`}:
    c.key==='name'?{...c,render:(r:RecordItem)=><span title={String(r.action)}>{r.name}</span>}:c);

  // Création puis rattachement du propriétaire ; le restaurant reste créé si le second appel échoue.
  const createRestaurant=async(v:Record<string,string|number>)=>{
    let created=false;
    const ok=await run('Restaurant créé',async()=>{
      const {id}=await tenants.create({name:String(v.name).trim(),defaultCurrencyCode:String(v.defaultCurrencyCode).trim().toUpperCase(),timeZoneId:String(v.timeZoneId).trim(),pricesIncludeTax:v.pricesIncludeTax==='TTC'});
      created=true;
      try{await tenants.addOwner(id,String(v.ownerEmail).trim())}
      catch(e){throw new Error(`Restaurant créé, mais sans propriétaire : ${describe(e)}`)}
    },['restaurants','users','audit-logs']);
    if(!ok&&created)await reload(touches.restaurants);
  };

  const activePlans=raw.plans.filter(p=>p.status==='Active'&&p.kind!=='AddOn');
  const freeTenants=raw.tenants.filter(t=>t.status!=='Closed'&&(!t.subscriptionStatus||t.subscriptionStatus==='Cancelled'));

  const create=domain==='users'?<FormDialog title="Inviter un utilisateur" fields={config.fields} onSave={v=>void run('Invitation envoyée',()=>users.invite(String(v.email).trim()),touches.users)}><Button><Plus size={16}/>Inviter un utilisateur</Button></FormDialog>
    :domain==='restaurants'?<FormDialog title="Créer un restaurant" fields={config.fields} initial={{defaultCurrencyCode:'EUR',timeZoneId:'Europe/Paris',pricesIncludeTax:'TTC'}} onSave={v=>void createRestaurant(v)}><Button><Plus size={16}/>Ajouter un restaurant</Button></FormDialog>
    :domain==='subscriptions'&&freeTenants.length&&activePlans.length?<StartSubscriptionDialog candidates={freeTenants}><Button><Plus size={16}/>Démarrer un abonnement</Button></StartSubscriptionDialog>
    :null;

  const rowMenu=(r:RecordItem)=>{
    const toggle=domain==='users'||domain==='restaurants'?(r.status==='Suspendu'||r.apiStatus==='Trial'?statusAction(domain,r,'Actif'):statusAction(domain,r,'Suspendu')):null;
    return <DropdownMenu><DropdownMenuTrigger asChild><Button variant="ghost" size="icon-sm" aria-label={`Actions pour ${r.name}`}><MoreHorizontal size={18}/></Button></DropdownMenuTrigger><DropdownMenuContent align="end"><DropdownMenuItem onClick={()=>setSelected(r.id)}><Eye size={15}/>Voir les détails</DropdownMenuItem>{toggle&&<DropdownMenuItem onClick={()=>r.status==='Suspendu'||r.apiStatus==='Trial'?void run('Statut mis à jour',toggle,domain==='users'?touches.users:touches.restaurants):setPending([r.id])}>{r.apiStatus==='Trial'?'Activer':r.status==='Suspendu'?'Réactiver':'Suspendre'}</DropdownMenuItem>}</DropdownMenuContent></DropdownMenu>;
  };

  return <>
    <PageHeader title={label} subtitle={summaries[domain]}>
      <Button variant="outline" size="icon" title="Actualiser" aria-label="Actualiser" onClick={()=>void reload(domain==='subscriptions'?touches.subscriptions:domain==='billing'?['billing','restaurants']:[domain])}><RefreshCw size={16}/></Button>
      {create}
      {domain==='subscriptions'&&!create&&activePlans.length===0&&<Button variant="outline" disabled title="Créez d’abord un plan actif">Aucun plan actif</Button>}
    </PageHeader>
    {status==='loading'?<LoadingSkeleton/>:<>
      {minis.length>0?<div className="mini-stats">{minis.map(([title,value,Icon,tone,delta])=><MiniStat key={title} label={title} value={number(value)} icon={Icon} tone={tone} delta={delta}/>)}</div>:stats.length>0&&<div className="stats-grid four">{stats.map(([title,value,Icon,caption])=><StatCard key={title} label={title} value={value} caption={caption} icon={Icon}/>)}</div>}
      {domain==='billing'&&<CollectionOverview/>}
      <DataTable key={domain} rows={rows} columns={columns} tabs={config.tabs} filters={config.filters} onOpen={r=>setSelected(r.id)} filename={domain} bulk={domain==='users'||domain==='restaurants'?change:undefined} onAction={domain==='audit-logs'||domain==='billing'?undefined:rowMenu}/>
      {truncated[domain]&&<div className="notice warn" role="status">{domain==='audit-logs'?`Seules les ${AUDIT_LIMIT} entrées les plus récentes sont affichées : le journal en contient davantage.`:`Liste incomplète : seuls les ${number(rows.length)} premiers éléments sont chargés. Les compteurs et alertes de cet écran sont partiels.`}</div>}
      <p className="table-note">{number(rows.length)} élément(s) chargé(s) depuis l’API{domain==='audit-logs'?` (${AUDIT_LIMIT} entrées les plus récentes au plus)`:domain==='billing'?' ; un paiement s’enregistre depuis l’écran Abonnements':''}.</p>
    </>}
    {selected&&<LiveDrawer key={`${domain}-${selected}`} domain={domain} id={selected} onClose={()=>setSelected(null)}/>}
    <Dialog open={!!pending} onOpenChange={v=>!v&&setPending(null)}><DialogContent><DialogHeader><DialogTitle>Suspendre {pending?.length} élément(s) ?</DialogTitle><DialogDescription>{domain==='users'?'Les comptes sélectionnés ne pourront plus se connecter jusqu’à leur réactivation.':'Les restaurants sélectionnés ne pourront plus enregistrer d’opérations jusqu’à leur réactivation (motif : autre).'}</DialogDescription></DialogHeader><DialogFooter><Button variant="outline" onClick={()=>setPending(null)}>Annuler</Button><Button variant="destructive" onClick={()=>{const ids=pending??[];setPending(null);void applyStatus(ids,'Suspendu')}}>Confirmer la suspension</Button></DialogFooter></DialogContent></Dialog>
  </>;
}
