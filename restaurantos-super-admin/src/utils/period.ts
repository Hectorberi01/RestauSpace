import type {Granularity} from '../services/api/admin-api';
import {isoDay} from './format';

export interface ReportPeriod {from:string;to:string;granularity:Granularity}

export function period(value:string,range:{from:string;to:string}):ReportPeriod{
  const today=new Date();
  const back=(days:number)=>{const d=new Date(today);d.setUTCDate(d.getUTCDate()-days+1);return isoDay(d)};
  if(value==='7 jours')return {from:back(7),to:isoDay(today),granularity:'Day'};
  if(value==='30 jours')return {from:back(30),to:isoDay(today),granularity:'Day'};
  if(value==='3 mois')return {from:back(91),to:isoDay(today),granularity:'Week'};
  if(value==='Personnalisé'){const days=(new Date(range.to).getTime()-new Date(range.from).getTime())/86400000+1;return {...range,granularity:days<=62?'Day':days<=190?'Week':'Month'}}
  return {from:back(365),to:isoDay(today),granularity:'Month'};
}

export const periodLabel=(iso:string,granularity:Granularity)=>{const d=new Date(iso.slice(0,10)+'T00:00:00Z');return granularity==='Month'?d.toLocaleDateString('fr-FR',{month:'short',year:'2-digit',timeZone:'UTC'}):d.toLocaleDateString('fr-FR',{day:'2-digit',month:'short',timeZone:'UTC'})};

export function bucketStart(iso:string,granularity:Granularity):string{
  const d=new Date(iso);
  const day=new Date(Date.UTC(d.getUTCFullYear(),d.getUTCMonth(),d.getUTCDate()));
  if(granularity==='Month')return isoDay(new Date(Date.UTC(day.getUTCFullYear(),day.getUTCMonth(),1)));
  if(granularity==='Week'){const shift=(day.getUTCDay()+6)%7;day.setUTCDate(day.getUTCDate()-shift)}
  return isoDay(day);
}

export function buckets(p:ReportPeriod):string[]{
  const out:string[]=[];
  let cursor=bucketStart(p.from+'T00:00:00Z',p.granularity);
  const end=p.to;
  for(let i=0;i<400&&cursor<=end;i++){
    out.push(cursor);
    const d=new Date(cursor+'T00:00:00Z');
    if(p.granularity==='Month')d.setUTCMonth(d.getUTCMonth()+1);else d.setUTCDate(d.getUTCDate()+(p.granularity==='Week'?7:1));
    cursor=isoDay(d);
  }
  return out;
}
