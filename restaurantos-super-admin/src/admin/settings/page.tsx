'use client';
import {useCallback,useEffect,useState,type FormEvent,type ReactNode} from 'react';
import {KeyRound,Bell,Globe,Plus,Power,PowerOff,RefreshCw,ExternalLink,Pencil,ArrowRight,CreditCard,Smartphone,Wallet,Landmark,Banknote,Ticket,ShieldCheck,Lock,Receipt,Coins,BookOpen} from 'lucide-react';
import {toast} from 'sonner';
import {Button} from '@/components/ui/button';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription,DialogFooter} from '@/components/ui/dialog';
import {touches,useAdmin} from '../../hooks/use-admin';
import {PageHeader,StatusBadge,LoadingSkeleton,EmptyState,SearchInput} from '../../components/ui/shared';
import {ConfirmDialog} from '../../components/feedback/dialogs';
import {methodLabels,missingText,offeredOf} from '../shared/collection';
import {ProviderTile} from '../shared/provider-ui';
import {NotificationsWorkspace,integrationNames,webhookHints} from './notifications';
import {API_URL,describe} from '../../services/api/client';
import {
  billingAdmin,notificationProviders,paymentProviders,type CollectorCandidate,
  type NotificationChannel,type NotificationConfigurationType,type NotificationFeature,type NotificationIntegrationDto,type NotificationProviderDto,
  type PaymentCapabilityInput,type PaymentIntegrationDto,type PaymentMethodType,type PaymentProviderDto,type PaymentProviderType,
} from '../../services/api/admin-api';

const sections=[['Paiements',KeyRound],['Notifications',Bell],['Compte et API',Globe]] as const;

const onlineMethods:PaymentMethodType[]=['Card','ApplePay','GooglePay','PayPal','MobileMoney','BankTransfer'];
const onSiteMethods:PaymentMethodType[]=['Cash','CardOnSite','MobileMoneyOnSite','MealVoucher'];
const channelLabels:Record<NotificationChannel,string>={Email:'Email',Sms:'SMS',WhatsApp:'WhatsApp',Push:'Push',InApp:'In-app'};
const configurationLabels:Record<NotificationConfigurationType,string>={Internal:'Interne',PlatformOnly:'Compte plateforme uniquement',TenantOnly:'Compte du restaurant uniquement',PlatformOrTenant:'Plateforme ou restaurant'};
const featureLabels:Record<NotificationFeature,string>={Text:'Texte',Html:'HTML',Attachments:'Pièces jointes',ProviderTemplates:'Modèles du prestataire'};
const credentialLabels:Record<string,[string,string?]>={
  host:['Serveur SMTP','ex. smtp-relay.brevo.com'],port:['Port','587 (starttls) ou 465 (ssl)'],security:['Sécurité','starttls, ssl ou none'],
  username:['Identifiant','Africa’s Talking : « sandbox » pour l’environnement de test'],password:['Mot de passe'],from_email:['Adresse d’expédition','ex. bonjour@votredomaine.com'],from_name:['Nom d’expéditeur','ex. RestauSpace'],
  access_token:['Jeton d’accès permanent','Utilisateur système de Meta Business'],phone_number_id:['Identifiant du numéro','Numéro WhatsApp Business, dans le gestionnaire WhatsApp'],
  app_secret:['Clé secrète de l’application','Vérifie la signature des webhooks'],verify_token:['Jeton de vérification du webhook','Valeur libre, à recopier chez Meta'],
  template_name:['Nom du modèle générique','Modèle approuvé par Meta dont le corps est {{1}}'],template_language:['Langue du modèle','ex. fr'],
  api_key:['Clé d’API'],webhook_secret:['Secret des webhooks'],secret_key:['Clé secrète'],
  webhook_password:['Mot de passe des webhooks','Valeur libre, à mettre dans l’URL déclarée chez le prestataire'],sms_sender:['Expéditeur SMS','11 caractères au plus, ex. RestauSpace'],
  account_sid:['SID du compte','AC…'],auth_token:['Jeton d’authentification'],from:['Expéditeur','+33… ou SID d’un Messaging Service MG…'],
  base_url:['URL de base du compte','ex. xxxxx.api.infobip.com'],sender:['Expéditeur','ex. RestauSpace'],sender_id:['Identifiant d’expéditeur','validé chez le prestataire'],
  region:['Région AWS','ex. eu-west-3'],access_key_id:['Clé d’accès IAM','AKIA…'],
  secret_access_key:['Clé secrète IAM'],app_id:['Identifiant de l’application','UUID OneSignal'],rest_api_key:['Clé REST API'],
  service_account:['Compte de service (JSON)','Contenu du fichier téléchargé depuis la console Firebase'],
  environment:['Environnement','sandbox ou live'],client_id:['Client ID'],client_secret:['Secret'],webhook_id:['ID du webhook'],
  site_id:['Identifiant du site (site ID)'],public_key:['Clé publique'],private_key:['Clé privée'],api_user:['Utilisateur API (API user)'],subscription_key:['Clé d’abonnement Collection'],
  webhook_hash:['Empreinte secrète des webhooks','Flutterwave : « Secret hash » choisi dans Paramètres › Webhooks'],
  account_email:['E-mail du compte marchand','Transmis comme client au prestataire, à la place de celui du payeur'],
  master_key:['Clé principale (master key)'],token:['Jeton (token)'],
};
const publicFields=new Set(['environment','client_id','webhook_id','site_id','public_key','api_user','account_email','host','port','security','username','from_email','from_name','phone_number_id','template_name','template_language','sms_sender','account_sid','from','base_url','sender','sender_id','region','access_key_id','app_id']);
// Champs longs, collés en entier (le JSON du compte de service garde ses sauts de ligne).
const longFields=new Set(['service_account']);
const integrationFeatures:Record<string,NotificationFeature[]>={SMTP:['Text','Html'],WHATSAPP_META:['Text','ProviderTemplates'],FAKE:['Text'],MAILJET:['Text','Html'],BREVO:['Text','Html'],BREVO_SMS:['Text'],TWILIO_WHATSAPP:['Text'],SES:['Text','Html'],RESEND:['Text','Html'],TWILIO:['Text'],INFOBIP:['Text'],TERMII:['Text'],AFRICASTALKING:['Text'],FCM:['Text'],ONESIGNAL:['Text']};
const codes=(text:string)=>text.split(/[,\s]+/).map(x=>x.trim().toUpperCase()).filter(Boolean);

