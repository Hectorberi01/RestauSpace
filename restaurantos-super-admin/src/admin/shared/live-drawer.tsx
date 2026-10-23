'use client';
import {useEffect,useState,type ComponentType,type ReactNode} from 'react';
import {Edit,Pause,Play,Ban,LogOut,Trash2,Check,X,Receipt,Clock,XCircle,Power,Download,Mail,Plus,Gift,Info as InfoIcon,Hash,Store,Coins,Globe,Percent,ShieldCheck,CreditCard,Layers,CalendarDays,FileText,Copy,MoreHorizontal,ShoppingCart,BarChart3,UserPlus} from 'lucide-react';
import {Button} from '@/components/ui/button';
import {DropdownMenu,DropdownMenuContent,DropdownMenuItem,DropdownMenuTrigger} from '@/components/ui/dropdown-menu';
import {toast} from 'sonner';
import {touches,useAdmin,type LiveDomain} from '../../hooks/use-admin';
import {TenantOnlineSales} from '../online-sales/tenant-online';
import {DetailDrawer,ConfirmDialog,FormDialog} from '../../components/feedback/dialogs';
import {StatusBadge,UserAvatar,ActivityTimeline,LoadingSkeleton} from '../../components/ui/shared';
import {describe} from '../../services/api/client';
import {addOns as addOnsApi,subscriptions as subscriptionsApi,tenants as tenantsApi,type SubscriptionCharge,type TenantAddOnDto,type TenantDto} from '../../services/api/admin-api';
import {monthly,shortId,subscriptionStatus} from '../../services/api/mappers';
import {activityLabel} from '../../services/api/profile';
import {money,date,dateTime} from '../../utils/format';
import {can,reasonFromLabel,subscriptionReasons,subscriptions,tenantReasons,tenants,users} from './live-actions';
import {ComplimentaryDialog} from './complimentary';
import {TenantActivity} from './tenant-activity';
import type {RecordItem} from '../../types';

function Info({items}:{items:[string,ReactNode][]}){
  return <dl className="info-list">{items.map(([label,value])=><div key={label}><dt>{label}</dt><dd>{typeof value==='boolean'?value?'Oui':'Non':value===''||value===null||value===undefined?'—':value}</dd></div>)}</dl>;
}

function Section({icon,title,action,children}:{icon:ReactNode;title:string;action?:ReactNode;children:ReactNode}){
  return <section className="drawer-section"><div className="drawer-section-head"><h4>{icon}{title}</h4>{action}</div>{children}</section>;
}

function Rows({items}:{items:[ComponentType<{size?:number}>,string,ReactNode][]}){
  return <dl className="drawer-rows">{items.map(([Icon,label,value])=><div key={label}><span className="row-icon"><Icon size={15}/></span><dt>{label}</dt><dd>{value===''||value===null||value===undefined?'—':value}</dd></div>)}</dl>;
}

function currencyName(code:string){
  try{const name=new Intl.DisplayNames(['fr'],{type:'currency'}).of(code);return name&&name!==code?`${code} (${name.replace(/^./,c=>c.toUpperCase())})`:code}catch{return code}
}

function daysLeft(end:string){
  const days=Math.ceil((new Date(end).getTime()-Date.now())/86400000);
  return days>1?`${days} jours restants`:days===1?'1 jour restant':'Se termine aujourd’hui';
}

function Activity({id,name}:{id:string;name?:string}){
  const {state}=useAdmin();
  const items=state['audit-logs'].filter(a=>a.targetId===id||(name!==undefined&&a.tenant===name)).slice(0,15).map(a=>({name:activityLabel(String(a.action)),detail:String(a.user),date:dateTime(a.date)}));
  return items.length?<ActivityTimeline items={items}/>:<p className="muted">Aucune entrée récente dans le journal d’audit.</p>;
}

function Tabs({tabs,tab,setTab}:{tabs:string[];tab:string;setTab:(t:string)=>void}){
  return <div className="drawer-tabs" role="tablist">{tabs.map(t=><button key={t} role="tab" aria-selected={t===tab} className={t===tab?'active':''} onClick={()=>setTab(t)}>{t}</button>)}</div>;
}

