'use client';
import {useCallback,useEffect,useRef,useState} from 'react';
import {ChevronLeft,ChevronRight,DatabaseBackup,Download,Play,RefreshCw,RotateCcw,Server,Trash2,X} from 'lucide-react';
import {Button} from '@/components/ui/button';
import {touches,useAdmin} from '../../hooks/use-admin';
import {PageHeader,EmptyState,LoadingSkeleton} from '../../components/ui/shared';
import {ConfirmDialog} from '../../components/feedback/dialogs';
import {describe,type Page} from '../../services/api/client';
import {backups,type BackupAgentDto,type BackupDto,type BackupSettingsDto,type RestoreDto} from '../../services/api/admin-api';
import {date,dateTime} from '../../utils/format';
import {activeBackup,activeRestore,ago,backupFailures,backupStatus,busyBackup,bytes,duration,restoreFailures,restoreStatus,span,triggerLabels} from './labels';
import {DownloadDialog,RestoreDialog} from './dialogs';

const PAGE_SIZE=20;
const RESTORE_PAGE_SIZE=10;
const POLL_MS=5000;

interface Data {
  agent:BackupAgentDto|null;
  settings:BackupSettingsDto|null;
  list:Page<BackupDto>|null;
  restores:Page<RestoreDto>|null;
  /** Une sauvegarde ou une restauration active (lues en tête de liste, quelle que soit la page affichée). */
  backupActive:boolean;
  restoreActive:boolean;
  errors:Partial<Record<'agent'|'settings'|'list'|'restores',string>>;
}

const settled=<T,>(r:PromiseSettledResult<T>)=>r.status==='fulfilled'?r.value:null;
const failure=(r:PromiseSettledResult<unknown>)=>r.status==='rejected'?describe(r.reason):undefined;

/** Heure courante rafraîchie chaque seconde (comptes à rebours, « vu il y a »). */
function useNow(){
  const [now,setNow]=useState(()=>Date.now());
  useEffect(()=>{const t=window.setInterval(()=>setNow(Date.now()),1000);return()=>window.clearInterval(t)},[]);
  return now;
}

function Status({map,value}:{map:Record<string,[string,string]>;value:string}){
  const [label,tone]=map[value]??[value,'gray'];
  return <span className={`status ${tone}`}>{label}</span>;
}

function Pager({page,hasMore,onPage}:{page:number;hasMore:boolean;onPage:(p:number)=>void}){
  if(page===1&&!hasMore)return null;
  return <div className="pagination"><span>Page {page}</span><div>
    <Button variant="outline" size="icon-sm" aria-label="Page précédente" disabled={page===1} onClick={()=>onPage(page-1)}><ChevronLeft size={15}/></Button>
    <Button variant="outline" size="icon-sm" aria-label="Page suivante" disabled={!hasMore} onClick={()=>onPage(page+1)}><ChevronRight size={15}/></Button>
  </div></div>;
}

function AgentPanel({agent,settings,errors,now}:{agent:BackupAgentDto|null;settings:BackupSettingsDto|null;errors:Data['errors'];now:number}){
  const online=agent?.online??false;
  return <section className="panel backup-agent">
    <div className="panel-heading"><div><h2><Server size={18}/> Agent du serveur de base</h2><p>Il exécute les sauvegardes et les restaurations demandées ici</p></div>
      {agent&&<span className={`status ${online?'green':'red'}`}>{online?'En ligne':'Hors ligne'}</span>}</div>
    {errors.agent?<p className="notice warn compact-notice">Agent : {errors.agent}</p>:!agent?<LoadingSkeleton/>:<dl className="backup-facts">
      <div><dt>Hôte</dt><dd>{agent.host??'—'}{agent.version&&<small> · v{agent.version}</small>}</dd></div>
      <div><dt>Dernier passage</dt><dd>{ago(agent.lastSeenAtUtc,now)}</dd></div>
      <div><dt>Restauration</dt><dd>{agent.restoreEnabled===null?'—':agent.restoreEnabled?'Activée sur le serveur':'Désactivée sur le serveur'}</dd></div>
    </dl>}
    {errors.settings?<p className="notice warn compact-notice">Réglages : {errors.settings}</p>:settings&&<dl className="backup-facts settings">
      <div><dt>Base</dt><dd><code>{settings.databaseName}</code></dd></div>
      <div><dt>Rétention</dt><dd>{settings.retentionDays} jours</dd></div>
      <div><dt>Minimum gardé</dt><dd>{settings.keepMin} sauvegardes</dd></div>
      <div><dt>Planification</dt><dd>Chaque jour à {settings.dailyAtUtc.slice(0,5)} UTC</dd></div>
      <div><dt>Stockage</dt><dd>{settings.storageConfigured?<span className="status green">Configuré</span>:<span className="status red">Non configuré</span>}</dd></div>
    </dl>}
  </section>;
}