function useLoad<T>(load:()=>Promise<T>){
  const [data,setData]=useState<T|null>(null);
  const [error,setError]=useState<string>();
  const reload=useCallback(async()=>{try{setError(undefined);setData(await load())}catch(e){setError(describe(e))}},[load]);
  useEffect(()=>{void reload()},[reload]);
  return {data,error,reload};
}

function Checks<T extends string>({options,labels,value,onChange}:{options:T[];labels:Record<T,string>;value:T[];onChange:(v:T[])=>void}){
  return <div className="check-grid">{options.map(o=><label key={o} className="check-item"><input type="checkbox" checked={value.includes(o)} onChange={e=>onChange(e.target.checked?[...value,o]:value.filter(x=>x!==o))}/>{labels[o]}</label>)}</div>;
}

function Modal({open,onClose,title,description,onSubmit,busy,children,submit='Enregistrer'}:{open:boolean;onClose:()=>void;title:string;description:string;onSubmit:()=>Promise<boolean>;busy?:boolean;children:ReactNode;submit?:string}){
  const [pending,setPending]=useState(false);
  const send=async(e:FormEvent)=>{e.preventDefault();setPending(true);try{if(await onSubmit())onClose()}finally{setPending(false)}};
  return <Dialog open={open} onOpenChange={v=>!v&&onClose()}><DialogContent className="form-dialog"><DialogHeader><DialogTitle>{title}</DialogTitle><DialogDescription>{description}</DialogDescription></DialogHeader><form onSubmit={send}><div className="form-stack">{children}</div><DialogFooter><Button type="button" variant="outline" onClick={onClose}>Annuler</Button><Button type="submit" disabled={pending||busy}>{pending?'Envoi…':submit}</Button></DialogFooter></form></DialogContent></Dialog>;
}

