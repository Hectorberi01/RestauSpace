'use client';
import {useEffect,useMemo,useState,type ComponentType} from 'react';
import {Activity,ChevronDown,Check,Copy,CreditCard,FileText,KeyRound,LogIn,MoreHorizontal,Package,Receipt,RefreshCw,Settings2,ShieldCheck,Store,UserCog,Users,Wallet} from 'lucide-react';
import {toast} from 'sonner';
import {Button} from '@/components/ui/button';
import {DropdownMenu,DropdownMenuContent,DropdownMenuItem,DropdownMenuSeparator,DropdownMenuTrigger} from '@/components/ui/dropdown-menu';
import {useAdmin} from '../../hooks/use-admin';
import {describe} from '../../services/api/client';
import {audit,type AuditEntryDto} from '../../services/api/admin-api';
import {shortId} from '../../services/api/mappers';
import {dateTime} from '../../utils/format';
import {LoadingSkeleton} from '../../components/ui/shared';

export type Tone='purple'|'green'|'amber'|'blue'|'red'|'gray';
type Icon=ComponentType<{size?:number}>;

/** Catégories du filtre : un module du journal peut alimenter une seule catégorie. */
const categories:{key:string;label:string;modules:string[]}[]=[
  {key:'billing',label:'Abonnement',modules:['billing']},
  {key:'security',label:'Sécurité',modules:['identity','integrations']},
  {key:'staff',label:'Utilisateurs',modules:['staff']},
  {key:'payments',label:'Paiements',modules:['payments','loyalty']},
  {key:'profile',label:'Restaurant',modules:['tenants']},
  {key:'other',label:'Autres',modules:[]},
];

export const moduleStyle:Record<string,[Icon,Tone,string]>={
  billing:[CreditCard,'purple','Abonnement'],
  identity:[ShieldCheck,'blue','Sécurité'],
  integrations:[KeyRound,'blue','Intégrations'],
  staff:[Users,'amber','Utilisateurs'],
  payments:[Wallet,'green','Paiements'],
  loyalty:[Wallet,'green','Fidélité'],
  tenants:[Store,'purple','Restaurant'],
  inventory:[Package,'amber','Stock'],
  customers:[UserCog,'gray','Clients'],
  notifications:[Settings2,'gray','Notifications'],
  reviews:[FileText,'gray','Avis'],
  reporting:[FileText,'gray','Rapports'],
};

const titles:Record<string,string>={
  'billing.subscription.trial_started':'Essai démarré',
  'billing.subscription.activated':'Abonnement activé',
  'billing.subscription.plan_changed':'Plan modifié',
  'billing.subscription.past_due':'Paiement en retard',
  'billing.subscription.suspended':'Abonnement suspendu',
  'billing.subscription.cancellation_scheduled':'Résiliation programmée',
  'billing.subscription.cancellation_unscheduled':'Résiliation annulée',
  'billing.subscription.cancelled':'Abonnement résilié',
  'billing.subscription.payment_recorded':'Paiement enregistré',
  'billing.checkout.started':'Paiement en ligne lancé',
  'billing.checkout.paid':'Paiement en ligne réussi',
  'billing.checkout.closed':'Paiement en ligne clos',
  'tenants.tenant.activated':'Restaurant activé',
  'tenants.tenant.suspended':'Restaurant suspendu',
  'tenants.tenant.reactivated':'Restaurant réactivé',
  'tenants.tenant.closed':'Restaurant fermé',
  'tenants.tenant.renamed':'Restaurant renommé',
  'tenants.tenant.price_tax_mode_changed':'Mode de prix modifié',
  'tenants.tenant.branding_updated':'Identité visuelle modifiée',
  'tenants.tenant.public_profile_updated':'Profil public modifié',
  'staff.role.created':'Rôle créé',
  'staff.role.renamed':'Rôle renommé',
  'staff.role.permission_granted':'Permission accordée',
  'staff.role.permission_revoked':'Permission retirée',
  'staff.role.deleted':'Rôle supprimé',
  'staff.member.invited':'Membre invité',
  'staff.member.invitation_resent':'Invitation renvoyée',
  'staff.member.activated':'Membre activé',
  'staff.member.owner_created':'Propriétaire ajouté',
  'staff.member.suspended':'Membre suspendu',
  'staff.member.reinstated':'Membre réintégré',
  'staff.member.tenant_role_assigned':'Rôle attribué',
  'staff.member.tenant_role_removed':'Rôle retiré',
  'payments.configuration.created':'Moyen de paiement ajouté',
  'payments.configuration.configured':'Compte de paiement enregistré',
  'payments.configuration.mode_changed':'Mode de paiement modifié',
  'payments.configuration.enabled':'Moyen de paiement activé',
  'payments.configuration.disabled':'Moyen de paiement désactivé',
  'payments.configuration.method_activated':'Méthode de paiement ouverte',
  'payments.configuration.method_deactivated':'Méthode de paiement fermée',
  'payments.refund.requested':'Remboursement demandé',
  'payments.payment.captured_after_cancellation':'Paiement capté après annulation',
  'integrations.api_key.issued':'Clé d’API créée',
  'integrations.api_key.rotated':'Clé d’API renouvelée',
  'integrations.api_key.revoked':'Clé d’API révoquée',
  'integrations.webhook_endpoint.created':'Webhook créé',
  'integrations.webhook_endpoint.enabled':'Webhook activé',
  'integrations.webhook_endpoint.disabled':'Webhook désactivé',
  'integrations.webhook_secret.rotated':'Secret de webhook renouvelé',
  'identity.session.started':'Connexion',
  'identity.session.revoked':'Session fermée',
  'inventory.stock.adjusted':'Stock corrigé',
  'inventory.stock.waste_recorded':'Perte enregistrée',
  'inventory.stock.count_confirmed':'Inventaire validé',
  'reporting.export.requested':'Export demandé',
  'loyalty.stored_value.issued':'Carte cadeau émise',
  'loyalty.stored_value.cancelled':'Carte cadeau annulée',
};

