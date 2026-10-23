/** Décimales fixes par devise, comme la console : 0 pour XOF/XAF, 2 pour EUR/USD ; sinon celles d'Intl. */
const currencyDigits=(currency:string)=>currency==='XOF'||currency==='XAF'?0:currency==='EUR'||currency==='USD'?2:undefined;
const digitsOf=(currency:string)=>{const d=currencyDigits(currency);return d===undefined?{}:{minimumFractionDigits:d,maximumFractionDigits:d}};
export const euro=(value:number)=>new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR',...digitsOf('EUR')}).format(value);
export const number=(value:number)=>new Intl.NumberFormat('fr-FR').format(value);
export const date=(value:unknown)=>{const d=new Date(String(value));return isNaN(d.getTime())?String(value):d.toLocaleDateString('fr-FR',{day:'2-digit',month:'short',year:'numeric'})};
export const initials=(value:string)=>value.split(' ').filter(Boolean).slice(0,2).map(n=>n[0]).join('').toUpperCase();
export const money=(value:number,currency='EUR')=>{try{return new Intl.NumberFormat('fr-FR',{style:'currency',currency,...digitsOf(currency)}).format(value)}catch{return `${number(value)} ${currency}`}};
export const dateTime=(value:unknown)=>{const d=new Date(String(value));return isNaN(d.getTime())?String(value??'—'):d.toLocaleString('fr-FR',{day:'2-digit',month:'short',year:'numeric',hour:'2-digit',minute:'2-digit'})};
export const isoDay=(d:Date)=>d.toISOString().slice(0,10);
/** Plus petite unité saisissable dans une devise : 1 pour XOF (sans décimale), 0,01 pour EUR. */
export const currencyStep=(currency:string)=>{try{const digits=new Intl.NumberFormat('fr-FR',{style:'currency',currency}).resolvedOptions().maximumFractionDigits??2;return digits===0?1:Number((1/10**digits).toFixed(digits))}catch{return 0.01}};
