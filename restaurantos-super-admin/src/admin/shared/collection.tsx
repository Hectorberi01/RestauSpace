'use client';
import {useEffect,useState} from 'react';
import Link from 'next/link';
import {Landmark,Settings2} from 'lucide-react';
import {StatusBadge,LoadingSkeleton} from '../../components/ui/shared';
import {describe} from '../../services/api/client';
import {billingAdmin,paymentProviders,type CollectionMissing,type CollectorCandidate,type OnlinePaymentRoute,type PaymentMethodType,type PaymentProviderDto} from '../../services/api/admin-api';
import {date} from '../../utils/format';

export const methodLabels:Record<PaymentMethodType,string>={Card:'Carte en ligne',ApplePay:'Apple Pay',GooglePay:'Google Pay',PayPal:'PayPal',MobileMoney:'Mobile money',BankTransfer:'Virement',Cash:'Espèces',CardOnSite:'Carte sur place',MobileMoneyOnSite:'Mobile money sur place',MealVoucher:'Titre-restaurant'};
export const missingLabels:Record<CollectionMissing,string>={disabled:'prestataire inactif',currency:'devise non autorisée',integration:'absent du code',credentials:'compte plateforme non enregistré',credentials_unreadable:'identifiants illisibles, à ressaisir'};
export const missingText=(c:CollectorCandidate)=>c.missing.map(m=>missingLabels[m]??m).join(', ');

/** Les prestataires proposés pour une devise, dans l'ordre (réponse d'une API plus ancienne : le seul prestataire). */
export const offeredOf=(route:OnlinePaymentRoute)=>route.providers??(route.provider?[route.provider]:[]);

/** Un prestataire proposé pour une devise, vu par l'administrateur : moyens proposés aux restaurants et ce qui manque. */
export function collectionOf(route:OnlinePaymentRoute,code:string,providers:PaymentProviderDto[]){
  const provider=providers.find(p=>p.code===code);
  const candidate=route.candidates?.find(c=>c.provider===code);
  return {
    code,
    name:candidate?.name??provider?.displayName??code,
    methods:(candidate?.methods??provider?.capability.methods??[]).map(m=>methodLabels[m]??m),
    account:!candidate?null:!candidate.missing.includes('credentials')&&!candidate.missing.includes('credentials_unreadable'),
    missing:candidate&&!candidate.ready?missingText(candidate):null,
    ready:candidate?candidate.ready:route.ready,
  };
}

/**
 * Lecture seule : comment la plateforme encaisse les abonnements, devise par devise (prestataire, moyens de paiement,
 * compte plateforme). La configuration se fait dans Paramètres > Paiements.
 */
export function CollectionOverview(){
  const [data,setData]=useState<{routes:OnlinePaymentRoute[];providers:PaymentProviderDto[]}|null>(null);
  const [error,setError]=useState<string>();
  useEffect(()=>{
    let active=true;
    Promise.all([billingAdmin.onlinePayment(),paymentProviders.list()])
      .then(([routes,providers])=>{if(active)setData({routes,providers})})
      .catch(e=>{if(active)setError(describe(e))});
    return()=>{active=false};
  },[]);

  return <section className="panel collection-overview">
    <div className="panel-heading"><div><h2><Landmark size={18}/> Encaissement des abonnements</h2><p>Les moyens de paiement proposés aux restaurants pour régler leur abonnement, sur le compte de la plateforme</p></div><Link href="/admin/settings"><Settings2 size={15}/> Configurer</Link></div>
    {error?<div className="notice warn">Configuration indisponible : {error}</div>
      :!data?<LoadingSkeleton/>
      :!data.routes.length?<p className="muted">Aucune devise vendue : créez un plan actif.</p>
      :<div className="collection-cards">{data.routes.map(route=>{
        const offered=offeredOf(route).map(code=>collectionOf(route,code,data.providers));
        return <article key={route.currencyCode} className="collection-card">
          <header><strong className="collection-code">{route.currencyCode}</strong><StatusBadge value={offered.length?(route.ready?'Prêt':'Incomplet'):'Non configuré'}/></header>
          {offered.length?<>
            <p className="tiny-note">{offered.length>1?`${offered.length} prestataires au choix du restaurant`:'Un prestataire'}{!route.chosen&&' · réglage du serveur'}{route.updatedAtUtc&&` · choisi le ${date(route.updatedAtUtc)}`}</p>
            <ul className="collection-offered">{offered.map(c=><li key={c.code}>
              <div className="collection-offered-head"><strong className="collection-provider">{c.name}</strong>{!c.ready&&<StatusBadge value="Incomplet"/>}</div>
              <div className="chips">{c.methods.length?c.methods.map(m=><span key={m} className="chip">{m}</span>):<span className="chip">Moyens non renseignés</span>}</div>
              <small>Compte plateforme : {c.account===null?'—':c.account?'enregistré':'à enregistrer'}{c.missing?` · à compléter : ${c.missing}`:''}</small>
            </li>)}</ul>
          </>:<p className="block-note">Aucun prestataire : les restaurants de cette devise ne peuvent pas payer en ligne. Leurs paiements s’enregistrent à la main depuis Abonnements.</p>}
        </article>;
      })}</div>}
  </section>;
}
