'use client';
import {useCallback,useEffect,useMemo,useState,type ReactNode} from 'react';
import {ArrowRight,BellRing,CheckCircle2,Clock,Lightbulb,Mail,MessageSquare,Plus,RefreshCw,Send,Smartphone,Archive,Pencil,CopyPlus,Upload} from 'lucide-react';
import {Button} from '@/components/ui/button';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription,DialogFooter} from '@/components/ui/dialog';
import {touches,useAdmin} from '../../hooks/use-admin';
import {StatCard,StatusBadge,LoadingSkeleton,EmptyState,SearchInput} from '../../components/ui/shared';
import {ConfirmDialog} from '../../components/feedback/dialogs';
import {describe} from '../../services/api/client';
import {
  notificationAdmin,notificationProviders,platformTemplates,
  type NotificationChannel,type NotificationConfigurationType,type NotificationFeature,type NotificationIntegrationDto,type NotificationLogDto,
  type NotificationProviderDto,type NotificationStatus,type NotificationTemplateDto,type NotificationTypeDto,type NotificationsOverview,
} from '../../services/api/admin-api';
import {ProviderTile} from '../shared/provider-ui';
import {dateTime,number} from '../../utils/format';

export const channelLabels:Record<NotificationChannel,string>={Email:'Email',Sms:'SMS',WhatsApp:'WhatsApp',Push:'Push',InApp:'In-app'};
const accountLabels:Record<NotificationConfigurationType,string>={Internal:'Interne',PlatformOnly:'Compte plateforme',TenantOnly:'Compte restaurant',PlatformOrTenant:'Plateforme ou restaurant'};
const featureLabels:Record<NotificationFeature,string>={Text:'Texte',Html:'HTML',Attachments:'Pièces jointes',ProviderTemplates:'Modèles du prestataire'};
const statusLabels:Record<NotificationStatus,string>={Pending:'En attente',Scheduled:'Programmée',Processing:'En cours',Sent:'Envoyée',PartiallyDelivered:'Remise en partie',Delivered:'Remise',Failed:'Échec',Cancelled:'Annulée'};
const templateStatusLabels={Draft:'Brouillon',Published:'Publié',Archived:'Archivé'} as const;
const allChannels:NotificationChannel[]=['Email','Sms','WhatsApp','Push','InApp'];

/** Libellés français des types ; un type inconnu garde sa clé. */
const typeNames:Record<string,string>={
  'order.confirmed':'Commande confirmée','order.ready':'Commande prête','order.cancelled':'Commande annulée','order.receipt':'Reçu de commande',
  'reservation.confirmed':'Réservation confirmée','reservation.reminder':'Rappel de réservation','reservation.cancelled':'Réservation annulée','reservation.deposit_request':'Acompte à payer',
  'waitlist.offer':'Table libérée','waitlist.table_ready':'Table prête','payment.succeeded':'Paiement reçu','payment.failed':'Paiement échoué','refund.succeeded':'Remboursement effectué',
  'loyalty.reward_earned':'Récompense de fidélité','marketing.promotional_campaign':'Campagne promotionnelle','marketing.new_menu':'Nouvelle carte','marketing.loyalty_campaign':'Campagne de fidélité',
  'marketing.customer_reactivation':'Relance client','marketing.special_event':'Événement spécial','account.password_reset':'Mot de passe oublié','account.verify_email':'Confirmation d’adresse',
  'account.welcome':'Bienvenue','security.otp':'Code de vérification','staff.low_stock':'Stock bas (équipe)','staff.new_reservation':'Nouvelle réservation (équipe)','staff.new_review':'Nouvel avis (équipe)',
  'staff.order_cancelled':'Commande annulée (équipe)','staff.payment_failed':'Paiement échoué (équipe)','staff.report_ready':'Rapport prêt (équipe)','staff.schedule_published':'Planning publié (équipe)',
  'staff.shift_swap':'Échange de service (équipe)','staff.subscription_ending':'Fin d’essai ou d’offre (équipe)','staff.subscription_offer':'Offre de la plateforme (équipe)','staff.webhook_disabled':'Webhook désactivé (équipe)',
};
export const typeLabel=(key:string)=>typeNames[key]??key;

