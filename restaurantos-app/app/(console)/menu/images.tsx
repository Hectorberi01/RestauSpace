'use client';
import {useEffect, useRef, useState} from 'react';
import {ChevronLeft, ChevronRight, ImagePlus, Loader2, Trash2, UtensilsCrossed} from 'lucide-react';
import {describe} from '@/lib/api';
import {deleteImage, imageProblem, imageUrl, IMAGE_TYPES, productImages, uploadProductImage, type MediaFile} from '@/lib/files';
import {useConsole} from '@/components/console';
import {cx, useToast} from '@/components/ui';

const tints = [
  'from-amber-100 to-orange-200 text-orange-700',
  'from-emerald-100 to-teal-200 text-teal-700',
  'from-sky-100 to-indigo-200 text-indigo-700',
  'from-rose-100 to-pink-200 text-rose-700',
  'from-lime-100 to-green-200 text-green-700',
  'from-violet-100 to-purple-200 text-purple-700',
];

function tintOf(value: string) {
  let hash = 0;
  for (const char of value) hash = (hash * 31 + char.charCodeAt(0)) | 0;
  return tints[Math.abs(hash) % tints.length];
}

/** Visuel de remplacement quand le produit n'a pas de photo. */
export function Placeholder({name, seed, className, small}: {name: string; seed: string; className?: string; small?: boolean}) {
  return (
    <div className={cx('flex flex-col items-center justify-center gap-1 bg-gradient-to-br', tintOf(seed), className)} aria-hidden="true">
      {!small && <UtensilsCrossed className="size-7 opacity-70" />}
      <span className="text-xl font-bold opacity-80">{name.trim().slice(0, 1).toUpperCase()}</span>
    </div>
  );
}

/** Photo de couverture d'un produit (la première envoyée), chargée à l'affichage. */
export function Cover({productId, name, seed, version, className, small, fill}: {productId: string; name: string; seed: string; version?: number; className?: string; small?: boolean; fill?: boolean}) {
  const {can} = useConsole();
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const allowed = can('files.view');

  useEffect(() => {
    if (!allowed) return;
    let active = true;
    setFailed(false);
    productImages(productId, version !== undefined && version > 0)
      .then(files => (files[0] ? imageUrl(files[0].thumbnailId ?? files[0].id) : null))
      .then(found => active && setUrl(found))
      .catch(() => active && setUrl(null));
    return () => {
      active = false;
    };
  }, [productId, allowed, version]);

  if (!url || failed) return <Placeholder name={name} seed={seed} className={className} small={small} />;
  if (small || fill) return <img src={url} alt="" loading="lazy" onError={() => setFailed(true)} className={cx('object-cover', className)} />;
  // Photo entière quel que soit son format : rien n'est rogné, le reste du cadre est un fond neutre.
  return (
    <span className={cx('relative block overflow-hidden bg-slate-50', className)}>
      <img src={url} alt="" loading="lazy" onError={() => setFailed(true)} className="relative size-full object-contain drop-shadow-sm" />
    </span>
  );
}

/** Photos d'un produit, l'une après l'autre, avec flèches quand il y en a plusieurs. */
export function Carousel({productId, name, seed, version, className}: {productId: string; name: string; seed: string; version?: number; className?: string}) {
  const {can} = useConsole();
  const [urls, setUrls] = useState<string[]>([]);
  const [index, setIndex] = useState(0);
  const allowed = can('files.view');

  useEffect(() => {
    if (!allowed) return;
    let active = true;
    setIndex(0);
    productImages(productId, version !== undefined && version > 0)
      .then(files => Promise.all(files.map(file => imageUrl(file.id).catch(() => null))))
      .then(found => active && setUrls(found.filter((url): url is string => !!url)))
      .catch(() => active && setUrls([]));
    return () => {
      active = false;
    };
  }, [productId, allowed, version]);

  if (urls.length === 0) return <Placeholder name={name} seed={seed} className={className} />;
  const current = Math.min(index, urls.length - 1);
  const arrow = 'absolute top-1/2 grid size-9 -translate-y-1/2 place-items-center rounded-full bg-white/90 text-ink-800 shadow-md transition hover:bg-white';
  return (
    <div className={cx('relative overflow-hidden bg-slate-50', className)}>
      <img src={urls[current]} alt={urls.length > 1 ? `${name}, photo ${current + 1} sur ${urls.length}` : name} className="size-full object-contain" />
      {urls.length > 1 && (
        <>
          <button type="button" className={cx(arrow, 'left-3')} aria-label="Photo précédente" onClick={() => setIndex((current - 1 + urls.length) % urls.length)}>
            <ChevronLeft className="size-5" />
          </button>
          <button type="button" className={cx(arrow, 'right-3')} aria-label="Photo suivante" onClick={() => setIndex((current + 1) % urls.length)}>
            <ChevronRight className="size-5" />
          </button>
          <span className="absolute bottom-3 left-1/2 flex -translate-x-1/2 gap-1.5" aria-hidden="true">
            {urls.map((url, position) => <span key={url} className={cx('size-1.5 rounded-full transition', position === current ? 'w-4 bg-brand-600' : 'bg-white/90 shadow')} />)}
          </span>
        </>
      )}
    </div>
  );
}

