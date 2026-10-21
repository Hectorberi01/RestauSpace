'use client';

const tileColors=['#5B3DF5','#0EA5E9','#F97316','#10B981','#E11D48','#7C3AED','#0F766E','#CA8A04'];
const tileColor=(code:string)=>tileColors[[...code].reduce((t,c)=>t+c.charCodeAt(0),0)%tileColors.length];

/** Pastille d'un prestataire : son initiale sur une couleur stable tirée de son code (aucun logo de marque). */
export function ProviderTile({code,name}:{code:string;name:string}){
  return <span className="provider-tile" style={{background:tileColor(code)}} aria-hidden="true">{name.trim().charAt(0).toUpperCase()}</span>;
}
