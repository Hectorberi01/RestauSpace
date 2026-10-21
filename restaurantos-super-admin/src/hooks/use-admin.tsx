'use client';
import {createContext,useCallback,useContext,useEffect,useMemo,useRef,useState,type ReactNode} from 'react';
import {toast} from 'sonner';
import {describe} from '../services/api/client';
import {audit,payments as paymentsApi,plans as plansApi,subscriptions as subscriptionsApi,tenants as tenantsApi,users as usersApi,type AuditEntryDto,type PlanDto,type PlatformTenantRow,type SubscriptionDto,type SubscriptionPaymentDto,type UserDto} from '../services/api/admin-api';
import {auditRecord,paymentRecord,planRecord,subscriptionRecord,tenantRecord,userRecord} from '../services/api/mappers';
import type {Me} from '../services/api/auth';
import type {AdminState,Domain} from '../types';

export const liveDomains=['users','restaurants','subscriptions','billing','plans','audit-logs'] as const satisfies readonly Domain[];
export type LiveDomain=typeof liveDomains[number];
export const isLive=(d:string):d is LiveDomain=>(liveDomains as readonly string[]).includes(d);

/** Domaines à recharger après une action : ce qu'elle touche, et le journal d'audit qu'elle alimente. */
export const touches={
  users:['users','audit-logs'],
  restaurants:['restaurants','audit-logs'],
  subscriptions:['subscriptions','restaurants','billing','audit-logs'],
  plans:['plans','audit-logs'],
  none:[],
} as const satisfies Record<string,readonly LiveDomain[]>;

interface Raw {users:UserDto[];tenants:PlatformTenantRow[];subscriptions:SubscriptionDto[];plans:PlanDto[];payments:SubscriptionPaymentDto[];audit:AuditEntryDto[]}
type Status='loading'|'ready'|'error';

interface Store {
  state:AdminState;
  raw:Raw;
  me:Me;
  status:Status;
  errors:Partial<Record<LiveDomain,string>>;
  /** Domaines dont la liste a atteint la limite de chargement : indicateurs et alertes calculés dessus sont partiels. */
  truncated:Partial<Record<LiveDomain,boolean>>;
  loadedAt:Date|null;
  reload:(domains?:readonly LiveDomain[])=>Promise<void>;
  run:(success:string,action:()=>Promise<unknown>,domains?:readonly LiveDomain[])=>Promise<boolean>;
}

const Context=createContext<Store|null>(null);
const empty:Raw={users:[],tenants:[],subscriptions:[],plans:[],payments:[],audit:[]};
const emptyState=():AdminState=>({users:[],restaurants:[],subscriptions:[],billing:[],plans:[],'audit-logs':[]});
const AUDIT_LIMIT=300;

const loaders:{[D in LiveDomain]:()=>Promise<unknown[]>}={
  users:()=>usersApi.list(),
  restaurants:()=>tenantsApi.list(),
  subscriptions:()=>subscriptionsApi.list(),
  plans:()=>plansApi.list(),
  'audit-logs':()=>audit.list({},AUDIT_LIMIT),
  billing:()=>paymentsApi.list(),
};

const key:{[D in LiveDomain]:keyof Raw}={users:'users',restaurants:'tenants',subscriptions:'subscriptions',plans:'plans','audit-logs':'audit',billing:'payments'};

function project(raw:Raw):AdminState{
  const tenantById=new Map(raw.tenants.map(t=>[t.tenantId,t]));
  const planById=new Map(raw.plans.map(p=>[p.id,p]));
  const userById=new Map(raw.users.map(u=>[u.id,u]));
  return {
    users:raw.users.map(userRecord),
    restaurants:raw.tenants.map(tenantRecord),
    subscriptions:raw.subscriptions.map(x=>subscriptionRecord(x,tenantById,planById)),
    plans:raw.plans.map(planRecord),
    billing:raw.payments.map(x=>paymentRecord(x,tenantById)),
    'audit-logs':raw.audit.map(e=>auditRecord(e,userById,tenantById)),
  };
}

export function AdminProvider({me,children}:{me:Me;children:ReactNode}){
  const [raw,setRaw]=useState<Raw>(empty);
  const [status,setStatus]=useState<Status>('loading');
  const [errors,setErrors]=useState<Partial<Record<LiveDomain,string>>>({});
  const [truncated,setTruncated]=useState<Partial<Record<LiveDomain,boolean>>>({});
  const [loadedAt,setLoadedAt]=useState<Date|null>(null);
  const generation=useRef<Partial<Record<LiveDomain,number>>>({});

  const reload=useCallback(async(domains:readonly LiveDomain[]=liveDomains)=>{
    if(!domains.length)return;
    const tickets=Object.fromEntries(domains.map(d=>[d,(generation.current[d]=(generation.current[d]??0)+1)])) as Partial<Record<LiveDomain,number>>;
    const results=await Promise.all(domains.map(async d=>{
      try{const list=await loaders[d]();return {d,list,error:undefined,cut:Boolean((list as {truncated?:boolean}).truncated)}}
      catch(error){return {d,list:undefined,error:describe(error),cut:false}}
    }));
    const fresh=results.filter(r=>generation.current[r.d]===tickets[r.d]);
    if(!fresh.length)return;
    setRaw(previous=>{const next={...previous};fresh.forEach(r=>{(next as Record<string,unknown>)[key[r.d]]=r.list??(r.error?[]:previous[key[r.d]])});return next});
    setErrors(previous=>{const next={...previous};fresh.forEach(r=>{if(r.error)next[r.d]=r.error;else delete next[r.d]});return next});
    setTruncated(previous=>{const next={...previous};fresh.forEach(r=>{next[r.d]=r.cut});return next});
    setLoadedAt(new Date());
    setStatus(current=>{
      if(current!=='loading')return current==='error'&&fresh.some(r=>!r.error)?'ready':current;
      return fresh.length===liveDomains.length&&fresh.every(r=>r.error)?'error':'ready';
    });
  },[]);

  useEffect(()=>{void reload()},[reload]);

  const run=useCallback(async(success:string,action:()=>Promise<unknown>,domains:readonly LiveDomain[]=liveDomains)=>{
    try{await action();toast.success(success);await reload(domains);return true}
    catch(error){toast.error(describe(error));return false}
  },[reload]);

  const state=useMemo(()=>project(raw),[raw]);
  const value=useMemo<Store>(()=>({state,raw,me,status,errors,truncated,loadedAt,reload,run}),[state,raw,me,status,errors,truncated,loadedAt,reload,run]);

  return <Context.Provider value={value}>{children}</Context.Provider>;
}

export function useAdmin(){const ctx=useContext(Context);if(!ctx)throw new Error('AdminProvider requis');return ctx}

export {AUDIT_LIMIT};