function CredentialsDialog({title,fields,open,onClose,save,note}:{title:string;fields:string[];open:boolean;onClose:()=>void;save:(values:Record<string,string>)=>Promise<boolean>;note?:ReactNode}){
  const [values,setValues]=useState<Record<string,string>>({});
  const close=()=>{setValues({});onClose()};
  return <Modal open={open} onClose={close} title={title} description="Les valeurs sont chiffrées et rangées au coffre côté serveur. Elles ne sont jamais réaffichées : saisissez-les toutes à chaque mise à jour." onSubmit={async()=>{const ok=await save(values);if(ok)setValues({});return ok}} submit="Enregistrer au coffre">
    {note}
    {fields.length?fields.map(f=><label key={f} className="settings-field"><span>{credentialLabels[f]?.[0]??f}</span>{longFields.has(f)?<textarea className="secret-area" rows={6} spellCheck={false} autoComplete="off" required value={values[f]??''} placeholder={credentialLabels[f]?.[1]} onChange={e=>setValues(v=>({...v,[f]:e.target.value}))}/>:<input type={publicFields.has(f)?'text':'password'} autoComplete={publicFields.has(f)?'off':'new-password'} spellCheck={false} required value={values[f]??''} placeholder={credentialLabels[f]?.[1]} onChange={e=>setValues(v=>({...v,[f]:e.target.value}))}/>}</label>):<p className="muted">Cette intégration ne demande aucun identifiant.</p>}
  </Modal>;
}

function PaymentForm({provider,integrations,open,onClose}:{provider?:PaymentProviderDto;integrations:PaymentIntegrationDto[];open:boolean;onClose:()=>void}){
  const {run}=useAdmin();
  const free=integrations.filter(i=>!i.isRegistered);
  const [type,setType]=useState<PaymentProviderType>(provider?.type??(free.length?'Online':'OnSite'));
  const [code,setCode]=useState(provider?.code??free[0]?.code??'');
  const [name,setName]=useState(provider?.displayName??free[0]?.name??'');
  const [methods,setMethods]=useState<PaymentMethodType[]>(provider?.capability.methods??[]);
  const [currencies,setCurrencies]=useState((provider?.capability.currencies??[]).join(', '));
  const [countries,setCountries]=useState((provider?.capability.countries??[]).join(', '));
  const [flags,setFlags]=useState({supportsRefund:provider?.capability.supportsRefund??false,supportsPartialRefund:provider?.capability.supportsPartialRefund??false,supportsWebhooks:provider?.capability.supportsWebhooks??false,supportsAuthorization:provider?.capability.supportsAuthorization??false,supportsRecurring:provider?.capability.supportsRecurring??false});
  const available=type==='Online'?onlineMethods:onSiteMethods;
  const submit=async()=>{
    if(!methods.length){toast.error('Choisissez au moins un moyen de paiement.');return false}
    const capability:PaymentCapabilityInput={methods,currencies:codes(currencies),countries:codes(countries),...flags};
    return provider
      ?run('Prestataire modifié',()=>paymentProviders.update(provider.id,name.trim(),capability),touches.none)
      :run('Prestataire ajouté (désactivé)',()=>paymentProviders.register({code:code.trim().toLowerCase(),type,displayName:name.trim(),capability}),touches.none);
  };
  return <Modal open={open} onClose={onClose} title={provider?`Modifier ${provider.displayName}`:'Ajouter un prestataire de paiement'} description={provider?'Moyens, devises et pays proposés aux restaurants.':'Il est ajouté désactivé : activez-le une fois configuré.'} onSubmit={submit}>
    {!provider&&<label className="settings-field"><span>Type</span><select value={type} onChange={e=>{const t=e.target.value as PaymentProviderType;setType(t);setMethods([]);if(t==='Online'&&free[0]){setCode(free[0].code);setName(free[0].name)}else{setCode('');setName('')}}}><option value="Online">En ligne (webhook signé)</option><option value="OnSite">Sur place</option></select></label>}
    {!provider&&(type==='Online'?<label className="settings-field"><span>Intégration</span>{free.length?<select value={code} onChange={e=>{setCode(e.target.value);setName(free.find(i=>i.code===e.target.value)?.name??'')}}>{free.map(i=><option key={i.code} value={i.code}>{i.name} ({i.code})</option>)}</select>:<small className="muted">Toutes les intégrations du code sont déjà au catalogue.</small>}</label>:<label className="settings-field"><span>Code (ex. cash, card_terminal)</span><input required value={code} onChange={e=>setCode(e.target.value)}/></label>)}
    <label className="settings-field"><span>Nom affiché</span><input required value={name} onChange={e=>setName(e.target.value)}/></label>
    <div className="settings-field"><span>Moyens de paiement</span><Checks options={available} labels={methodLabels} value={methods} onChange={setMethods}/></div>
    <label className="settings-field"><span>Devises ISO (vide : toutes)</span><input placeholder="EUR, XOF" value={currencies} onChange={e=>setCurrencies(e.target.value)}/></label>
    <label className="settings-field"><span>Pays ISO (vide : tous)</span><input placeholder="FR, BJ" value={countries} onChange={e=>setCountries(e.target.value)}/></label>
    {type==='Online'&&<div className="check-grid">{([['supportsRefund','Remboursement'],['supportsPartialRefund','Remboursement partiel'],['supportsWebhooks','Webhooks'],['supportsAuthorization','Autorisation puis capture'],['supportsRecurring','Paiement récurrent']] as const).map(([k,l])=><label key={k} className="check-item"><input type="checkbox" checked={flags[k]} onChange={e=>setFlags(f=>({...f,[k]:e.target.checked}))}/>{l}</label>)}</div>}
  </Modal>;
}