/** Noms affichés des intégrations présentes dans le code. */
export const integrationNames:Record<string,string>={
  SMTP:'Email (SMTP)',MAILJET:'Mailjet',BREVO:'Brevo (email)',BREVO_SMS:'Brevo (SMS)',SES:'Amazon SES',RESEND:'Resend',WHATSAPP_META:'WhatsApp Business (Meta)',
  TWILIO:'Twilio (SMS)',TWILIO_WHATSAPP:'Twilio (WhatsApp)',INFOBIP:'Infobip',TERMII:'Termii',AFRICASTALKING:'Africa’s Talking',FCM:'Firebase Cloud Messaging',ONESIGNAL:'OneSignal',
  INAPP:'In-app',FAKE:'Simulé (développement)',
};

/** Où déclarer le webhook de remise chez le prestataire, et comment il est authentifié. */
export const webhookHints:Record<string,string>={
  BREVO:'Webhook transactionnel email : URL avec le mot de passe en Basic.',
  BREVO_SMS:'Webhook transactionnel SMS : URL avec le mot de passe en Basic.',
  TWILIO:'Status callback du Messaging Service ou du numéro : URL avec le mot de passe en Basic.',
  TWILIO_WHATSAPP:'Status callback de l’expéditeur WhatsApp : URL avec le mot de passe en Basic. Hors fenêtre de 24 h, WhatsApp exige un modèle approuvé.',
  INFOBIP:'Delivery reports : URL avec le mot de passe en Basic.',
  AFRICASTALKING:'Delivery reports callback : URL avec le mot de passe en Basic.',
  TERMII:'Webhook signé par la clé secrète du compte (X-Termii-Signature).',
  RESEND:'Webhook signé (Svix) : copiez le secret whsec_… affiché par Resend.',
  MAILJET:'Event API : URL avec le mot de passe en Basic.',
  SES:'Les rebonds passent par Amazon SNS, pas encore relié : un email accepté reste « envoyé ».',
  FCM:'Pas d’accusé de remise : un push accepté reste « envoyé ».',
  ONESIGNAL:'Pas d’accusé de remise relié : un push accepté reste « envoyé ».',
};

/** Les rubriques de la page, dans l'ordre de la maquette. */
const groups:{title:string;description:string;channels:NotificationChannel[];icon:typeof Mail}[]=[
  {title:'Email',description:'Confirmations, rappels, comptes et messages marketing par email.',channels:['Email'],icon:Mail},
  {title:'SMS & WhatsApp',description:'Rappels de réservation, confirmations et codes par SMS ou WhatsApp.',channels:['Sms','WhatsApp'],icon:MessageSquare},
  {title:'Push & In-App',description:'Notifications de l’application mobile et du tableau de bord, en temps réel.',channels:['Push','InApp'],icon:BellRing},
];

const seconds=(value:number|null)=>value===null?'—':value<60?`${value.toLocaleString('fr-FR',{maximumFractionDigits:1})} s`:`${(value/60).toLocaleString('fr-FR',{maximumFractionDigits:1})} min`;
const rate=(part:number,whole:number)=>whole?`${(Math.round(part/whole*1000)/10).toLocaleString('fr-FR')} %`:'—';

function useLoaded<T>(load:()=>Promise<T>){
  const [data,setData]=useState<T|null>(null);
  const [error,setError]=useState<string>();
  const reload=useCallback(async()=>{try{setError(undefined);setData(await load())}catch(e){setError(describe(e))}},[load]);
  useEffect(()=>{void reload()},[reload]);
  return {data,error,reload};
}

