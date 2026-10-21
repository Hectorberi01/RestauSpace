'use client';
import {useCallback,useEffect,useMemo,useState} from 'react';
import Link from 'next/link';
import {Bell,CheckCheck,CreditCard,Store,UserRound,RefreshCw,RotateCcw,Ban,Send} from 'lucide-react';
import {Button} from '@/components/ui/button';
import {touches,useAdmin} from '../../hooks/use-admin';
import {PageHeader,StatusBadge,EmptyState,LoadingSkeleton} from '../../components/ui/shared';
import {ConfirmDialog} from '../../components/feedback/dialogs';
import {describe} from '../../services/api/client';
import {deadLetters,type DeadLetterDto} from '../../services/api/admin-api';
import {platformAlerts,readAlerts,saveReadAlerts,type AlertCategory} from '../../services/alerts';
import {date,dateTime} from '../../utils/format';

const icons:Record<AlertCategory,typeof Bell>={Abonnement:CreditCard,Restaurant:Store,Compte:UserRound};
const channels:Record<string,string>={Email:'Email',Sms:'SMS',WhatsApp:'WhatsApp',Push:'Push',InApp:'In-app'};

function FailedSends(){
  const {run,raw}=useAdmin();
  const [rows,setRows]=useState<DeadLetterDto[]|null>(null);
  const [error,setError]=useState<string>();
  const load=useCallback(async()=>{try{setError(undefined);setRows(await deadLetters.list())}catch(e){setError(describe(e))}},[]);
  useEffect(()=>{void load()},[load]);
  const act=async(label:string,action:()=>Promise<unknown>)=>{if(await run(label,action,touches.none))await load()};
  const tenant=(id:string|null)=>id?raw.tenants.find(t=>t.tenantId===id)?.name??id.slice(0,8):'Plateforme';
  if(error)return <div className="notice warn">{error} <button className="text-link" onClick={()=>void load()}>Réessayer</button></div>;
  if(!rows)return <LoadingSkeleton/>;
  if(!rows.length)return <EmptyState title="Aucun envoi en échec" description="Toutes les notifications ont été remises ou abandonnées."/>;
  return <div className="table-scroll"><table><thead><tr><th>Notification</th><th>Canal</th><th>Restaurant</th><th>Tentatives</th><th>Dernière erreur</th><th>Créée le</th><th>Actions</th></tr></thead><tbody>{rows.map(d=><tr key={d.id}>
    <td><code>{d.type}</code><br/><small>{d.provider}</small></td>
    <td>{channels[d.channel]??d.channel}</td>
    <td>{tenant(d.tenantId)}</td>
    <td>{d.attemptCount}</td>
    <td>{d.lastFailureCode?<><code>{d.lastFailureCode}</code><br/><StatusBadge value={d.lastFailureKind==='Permanent'?'Échec':'En attente'}/></>:'—'}</td>
    <td>{dateTime(d.createdAtUtc)}</td>
    <td><div className="row-actions">
      <Button size="sm" variant="outline" onClick={()=>void act('Nouvel envoi programmé',()=>deadLetters.retry(d.id))}><RotateCcw size={14}/>Renvoyer</Button>
      <ConfirmDialog title="Abandonner cet envoi ?" description="La notification ne sera plus tentée. Elle reste dans l’historique avec le statut abandonné." onConfirm={()=>void act('Envoi abandonné',()=>deadLetters.abandon(d.id))}><Button size="sm" variant="ghost"><Ban size={14}/>Abandonner</Button></ConfirmDialog>
    </div></td></tr>)}</tbody></table></div>;
}

export default function Notifications(){
  const {raw,status,reload}=useAdmin();
  const [tab,setTab]=useState('Alertes');
  const [filter,setFilter]=useState('Toutes');
  const [category,setCategory]=useState('Toutes catégories');
  const [read,setRead]=useState<Set<string>>(()=>new Set());
  useEffect(()=>{setRead(readAlerts())},[]);
  const alerts=useMemo(()=>platformAlerts(raw),[raw]);
  const mark=(ids:string[])=>setRead(previous=>{const next=new Set(previous);ids.forEach(id=>next.add(id));saveReadAlerts(next);return next});
  const unread=alerts.filter(a=>!read.has(a.id));
  const rows=alerts.filter(a=>(filter==='Toutes'||filter==='Non lues'&&!read.has(a.id)||filter==='Importantes'&&a.priority==='Haute')&&(category==='Toutes catégories'||a.category===category));
  return <>
    <PageHeader title="Notifications" subtitle="Ce qui demande une action sur la plateforme, calculé à partir des données de l’API.">
      <Button variant="outline" onClick={()=>void reload()}><RefreshCw size={16}/>Actualiser</Button>
      {tab==='Alertes'&&<Button variant="outline" disabled={!unread.length} onClick={()=>mark(alerts.map(a=>a.id))}><CheckCheck size={16}/>Tout marquer comme lu</Button>}
    </PageHeader>
    <div className="table-tabs page-tabs">{[['Alertes',Bell,unread.length],['Envois en échec',Send,0]].map(([t,Icon,n])=>{const I=Icon as typeof Bell;return <button key={String(t)} className={tab===t?'active':''} onClick={()=>setTab(String(t))}><I size={15}/> {String(t)}{Number(n)>0&&<span>{String(n)}</span>}</button>})}</div>
    <div className="panel notification-panel">
      {tab==='Envois en échec'?<FailedSends/>:status==='loading'?<LoadingSkeleton/>:<>
        <div className="notification-toolbar"><div className="table-tabs">{['Toutes','Non lues','Importantes'].map(t=><button key={t} className={t===filter?'active':''} onClick={()=>setFilter(t)}>{t}{t==='Non lues'&&<span>{unread.length}</span>}</button>)}</div><select value={category} onChange={e=>setCategory(e.target.value)} aria-label="Catégorie">{['Toutes catégories','Abonnement','Restaurant','Compte'].map(c=><option key={c}>{c}</option>)}</select></div>
        {rows.length?rows.map(a=>{const Icon=icons[a.category];const isRead=read.has(a.id);return <article className={`notification-row ${!isRead?'unread':''}`} key={a.id}>
          <span className="notification-type-icon"><Icon size={21}/></span>
          <Link href={a.href} className="notification-text alert-link" onClick={()=>mark([a.id])}><div><h3>{a.title}</h3>{!isRead&&<span className="unread-dot"/>}</div><p>{a.description}</p><small>{date(a.date)} · {a.category}</small></Link>
          <div className="notification-actions"><StatusBadge value={a.priority==='Haute'?'Urgente':a.priority}/>{!isRead&&<Button variant="ghost" size="icon-sm" aria-label={'Marquer comme lu '+a.title} onClick={()=>mark([a.id])}><CheckCheck size={17}/></Button>}</div>
        </article>}):<EmptyState title="Tout est à jour" description="Aucune alerte pour ces critères."/>}
        <p className="table-note">Alertes recalculées à chaque chargement. L’état « lu » est conservé dans ce navigateur.</p>
      </>}
    </div>
  </>;
}