const methodIcons:Partial<Record<PaymentMethodType,typeof CreditCard>>={Card:CreditCard,ApplePay:Smartphone,GooglePay:Smartphone,PayPal:Wallet,MobileMoney:Smartphone,BankTransfer:Landmark,Cash:Banknote,CardOnSite:CreditCard,MobileMoneyOnSite:Smartphone,MealVoucher:Ticket};

function MethodChips({methods}:{methods:PaymentMethodType[]}){
  if(!methods.length)return <span className="method-chip muted-chip">Moyens non renseignés</span>;
  return <>{methods.map(m=>{const Icon=methodIcons[m]??CreditCard;return <span key={m} className="method-chip"><Icon size={13}/>{methodLabels[m]??m}</span>})}</>;
}

/** L'état d'un prestataire pour la devise affichée, du plus utile au moins utile. */
function candidateState(c:CollectorCandidate,on:boolean,isDefault:boolean,count:number){
  if(on)return isDefault&&count>1?'Par défaut':'Activé';
  if(c.ready)return 'Prêt';
  if(c.missing.includes('integration'))return 'Absent du code';
  if(c.missing.includes('disabled'))return 'Inactif';
  if(c.missing.includes('currency'))return 'Devise non autorisée';
  return 'À configurer';
}

/**
 * Les prestataires qui encaissent les abonnements des restaurants sur le compte de la plateforme, devise par devise :
 * l'interrupteur propose le prestataire aux restaurants (il doit être prêt : actif, devise autorisée, compte plateforme
 * enregistré), « Configurer » complète ce qui manque. Le restaurant choisit au moment de payer ; le premier activé est
 * proposé par défaut.
 */