/** L'état d'un prestataire sur sa carte, du plus bloquant au plus avancé. */
function providerState(p:NotificationProviderDto){
  if(!p.hasIntegration&&p.configurationType!=='Internal')return 'Absent du code';
  if(p.isEnabled&&p.hasPlatformCredentials===false)return 'À configurer';
  return p.isEnabled?'Actif':'Inactif';
}

function Kpis({providers,overview,error}:{providers:NotificationProviderDto[];overview:NotificationsOverview|null;error?:string}){
  const active=allChannels.filter(c=>providers.some(p=>p.isEnabled&&p.channel===c)).length;
  const month=overview?new Date(overview.sinceUtc).toLocaleDateString('fr-FR',{month:'long'}):'ce mois';
  return <>
    <div className="stats-grid four">
      <StatCard label="Canaux actifs" value={`${active}`} caption={`sur ${allChannels.length} disponibles`} icon={Send}/>
      <StatCard label="Notifications envoyées" value={overview?number(overview.handedOver):'—'} caption={overview?`depuis le 1er ${month} · ${number(overview.created)} créées`:'ce mois-ci'} icon={Mail}/>
      <StatCard label="Taux de réussite" value={overview?rate(overview.handedOver,overview.handedOver+overview.failed):'—'} caption={overview?`acceptées par le prestataire · ${number(overview.failed)} en échec`:'tous canaux'} icon={CheckCircle2}/>
      <StatCard label="Délai moyen d’envoi" value={overview?seconds(overview.averageSendSeconds):'—'} caption="de la création à l’acceptation" icon={Clock}/>
    </div>
    {error&&<div className="notice warn">Indicateurs indisponibles : {error}</div>}
  </>;
}

function ProviderCard({provider,onToggle,onCredentials}:{provider:NotificationProviderDto;onToggle:(p:NotificationProviderDto,on:boolean)=>void;onCredentials:(p:NotificationProviderDto)=>void}){
  const state=providerState(provider);
  const usesPlatform=provider.configurationType==='PlatformOnly'||provider.configurationType==='PlatformOrTenant';
  const configurable=usesPlatform&&provider.hasIntegration&&provider.hasPlatformCredentials!==null&&provider.hasPlatformCredentials!==undefined;
  return <article className={`collect-card notif-card${provider.isEnabled?' on':''}`}>
    <ProviderTile code={provider.code} name={provider.name}/>
    <div className="collect-card-body">
      <div className="collect-card-title"><strong>{provider.name}</strong><StatusBadge value={state}/></div>
      <p className="collect-card-text">{accountLabels[provider.configurationType]}</p>
      <p className="collect-card-text small">{channelLabels[provider.channel]} · {provider.capability.features.map(f=>featureLabels[f]??f).join(', ')||'—'}{usesPlatform&&provider.hasPlatformCredentials!=null?` · identifiants ${provider.hasPlatformCredentials?'enregistrés':'à saisir'}`:''}</p>
      <div className="collect-card-actions">
        <span/>
        <button type="button" className="card-btn" disabled={!configurable} title={configurable?undefined:provider.configurationType==='TenantOnly'?'Chaque restaurant saisit son propre compte':'Aucun identifiant à saisir'} onClick={()=>onCredentials(provider)}>Configurer<ArrowRight size={14}/></button>
      </div>
    </div>
    <label className="switch">
      <input type="checkbox" role="switch" checked={provider.isEnabled} disabled={!provider.hasIntegration&&provider.configurationType!=='Internal'&&!provider.isEnabled} aria-label={`${provider.isEnabled?'Désactiver':'Activer'} ${provider.name}`} onChange={e=>onToggle(provider,e.target.checked)}/>
      <span aria-hidden="true"/>
    </label>
  </article>;
}