function UserDetail({r}:{r:RecordItem}){
  const {run}=useAdmin();
  const [tab,setTab]=useState('Détails');
  return <>
    <Tabs tabs={['Détails','Activité']} tab={tab} setTab={setTab}/>
    {tab==='Activité'?<Activity id={r.id}/>:<>
      <div className="drawer-profile"><UserAvatar name={r.name} large/><h3>{r.name}</h3><span>{shortId(r.id)}</span><StatusBadge value={r.status}/></div>
      <Info items={[['ID',<code key="id">{r.id}</code>],['Email vérifié',<StatusBadge key="v" value={r.verified}/>],['Mot de passe défini',Boolean(r.password)],['Peut se connecter',Boolean(r.canStartSession)],['Création',dateTime(r.date)],['Vérification',r.verifiedAt?dateTime(r.verifiedAt):''],['Mot de passe changé',r.passwordChangedAt?dateTime(r.passwordChangedAt):'']]}/>
    </>}
    <div className="drawer-actions">
      <FormDialog title="Modifier l’email" fields={[{key:'email',label:'Nouvel email',type:'email'}]} initial={{email:r.email}} description="Un message de vérification est envoyé à la nouvelle adresse." onSave={v=>void run('Email modifié',()=>users.changeEmail(r.id,String(v.email).trim()),touches.users)}><Button><Edit size={15}/>Modifier l’email</Button></FormDialog>
      {r.apiStatus==='Pending'&&!r.password&&<Button variant="outline" onClick={()=>void run('Invitation renvoyée',()=>users.resendInvitation(r.id),touches.users)}><Mail size={15}/>Renvoyer l’invitation</Button>}
      {can.unlockUser(r)&&<Button variant="outline" onClick={()=>void run('Compte réactivé',()=>users.unlock(r.id),touches.users)}><Play size={15}/>Réactiver</Button>}
      {can.lockUser(r)&&<ConfirmDialog title="Suspendre ce compte ?" description="L’utilisateur ne pourra plus se connecter et ses sessions en cours seront fermées." onConfirm={()=>void run('Compte suspendu',()=>users.lock(r.id),touches.users)}><Button variant="outline"><Pause size={15}/>Suspendre</Button></ConfirmDialog>}
      <ConfirmDialog title="Fermer toutes les sessions ?" description="L’utilisateur devra se reconnecter sur tous ses appareils." danger={false} onConfirm={()=>void run('Sessions fermées',()=>users.revokeSessions(r.id),touches.users)}><Button variant="outline"><LogOut size={15}/>Fermer les sessions</Button></ConfirmDialog>
      {can.disableUser(r)&&<ConfirmDialog title="Désactiver ce compte ?" description="Le compte est désactivé définitivement : l’utilisateur ne pourra plus se connecter." confirmText={String(r.email)} onConfirm={()=>void run('Compte désactivé',()=>users.disable(r.id),touches.users)}><Button variant="outline"><Ban size={15}/>Désactiver</Button></ConfirmDialog>}
      <ConfirmDialog title="Effacer ce compte ?" description="Les données personnelles du compte sont effacées (droit à l’effacement). Cette action est irréversible." confirmText={String(r.email)} onConfirm={()=>void run('Compte effacé',()=>users.erase(r.id),touches.users)}><Button variant="destructive"><Trash2 size={15}/>Effacer le compte</Button></ConfirmDialog>
    </div>
  </>;
}

function TenantAddOns({tenantId,currency}:{tenantId:string;currency?:string}){
  const {run,raw}=useAdmin();
  const [rows,setRows]=useState<TenantAddOnDto[]|null>(null);
  const [error,setError]=useState<string>();
  const [version,setVersion]=useState(0);
  useEffect(()=>{let active=true;addOnsApi.list(tenantId).then(list=>{if(active)setRows(list)}).catch(e=>{if(active)setError(describe(e))});return()=>{active=false}},[tenantId,version]);
  const act=async(success:string,action:()=>Promise<unknown>)=>{if(await run(success,action,touches.restaurants))setVersion(v=>v+1)};
  const active=(rows??[]).filter(x=>x.status==='Active');
  const offered=raw.plans.filter(p=>p.kind==='AddOn'&&p.status==='Active'&&(!currency||p.currencyCode===currency)&&!active.some(x=>x.addOn.id===p.id));
  return <>
    <h3>Options</h3>
    {error?<p className="login-error">{error}</p>:!rows?<LoadingSkeleton/>:<div className="feature-list">
      {active.length===0&&<p className="muted">Aucune option.</p>}
      {active.map(x=><div key={x.id} className="addon-row">
        <p><Check size={15}/>{x.addOn.name} · {money(x.addOn.price,x.addOn.currencyCode)} / {x.addOn.billingInterval==='Yearly'?'an':'mois'}{x.quantity>1?` × ${x.quantity}`:''}</p>
        <span className="addon-actions">
          <FormDialog title={`Quantité : ${x.addOn.name}`} fields={[{key:'quantity',label:'Quantité (ex. nombre de bornes)',type:'number',min:1}]} initial={{quantity:x.quantity}} onSave={v=>void act('Quantité modifiée',()=>addOnsApi.changeQuantity(tenantId,x.id,Number(v.quantity)))}><button className="text-link"><Edit size={14}/> Quantité</button></FormDialog>
          <ConfirmDialog title={`Retirer l’option ${x.addOn.name} ?`} description="Les droits de l’option cessent aussitôt. La résiliation est définitive." onConfirm={()=>void act('Option retirée',()=>addOnsApi.cancel(tenantId,x.id))}><button className="text-link"><XCircle size={14}/> Retirer</button></ConfirmDialog>
        </span>
      </div>)}
    </div>}
    {offered.length===0&&rows&&<p className="muted">{raw.plans.some(p=>p.kind==='AddOn'&&p.status==='Active')?`Aucune autre option active${currency?` en ${currency}`:''} au catalogue.`:'Aucune option au catalogue : créez-en une dans Plans & Fonctionnalités (type « Option »).'}</p>}
    {offered.length>0&&<FormDialog title="Ajouter une option" fields={[{key:'plan',label:'Option',type:'select',options:offered.map(planOption)},{key:'quantity',label:'Quantité (ex. nombre de bornes)',type:'number',min:1,required:false}]} initial={{quantity:1}} onSave={v=>{const p=offered.find(x=>planOption(x)===v.plan);if(p)void act('Option ajoutée',()=>addOnsApi.activate(tenantId,p.id,Number(v.quantity)>0?Number(v.quantity):1))}}><Button variant="outline"><Plus size={15}/>Ajouter une option</Button></FormDialog>}
  </>;
}