function SubscriptionCollection({providers,version,onCredentials,onEdit}:{providers:PaymentProviderDto[];version:number;onCredentials:(provider:PaymentProviderDto)=>void;onEdit:(provider:PaymentProviderDto)=>void}){
  const {run}=useAdmin();
  const load=useCallback(()=>billingAdmin.onlinePayment(),[]);
  const {data,error,reload}=useLoad(load);
  const [currency,setCurrency]=useState('');
  useEffect(()=>{if(version)void reload()},[version,reload]);
  const save=async(code:string,list:string[],label:string)=>{if(await run(label,()=>billingAdmin.setOnlinePayment(code,list),touches.none))await reload()};
  if(error)return <section className="panel collect-panel"><div className="notice warn">{error}</div></section>;
  if(!data)return <section className="panel collect-panel"><LoadingSkeleton/></section>;
  const route=data.find(r=>r.currencyCode===currency)??data[0];
  const candidates=route?.candidates??[];
  const offered=route?.chosen?offeredOf(route):[];
  const ordered=[...offered.map(code=>candidates.find(c=>c.provider===code)).filter((c):c is CollectorCandidate=>!!c),...candidates.filter(c=>!offered.includes(c.provider)).sort((a,b)=>Number(b.ready)-Number(a.ready))];
  const toggle=(code:string,on:boolean,name:string)=>void save(route!.currencyCode,on?[...offered,code]:offered.filter(p=>p!==code),on?`${name} proposé pour ${route!.currencyCode}`:`${name} retiré pour ${route!.currencyCode}`);
  const makeDefault=(code:string,name:string)=>void save(route!.currencyCode,[code,...offered.filter(p=>p!==code)],`${name} proposé par défaut`);
  const configure=(c:CollectorCandidate,provider?:PaymentProviderDto)=>{if(!provider)return;if(c.missing.includes('disabled')||c.missing.includes('currency'))onEdit(provider);else onCredentials(provider)};
  const defaultName=offered.length?candidates.find(c=>c.provider===offered[0])?.name??offered[0]:null;

  return <div className="collect-layout">
    <section className="panel collect-panel">
      <div className="collect-head">
        <div><h2>Encaissement des abonnements</h2><p className="muted">Activez les prestataires proposés aux restaurants pour payer leur abonnement, sur le compte de la plateforme.</p></div>
        {data.length>0&&<select aria-label="Devise" className="collect-currency" value={route?.currencyCode??''} onChange={e=>setCurrency(e.target.value)}>{data.map(r=><option key={r.currencyCode} value={r.currencyCode}>{r.currencyCode}{r.chosen?` · ${offeredOf(r).length} activé${offeredOf(r).length>1?'s':''}`:''}</option>)}</select>}
      </div>
      {!route?<EmptyState title="Aucune devise" description="Créez un plan actif pour choisir les prestataires de sa devise."/>
        :<>
          <p className={`collect-summary ${route.ready?'ok':'warn'}`}>{offered.length
            ?<>{offered.length>1?`${offered.length} prestataires au choix du restaurant, ${defaultName} par défaut.`:`${defaultName} encaisse les abonnements en ${route.currencyCode}.`}{!route.ready&&' Aucun n’est prêt pour le moment : les restaurants ne peuvent pas payer en ligne.'}</>
            :route.provider?<>Réglage du serveur : {route.provider}{route.ready?'':' (pas prêt)'}, tant qu’aucun prestataire n’est activé ici.</>
            :<>Aucun prestataire activé : les restaurants en {route.currencyCode} ne peuvent pas payer en ligne. Leurs paiements s’enregistrent à la main depuis Abonnements.</>}</p>
          {route.candidates==null?<p className="block-note">L’API ne renvoie pas encore l’état des prestataires : reconstruisez et redémarrez l’API.</p>
            :!candidates.length?<p className="block-note">Aucun prestataire en ligne dans le catalogue : ajoutez-en un ci-dessous (bouton « Ajouter »).</p>
            :<div className="collect-grid">{ordered.map(c=>{
              const on=offered.includes(c.provider);
              const provider=providers.find(p=>p.code===c.provider);
              const methods=(c.methods??provider?.capability.methods??[]) as PaymentMethodType[];
              const state=candidateState(c,on,offered[0]===c.provider,offered.length);
              const canConfigure=!!provider&&!c.missing.includes('integration');
              return <article key={c.provider} className={`collect-card${on?' on':''}`}>
                <ProviderTile code={c.provider} name={c.name}/>
                <div className="collect-card-body">
                  <div className="collect-card-title"><strong>{c.name}</strong><StatusBadge value={state}/>{c.provider==='sandbox'&&<StatusBadge value="Test"/>}</div>
                  <p className="collect-card-text">{c.ready||on?(methods.length?methods.map(m=>methodLabels[m]??m).join(', '):'Moyens non renseignés'):`À compléter : ${missingText(c)}.`}</p>
                  <div className="method-chips"><MethodChips methods={methods}/></div>
                  <div className="collect-card-actions">
                    {on&&offered[0]!==c.provider?<button type="button" className="text-link" onClick={()=>makeDefault(c.provider,c.name)}>Proposer par défaut</button>:<span/>}
                    <button type="button" className="card-btn" disabled={!canConfigure} title={canConfigure?undefined:'Prestataire absent du catalogue ou du code'} onClick={()=>configure(c,provider)}>Configurer<ArrowRight size={14}/></button>
                  </div>
                </div>
                <label className="switch" title={!on&&!c.ready?'À configurer avant de l’activer':undefined}>
                  <input type="checkbox" role="switch" checked={on} disabled={!on&&!c.ready} aria-label={`Proposer ${c.name} pour ${route.currencyCode}`} onChange={e=>toggle(c.provider,e.target.checked,c.name)}/>
                  <span aria-hidden="true"/>
                </label>
              </article>})}</div>}
        </>}
    </section>
    <aside className="panel collect-aside">
      <div className="collect-aside-art" aria-hidden="true"><ShieldCheck size={46}/></div>
      <h3>Clés protégées</h3>
      <p className="muted">Les restaurants paient sur la page du prestataire : la plateforme ne voit ni ne stocke aucune donnée de carte.</p>
      <ul className="collect-facts-list">
        <li><Lock size={15}/>Identifiants chiffrés au coffre, jamais réaffichés</li>
        <li><RefreshCw size={15}/>Paiement relu chez le prestataire jusqu’à sa confirmation</li>
        <li><Receipt size={15}/>Facture disponible dès la confirmation</li>
        <li><Coins size={15}/>Un ou plusieurs prestataires par devise</li>
      </ul>
    </aside>
  </div>;
}