function IntegrationCard({integration,name,onAdd}:{integration:NotificationIntegrationDto;name:string;onAdd:(code:string)=>void}){
  return <article className="collect-card notif-card ghost">
    <ProviderTile code={integration.provider} name={name}/>
    <div className="collect-card-body">
      <div className="collect-card-title"><strong>{name}</strong><StatusBadge value="Non ajouté"/></div>
      <p className="collect-card-text">Intégration présente dans le code, pas encore au catalogue.</p>
      <p className="collect-card-text small">{integration.channels.map(c=>channelLabels[c]).join(', ')}</p>
      <div className="collect-card-actions"><span/><button type="button" className="card-btn" onClick={()=>onAdd(integration.provider)}><Plus size={14}/>Ajouter</button></div>
    </div>
  </article>;
}

function ProvidersTab({providers,integrations,nameOf,onToggle,onCredentials,onAdd,onReload}:{
  providers:NotificationProviderDto[];integrations:NotificationIntegrationDto[];nameOf:(code:string)=>string;
  onToggle:(p:NotificationProviderDto,on:boolean)=>void;onCredentials:(p:NotificationProviderDto)=>void;onAdd:(code?:string)=>void;onReload:()=>void;
}){
  const [query,setQuery]=useState('');
  const [channel,setChannel]=useState<NotificationChannel|''>('');
  const registered=new Set(providers.map(p=>p.code.toUpperCase()));
  const free=integrations.filter(i=>!registered.has(i.provider.toUpperCase()));
  const needle=query.trim().toLowerCase();
  const shown=providers.filter(p=>(!channel||p.channel===channel)&&(!needle||`${p.name} ${p.code}`.toLowerCase().includes(needle)));
  return <>
    <div className="collect-layout">
      <section className="panel collect-panel">
        <div className="collect-head"><div><h2>Prestataires de notification</h2><p className="muted">Activez et configurez les canaux de la plateforme. Selon le prestataire, un restaurant passe par le compte de la plateforme ou par le sien.</p></div></div>
        {groups.map(group=>{
          const list=providers.filter(p=>group.channels.includes(p.channel));
          const extra=free.filter(i=>i.channels.some(c=>group.channels.includes(c)));
          const Icon=group.icon;
          return <div key={group.title} className="notif-group">
            <div className="notif-group-head"><span className="notif-group-icon"><Icon size={19}/></span><div><h3>{group.title}</h3><p className="muted">{group.description}</p></div></div>
            {list.length+extra.length?<div className="notif-grid">
              {list.map(p=><ProviderCard key={p.id} provider={p} onToggle={onToggle} onCredentials={onCredentials}/>)}
              {extra.map(i=><IntegrationCard key={i.provider} integration={i} name={nameOf(i.provider)} onAdd={onAdd}/>)}
            </div>:<p className="block-note">Aucun prestataire pour ce canal. Un nouveau prestataire demande d’abord son intégration côté API.</p>}
          </div>;
        })}
      </section>
      <aside className="collect-aside-stack">
        <section className="panel collect-aside">
          <div className="collect-aside-art" aria-hidden="true"><BellRing size={46}/></div>
          <h3>Restez toujours en contact</h3>
          <p className="muted">Les messages que la plateforme envoie pour les restaurants et leurs clients.</p>
          <ul className="collect-facts-list checks">
            {['Confirmations de commande','Rappels de réservation','Notifications d’équipe','Campagnes marketing','Notifications in-app en temps réel'].map(t=><li key={t}><CheckCircle2 size={16}/>{t}</li>)}
          </ul>
        </section>
        <section className="panel tip-card">
          <h3><Lightbulb size={16}/> Bon à savoir</h3>
          <p>Un prestataire « Plateforme ou restaurant » passe par le compte de la plateforme, sauf si le restaurant saisit ses propres identifiants dans sa console.</p>
        </section>
      </aside>
    </div>
    <section className="panel catalog-panel">
      <div className="settings-heading"><div><h2>Catalogue complet des prestataires</h2><p className="muted">Tous les prestataires de notification de la plateforme.</p></div>
        <div className="page-actions"><SearchInput value={query} onChange={setQuery} placeholder="Rechercher un prestataire…"/>
          <select aria-label="Canal" value={channel} onChange={e=>setChannel(e.target.value as NotificationChannel|'')}><option value="">Tous les canaux</option>{allChannels.map(c=><option key={c} value={c}>{channelLabels[c]}</option>)}</select>
          <Button variant="outline" onClick={onReload}><RefreshCw size={15}/>Actualiser</Button><Button onClick={()=>onAdd()}><Plus size={15}/>Ajouter</Button></div></div>
      {providers.length?shown.length?<div className="table-scroll"><table><thead><tr><th>Prestataire</th><th>Canal</th><th>Compte</th><th>Fonctionnalités</th><th>Statut</th><th>Actions</th></tr></thead><tbody>{shown.map(p=><tr key={p.id}>
        <td><span className="provider-cell"><ProviderTile code={p.code} name={p.name}/><span><strong>{p.name}</strong><small><code>{p.code}</code></small></span></span></td>
        <td>{channelLabels[p.channel]}</td>
        <td>{accountLabels[p.configurationType]}</td>
        <td>{p.capability.features.map(f=>featureLabels[f]??f).join(', ')}</td>
        <td><StatusBadge value={providerState(p)}/></td>
        <td><div className="row-actions">
          {(p.configurationType==='PlatformOnly'||p.configurationType==='PlatformOrTenant')&&p.hasIntegration&&<Button size="sm" variant="outline" onClick={()=>onCredentials(p)}>Configurer<ArrowRight size={14}/></Button>}
          {p.isEnabled?<ConfirmDialog title={`Désactiver ${p.name} ?`} description="Les envois passeront par un autre prestataire du même canal s’il y en a un." onConfirm={()=>onToggle(p,false)}><Button size="sm" variant="outline" className="danger-outline">Désactiver</Button></ConfirmDialog>:<Button size="sm" variant="outline" onClick={()=>onToggle(p,true)}>Activer</Button>}
        </div></td></tr>)}</tbody></table></div>:<p className="muted">Aucun prestataire ne correspond.</p>:<EmptyState title="Aucun prestataire" description="Ajoutez un prestataire à partir des intégrations disponibles."/>}
    </section>
  </>;
}

