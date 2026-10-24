import type {BackupFailureCode,BackupStatus,BackupTrigger,RestoreFailureCode,RestoreStatus} from '../../services/api/admin-api';
import {dateTime} from '../../utils/format';

export const triggerLabels:Record<BackupTrigger,string>={Manual:'Manuelle',Scheduled:'Planifiée'};

export const backupStatus:Record<BackupStatus,[string,string]>={
  Pending:['En attente','amber'],
  Running:['En cours','purple'],
  Uploading:['Envoi','purple'],
  Completed:['Terminée','green'],
  Failed:['Échec','red'],
  Deleting:['Suppression','amber'],
  Deleted:['Supprimée','gray'],
};

export const restoreStatus:Record<RestoreStatus,[string,string]>={
  Pending:['Programmée','amber'],
  Running:['En cours','purple'],
  Completed:['Terminée','green'],
  Failed:['Échec','red'],
  Cancelled:['Annulée','gray'],
};

export const backupFailures:Record<BackupFailureCode,string>={
  dump_failed:'Échec de pg_dump',
  verify_failed:'Vérification du fichier impossible (pg_restore --list)',
  encrypt_failed:'Échec du chiffrement (age)',
  upload_failed:'Échec de l’envoi vers le stockage',
  timeout:'Délai dépassé : l’agent ne donnait plus de nouvelles',
  agent_restarted:'Agent redémarré pendant le travail',
};

export const restoreFailures:Record<RestoreFailureCode,string>={
  disabled:'Restauration désactivée sur le serveur de base',
  download_failed:'Téléchargement de la sauvegarde impossible',
  checksum_mismatch:'Empreinte SHA-256 différente : fichier altéré',
  invalid_key:'Emplacement du fichier invalide',
  key_missing:'Clé privée age absente du serveur',
  decrypt_failed:'Échec du déchiffrement',
  restore_failed:'Échec de pg_restore',
  swap_failed:'Échec de l’échange des bases',
  timeout:'Délai dépassé : l’agent ne donnait plus de nouvelles',
};

/** Statuts où l'agent travaille encore : la liste se rafraîchit. */
export const busyBackup=(s:BackupStatus)=>s==='Pending'||s==='Running'||s==='Uploading'||s==='Deleting';
/** Sauvegarde active au sens de l'API (index unique) : bloque une nouvelle demande. */
export const activeBackup=(s:BackupStatus)=>s==='Pending'||s==='Running'||s==='Uploading';
export const activeRestore=(s:RestoreStatus)=>s==='Pending'||s==='Running';

export function bytes(value:number|null){
  if(value===null)return '—';
  const units=['o','Ko','Mo','Go','To'];
  let v=value,i=0;
  while(v>=1024&&i<units.length-1){v/=1024;i++}
  return `${v.toLocaleString('fr-FR',{maximumFractionDigits:i===0?0:1})} ${units[i]}`;
}

export function span(ms:number){
  const s=Math.max(0,Math.round(ms/1000));
  if(s<60)return `${s} s`;
  const m=Math.floor(s/60);
  if(m<60)return `${m} min ${String(s%60).padStart(2,'0')} s`;
  return `${Math.floor(m/60)} h ${String(m%60).padStart(2,'0')} min`;
}

export function ago(value:string|null,now:number){
  if(!value)return 'jamais';
  const ms=now-new Date(value).getTime();
  if(ms<60000)return 'à l’instant';
  if(ms<3600000)return `il y a ${Math.floor(ms/60000)} min`;
  if(ms<86400000)return `il y a ${Math.floor(ms/3600000)} h`;
  return `le ${dateTime(value)}`;
}

/** Durée d'une sauvegarde ; en cours, depuis son démarrage. */
export function duration(startedAtUtc:string|null,completedAtUtc:string|null,now:number){
  if(!startedAtUtc)return '—';
  const end=completedAtUtc?new Date(completedAtUtc).getTime():now;
  return span(end-new Date(startedAtUtc).getTime());
}