export function title(action:string){
  if(titles[action])return titles[action];
  const last=action.split('.').pop()??action;
  return last.replace(/_/g,' ').replace(/^./,c=>c.toUpperCase());
}

const dayStart=(d:Date)=>new Date(d.getFullYear(),d.getMonth(),d.getDate()).getTime();

function group(iso:string){
  const today=dayStart(new Date());
  const day=dayStart(new Date(iso));
  if(day===today)return 'Aujourd’hui';
  if(day===today-86400000)return 'Hier';
  if(day>today-7*86400000)return 'Cette semaine';
  return 'Plus ancien';
}

function copy(value:string,label:string){void navigator.clipboard?.writeText(value).then(()=>toast.success(`${label} copié`),()=>toast.error('Copie impossible'))}

/** Le journal d'audit du restaurant, groupé par jour, filtrable par catégorie. */
export function TenantActivity({tenantId}:{tenantId:string}){
  const {raw}=useAdmin();
  const [entries,setEntries]=useState<AuditEntryDto[]|null>(null);
  const [truncated,setTruncated]=useState(false);
  const [error,setError]=useState<string>();
  const [category,setCategory]=useState('all');
  const [limit,setLimit]=useState(100);
  const [version,setVersion]=useState(0);

  useEffect(()=>{
    let active=true;
    setError(undefined);
    audit.list({tenantId},limit).then(list=>{if(!active)return;setEntries(list);setTruncated(list.truncated)}).catch(e=>{if(active)setError(describe(e))});
    return()=>{active=false};
  },[tenantId,limit,version]);

  const planName=(id?:string)=>id?raw.plans.find(p=>p.id===id)?.name??shortId(id):undefined;
  const actorName=(e:AuditEntryDto)=>e.actorType==='User'&&e.actorId?raw.users.find(u=>u.id===e.actorId)?.email??`Utilisateur ${shortId(e.actorId)}`:e.actorType==='System'?'Traitement automatique':e.actorType==='ApiCredential'?'Clé d’API':undefined;

  /** Une ligne lisible tirée des métadonnées (déjà filtrées par le journal : ids, codes, montants). */
  const detail=(e:AuditEntryDto)=>{
    const m=e.metadata??{};
    const parts:string[]=[];
    if(e.action==='billing.subscription.plan_changed'&&m.previous_plan_id&&m.new_plan_id)parts.push(`Passage du plan ${planName(m.previous_plan_id)} vers ${planName(m.new_plan_id)}`);
    else if(m.plan_id)parts.push(`Plan ${planName(m.plan_id)}`);
    if(e.action==='tenants.tenant.renamed'&&m.previous_name&&m.new_name)parts.push(`${m.previous_name} → ${m.new_name}`);
    if(m.amount)parts.push(m.amount);
    if(m.paid_period)parts.push(`Période ${m.paid_period}`);
    if(m.trial_period)parts.push(`Essai ${m.trial_period}`);
    if(m.provider)parts.push(m.provider);
    if(m.permission)parts.push(m.permission);
    if(m.method)parts.push(m.method);
    if(m.reason)parts.push(`Motif : ${m.reason}`);
    const actor=actorName(e);
    if(actor)parts.push(actor);
    return parts.join(' · ');
  };

  const shown=useMemo(()=>{
    if(!entries)return [];
    const cat=categories.find(c=>c.key===category);
    const known=categories.flatMap(c=>c.modules);
    return entries.filter(e=>{
      const module=e.action.split('.')[0];
      if(category==='all'||!cat)return true;
      return cat.key==='other'?!known.includes(module):cat.modules.includes(module);
    }).sort((a,b)=>b.occurredAtUtc.localeCompare(a.occurredAtUtc));
  },[entries,category]);

  const groups=useMemo(()=>{
    const map=new Map<string,AuditEntryDto[]>();
    shown.forEach(e=>{const g=group(e.occurredAtUtc);map.set(g,[...(map.get(g)??[]),e])});
    return [...map.entries()];
  },[shown]);

  const current=category==='all'?'Toute l’activité':categories.find(c=>c.key===category)?.label;

  return <div className="activity">
    <div className="activity-head">
      <span className="activity-head-icon"><Activity size={22}/></span>
      <div><h4>Journal d’activité</h4><p>Actions et événements enregistrés pour ce restaurant.</p></div>
      <DropdownMenu><DropdownMenuTrigger asChild><button className="filter-pill">{current}<ChevronDown size={14}/></button></DropdownMenuTrigger><DropdownMenuContent align="end">
        <DropdownMenuItem onClick={()=>setCategory('all')}>{category==='all'&&<Check size={14}/>}Toute l’activité</DropdownMenuItem>
        <DropdownMenuSeparator/>
        {categories.map(c=><DropdownMenuItem key={c.key} onClick={()=>setCategory(c.key)}>{category===c.key&&<Check size={14}/>}{c.label}</DropdownMenuItem>)}
      </DropdownMenuContent></DropdownMenu>
    </div>

    {error?<div className="drawer-banner danger">{error} <button className="text-link" onClick={()=>setVersion(v=>v+1)}>Réessayer</button></div>
      :!entries?<LoadingSkeleton/>
      :groups.length===0?<p className="muted activity-empty">{category==='all'?'Aucune activité enregistrée pour ce restaurant.':'Aucune activité dans cette catégorie.'}</p>
      :<div className="activity-timeline">{groups.map(([label,items])=><div key={label} className="activity-group">
        <h5>{label}</h5>
        <ol>{items.map(e=>{
          const module=e.action.split('.')[0];
          const [Icon,tone,tag]=moduleStyle[module]??[Receipt,'gray' as Tone,module];
          const text=detail(e);
          return <li key={e.id}>
            <span className={`activity-icon ${tone}`}><Icon size={18}/></span>
            <div className="activity-body">
              <strong>{title(e.action)}</strong>
              {text&&<p>{text}</p>}
              <div className="activity-tags"><span>{tag}</span>{e.action.startsWith('billing.subscription.plan')&&<span className="accent">Plan</span>}{e.actorType==='System'&&<span>Automatique</span>}</div>
            </div>
            <time dateTime={e.occurredAtUtc}>{dateTime(e.occurredAtUtc)}</time>
            <DropdownMenu><DropdownMenuTrigger asChild><Button variant="ghost" size="icon-sm" aria-label="Actions sur l’entrée"><MoreHorizontal size={16}/></Button></DropdownMenuTrigger><DropdownMenuContent align="end">
              <DropdownMenuItem onClick={()=>copy(e.id,'Identifiant')}><Copy size={14}/>Copier l’identifiant</DropdownMenuItem>
              <DropdownMenuItem onClick={()=>copy(e.correlationId,'Corrélation')}><Copy size={14}/>Copier la corrélation</DropdownMenuItem>
              <DropdownMenuItem onClick={()=>copy(e.action,'Code d’action')}><LogIn size={14}/>Copier le code d’action</DropdownMenuItem>
            </DropdownMenuContent></DropdownMenu>
          </li>;
        })}</ol>
      </div>)}
      {truncated&&<Button variant="outline" className="activity-more" onClick={()=>setLimit(l=>l+100)}><RefreshCw size={14}/>Afficher plus</Button>}
      </div>}
  </div>;
}
