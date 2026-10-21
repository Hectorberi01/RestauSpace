'use client';
import {useCallback,useEffect,useMemo,useState} from 'react';
import {Inbox,Mail,MessageCircle,Phone,RefreshCw,Trash2} from 'lucide-react';
import {Button} from '@/components/ui/button';
import {Dialog,DialogContent,DialogDescription,DialogFooter,DialogHeader,DialogTitle} from '@/components/ui/dialog';
import {touches,useAdmin} from '../../hooks/use-admin';
import {PageHeader,StatCard,EmptyState,LoadingSkeleton} from '../../components/ui/shared';
import {ConfirmDialog} from '../../components/feedback/dialogs';
import {describe} from '../../services/api/client';
import {demoRequests,type DemoRequestDto,type DemoRequestStatus} from '../../services/api/admin-api';
import {dateTime,number} from '../../utils/format';

const labels:Record<DemoRequestStatus,string>={New:'Nouvelle',Contacted:'Contactée',Converted:'Convertie',Declined:'Sans suite'};
const tone:Record<DemoRequestStatus,string>={New:'amber',Contacted:'purple',Converted:'green',Declined:'gray'};
const tabs:[string,DemoRequestStatus|null][]=[['Nouvelles','New'],['Contactées','Contacted'],['Converties','Converted'],['Sans suite','Declined'],['Toutes',null]];

function Detail({request,onClose,onChanged}:{request:DemoRequestDto;onClose:()=>void;onChanged:()=>Promise<void>}){
  const {run}=useAdmin();
  const [status,setStatus]=useState<DemoRequestStatus>(request.status);
  const [note,setNote]=useState(request.note??'');
  const save=async()=>{if(await run('Demande mise à jour',()=>demoRequests.update(request.id,status,note.trim()||null),touches.none)){await onChanged();onClose()}};
  const remove=async()=>{if(await run('Demande supprimée',()=>demoRequests.remove(request.id),touches.none)){await onChanged();onClose()}};
  const whatsapp=request.phone?`https://wa.me/${request.phone.replace(/[^0-9]/g,'')}`:'';
  return <Dialog open onOpenChange={v=>!v&&onClose()}><DialogContent className="max-w-xl">
    <DialogHeader><DialogTitle>{request.restaurantName}</DialogTitle><DialogDescription>Demande de {request.contactName} · {request.country} · reçue le {dateTime(request.submittedAtUtc)}</DialogDescription></DialogHeader>
    <div className="grid gap-4 text-sm">
      <div className="flex flex-wrap gap-2">
        {request.email&&<Button asChild variant="outline" size="sm"><a href={`mailto:${request.email}?subject=${encodeURIComponent('Votre démonstration RestauSpace')}`}><Mail size={14}/>{request.email}</a></Button>}
        {request.phone&&<Button asChild variant="outline" size="sm"><a href={`tel:${request.phone}`}><Phone size={14}/>{request.phone}</a></Button>}
        {whatsapp&&<Button asChild variant="outline" size="sm"><a href={whatsapp} target="_blank" rel="noopener noreferrer"><MessageCircle size={14}/>WhatsApp</a></Button>}
      </div>
      <div><strong>Plan qui l’intéresse :</strong> {request.planInterest||'non précisé'}</div>
      {request.message&&<div><strong>Message</strong><p className="mt-1 whitespace-pre-line rounded-lg bg-muted p-3">{request.message}</p></div>}
      <label className="grid gap-1.5"><strong>Suivi</strong><select value={status} onChange={e=>setStatus(e.target.value as DemoRequestStatus)} className="h-9 rounded-md border px-2">{(Object.keys(labels) as DemoRequestStatus[]).map(s=><option key={s} value={s}>{labels[s]}</option>)}</select></label>
      <label className="grid gap-1.5"><strong>Note interne</strong><textarea value={note} maxLength={1000} rows={3} onChange={e=>setNote(e.target.value)} className="rounded-md border p-2" placeholder="Démo prévue jeudi 15 h…"/></label>
    </div>
    <DialogFooter className="sm:justify-between">
      <ConfirmDialog title="Supprimer cette demande ?" description="Les coordonnées du prospect sont effacées définitivement (par exemple à sa demande)." onConfirm={()=>void remove()}><Button variant="ghost" className="text-red-600"><Trash2 size={15}/>Supprimer</Button></ConfirmDialog>
      <div className="flex gap-2"><Button variant="outline" onClick={onClose}>Fermer</Button><Button onClick={()=>void save()}>Enregistrer</Button></div>
    </DialogFooter>
  </DialogContent></Dialog>;
}

