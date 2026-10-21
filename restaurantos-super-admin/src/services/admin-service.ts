import type {RecordItem} from '../types';
export function exportCsv(rows:RecordItem[],filename:string){const keys=Object.keys(rows[0]||{});const text='\uFEFF'+keys.join(';')+'\n'+rows.map(r=>keys.map(k=>'"'+String(r[k]??'').replaceAll('"','""')+'"').join(';')).join('\n');download(new Blob([text],{type:'text/csv;charset=utf-8'}),filename+'.csv')}
export function download(blob:Blob,name:string){const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000)}