function TemplateDialog({template,types,onClose,onSaved}:{template:NotificationTemplateDto|null;types:NotificationTypeDto[];onClose:()=>void;onSaved:()=>void}){
  const {run}=useAdmin();
  const editing=!!template;
  const [key,setKey]=useState(template?.key??types[0]?.key??'');
  const [channel,setChannel]=useState<NotificationChannel>(template?.channel??'Email');
  const [language,setLanguage]=useState(template?.language??'fr');
  const [subject,setSubject]=useState(template?.subject??'');
  const [body,setBody]=useState(template?.body??'');
  const variables=template?.variables.length?template.variables:types.find(t=>t.key===key)?.variables??[];
  const submit=async()=>{
    const ok=await run(editing?'Brouillon enregistré':'Modèle créé (brouillon)',()=>editing
      ?platformTemplates.update(template!.id,channel==='Email'?subject||null:null,body)
      :platformTemplates.create({key,channel,language,subject:channel==='Email'?subject||null:null,body}),touches.none);
    if(ok){onSaved();onClose()}
  };
  return <Dialog open onOpenChange={v=>!v&&onClose()}>
    <DialogContent className="form-dialog wide-dialog">
      <DialogHeader><DialogTitle>{editing?`Modifier · ${typeLabel(template!.key)}`:'Nouveau modèle de la plateforme'}</DialogTitle><DialogDescription>Le modèle reste un brouillon tant qu’il n’est pas publié. Un modèle publié s’applique à tous les restaurants qui n’ont pas le leur.</DialogDescription></DialogHeader>
      <form onSubmit={e=>{e.preventDefault();void submit()}}>
        <div className="form-fields">
          {!editing&&<div className="settings-fields three full">
            <label className="settings-field"><span>Type de message</span><select value={key} onChange={e=>setKey(e.target.value)}>{types.map(t=><option key={t.key} value={t.key}>{typeLabel(t.key)}</option>)}</select></label>
            <label className="settings-field"><span>Canal</span><select value={channel} onChange={e=>setChannel(e.target.value as NotificationChannel)}>{allChannels.map(c=><option key={c} value={c}>{channelLabels[c]}</option>)}</select></label>
            <label className="settings-field"><span>Langue</span><select value={language} onChange={e=>setLanguage(e.target.value)}><option value="fr">Français</option><option value="en">Anglais</option></select></label>
          </div>}
          {channel==='Email'&&<label className="settings-field full"><span>Objet</span><input value={subject} onChange={e=>setSubject(e.target.value)} required/></label>}
          <label className="settings-field full"><span>Message</span><textarea value={body} onChange={e=>setBody(e.target.value)} required rows={8}/></label>
          {variables.length>0&&<p className="tiny-note full">Variables : {variables.map(v=><code key={v} className="var-chip">{`{{${v}}}`}</code>)}</p>}
        </div>
        <DialogFooter><Button type="button" variant="outline" onClick={onClose}>Annuler</Button><Button type="submit">{editing?'Enregistrer':'Créer le brouillon'}</Button></DialogFooter>
      </form>
    </DialogContent>
  </Dialog>;
}