function Payments(){
  const {run}=useAdmin();
  const load=useCallback(()=>Promise.all([paymentProviders.list(),paymentProviders.integrations()]),[]);
  const {data,error,reload}=useLoad(load);
  const [form,setForm]=useState<{provider?:PaymentProviderDto}|null>(null);
  const [credentials,setCredentials]=useState<PaymentProviderDto|null>(null);
  const [version,setVersion]=useState(0);
  const [query,setQuery]=useState('');
  const act=async(label:string,action:()=>Promise<unknown>)=>{const ok=await run(label,action,touches.none);if(ok){await reload();setVersion(v=>v+1)}return ok};
  if(error)return <div className="notice warn">{error} <button className="text-link" onClick={()=>void reload()}>Réessayer</button></div>;
  if(!data)return <LoadingSkeleton/>;
  const [providers,integrations]=data;
  const needle=query.trim().toLowerCase();
  const shown=needle?providers.filter(p=>`${p.displayName} ${p.code} ${p.capability.methods.map(m=>methodLabels[m]).join(' ')}`.toLowerCase().includes(needle)):providers;
  return <>
    <SubscriptionCollection providers={providers} version={version} onCredentials={setCredentials} onEdit={p=>setForm({provider:p})}/>
    <section className="panel catalog-panel">
      <div className="settings-heading"><div><h2>Prestataires de paiement</h2><p className="muted">Le catalogue proposé aux restaurants pour encaisser leurs commandes.</p></div><div className="page-actions"><SearchInput value={query} onChange={setQuery} placeholder="Rechercher un prestataire…"/><Button variant="outline" onClick={()=>void reload()}><RefreshCw size={15}/>Actualiser</Button><Button onClick={()=>setForm({})}><Plus size={15}/>Ajouter</Button></div></div>
      {providers.length?shown.length?<div className="table-scroll"><table><thead><tr><th>Prestataire</th><th>Type</th><th>Moyens</th><th>Devises</th><th>Statut</th><th>Actions</th></tr></thead><tbody>{shown.map(p=><tr key={p.id}>
        <td><span className="provider-cell"><ProviderTile code={p.code} name={p.displayName}/><span><strong>{p.displayName}</strong><small><code>{p.code}</code>{p.type==='Online'&&!p.hasIntegration&&' · intégration absente du code'}</small></span></span></td>
        <td>{p.type==='Online'?'En ligne':'Sur place'}</td>
        <td><div className="method-chips"><MethodChips methods={p.capability.methods}/></div></td>
        <td>{p.capability.currencies.join(', ')||'Toutes'}</td>
        <td><StatusBadge value={p.isEnabled?'Actif':'Inactif'}/></td>
        <td><div className="row-actions">
          <Button size="sm" variant="outline" onClick={()=>setForm({provider:p})}><Pencil size={14}/>Modifier</Button>
          {p.type==='Online'&&<Button size="sm" variant="outline" onClick={()=>setCredentials(p)}><KeyRound size={14}/>Compte plateforme</Button>}
          {p.isEnabled?<ConfirmDialog title={`Désactiver ${p.displayName} ?`} description="Les restaurants ne pourront plus créer de nouveaux paiements avec ce prestataire." onConfirm={()=>void act('Prestataire désactivé',()=>paymentProviders.disable(p.id))}><Button size="sm" variant="outline" className="danger-outline"><PowerOff size={14}/>Désactiver</Button></ConfirmDialog>:<Button size="sm" variant="outline" onClick={()=>void act('Prestataire activé',()=>paymentProviders.enable(p.id))}><Power size={14}/>Activer</Button>}
        </div></td></tr>)}</tbody></table></div>:<p className="muted">Aucun prestataire ne correspond à « {query} ».</p>:<EmptyState title="Aucun prestataire" description="Ajoutez un prestataire en ligne (intégration du code) ou sur place (espèces, TPE...)."/>}
    </section>
    {form&&<PaymentForm key={form.provider?.id??'new'} provider={form.provider} integrations={integrations} open onClose={()=>{setForm(null);void reload();setVersion(v=>v+1)}}/>}
    {credentials&&<CredentialsDialog open title={`Compte plateforme · ${credentials.displayName}`} note={<PaymentWebhookNote code={credentials.code}/>} fields={integrations.find(i=>i.code===credentials.code)?.credentialFields??credentials.credentialFields} onClose={()=>setCredentials(null)} save={values=>act('Identifiants enregistrés',()=>paymentProviders.setCredentials(credentials.id,values))}/>}
  </>;
}