function Thumb({file, productId, onDeleted}: {file: MediaFile; productId: string; onDeleted: () => void}) {
  const {can} = useConsole();
  const toast = useToast();
  const [url, setUrl] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let active = true;
    imageUrl(file.id).then(found => active && setUrl(found)).catch(() => undefined);
    return () => {
      active = false;
    };
  }, [file.id]);
  return (
    <div className="group relative aspect-square overflow-hidden rounded-xl bg-slate-100">
      {url && <img src={url} alt={file.fileName} className="size-full object-cover" />}
      {can('files.manage') && (
        <button
          type="button"
          disabled={busy}
          aria-label={`Supprimer la photo ${file.fileName}`}
          onClick={async () => {
            setBusy(true);
            try {
              await deleteImage(productId, file.id);
              onDeleted();
            } catch (error) {
              toast('error', describe(error));
            } finally {
              setBusy(false);
            }
          }}
          className="absolute top-1.5 right-1.5 grid size-7 place-items-center rounded-lg bg-white/90 text-red-600 opacity-100 shadow-sm transition sm:opacity-0 sm:group-hover:opacity-100 sm:focus-visible:opacity-100">
          {busy ? <Loader2 className="size-4 animate-spin" /> : <Trash2 className="size-4" />}
        </button>
      )}
    </div>
  );
}

/** Galerie d'un produit : photos disponibles, ajout (JPEG, PNG, WebP, 5 Mo) et suppression. */
export function Gallery({productId, onChanged}: {productId: string; onChanged: () => void}) {
  const {can} = useConsole();
  const toast = useToast();
  const input = useRef<HTMLInputElement>(null);
  const [files, setFiles] = useState<MediaFile[] | null>(null);
  const [uploading, setUploading] = useState(false);
  const [version, setVersion] = useState(0);

  useEffect(() => {
    if (!can('files.view')) return;
    let active = true;
    productImages(productId, version > 0)
      .then(found => active && setFiles(found))
      .catch(() => active && setFiles([]));
    return () => {
      active = false;
    };
  }, [productId, version, can]);

  const refresh = () => {
    setVersion(value => value + 1);
    onChanged();
  };

  if (!can('files.view')) return <p className="text-sm text-ink-500">Réservé aux membres qui peuvent consulter les fichiers.</p>;

  return (
    <div className="grid grid-cols-4 gap-2">
      {files?.map(file => <Thumb key={file.id} file={file} productId={productId} onDeleted={refresh} />)}
      {can('files.upload') && (
        <>
          <button
            type="button"
            disabled={uploading}
            onClick={() => input.current?.click()}
            className="flex aspect-square flex-col items-center justify-center gap-1 rounded-xl border-2 border-dashed border-slate-300 text-xs font-medium text-ink-500 transition hover:border-brand-400 hover:text-brand-700 disabled:opacity-60">
            {uploading ? <Loader2 className="size-5 animate-spin" aria-hidden="true" /> : <ImagePlus className="size-5" aria-hidden="true" />}
            {uploading ? 'Envoi…' : 'Ajouter'}
          </button>
          <input
            ref={input}
            type="file"
            accept={IMAGE_TYPES.join(',')}
            className="sr-only"
            tabIndex={-1}
            aria-label="Choisir une photo"
            onChange={async event => {
              const file = event.target.files?.[0];
              event.target.value = '';
              if (!file) return;
              const problem = imageProblem(file);
              if (problem) return toast('error', problem);
              setUploading(true);
              try {
                await uploadProductImage(productId, file);
                toast('success', 'Photo ajoutée.');
                refresh();
              } catch (error) {
                toast('error', describe(error));
              } finally {
                setUploading(false);
              }
            }}
          />
        </>
      )}
      {files && files.length === 0 && !can('files.upload') && <p className="col-span-4 text-sm text-ink-500">Aucune photo.</p>}
    </div>
  );
}
