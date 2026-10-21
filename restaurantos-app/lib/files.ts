import {api, del, post} from './api';

export const PRODUCT_OWNER = 'catalog.product';
export const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
export const MAX_IMAGE_BYTES = 5 * 1024 * 1024;

export interface MediaFile {
  id: string;
  ownerType: string;
  ownerId: string;
  fileName: string;
  contentType: string;
  sizeInBytes: number;
  status: 'Pending' | 'Available' | 'Quarantined' | 'Deleted';
  createdAtUtc: string;
  availableAtUtc: string | null;
  /** La miniature disponible de la photo, pour les listes. */
  thumbnailId?: string | null;
}

interface UploadTicket {
  fileId: string;
  uploadUrl: string;
  method: string;
  headers: Record<string, string>;
}

interface UploadResult {
  fileId: string;
  outcome: 'Available' | 'Quarantined';
  reason: string | null;
}

const links = new Map<string, {url: string; expires: number}>();
const listings = new Map<string, {files: Promise<MediaFile[]>; at: number}>();

/** Photos disponibles d'un produit, de la plus ancienne (couverture) à la plus récente ; mises en cache 60 s. */
export function productImages(productId: string, fresh = false): Promise<MediaFile[]> {
  const cached = listings.get(productId);
  if (!fresh && cached && Date.now() - cached.at < 60_000) return cached.files;
  const files = api<MediaFile[]>('/files', {query: {ownerType: PRODUCT_OWNER, ownerId: productId}}).then(items =>
    items.filter(item => item.status === 'Available').sort((a, b) => a.createdAtUtc.localeCompare(b.createdAtUtc)),
  );
  files.catch(() => listings.delete(productId));
  listings.set(productId, {files, at: Date.now()});
  return files;
}

/** Lien de lecture signé (courte durée), réutilisé tant qu'il lui reste plus de 30 secondes. */
export async function imageUrl(fileId: string): Promise<string> {
  const cached = links.get(fileId);
  if (cached && cached.expires - Date.now() > 30_000) return cached.url;
  const link = await api<{url: string; expiresAtUtc: string}>(`/files/${fileId}/download`);
  links.set(fileId, {url: link.url, expires: new Date(link.expiresAtUtc).getTime()});
  return link.url;
}

export function imageProblem(file: File): string | null {
  if (!IMAGE_TYPES.includes(file.type)) return 'Formats acceptés : JPEG, PNG ou WebP.';
  if (file.size > MAX_IMAGE_BYTES) return 'La photo dépasse 5 Mo.';
  if (file.size === 0) return 'Le fichier est vide.';
  return null;
}

/**
 * Envoie une photo : déclaration à l'API (type et taille contrôlés), envoi direct des octets au stockage par l'URL
 * signée, puis confirmation (le serveur vérifie le type réel et la taille reçue).
 */
export async function uploadProductImage(productId: string, file: File): Promise<void> {
  const prepared = await prepareImage(file, MAX_SIDE, 0.85);
  const photo = await uploadImage(PRODUCT_OWNER, productId, prepared);
  // La miniature est un plus : son échec ne fait pas échouer la photo.
  try {
    const thumbnail = await prepareImage(file, THUMBNAIL_SIDE, 0.75, true);
    if (thumbnail.size <= MAX_THUMBNAIL_BYTES) await uploadImage(PRODUCT_OWNER, productId, thumbnail, photo);
  } catch {
    // Sans miniature, les listes affichent la photo.
  }
  listings.delete(productId);
}

const MAX_SIDE = 1600;
const THUMBNAIL_SIDE = 480;
const MAX_THUMBNAIL_BYTES = 512 * 1024;

const canvasBlob = (canvas: HTMLCanvasElement, type: string, quality: number) =>
  new Promise<Blob | null>(resolve => canvas.toBlob(resolve, type, quality));

/**
 * Réduit la photo dans le navigateur avant l'envoi : au plus {@link MAX_SIDE} pixels de côté, en WebP (JPEG si le
 * navigateur ne sait pas l'écrire). Une photo déjà petite et légère part telle quelle, sauf pour une miniature.
 */
async function prepareImage(file: File, side: number, quality: number, always = false): Promise<File> {
  if (typeof createImageBitmap !== 'function') {
    if (always) throw new Error('unsupported');
    return file;
  }
  const bitmap = await createImageBitmap(file);
  try {
    const scale = Math.min(1, side / Math.max(bitmap.width, bitmap.height));
    if (!always && scale === 1 && file.size <= 1.5 * 1024 * 1024) return file;
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(bitmap.width * scale));
    canvas.height = Math.max(1, Math.round(bitmap.height * scale));
    canvas.getContext('2d')!.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    let blob = await canvasBlob(canvas, 'image/webp', quality);
    if (!blob || blob.type !== 'image/webp') blob = await canvasBlob(canvas, 'image/jpeg', quality);
    if (!blob) throw new Error('unsupported');
    if (!always && blob.size >= file.size) return file;
    const extension = blob.type === 'image/webp' ? 'webp' : 'jpg';
    return new File([blob], `${file.name.replace(/\.[^.]+$/, '') || 'photo'}${always ? '-mini' : ''}.${extension}`, {type: blob.type});
  } finally {
    bitmap.close();
  }
}

export const TENANT_OWNER = 'tenants.tenant';

/** Le logo du restaurant (2 Mo au plus), envoyé comme les photos ; renvoie son lien public stable. */
export async function uploadLogo(tenantId: string, file: File): Promise<string> {
  if (file.size > 2 * 1024 * 1024) throw new Error('Le logo dépasse 2 Mo.');
  const fileId = await uploadImage(TENANT_OWNER, tenantId, file);
  return (await api<{url: string}>(`/files/${fileId}/public-link`)).url;
}

/** La photo de couverture du restaurant, réduite à 2 000 pixels de côté ; renvoie son lien public stable. */
export async function uploadCover(tenantId: string, file: File): Promise<string> {
  const prepared = await prepareImage(file, 2000, 0.85);
  const fileId = await uploadImage(TENANT_OWNER, tenantId, prepared);
  return (await api<{url: string}>(`/files/${fileId}/public-link`)).url;
}

async function uploadImage(ownerType: string, ownerId: string, file: File, thumbnailOf?: string): Promise<string> {
  const problem = imageProblem(file);
  if (problem) throw new Error(problem);
  const name = file.name.replace(/[\\/\u0000-\u001f]/g, '_').slice(-120) || 'photo';
  const ticket = await post<UploadTicket>('/files/uploads', {
    ownerType,
    ownerId,
    fileName: name,
    contentType: file.type,
    sizeInBytes: file.size,
    thumbnailOf: thumbnailOf ?? null,
  });
  let response: Response;
  try {
    response = await fetch(ticket.uploadUrl, {method: ticket.method, headers: ticket.headers, body: file});
  } catch {
    throw new Error('Le stockage des photos est injoignable.');
  }
  if (!response.ok) throw new Error('Le stockage a refusé la photo.');
  const result = await post<UploadResult>(`/files/${ticket.fileId}/complete`);
  if (result.outcome === 'Quarantined') throw new Error('L’image a été refusée : son contenu ne correspond pas à une image valide.');
  return ticket.fileId;
}

export async function deleteImage(productId: string, fileId: string): Promise<void> {
  await del(`/files/${fileId}`);
  links.delete(fileId);
  listings.delete(productId);
}
