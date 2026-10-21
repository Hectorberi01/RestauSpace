'use client';
import {useEffect,useRef,useState,useSyncExternalStore,type FormEvent,type ReactNode} from 'react';
import {LogIn,UtensilsCrossed} from 'lucide-react';
import {Button} from '@/components/ui/button';
import {API_URL,ApiError,api,describe,getSession,onSessionChange} from '../../services/api/client';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription,DialogFooter} from '@/components/ui/dialog';
import {login,loginWithCode,logout,me as loadMe,type Me} from '../../services/api/auth';
import {LoadingSkeleton} from '../ui/shared';

const subscribe=(listener:()=>void)=>onSessionChange(listener);
const snapshot=()=>getSession();
const serverSnapshot=()=>undefined;

function LoginScreen({message}:{message?:string}){
  const [email,setEmail]=useState(''),[password,setPassword]=useState(''),[error,setError]=useState(message),[busy,setBusy]=useState(false);
  const [challenge,setChallenge]=useState<string|null>(null),[code,setCode]=useState('');
  const submit=async(e:FormEvent)=>{e.preventDefault();setBusy(true);setError(undefined);try{if(challenge){await loginWithCode(challenge,code.trim())}else{const next=await login(email.trim(),password);if(next){setChallenge(next);setPassword('')}}}catch(err){if(challenge&&err instanceof ApiError&&err.code==='identity.two_factor.challenge_invalid'){setChallenge(null);setCode('');setError('Le délai est dépassé : saisissez de nouveau votre mot de passe.')}else setError(err instanceof ApiError&&err.status===401?(challenge?'Code incorrect.':'Email ou mot de passe incorrect.'):describe(err))}finally{setBusy(false)}};
  return <div className="login-screen"><form className="login-card" onSubmit={submit}><div className="login-brand"><span className="brand-icon"><UtensilsCrossed size={21}/></span><span>Restau<span className="brand-os">Space</span></span></div><h1>Administration</h1>{challenge?<><p>Saisissez le code à 6 chiffres de votre application d’authentification, ou un code de secours.</p><label>Code<input autoFocus autoComplete="one-time-code" required maxLength={12} value={code} onChange={e=>setCode(e.target.value)}/></label></>:<><p>Connectez-vous avec un compte administrateur de la plateforme.</p><label>Email<input type="email" autoComplete="username" required value={email} onChange={e=>setEmail(e.target.value)}/></label><label>Mot de passe<input type="password" autoComplete="current-password" required value={password} onChange={e=>setPassword(e.target.value)}/></label></>}{error&&<p className="login-error" role="alert">{error}</p>}<Button type="submit" disabled={busy}><LogIn size={16}/>{busy?'Connexion…':'Se connecter'}</Button><small>API : {API_URL}</small></form></div>;
}

/** Délai d'inactivité avant déconnexion (minutes) ; l'avertissement apparaît 5 minutes avant. */
const IDLE_MINUTES=Math.max(2,Number(process.env.NEXT_PUBLIC_IDLE_MINUTES)||30);
const WARNING_MINUTES=Math.min(5,IDLE_MINUTES-1);
const ACTIVITY=['pointerdown','keydown','wheel','touchstart'] as const;

/** Déconnexion après inactivité : un poste laissé ouvert ne reste pas administrateur. */
function IdleGuard({onExpire}:{onExpire:()=>void}){
  const last=useRef(Date.now());
  const [remaining,setRemaining]=useState<number|null>(null);
  useEffect(()=>{
    const touch=()=>{last.current=Date.now()};
    ACTIVITY.forEach(e=>window.addEventListener(e,touch,{passive:true}));
    const timer=window.setInterval(()=>{
      const idle=Date.now()-last.current;
      const left=IDLE_MINUTES*60000-idle;
      if(left<=0){window.clearInterval(timer);onExpire();return}
      setRemaining(left<=WARNING_MINUTES*60000?left:null);
    },1000);
    return()=>{ACTIVITY.forEach(e=>window.removeEventListener(e,touch));window.clearInterval(timer)};
  },[onExpire]);
  const minutes=remaining===null?0:Math.ceil(remaining/60000);
  return <Dialog open={remaining!==null} onOpenChange={open=>{if(!open){last.current=Date.now();setRemaining(null)}}}><DialogContent><DialogHeader><DialogTitle>Toujours là ?</DialogTitle><DialogDescription>Sans activité, vous serez déconnecté dans {minutes} minute{minutes>1?'s':''}.</DialogDescription></DialogHeader><DialogFooter><Button variant="outline" onClick={onExpire}>Me déconnecter</Button><Button onClick={()=>{last.current=Date.now();setRemaining(null)}}>Rester connecté</Button></DialogFooter></DialogContent></Dialog>;
}

export function AuthGate({children}:{children:(me:Me)=>ReactNode}){
  const session=useSyncExternalStore(subscribe,snapshot,serverSnapshot);
  const [profile,setProfile]=useState<{sessionId:string;me:Me}|null>(null);
  const [refused,setRefused]=useState<string>();
  const sessionId=session?.sessionId;
  useEffect(()=>{
    if(!sessionId)return;
    let active=true;
    (async()=>{
      try{
        const me=await loadMe();
        // Un compte de restaurant se connecte aussi : on vérifie tout de suite qu'il a accès aux routes d'administration.
        await api('/admin/reporting/tenants',{query:{page:1,pageSize:1}});
        if(active){setRefused(undefined);setProfile({sessionId,me})}
      }catch(err){
        if(!active)return;
        setRefused(err instanceof ApiError&&err.status===403?'Ce compte n’est pas administrateur de la plateforme : utilisez la console du restaurant.':describe(err));
        await logout();
      }
    })();
    return()=>{active=false};
  },[sessionId]);
  const expire=useRef(()=>{setRefused(`Vous avez été déconnecté après ${IDLE_MINUTES} minutes d’inactivité.`);void logout()}).current;
  if(session===undefined)return <div className="login-screen"><LoadingSkeleton/></div>;
  if(!session)return <LoginScreen key={refused} message={refused}/>;
  if(!profile||profile.sessionId!==session.sessionId)return <div className="login-screen"><LoadingSkeleton/></div>;
  return <><IdleGuard onExpire={expire}/>{children(profile.me)}</>;
}