function TemplatesTab(){
  const {run}=useAdmin();
  const load=useCallback(()=>Promise.all([platformTemplates.list(),platformTemplates.types()]),[]);
  const {data,error,reload}=useLoaded(load);
  const [editing,setEditing]=useState<NotificationTemplateDto|null|'new'>(null);
  const [channel,setChannel]=useState<NotificationChannel|''>('');
  const [showArchived,setShowArchived]=useState(false);
  const act=async(label:string,action:()=>Promise<unknown>)=>{if(await run(label,action,touches.none))await reload()};
  if(error)return <div className="notice warn">{error}</div>;
  if(!data)return <LoadingSkeleton/>;
  const [templates,types]=data;
  const shown=templates.filter(t=>(!channel||t.channel===channel)&&(showArchived||t.status!=='Archived'))
    .sort((a,b)=>typeLabel(a.key).localeCompare(typeLabel(b.key),'fr')||a.channel.localeCompare(b.channel)||b.version-a.version);
  return <section className="panel catalog-panel">
    <div className="settings-heading"><div><h2>Modèles de messages</h2><p className="muted">Les modèles de la plateforme, utilisés par les restaurants qui n’ont pas écrit le leur. Sans modèle ici, le texte intégré au code s’applique.</p></div>
      <div className="page-actions">
        <select aria-label="Canal" value={channel} onChange={e=>setChannel(e.target.value as NotificationChannel|'')}><option value="">Tous les canaux</option>{allChannels.map(c=><option key={c} value={c}>{channelLabels[c]}</option>)}</select>
        <label className="check-item"><input type="checkbox" checked={showArchived} onChange={e=>setShowArchived(e.target.checked)}/>Archivés</label>
        <Button onClick={()=>setEditing('new')}><Plus size={15}/>Nouveau modèle</Button>
      </div></div>
    {shown.length?<div className="table-scroll"><table><thead><tr><th>Type</th><th>Canal</th><th>Langue</th><th>Version</th><th>Statut</th><th>Mis à jour</th><th>Actions</th></tr></thead><tbody>{shown.map(t=><tr key={t.id}>
      <td><strong>{typeLabel(t.key)}</strong><br/><small><code>{t.key}</code></small></td>
      <td>{channelLabels[t.channel]}</td>
      <td>{t.language.toUpperCase()}</td>
      <td>v{t.version}</td>
      <td><StatusBadge value={t.status==='Published'&&!t.isEnabled?'Désactivé':templateStatusLabels[t.status]}/></td>
      <td>{dateTime(t.updatedAtUtc)}</td>
      <td><div className="row-actions">
        {t.status==='Draft'&&<><Button size="sm" variant="outline" onClick={()=>setEditing(t)}><Pencil size={14}/>Modifier</Button><Button size="sm" variant="outline" onClick={()=>void act('Modèle publié',()=>platformTemplates.publish(t.id))}><Upload size={14}/>Publier</Button></>}
        {t.status==='Published'&&<><Button size="sm" variant="outline" onClick={()=>void act('Nouvelle version créée (brouillon)',()=>platformTemplates.newVersion(t.id))}><CopyPlus size={14}/>Nouvelle version</Button>
          {t.isEnabled?<Button size="sm" variant="outline" onClick={()=>void act('Modèle désactivé',()=>platformTemplates.disable(t.id))}>Désactiver</Button>:<Button size="sm" variant="outline" onClick={()=>void act('Modèle activé',()=>platformTemplates.enable(t.id))}>Activer</Button>}</>}
        {t.status!=='Archived'&&<ConfirmDialog title="Archiver ce modèle ?" description="Il ne sera plus utilisé ; une version publiée reste consultable comme preuve de ce qui a été envoyé." onConfirm={()=>void act('Modèle archivé',()=>platformTemplates.archive(t.id))}><Button size="sm" variant="ghost" aria-label="Archiver"><Archive size={14}/></Button></ConfirmDialog>}
      </div></td></tr>)}</tbody></table></div>:<EmptyState title="Aucun modèle" description="Les textes intégrés au code s’appliquent. Créez un modèle pour les remplacer."/>}
    {editing&&<TemplateDialog template={editing==='new'?null:editing} types={types} onClose={()=>setEditing(null)} onSaved={()=>void reload()}/>}
  </section>;
}

