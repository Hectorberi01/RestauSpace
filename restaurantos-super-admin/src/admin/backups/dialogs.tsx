'use client';
import {useState,type FormEvent,type ReactNode} from 'react';
import {Download,RotateCcw} from 'lucide-react';
import {toast} from 'sonner';
import {Button} from '@/components/ui/button';
import {Dialog,DialogContent,DialogDescription,DialogFooter,DialogHeader,DialogTitle} from '@/components/ui/dialog';
import {describe} from '../../services/api/client';
import {backups,type BackupAgentDto,type BackupDto,type BackupSettingsDto} from '../../services/api/admin-api';
import {dateTime} from '../../utils/format';
import {bytes} from './labels';

const digits=(v:string)=>v.replace(/\D/g,'').slice(0,6);

function CodeField({value,onChange,autoFocus=false}:{value:string;onChange:(v:string)=>void;autoFocus?:boolean}){
  return <label>Code de validation en deux étapes<input autoFocus={autoFocus} inputMode="numeric" autoComplete="one-time-code" placeholder="123456" value={value} onChange={e=>onChange(digits(e.target.value))} aria-label="Code à 6 chiffres"/></label>;
}

function Shell({title,description,onClose,onSubmit,children,footer}:{title:string;description:ReactNode;onClose:()=>void;onSubmit:(e:FormEvent)=>void;children:ReactNode;footer:ReactNode}){
  return <Dialog open onOpenChange={v=>!v&&onClose()}><DialogContent className="form-dialog backup-dialog">
    <DialogHeader><DialogTitle>{title}</DialogTitle><DialogDescription asChild><div>{description}</div></DialogDescription></DialogHeader>
    <form onSubmit={onSubmit}>{children}<DialogFooter>{footer}</DialogFooter></form>
  </DialogContent></Dialog>;
}

/** Ouvre le lien pré-signé sans le garder ni l'afficher. */
function open(url:string){
  const link=document.createElement('a');
  link.href=url;link.target='_blank';link.rel='noopener noreferrer';
  link.click();
}

export function DownloadDialog({backup,onClose}:{backup:BackupDto;onClose:()=>void}){
  const [code,setCode]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState<string>();
  const submit=async(e:FormEvent)=>{
    e.preventDefault();setBusy(true);setError(undefined);
    try{
      const {url}=await backups.downloadLink(backup.id,code);
      open(url);
      toast.success('Téléchargement lancé.');
      onClose();
    }catch(err){setError(describe(err));setCode('')}finally{setBusy(false)}
  };
  return <Shell title="Télécharger la sauvegarde" onClose={onClose} onSubmit={e=>void submit(e)}
    description={<>Sauvegarde du {dateTime(backup.createdAtUtc)} · {bytes(backup.sizeBytes)}</>}
    footer={<><Button type="button" variant="outline" onClick={onClose}>Annuler</Button><Button type="submit" disabled={busy||code.length!==6}><Download size={15}/>{busy?'Vérification…':'Télécharger'}</Button></>}>
    <p className="notice compact-notice">Le fichier est chiffré avec age : il faut la clé privée de sauvegarde pour le lire.</p>
    <div className="form-fields single"><CodeField autoFocus value={code} onChange={setCode}/></div>
    {error&&<p className="field-error" role="alert">{error}</p>}
  </Shell>;
}

export function RestoreDialog({backup,settings,agent,onClose,onDone}:{backup:BackupDto;settings:BackupSettingsDto|null;agent:BackupAgentDto|null;onClose:()=>void;onDone:()=>void}){
  const database=settings?.databaseName??backup.databaseName;
  const delay=settings?.restoreDelayMinutes??5;
  const [typed,setTyped]=useState(''),[code,setCode]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState<string>();
  const disabled=agent?.restoreEnabled===false;
  const ready=typed===database&&code.length===6&&!busy&&!disabled;
  const submit=async(e:FormEvent)=>{
    e.preventDefault();if(!ready)return;setBusy(true);setError(undefined);
    try{
      await backups.restore(backup.id,code,typed);
      toast.success(`Restauration programmée dans ${delay} minutes.`);
      onDone();onClose();
    }catch(err){setError(describe(err));setCode('')}finally{setBusy(false)}
  };
  return <Shell title="Restaurer cette sauvegarde ?" onClose={onClose} onSubmit={e=>void submit(e)}
    description={<>Sauvegarde du {dateTime(backup.createdAtUtc)} · {bytes(backup.sizeBytes)}</>}
    footer={<><Button type="button" variant="outline" onClick={onClose}>Annuler</Button><Button type="submit" variant="destructive" disabled={!ready}><RotateCcw size={15}/>{busy?'Envoi…':'Programmer la restauration'}</Button></>}>
    <ul className="backup-effects">
      <li>La base <strong>{database}</strong> est remplacée par le contenu de cette sauvegarde. L’ancienne base est gardée sous un autre nom ({database}_before_…).</li>
      <li>La restauration démarre dans <strong>{delay} minutes</strong> : d’ici là, vous pouvez l’annuler depuis la liste des restaurations.</li>
      <li>Ensuite, relancez le déploiement <code>./deploy.sh &lt;version&gt;</code> pour appliquer les migrations et les droits.</li>
    </ul>
    {disabled&&<p className="notice warn compact-notice">La restauration est désactivée sur le serveur de base (RESTORE_ENABLED) : la demande échouerait.</p>}
    {!disabled&&agent&&!agent.online&&<p className="notice warn compact-notice">L’agent est hors ligne : la restauration attendra son retour.</p>}
    <div className="form-fields single">
      <label className="confirm-typed">Pour confirmer, saisissez le nom de la base <strong>{database}</strong><input value={typed} onChange={e=>setTyped(e.target.value)} autoComplete="off" spellCheck={false} aria-label="Nom de la base"/></label>
      <CodeField value={code} onChange={setCode}/>
    </div>
    {error&&<p className="field-error" role="alert">{error}</p>}
  </Shell>;
}