const planOption=(p:{name:string;price:number;currencyCode:string;billingInterval:string})=>`${p.name} · ${money(p.price,p.currencyCode)} / ${p.billingInterval==='Yearly'?'an':'mois'}`;

/** Copie une valeur dans le presse-papiers et le confirme. */
function copy(value:string,label:string){void navigator.clipboard?.writeText(value).then(()=>toast.success(`${label} copié`),()=>toast.error('Copie impossible'))}

function CopyButton({value,label}:{value:string;label:string}){
  return <button type="button" className="copy-button" aria-label={`Copier ${label.toLowerCase()}`} title="Copier" onClick={e=>{e.stopPropagation();copy(value,label)}}><Copy size={13}/></button>;
}

const restaurantTabs:[string,ComponentType<{size?:number}>][]=[['Détails',FileText],['Abonnement',CalendarDays],['Vente en ligne',ShoppingCart],['Activité',BarChart3]];

function IconTabs({tab,setTab}:{tab:string;setTab:(t:string)=>void}){
  return <div className="drawer-tabs icon-tabs" role="tablist">{restaurantTabs.map(([t,Icon])=><button key={t} role="tab" aria-selected={t===tab} className={t===tab?'active':''} onClick={()=>setTab(t)}><Icon size={16}/>{t}</button>)}</div>;
}