export default function Backups(){
  const {run}=useAdmin();
  const now=useNow();
  const [page,setPage]=useState(1);
  const [restorePage,setRestorePage]=useState(1);
  const [data,setData]=useState<Data|null>(null);
  const [creating,setCreating]=useState(false);
  const [downloading,setDownloading]=useState<BackupDto|null>(null);
  const [restoring,setRestoring]=useState<BackupDto|null>(null);
  const generation=useRef(0);

  const load=useCallback(async()=>{
    const ticket=++generation.current;
    const [agent,settings,list,restores,backupHead,restoreHead]=await Promise.allSettled([
      backups.agent(),backups.settings(),
      backups.list(page,PAGE_SIZE),backups.restores(restorePage,RESTORE_PAGE_SIZE),
      // La sauvegarde ou la restauration active est toujours la plus récente.
      page===1?Promise.resolve(null):backups.list(1,1),
      restorePage===1?Promise.resolve(null):backups.restores(1,1),
    ]);
    if(ticket!==generation.current)return;
    const l=settled(list),r=settled(restores);
    const bHead=settled(backupHead)?.items??l?.items??[];
    const rHead=settled(restoreHead)?.items??r?.items??[];
    setData(previous=>({
      agent:settled(agent)??previous?.agent??null,
      settings:settled(settings)??previous?.settings??null,
      list:l??previous?.list??null,
      restores:r??previous?.restores??null,
      backupActive:bHead.some(b=>activeBackup(b.status)),
      restoreActive:rHead.some(x=>activeRestore(x.status)),
      errors:{agent:failure(agent),settings:failure(settings),list:failure(list),restores:failure(restores)},
    }));
  },[page,restorePage]);

  useEffect(()=>{void load()},[load]);

  // Rafraîchissement tant que l'agent a du travail en cours.
  const working=!!data&&(data.backupActive||data.restoreActive||!!data.list?.items.some(b=>busyBackup(b.status))||!!data.restores?.items.some(x=>activeRestore(x.status)));
  useEffect(()=>{
    if(!working)return;
    const t=window.setTimeout(()=>void load(),POLL_MS);
    return()=>window.clearTimeout(t);
  },[working,data,load]);

  const create=async()=>{
    setCreating(true);
    if(await run('Sauvegarde demandée',()=>backups.create(),touches.none)){setPage(1);await load()}
    setCreating(false);
  };
  const remove=async(b:BackupDto)=>{if(await run('Suppression demandée',()=>backups.remove(b.id),touches.none))await load()};
  const cancel=async(x:RestoreDto)=>{if(await run('Restauration annulée',()=>backups.cancelRestore(x.id),touches.none))await load()};

  const blocked=!data||data.backupActive||data.restoreActive;
  const items=data?.list?.items??[];
  const restores=data?.restores?.items??[];
  const backupOf=(id:string)=>items.find(b=>b.id===id);
  const latestRestore=restorePage===1?restores[0]:undefined;

  return <div className="backups-page">
    <PageHeader title="Sauvegardes" subtitle="Sauvegardes chiffrées de la base, téléchargement et restauration." icon={DatabaseBackup}>
      <Button variant="outline" onClick={()=>void load()}><RefreshCw size={16}/>Actualiser</Button>
      <Button disabled={blocked||creating} title={data?.restoreActive?'Une restauration est en cours ou programmée.':data?.backupActive?'Une sauvegarde est déjà en cours.':undefined} onClick={()=>void create()}><Play size={16}/>{creating?'Demande…':'Sauvegarder maintenant'}</Button>
    </PageHeader>

    {!data?<LoadingSkeleton/>:<>
      {latestRestore?.status==='Running'&&<div className="notice warn">Restauration en cours : l’application peut être indisponible quelques minutes.</div>}
      {latestRestore?.status==='Completed'&&now-new Date(latestRestore.completedAtUtc??0).getTime()<86400000&&<div className="notice">Dernière restauration terminée le {dateTime(latestRestore.completedAtUtc)}. Si ce n’est pas déjà fait, relancez le déploiement <code>./deploy.sh &lt;version&gt;</code> pour appliquer les migrations et les droits.</div>}

      <AgentPanel agent={data.agent} settings={data.settings} errors={data.errors} now={now}/>

      <section className="panel">
        <div className="panel-heading"><div><h2>Sauvegardes</h2><p>Les plus récentes d’abord{working?' · actualisation automatique':''}</p></div></div>
        {data.errors.list&&!data.list?<div className="notice warn">{data.errors.list} <button className="text-link" onClick={()=>void load()}>Réessayer</button></div>
        :!items.length?<EmptyState title="Aucune sauvegarde" description="Lancez une sauvegarde ou attendez la prochaine sauvegarde planifiée."/>
        :<div className="table-scroll"><table><thead><tr><th>Date</th><th>Déclenchement</th><th>Statut</th><th>Taille</th><th>Durée</th><th>Expiration</th><th>Échec</th><th>Actions</th></tr></thead><tbody>{items.map(b=><tr key={b.id}>
          <td data-label="Date">{dateTime(b.createdAtUtc)}</td>
          <td data-label="Déclenchement">{triggerLabels[b.trigger]??b.trigger}</td>
          <td data-label="Statut"><Status map={backupStatus} value={b.status}/></td>
          <td data-label="Taille">{bytes(b.sizeBytes)}</td>
          <td data-label="Durée">{b.completedAtUtc||b.status==='Running'||b.status==='Uploading'?duration(b.startedAtUtc,b.completedAtUtc,now):'—'}</td>
          <td data-label="Expiration">{b.expiresAtUtc?date(b.expiresAtUtc):'—'}</td>
          <td data-label="Échec" className="backup-failure">{b.failureCode?<span className="danger-text">{backupFailures[b.failureCode]??b.failureCode}</span>:'—'}</td>
          <td data-label="Actions">{b.status==='Completed'&&<div className="row-actions">
            <Button size="sm" variant="outline" onClick={()=>setDownloading(b)}><Download size={14}/>Télécharger</Button>
            <Button size="sm" variant="outline" disabled={blocked} title={blocked?'Une sauvegarde ou une restauration est active.':undefined} onClick={()=>setRestoring(b)}><RotateCcw size={14}/>Restaurer</Button>
            <ConfirmDialog title="Supprimer cette sauvegarde ?" description={`Le fichier du ${dateTime(b.createdAtUtc)} sera effacé du stockage. Au moins ${data.settings?.keepMin??7} sauvegardes terminées sont toujours gardées.`} onConfirm={()=>void remove(b)}>
              <Button size="sm" variant="ghost" aria-label="Supprimer"><Trash2 size={14}/></Button>
            </ConfirmDialog>
          </div>}</td>
        </tr>)}</tbody></table></div>}
        {data.list&&<Pager page={page} hasMore={data.list.hasMore} onPage={setPage}/>}
      </section>

      <section className="panel">
        <div className="panel-heading"><div><h2>Restaurations</h2><p>Une restauration programmée peut être annulée jusqu’à son démarrage</p></div></div>
        {data.errors.restores&&!data.restores?<div className="notice warn">{data.errors.restores} <button className="text-link" onClick={()=>void load()}>Réessayer</button></div>
        :!restores.length?<EmptyState title="Aucune restauration" description="Les restaurations demandées depuis une sauvegarde apparaîtront ici."/>
        :<div className="table-scroll"><table><thead><tr><th>Statut</th><th>Sauvegarde source</th><th>Prévue le</th><th>Base précédente</th><th>Échec</th><th>Actions</th></tr></thead><tbody>{restores.map(x=>{
          const source=backupOf(x.backupId);
          const left=new Date(x.notBeforeUtc).getTime()-now;
          return <tr key={x.id}>
            <td data-label="Statut"><Status map={restoreStatus} value={x.status}/></td>
            <td data-label="Sauvegarde source">{source?`Sauvegarde du ${dateTime(source.createdAtUtc)}`:<code>{x.backupId.slice(0,8)}</code>}<br/><small className="muted">demandée le {dateTime(x.createdAtUtc)}</small></td>
            <td data-label="Prévue le">{dateTime(x.notBeforeUtc)}{x.status==='Pending'&&<><br/><small className="muted">{left>0?`dans ${span(left)}`:'au prochain passage de l’agent'}</small></>}</td>
            <td data-label="Base précédente">{x.previousDatabaseName?<code>{x.previousDatabaseName}</code>:'—'}</td>
            <td data-label="Échec" className="backup-failure">{x.failureCode?<span className="danger-text">{restoreFailures[x.failureCode]??x.failureCode}</span>:'—'}</td>
            <td data-label="Actions">{x.status==='Pending'&&<Button size="sm" variant="outline" onClick={()=>void cancel(x)}><X size={14}/>Annuler</Button>}</td>
          </tr>})}</tbody></table></div>}
        {data.restores&&<Pager page={restorePage} hasMore={data.restores.hasMore} onPage={setRestorePage}/>}
      </section>
    </>}

    {downloading&&<DownloadDialog key={downloading.id} backup={downloading} onClose={()=>setDownloading(null)}/>}
    {restoring&&<RestoreDialog key={restoring.id} backup={restoring} settings={data?.settings??null} agent={data?.agent??null} onClose={()=>setRestoring(null)} onDone={()=>{setRestorePage(1);void load()}}/>}
  </div>;
}