export default function DemoRequests(){
  const [rows,setRows]=useState<DemoRequestDto[]|null>(null);
  const [error,setError]=useState<string>();
  const [tab,setTab]=useState('Nouvelles');
  const [selected,setSelected]=useState<string|null>(null);
  const load=useCallback(async()=>{try{setError(undefined);setRows(await demoRequests.list())}catch(e){setError(describe(e))}},[]);
  useEffect(()=>{void load()},[load]);
  const wanted=tabs.find(([t])=>t===tab)?.[1]??null;
  const visible=useMemo(()=>(rows??[]).filter(r=>!wanted||r.status===wanted),[rows,wanted]);
  const count=(s:DemoRequestStatus)=>(rows??[]).filter(r=>r.status===s).length;
  const current=rows?.find(r=>r.id===selected);
  const recent=(rows??[]).filter(r=>Date.now()-new Date(r.submittedAtUtc).getTime()<30*86400000).length;
  const converted=count('Converted'),closed=converted+count('Declined');

  return <>
    <PageHeader title="Demandes de démo" subtitle="Les demandes envoyées depuis le formulaire du site vitrine.">
      <Button variant="outline" onClick={()=>void load()}><RefreshCw size={16}/>Actualiser</Button>
    </PageHeader>
    {error?<div className="notice warn">{error} <button className="text-link" onClick={()=>void load()}>Réessayer</button></div>:!rows?<LoadingSkeleton/>:<>
      <div className="stats-grid four">
        <StatCard label="À traiter" value={number(count('New'))} icon={Inbox} caption="Nouvelles demandes"/>
        <StatCard label="En cours" value={number(count('Contacted'))} icon={Phone} caption="Prospects contactés"/>
        <StatCard label="Reçues sur 30 jours" value={number(recent)} icon={Mail}/>
        <StatCard label="Taux de conversion" value={closed?`${Math.round(converted/closed*100)} %`:'—'} icon={MessageCircle} caption="Sur les demandes clôturées"/>
      </div>
      <div className="table-tabs page-tabs">{tabs.map(([t,s])=><button key={t} className={tab===t?'active':''} onClick={()=>setTab(t)}>{t}<span>{s?count(s):rows.length}</span></button>)}</div>
      <div className="panel">
        {!visible.length?<EmptyState title="Aucune demande" description={tab==='Nouvelles'?'Les nouvelles demandes du site apparaîtront ici.':'Aucune demande dans cette catégorie.'}/>:
        <div className="table-scroll"><table><thead><tr><th>Restaurant</th><th>Contact</th><th>Pays</th><th>Plan</th><th>Statut</th><th>Reçue le</th></tr></thead><tbody>{visible.map(r=><tr key={r.id}>
          <td><button className="cell-open" onClick={()=>setSelected(r.id)}><strong>{r.restaurantName}</strong></button></td>
          <td>{r.contactName}<br/><small>{r.email??r.phone}</small></td>
          <td>{r.country}</td>
          <td>{r.planInterest??'—'}</td>
          <td><span className={`status ${tone[r.status]}`}>{labels[r.status]}</span></td>
          <td>{dateTime(r.submittedAtUtc)}</td>
        </tr>)}</tbody></table></div>}
      </div>
    </>}
    {current&&<Detail key={current.id} request={current} onClose={()=>setSelected(null)} onChanged={load}/>}
  </>;
}