/** Le restaurant : en-tête (identité, devise, fuseau, création), onglets, et actions fixées en bas. */
function RestaurantDrawer({r,onClose}:{r:RecordItem;onClose:()=>void}){
  const {run,raw}=useAdmin();
  const [tab,setTab]=useState('Détails');
  const [tenant,setTenant]=useState<TenantDto|null>(null);
  const [error,setError]=useState<string>();
  useEffect(()=>{let active=true;tenantsApi.get(r.id).then(t=>{if(active)setTenant(t)}).catch(e=>{if(active)setError(describe(e))});return()=>{active=false}},[r.id,r.status,r.name]);
  const subs=raw.subscriptions.filter(s=>s.tenantId===r.id).sort((a,b)=>b.startedAtUtc.localeCompare(a.startedAtUtc));
  const current=subs.find(s=>s.status!=='Cancelled')??subs[0];
  const plan=current?raw.plans.find(p=>p.id===current.planId):undefined;
  const activePlans=raw.plans.filter(p=>p.status==='Active'&&p.kind!=='AddOn'&&(!tenant||p.currencyCode===tenant.defaultCurrencyCode));
  const canStart=r.apiStatus!=='Closed'&&(!current||current.status==='Cancelled')&&activePlans.length>0;
  const lastPayment=current?raw.payments.filter(p=>p.subscriptionId===current.id).sort((a,b)=>b.recordedAtUtc.localeCompare(a.recordedAtUtc))[0]:undefined;
  const reasons=Object.values(tenantReasons);
  const live=current&&current.status!=='Cancelled';

  const rename=(trigger:ReactNode)=><FormDialog title="Renommer le restaurant" fields={[{key:'name',label:'Nom'}]} initial={{name:r.name}} onSave={v=>void run('Restaurant renommé',()=>tenants.rename(r.id,String(v.name).trim()),touches.subscriptions)}>{trigger}</FormDialog>;
  const changePlan=current&&live&&current.isComplimentary
    ?<ComplimentaryDialog tenantId={r.id} currentPlanId={current.planId} until={current.complimentaryUntilUtc??undefined}><Button variant="outline" size="sm" className="hero-button"><Edit size={14}/>Modifier le plan</Button></ComplimentaryDialog>
    :<Button variant="outline" size="sm" className="hero-button" onClick={()=>setTab('Abonnement')}><Edit size={14}/>Gérer l’abonnement</Button>;

  const details=error?<p className="login-error">{error}</p>:!tenant?<LoadingSkeleton/>:<>
    {current&&live?<div className="plan-hero">
      <div className="plan-hero-top">
        <div>
          <span className="plan-hero-label">Abonnement actuel</span>
          <h3>{plan?.name??String(r.plan)}</h3>
          <strong className="plan-hero-price">{current.isComplimentary?'Offert':plan?money(monthly(plan),plan.currencyCode):'—'}{!current.isComplimentary&&plan&&<small>/mois</small>}</strong>
          <StatusBadge value={subscriptionStatus[current.status]}/>
        </div>
        {changePlan}
      </div>
      <div className="plan-hero-strip">
        <div><span className="strip-icon"><CalendarDays size={16}/></span><span><small>{current.status==='Trialing'?'Essai':'Période en cours'}</small><strong>{current.isComplimentary&&!current.complimentaryUntilUtc?`Depuis le ${date(current.currentPeriodStartUtc)}`:`${date(current.currentPeriodStartUtc)} → ${date(current.currentPeriodEndUtc)}`}</strong>{current.status==='Trialing'&&<em>{daysLeft(current.currentPeriodEndUtc)}</em>}</span></div>
        <div><span className="strip-icon"><Power size={16}/></span><span><small>Renouvellement</small><span className={`status ${current.cancelAtPeriodEnd?'amber':'green'}`}>{current.cancelAtPeriodEnd?'Fin de période':'Automatique'}</span></span></div>
      </div>
    </div>:<div className="plan-hero empty">
      <span className="plan-hero-label">Abonnement</span>
      <h3>Aucun abonnement en cours</h3>
      <p className="muted">Démarrez un abonnement ou offrez un plan pour ouvrir les options (vente en ligne, bornes).</p>
      {r.apiStatus!=='Closed'&&<div className="plan-hero-actions">
        {canStart&&<FormDialog title="Démarrer un abonnement" fields={[{key:'plan',label:'Plan',type:'select',options:activePlans.map(planOption)},{key:'trialDays',label:'Jours d’essai (vide : défaut du plan)',type:'number',required:false}]} onSave={v=>{const p=activePlans.find(x=>planOption(x)===v.plan);if(p)void run('Abonnement démarré',()=>subscriptions.start(r.id,p.id,Number(v.trialDays)>0?Number(v.trialDays):undefined),touches.subscriptions)}}><Button size="sm"><Receipt size={14}/>Démarrer un abonnement</Button></FormDialog>}
        <ComplimentaryDialog tenantId={r.id}><Button size="sm" variant="outline"><Gift size={14}/>Offrir un plan</Button></ComplimentaryDialog>
      </div>}
    </div>}

    {current&&live&&current.isComplimentary&&<div className="drawer-banner"><Gift size={17}/>Plan offert par la plateforme {current.complimentaryUntilUtc?`jusqu’au ${date(current.complimentaryUntilUtc)}`:'sans limite de durée'}.</div>}
    {tenant.suspendedAtUtc&&r.apiStatus==='Suspended'&&<div className="drawer-banner warn"><Pause size={17}/>Suspendu le {dateTime(tenant.suspendedAtUtc)}{tenant.suspensionReason?` · ${tenantReasons[tenant.suspensionReason]}`:''}.</div>}
    {tenant.closedAtUtc&&<div className="drawer-banner danger"><XCircle size={17}/>Fermé le {dateTime(tenant.closedAtUtc)}.</div>}

    <Section icon={<InfoIcon size={17}/>} title="Informations générales" action={r.apiStatus!=='Closed'?rename(<Button variant="outline" size="sm"><Edit size={14}/>Modifier</Button>):undefined}>
      <Rows items={[
        [Store,'Nom',tenant.branding.displayName&&tenant.branding.displayName!==tenant.name?`${tenant.name} (affiché : ${tenant.branding.displayName})`:tenant.name],
        [Hash,'ID',<span key="id" className="row-split"><code>{r.id}</code><CopyButton value={r.id} label="Identifiant"/></span>],
        [Coins,'Devise',currencyName(tenant.defaultCurrencyCode)],
        [Globe,'Fuseau horaire',tenant.timeZoneId],
        [Percent,'Prix saisis',tenant.pricesIncludeTax?'TTC':'HT'],
        [ShieldCheck,'Opérations autorisées',tenant.canAcceptBusinessWrites?'Oui':'Non'],
      ]}/>
    </Section>

    {current&&live&&<div className="drawer-grid">
      <section className="drawer-card tint">
        <h4>Droits du plan</h4>
        {plan&&plan.entitlements.length?<ul className="right-list">{plan.entitlements.map(e=><li key={e.featureCode} className={e.enabled?'':'off'}>{e.enabled?<Check size={15}/>:<X size={15}/>}<span>{e.featureCode}{e.enabled&&e.limit!==null?` (${e.limit})`:''}</span></li>)}</ul>:<p className="muted">Aucun droit défini.</p>}
      </section>
      <div className="drawer-stack">
        <section className="drawer-card tint">
          <h4>Prochaine échéance</h4>
          <NextCharge subscriptionId={current.id}/>
          {lastPayment&&<a className="soft-button" href={`/admin/billing?record=${lastPayment.id}`}><Receipt size={14}/>Voir le dernier paiement</a>}
        </section>
        <section className="drawer-card">
          <TenantAddOns tenantId={r.id} currency={tenant.defaultCurrencyCode}/>
        </section>
      </div>
    </div>}
  </>;

  const subscriptionTab=current?<>
    <div className="subscription-highlight"><span>ABONNEMENT {plan?.name??shortId(current.planId)}</span><strong>{plan?money(monthly(plan),plan.currencyCode):'—'}<small>/ mois</small></strong><StatusBadge value={subscriptionStatus[current.status]}/></div>
    {current.isComplimentary&&<p className="notice">Plan offert par la plateforme {current.complimentaryUntilUtc?`jusqu’au ${date(current.complimentaryUntilUtc)}`:'sans limite de durée'}.</p>}
    <Info items={[['Période en cours',current.isComplimentary&&!current.complimentaryUntilUtc?`Depuis le ${date(current.currentPeriodStartUtc)}`:`${date(current.currentPeriodStartUtc)} → ${date(current.currentPeriodEndUtc)}`],['Renouvellement',current.cancelAtPeriodEnd?'Fin de période':'Automatique'],['Début',dateTime(current.startedAtUtc)],['Résiliation',current.cancelledAtUtc?dateTime(current.cancelledAtUtc):'']]}/>
    <h3>Paiements</h3><PaymentHistory subscriptionId={current.id}/>
    {live&&<div className="inline-actions"><ComplimentaryDialog tenantId={r.id} currentPlanId={current.planId} until={current.complimentaryUntilUtc??undefined}><Button variant="outline"><Gift size={15}/>{current.isComplimentary?'Modifier l’offre':'Offrir le plan'}</Button></ComplimentaryDialog>{can.recordPayment({id:current.id,name:r.name,status:subscriptionStatus[current.status],apiStatus:current.status,complimentary:!!current.isComplimentary})&&<ConfirmDialog title="Enregistrer un paiement ?" description="Le paiement de la période est enregistré : l’abonnement passe ou reste actif et la période est prolongée." danger={false} onConfirm={()=>void run('Paiement enregistré',()=>subscriptions.recordPayment(current.id),touches.subscriptions)}><Button variant="outline"><Receipt size={15}/>Enregistrer un paiement</Button></ConfirmDialog>}</div>}
  </>:<p className="muted">Aucun abonnement : démarrez-en un, ou offrez un plan, depuis l’onglet Détails.</p>;

  const menu=<DropdownMenu><DropdownMenuTrigger asChild><Button variant="outline" size="icon-sm" aria-label="Plus d’actions"><MoreHorizontal size={17}/></Button></DropdownMenuTrigger><DropdownMenuContent align="end">
    <DropdownMenuItem onClick={()=>copy(r.id,'Identifiant')}><Copy size={15}/>Copier l’identifiant</DropdownMenuItem>
    <DropdownMenuItem onClick={()=>setTab('Activité')}><BarChart3 size={15}/>Voir l’activité</DropdownMenuItem>
    {lastPayment&&<DropdownMenuItem asChild><a href={`/admin/billing?record=${lastPayment.id}`}><Receipt size={15}/>Dernier paiement</a></DropdownMenuItem>}
  </DropdownMenuContent></DropdownMenu>;

  return <DetailDrawer open onClose={onClose} title={tenant?.branding.displayName||r.name} badge={<StatusBadge value={r.status}/>}
    lead={<span className="drawer-avatar"><UserAvatar name={r.name} large/></span>}
    subtitle={<span className="drawer-id">ID : {shortId(r.id)}<CopyButton value={r.id} label="Identifiant"/></span>}
    meta={tenant&&<div className="drawer-meta"><span><Coins size={14}/>{tenant.defaultCurrencyCode}</span><span><Globe size={14}/>{tenant.timeZoneId}</span><span><CalendarDays size={14}/>Créé le {date(tenant.createdAtUtc)}</span></div>}
    menu={menu}>
    <IconTabs tab={tab} setTab={setTab}/>
    {tab==='Activité'?<TenantActivity tenantId={r.id}/>:tab==='Vente en ligne'?<TenantOnlineSales tenantId={r.id}/>:tab==='Abonnement'?subscriptionTab:details}
    <div className="drawer-actions">
      {r.apiStatus!=='Closed'&&rename(<Button><Edit size={15}/>Renommer</Button>)}
      {r.apiStatus!=='Closed'&&<FormDialog title="Ajouter un propriétaire" fields={[{key:'email',label:'Email du propriétaire',type:'email'}]} description="Le compte est invité s’il n’existe pas encore." onSave={v=>void run('Propriétaire ajouté',()=>tenants.addOwner(r.id,String(v.email).trim()),['restaurants','users','audit-logs'])}><Button variant="outline"><UserPlus size={15}/>Ajouter un propriétaire</Button></FormDialog>}
      {can.activateTenant(r)&&<Button variant="outline" onClick={()=>void run('Restaurant activé',()=>tenants.activate(r.id),touches.restaurants)}><Power size={15}/>Activer</Button>}
      {can.reactivateTenant(r)&&<Button variant="outline" onClick={()=>void run('Restaurant réactivé',()=>tenants.reactivate(r.id),touches.restaurants)}><Play size={15}/>Réactiver</Button>}
      {can.suspendTenant(r)&&<FormDialog title="Suspendre le restaurant" fields={[{key:'reason',label:'Motif',type:'select',options:reasons}]} description="Le restaurant ne pourra plus enregistrer d’opérations jusqu’à sa réactivation." onSave={v=>void run('Restaurant suspendu',()=>tenants.suspend(r.id,reasonFromLabel(tenantReasons,v.reason)),touches.restaurants)}><Button variant="outline"><Pause size={15}/>Suspendre</Button></FormDialog>}
      {can.closeTenant(r)&&<ConfirmDialog title="Fermer ce restaurant ?" description="La fermeture est définitive : le restaurant ne pourra plus être réactivé." confirmText={r.name} onConfirm={()=>void run('Restaurant fermé',()=>tenants.close(r.id),touches.restaurants)}><Button variant="destructive"><Trash2 size={15}/>Fermer</Button></ConfirmDialog>}
    </div>
  </DetailDrawer>;
}