const PAGE_SIZE=50;

function SendLogTab(){
  const {raw}=useAdmin();
  const [channel,setChannel]=useState<NotificationChannel|''>('');
  const [status,setStatus]=useState<NotificationStatus|''>('');
  const [rows,setRows]=useState<NotificationLogDto[]|null>(null);
  const [page,setPage]=useState(1);
  const [more,setMore]=useState(false);
  const [error,setError]=useState<string>();
  const [busy,setBusy]=useState(false);
  const tenant=useMemo(()=>new Map(raw.tenants.map(t=>[t.tenantId,t.name])),[raw.tenants]);
  const load=useCallback(async(next:number)=>{
    setBusy(true);
    try{
      setError(undefined);
      const result=await notificationAdmin.log({channel:channel||undefined,status:status||undefined,page:next,pageSize:PAGE_SIZE});
      setRows(current=>next===1?result.items:[...(current??[]),...result.items]);
      setMore(result.hasMore);setPage(next);
    }catch(e){setError(describe(e))}finally{setBusy(false)}
  },[channel,status]);
  useEffect(()=>{void load(1)},[load]);
  return <section className="panel catalog-panel">
    <div className="settings-heading"><div><h2>Journal d’envoi</h2><p className="muted">Les derniers envois de tous les restaurants. Ni le destinataire ni le contenu ne sont affichés.</p></div>
      <div className="page-actions">
        <select aria-label="Canal" value={channel} onChange={e=>setChannel(e.target.value as NotificationChannel|'')}><option value="">Tous les canaux</option>{allChannels.map(c=><option key={c} value={c}>{channelLabels[c]}</option>)}</select>
        <select aria-label="Statut" value={status} onChange={e=>setStatus(e.target.value as NotificationStatus|'')}><option value="">Tous les statuts</option>{(Object.keys(statusLabels) as NotificationStatus[]).map(s=><option key={s} value={s}>{statusLabels[s]}</option>)}</select>
        <Button variant="outline" onClick={()=>void load(1)}><RefreshCw size={15}/>Actualiser</Button>
      </div></div>
    {error?<div className="notice warn">{error}</div>:!rows?<LoadingSkeleton/>:rows.length?<>
      <div className="table-scroll"><table><thead><tr><th>Créée le</th><th>Restaurant</th><th>Type</th><th>Canal</th><th>Prestataire</th><th>Statut</th><th>Tentatives</th><th>Erreur</th></tr></thead><tbody>{rows.map(r=><tr key={r.id}>
        <td>{dateTime(r.createdAtUtc)}{r.sentAtUtc&&<><br/><small>envoyée {dateTime(r.sentAtUtc)}</small></>}</td>
        <td>{r.tenantId?tenant.get(r.tenantId)??r.tenantId.slice(0,8):<span className="muted">Plateforme</span>}</td>
        <td>{typeLabel(r.type)}</td>
        <td>{channelLabels[r.channel]}</td>
        <td><code>{r.provider}</code></td>
        <td><StatusBadge value={statusLabels[r.status]}/></td>
        <td>{r.attemptCount}</td>
        <td>{r.lastFailureCode?<code>{r.lastFailureCode}</code>:'—'}</td>
      </tr>)}</tbody></table></div>
      {more&&<div className="load-more"><Button variant="outline" disabled={busy} onClick={()=>void load(page+1)}>{busy?'Chargement…':'Afficher plus'}</Button></div>}
    </>:<EmptyState title="Aucun envoi" description="Rien ne correspond à ces filtres."/>}
  </section>;
}