function NotificationForm({integrations,open,onClose,initialCode}:{integrations:NotificationIntegrationDto[];open:boolean;onClose:()=>void;initialCode?:string}){
  const {run}=useAdmin();
  const first=integrations.find(i=>i.provider===initialCode)??integrations[0];
  const [code,setCode]=useState(first?.provider??'');
  const integration=integrations.find(i=>i.provider===code);
  const [name,setName]=useState(integrationNames[first?.provider??'']??'');
  const [channel,setChannel]=useState<NotificationChannel>(integration?.channels[0]??'Email');
  const [configurationType,setConfigurationType]=useState<NotificationConfigurationType>('PlatformOrTenant');
  const [features,setFeatures]=useState<NotificationFeature[]>(integrationFeatures[first?.provider??'']??['Text']);
  const channels=integration?.channels.length?integration.channels:(Object.keys(channelLabels) as NotificationChannel[]);
  const submit=async()=>{
    if(!integration){toast.error('Aucune intégration disponible.');return false}
    if(!features.length){toast.error('Choisissez au moins une fonctionnalité.');return false}
    return run('Prestataire ajouté (désactivé)',()=>notificationProviders.create({code:code.trim().toLowerCase(),name:name.trim(),channel,configurationType,capability:{features}}),touches.none);
  };
  return <Modal open={open} onClose={onClose} title="Ajouter un prestataire de notification" description="Son code doit correspondre à une intégration du code. Il est ajouté désactivé." onSubmit={submit}>
    <label className="settings-field"><span>Intégration</span>{integrations.length?<select value={code} onChange={e=>{const value=e.target.value;setCode(value);const i=integrations.find(x=>x.provider===value);if(i?.channels[0])setChannel(i.channels[0]);if(integrationNames[value])setName(integrationNames[value]);if(integrationFeatures[value])setFeatures(integrationFeatures[value])}}>{integrations.map(i=><option key={i.provider} value={i.provider}>{integrationNames[i.provider]??i.provider} ({i.channels.map(c=>channelLabels[c]).join(', ')})</option>)}</select>:<small className="muted">Toutes les intégrations présentes dans le code sont déjà au catalogue. Un nouveau prestataire (Postmark, SendGrid…) demande d’abord son intégration côté API.</small>}</label>
    <label className="settings-field"><span>Nom affiché</span><input required value={name} onChange={e=>setName(e.target.value)}/></label>
    <label className="settings-field"><span>Canal</span><select value={channel} onChange={e=>setChannel(e.target.value as NotificationChannel)}>{channels.map(c=><option key={c} value={c}>{channelLabels[c]}</option>)}</select></label>
    <label className="settings-field"><span>Compte utilisé</span><select value={configurationType} onChange={e=>setConfigurationType(e.target.value as NotificationConfigurationType)}>{(Object.keys(configurationLabels) as NotificationConfigurationType[]).map(c=><option key={c} value={c}>{configurationLabels[c]}</option>)}</select></label>
    <div className="settings-field"><span>Fonctionnalités</span><Checks options={Object.keys(featureLabels) as NotificationFeature[]} labels={featureLabels} value={features} onChange={setFeatures}/></div>
  </Modal>;
}

/** Webhook du compte plateforme chez le prestataire : une adresse par prestataire, sans id. */
function PaymentWebhookNote({code}:{code:string}){
  return <p className="notice">Webhook à déclarer chez le prestataire (compte de la plateforme) : <code>{`${API_URL}/v1/payments/webhooks/${code}`}</code>. Il confirme les paiements d’abonnement tout de suite ; sans lui, la page de paiement puis une relecture toutes les 15 minutes s’en chargent.</p>;
}