function SubscriptionDetail({r}:{r:RecordItem}){
  const {run,raw}=useAdmin();
  const plan=raw.plans.find(p=>p.id===r.planId);
  const reasons=Object.values(subscriptionReasons);
  return <>
    <div className="subscription-highlight"><span>ABONNEMENT {String(r.plan)}</span><strong>{r.complimentary?'Offert':plan?money(monthly(plan),plan.currencyCode):'—'}{!r.complimentary&&<small>/ mois</small>}</strong><StatusBadge value={r.status}/></div>
    <Info items={[['Restaurant',r.name],['ID',<code key="id">{r.id}</code>],['Offre de la plateforme',r.complimentary?(r.complimentaryUntil?`Jusqu’au ${date(r.complimentaryUntil)}`:'Sans limite de durée'):''],['Période en cours',`${date(r.periodStart)} → ${date(r.end)}`],['Renouvellement',r.renewal?'Automatique':'Fin de période'],['Facturation',plan?(plan.billingInterval==='Yearly'?`Annuelle (${money(plan.price,plan.currencyCode)})`:'Mensuelle'):''],['Début',dateTime(r.date)],['Motif de suspension',r.suspensionReason?subscriptionReasons[r.suspensionReason as keyof typeof subscriptionReasons]:''],['Résiliation',r.cancelledAt?dateTime(r.cancelledAt):'']]}/>
    {plan&&<><h3>Droits du plan</h3><div className="feature-list">{plan.entitlements.length?plan.entitlements.map(e=><p key={e.featureCode}>{e.enabled?<Check size={15}/>:<X size={15}/>}{e.featureCode}{e.enabled&&e.limit!==null?` (${e.limit})`:''}</p>):<p>Aucun droit défini.</p>}</div></>}
    {r.apiStatus!=='Cancelled'&&<><h3>Prochaine échéance</h3><NextCharge subscriptionId={r.id}/></>}
    <h3>Paiements</h3><PaymentHistory subscriptionId={r.id}/>
    <h3>Activité</h3><Activity id={r.id}/>
    <div className="drawer-actions">
      {can.grantComplimentary(r)&&<ComplimentaryDialog tenantId={String(r.tenantId)} currentPlanId={String(r.planId)} until={r.complimentary&&r.complimentaryUntil?String(r.complimentaryUntil):undefined}><Button variant={r.complimentary?'outline':'default'}><Gift size={15}/>{r.complimentary?'Modifier l’offre':'Offrir le plan'}</Button></ComplimentaryDialog>}
      {can.endComplimentary(r)&&<ConfirmDialog title="Mettre fin à l’offre ?" description="L’offre s’arrête maintenant : le restaurant doit régler la période suivante (délai de grâce de 7 jours, puis suspension)." onConfirm={()=>void run('Offre terminée',()=>subscriptions.endComplimentary(r.id),touches.subscriptions)}><Button variant="outline"><XCircle size={15}/>Mettre fin à l’offre</Button></ConfirmDialog>}
      {can.recordPayment(r)&&<ConfirmDialog title="Enregistrer un paiement ?" description="Le paiement de la période est enregistré : l’abonnement passe ou reste actif et la période est prolongée." danger={false} onConfirm={()=>void run('Paiement enregistré',()=>subscriptions.recordPayment(r.id),touches.subscriptions)}><Button><Receipt size={15}/>Enregistrer un paiement</Button></ConfirmDialog>}
      {can.markPastDue(r)&&<ConfirmDialog title="Marquer en retard de paiement ?" description="L’abonnement passe en impayé ; il reste facturé." onConfirm={()=>void run('Abonnement marqué en retard',()=>subscriptions.markPastDue(r.id),touches.subscriptions)}><Button variant="outline"><Clock size={15}/>Marquer en retard</Button></ConfirmDialog>}
      {can.suspendSubscription(r)&&<FormDialog title="Suspendre l’abonnement" fields={[{key:'reason',label:'Motif',type:'select',options:reasons}]} onSave={v=>void run('Abonnement suspendu',()=>subscriptions.suspend(r.id,reasonFromLabel(subscriptionReasons,v.reason)),touches.subscriptions)}><Button variant="outline"><Pause size={15}/>Suspendre</Button></FormDialog>}
      {can.cancelSubscription(r)&&<ConfirmDialog title="Résilier cet abonnement ?" description="La résiliation est immédiate et définitive." confirmText={r.name} onConfirm={()=>void run('Abonnement résilié',()=>subscriptions.cancel(r.id),touches.subscriptions)}><Button variant="destructive"><XCircle size={15}/>Résilier</Button></ConfirmDialog>}
    </div>
  </>;
}

