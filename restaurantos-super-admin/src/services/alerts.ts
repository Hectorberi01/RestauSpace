import type {PlatformTenantRow,SubscriptionDto,UserDto} from './api/admin-api';

export type AlertCategory='Abonnement'|'Restaurant'|'Compte';
export type AlertPriority='Haute'|'Normale'|'Basse';

export interface PlatformAlert {
  id:string;
  category:AlertCategory;
  priority:AlertPriority;
  title:string;
  description:string;
  date:string;
  href:string;
}

const DAY=86400000;
const days=(ms:number)=>Math.max(0,Math.ceil(ms/DAY));
const day=(iso:string)=>new Date(iso).toLocaleDateString('fr-FR',{day:'2-digit',month:'short',year:'numeric'});

export function platformAlerts(input:{users:UserDto[];tenants:PlatformTenantRow[];subscriptions:SubscriptionDto[]},now=Date.now()):PlatformAlert[]{
  const name=(tenantId:string)=>input.tenants.find(t=>t.tenantId===tenantId)?.name??`Restaurant ${tenantId.slice(0,8)}`;
  const alerts:PlatformAlert[]=[];
  for(const s of input.subscriptions){
    const end=new Date(s.currentPeriodEndUtc).getTime();
    const link=`/admin/subscriptions?record=${s.id}`;
    if(s.status==='PastDue')alerts.push({id:`past-due:${s.id}:${s.currentPeriodEndUtc}`,category:'Abonnement',priority:'Haute',title:`Paiement en retard · ${name(s.tenantId)}`,description:`Période échue le ${day(s.currentPeriodEndUtc)}. Enregistrez le paiement ou relancez le restaurant avant la suspension.`,date:s.currentPeriodEndUtc,href:link});
    else if(s.status==='Suspended')alerts.push({id:`suspended:${s.id}:${s.currentPeriodEndUtc}`,category:'Abonnement',priority:'Haute',title:`Abonnement suspendu · ${name(s.tenantId)}`,description:s.suspensionReason==='TrialExpired'?'Essai terminé sans paiement.':s.suspensionReason==='PaymentOverdue'?'Impayé au-delà du délai de grâce.':'Suspendu par la plateforme.',date:s.currentPeriodEndUtc,href:link});
    else if(s.status==='Trialing'&&end>now&&end-now<=7*DAY)alerts.push({id:`trial-ending:${s.id}:${s.currentPeriodEndUtc}`,category:'Abonnement',priority:'Normale',title:`Fin d’essai dans ${days(end-now)} j · ${name(s.tenantId)}`,description:`L’essai se termine le ${day(s.currentPeriodEndUtc)} ; sans paiement, l’abonnement sera suspendu.`,date:s.currentPeriodEndUtc,href:link});
    else if(s.cancelAtPeriodEnd&&s.status!=='Cancelled'&&end>now&&end-now<=14*DAY)alerts.push({id:`cancelling:${s.id}:${s.currentPeriodEndUtc}`,category:'Abonnement',priority:'Normale',title:`Résiliation programmée · ${name(s.tenantId)}`,description:`L’abonnement prendra fin le ${day(s.currentPeriodEndUtc)}.`,date:s.currentPeriodEndUtc,href:link});
  }
  for(const t of input.tenants){
    if(t.status==='Suspended')alerts.push({id:`tenant-suspended:${t.tenantId}`,category:'Restaurant',priority:'Haute',title:`Restaurant suspendu · ${t.name}`,description:'Il ne peut plus enregistrer d’opérations jusqu’à sa réactivation.',date:t.createdAtUtc,href:`/admin/restaurants?record=${t.tenantId}`});
    else if(t.status!=='Closed'&&(!t.subscriptionStatus||t.subscriptionStatus==='Cancelled'))alerts.push({id:`no-subscription:${t.tenantId}:${t.subscriptionStatus??'none'}`,category:'Restaurant',priority:'Normale',title:`Sans abonnement · ${t.name}`,description:'Démarrez un abonnement pour ce restaurant.',date:t.createdAtUtc,href:`/admin/restaurants?record=${t.tenantId}`});
  }
  for(const u of input.users){
    const age=now-new Date(u.createdAtUtc).getTime();
    if(u.status==='Pending'&&age>7*DAY)alerts.push({id:`pending:${u.id}`,category:'Compte',priority:'Basse',title:`Email non vérifié · ${u.email}`,description:`Compte créé il y a ${days(age)} jours, toujours en attente de vérification.`,date:u.createdAtUtc,href:`/admin/users?record=${u.id}`});
  }
  const rank={Haute:0,Normale:1,Basse:2};
  return alerts.sort((a,b)=>rank[a.priority]-rank[b.priority]||b.date.localeCompare(a.date));
}

const KEY='restaurantos.alerts.read';
export function readAlerts():Set<string>{try{return new Set(JSON.parse(window.localStorage.getItem(KEY)??'[]') as string[])}catch{return new Set()}}
export function saveReadAlerts(ids:Set<string>){try{window.localStorage.setItem(KEY,JSON.stringify([...ids].slice(-2000)))}catch{}}