/** Où déclarer le webhook de remise et comment il est authentifié, pour le prestataire dont on saisit le compte. */
function NotificationAccountNote({code}:{code:string}){
  const url=`${API_URL}/v1/notifications/webhooks/${code}`;
  if(code==='WHATSAPP_META')return <p className="notice">Chez Meta, URL de rappel du webhook : <code>{url}</code>, avec le même jeton de vérification que ci-dessous ; abonnez le champ <code>messages</code>.</p>;
  if(code==='SMTP')return <p className="notice">Le serveur SMTP n’envoie pas d’accusé de remise : un email accepté reste « envoyé ».</p>;
  const hint=webhookHints[code];
  if(!hint)return null;
  const basic=hint.includes('Basic');
  return <p className="notice">{hint}{basic&&<> URL : <code>{url.replace('://','://restauspace:<mot de passe>@')}</code></>}{!basic&&!hint.startsWith('Pas')&&!hint.startsWith('Les rebonds')&&<> URL : <code>{url}</code></>}</p>;
}

function NotificationsProviders(){
  const {run}=useAdmin();
  const [adding,setAdding]=useState<{integrations:NotificationIntegrationDto[];code?:string}|null>(null);
  const [credentials,setCredentials]=useState<{provider:NotificationProviderDto;integrations:NotificationIntegrationDto[]}|null>(null);
  const [version,setVersion]=useState(0);
  const act=async(label:string,action:()=>Promise<unknown>)=>{const ok=await run(label,action,touches.none);if(ok)setVersion(v=>v+1);return ok};
  const target=credentials?.provider;
  return <NotificationsWorkspace version={version}
    onCredentials={(provider,integrations)=>setCredentials({provider,integrations})}
    onAdd={(integrations,providers,code)=>{const registered=new Set(providers.map(p=>p.code.toUpperCase()));setAdding({integrations:integrations.filter(i=>!registered.has(i.provider.toUpperCase())),code})}}
    dialogs={<>
      {adding&&<NotificationForm integrations={adding.integrations} initialCode={adding.code} open onClose={()=>{setAdding(null);setVersion(v=>v+1)}}/>}
      {target&&<CredentialsDialog open title={`Compte plateforme · ${target.name}`} note={<NotificationAccountNote code={target.code.toUpperCase()}/>} fields={credentials.integrations.find(i=>i.provider.toUpperCase()===target.code.toUpperCase())?.credentialFields??[]} onClose={()=>setCredentials(null)} save={values=>act('Identifiants enregistrés',()=>notificationProviders.setCredentials(target.id,values))}/>}
    </>}/>;
}

function Account(){
  const {me,loadedAt}=useAdmin();
  return <>
    <h2>Compte et API</h2>
    <dl className="info-list">
      <div><dt>Compte connecté</dt><dd>{me.email}</dd></div>
      <div><dt>Rôle</dt><dd>Administrateur de la plateforme</dd></div>
      <div><dt>API</dt><dd><code>{API_URL}</code></dd></div>
      <div><dt>Dernier chargement</dt><dd>{loadedAt?loadedAt.toLocaleString('fr-FR'):'—'}</dd></div>
    </dl>
    <div className="drawer-actions">
      <a className="text-link" href={`${API_URL}/docs`} target="_blank" rel="noreferrer"><ExternalLink size={14}/> Documentation interactive (Scalar)</a>
      <a className="text-link" href={`${API_URL}/openapi/v1.json`} target="_blank" rel="noreferrer"><ExternalLink size={14}/> Document OpenAPI</a>
    </div>
    <p className="tiny-note">Les administrateurs sont déclarés côté serveur dans Authorization:PlatformAdminEmails. La session est conservée dans cet onglet uniquement.</p>
  </>;
}

export default function Settings(){
  const [tab,setTab]=useState<string>('Paiements');
  return <><PageHeader title="Paramètres" subtitle="Prestataires de paiement et de notification de la plateforme, accès à l’API."><a className="doc-link" href={`${API_URL}/docs`} target="_blank" rel="noreferrer"><BookOpen size={16}/>Documentation</a></PageHeader>
    <nav className="settings-tabs" aria-label="Sections des paramètres">{sections.map(([name,Icon])=><button key={name} aria-current={tab===name?'page':undefined} className={tab===name?'active':''} onClick={()=>setTab(name)}><Icon size={16}/>{name}</button>)}</nav>
    {tab==='Paiements'?<div className="payments-page"><Payments/></div>:tab==='Notifications'?<NotificationsProviders/>:<section className="panel settings-panel"><Account/></section>}</>;
}