/** Ce que coûtera la prochaine période : le plan et les options actives, ligne par ligne. */
function NextCharge({subscriptionId}:{subscriptionId:string}){
  const {raw}=useAdmin();
  const [charge,setCharge]=useState<SubscriptionCharge|null>(null);
  const [error,setError]=useState<string>();
  useEffect(()=>{let active=true;setCharge(null);subscriptionsApi.charge(subscriptionId).then(c=>{if(active)setCharge(c)}).catch(e=>{if(active)setError(describe(e))});return()=>{active=false}},[subscriptionId,raw.subscriptions,raw.tenants]);
  if(error)return <p className="login-error">{error}</p>;
  if(!charge)return <LoadingSkeleton/>;
  return <div className="charge-lines">{charge.lines.map(l=><p className="entitlement-row" key={l.planId}><span>{l.label}{l.quantity>1?` × ${l.quantity}`:''}{l.kind==='AddOn'&&<small> option</small>}</span><span>{money(l.amount,charge.currencyCode)}</span></p>)}<p className="entitlement-row charge-total"><span>Total / {charge.billingInterval==='Yearly'?'an':'mois'}</span><span>{money(charge.total,charge.currencyCode)}</span></p></div>;
}

function PaymentHistory({subscriptionId}:{subscriptionId:string}){
  const {raw}=useAdmin();
  const rows=raw.payments.filter(p=>p.subscriptionId===subscriptionId);
  return rows.length?<div>{rows.map(p=><p className="entitlement-row" key={p.id}><span>{date(p.paidFromUtc)} → {date(p.paidUntilUtc)}{p.lines&&p.lines.length>1?<small> · plan et {p.lines.length-1} option(s)</small>:null}</span><span>{money(p.amount,p.currencyCode)} · {date(p.recordedAtUtc)}</span></p>)}</div>:<p className="muted">Aucun paiement enregistré.</p>;
}