const subTabs=[['providers','Prestataires',Smartphone],['templates','Modèles de messages',MessageSquare],['log','Journal d’envoi',Send]] as const;
type SubTab=typeof subTabs[number][0];

/**
 * Paramètres > Notifications : indicateurs du mois, prestataires par canal (interrupteur, compte plateforme), modèles
 * de la plateforme et journal d'envoi de tous les restaurants. Les dialogues d'ajout et d'identifiants restent fournis
 * par la page.
 */
export function NotificationsWorkspace({onCredentials,onAdd,dialogs,version}:{onCredentials:(p:NotificationProviderDto,integrations:NotificationIntegrationDto[])=>void;onAdd:(integrations:NotificationIntegrationDto[],providers:NotificationProviderDto[],code?:string)=>void;dialogs:ReactNode;version:number}){
  const {run}=useAdmin();
  const [tab,setTab]=useState<SubTab>('providers');
  const load=useCallback(()=>Promise.all([notificationProviders.list(),notificationProviders.integrations()]),[]);
  const {data,error,reload}=useLoaded(load);
  const loadOverview=useCallback(()=>notificationAdmin.overview(),[]);
  const overview=useLoaded(loadOverview);
  useEffect(()=>{if(version)void reload()},[version,reload]);
  const nameOf=(code:string)=>integrationNames[code.toUpperCase()]??code;
  const toggle=async(p:NotificationProviderDto,on:boolean)=>{if(await run(on?`${p.name} activé`:`${p.name} désactivé`,()=>on?notificationProviders.enable(p.id):notificationProviders.disable(p.id),touches.none))await reload()};
  return <div className="payments-page">
    {data&&<Kpis providers={data[0]} overview={overview.data} error={overview.error}/>}
    <nav className="sub-tabs" aria-label="Notifications">{subTabs.map(([key,label,Icon])=><button key={key} type="button" aria-current={tab===key?'page':undefined} className={tab===key?'active':''} onClick={()=>setTab(key)}><Icon size={15}/>{label}</button>)}</nav>
    {tab==='providers'?(error?<div className="notice warn">{error} <button className="text-link" onClick={()=>void reload()}>Réessayer</button></div>:!data?<LoadingSkeleton/>
      :<ProvidersTab providers={data[0]} integrations={data[1]} nameOf={nameOf} onToggle={(p,on)=>void toggle(p,on)} onCredentials={p=>onCredentials(p,data[1])} onAdd={code=>onAdd(data[1],data[0],code)} onReload={()=>{void reload();void overview.reload()}}/>)
      :tab==='templates'?<TemplatesTab/>:<SendLogTab/>}
    {dialogs}
  </div>;
}