async function receipt(r:RecordItem){
  const {jsPDF}=await import('jspdf');
  const doc=new jsPDF();
  const amount=money(Number(r.amount),String(r.currency)).replace(/\u202f|\u00a0/g,' ');
  doc.setFontSize(22);doc.text('RestauSpace',20,25);
  doc.setFontSize(10);doc.text("RECU DE PAIEMENT D'ABONNEMENT - DOCUMENT NON COMPTABLE, CE N'EST PAS UNE FACTURE",20,35);
  doc.setFontSize(12);
  doc.text([`Paiement : ${r.id}`,`Restaurant : ${r.name}`,`Abonnement : ${String(r.subscriptionId)}`,`Plan : ${String(r.plan)}`,`Période couverte : ${date(r.periodStart)} au ${date(r.end)}`,`Enregistré le : ${dateTime(r.date)}`],20,55);
  const lines=(Array.isArray(r.lines)?r.lines:[]).map(l=>{const [label,value]=String(l).split('|');return `${label} : ${money(Number(value),String(r.currency)).replace(/\u202f|\u00a0/g,' ')}`});
  if(lines.length)doc.text(lines,20,100);
  const y=100+lines.length*7+5;
  doc.line(20,y,190,y);
  doc.setFontSize(16);doc.text(`Montant : ${amount}`,20,y+15);
  doc.save(`recu-${shortId(r.id)}.pdf`);
}

function PaymentDetail({r}:{r:RecordItem}){
  return <>
    <div className="invoice-summary"><Receipt size={32}/><h3>{money(Number(r.amount),String(r.currency))}</h3><StatusBadge value={r.status}/></div>
    <Info items={[['Restaurant',r.name],['Plan',String(r.plan)],...(Array.isArray(r.lines)&&r.lines.length>1?r.lines.map((l,i)=>{const [label,value]=String(l).split('|');return [i===0?'Détail':' '.repeat(i),`${label} · ${money(Number(value),String(r.currency))}`] as [string,ReactNode]}):[]),['Montant',money(Number(r.amount),String(r.currency))],['Période couverte',`${date(r.periodStart)} → ${date(r.end)}`],['Enregistré le',dateTime(r.date)],['ID du paiement',<code key="id">{r.id}</code>],['Abonnement',<code key="s">{String(r.subscriptionId)}</code>]]}/>
    <p className="tiny-note">Montants du plan et des options au moment du paiement. Ce reçu n’est pas une facture.</p>
    <div className="drawer-actions"><Button onClick={()=>void receipt(r)}><Download size={15}/>Télécharger le reçu (PDF)</Button></div>
  </>;
}

function AuditDetail({r}:{r:RecordItem}){
  const metadata=Array.isArray(r.metadata)?r.metadata:[];
  return <>
    <Info items={[['Date',dateTime(r.date)],['Action',activityLabel(String(r.action))],['Code d’action',<code key="a">{String(r.action)}</code>],['Auteur',String(r.user)],['Type d’auteur',String(r.actorType)],['Cible',String(r.resource)],['ID de la cible',<code key="t">{String(r.targetId)}</code>],['Restaurant',String(r.tenant)],['Corrélation',<code key="c">{String(r.correlationId)}</code>]]}/>
    <h3>Métadonnées</h3>
    {metadata.length?<div className="feature-list">{metadata.map(m=><p key={m}>{m}</p>)}</div>:<p className="muted">Aucune.</p>}
  </>;
}

export function LiveDrawer({domain,id,onClose}:{domain:Exclude<LiveDomain,'plans'>;id:string;onClose:()=>void}){
  const {state}=useAdmin();
  const r=state[domain].find(x=>x.id===id);
  if(!r)return null;
  const title=domain==='audit-logs'?'Entrée du journal':domain==='billing'?'Paiement d’abonnement':r.name;
  if(domain==='restaurants')return <RestaurantDrawer r={r} onClose={onClose}/>;
  return <DetailDrawer open onClose={onClose} title={title} badge={domain==='users'||domain==='subscriptions'?<StatusBadge value={r.status}/>:undefined} subtitle={domain==='audit-logs'?activityLabel(String(r.action)):domain==='billing'?r.name:`ID : ${shortId(r.id)}`}>
    {domain==='users'?<UserDetail r={r}/>:domain==='subscriptions'?<SubscriptionDetail r={r}/>:domain==='billing'?<PaymentDetail r={r}/>:<AuditDetail r={r}/>}
  </DetailDrawer>;
}
