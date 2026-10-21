import {useEffect, useSyncExternalStore} from 'react';
import {en} from './i18n-en';

/**
 * Langue des pages des clients (commande, borne, suivi, réservation, compte) : français ou anglais. Choisie par
 * `?lang=en` ou par le sélecteur, puis mémorisée sur l'appareil ; le français par défaut. Les textes sont écrits en
 * français dans le code et traduits par {@link tr} : un texte sans traduction reste en français. Le premier rendu est
 * toujours en français (celui du serveur), la langue choisie s'applique juste après.
 */
export type Lang = 'fr' | 'en';

export const languages: {value: Lang; label: string; name: string}[] = [
  {value: 'fr', label: 'FR', name: 'Français'},
  {value: 'en', label: 'EN', name: 'English'},
];

const storageKey = 'storefront.lang';
const listeners = new Set<() => void>();
let current: Lang = 'fr';
// La langue du rendu en cours : celle du serveur pendant l'hydratation d'un composant, puis la langue choisie.
let shown: Lang = 'fr';
let detected = false;

function detect(): Lang {
  const asked = new URLSearchParams(window.location.search).get('lang');
  if (asked === 'fr' || asked === 'en') return asked;
  try {
    const saved = window.localStorage.getItem(storageKey);
    if (saved === 'fr' || saved === 'en') return saved;
  } catch {
    // Stockage indisponible : français.
  }

  return 'fr';
}

export const currentLang = (): Lang => current;

function apply(lang: Lang) {
  current = lang;
  shown = lang;
  document.documentElement.lang = lang;
  listeners.forEach(listener => listener());
}

export function setLang(lang: Lang) {
  try {
    window.localStorage.setItem(storageKey, lang);
  } catch {
    // Stockage indisponible : vaut pour cette visite.
  }

  apply(lang);
}

const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => listeners.delete(listener);
};

/** La langue courante ; le composant se redessine quand elle change. */
export function useLang(): Lang {
  useEffect(() => {
    if (detected) return;
    detected = true;
    const lang = detect();
    if (lang !== current) apply(lang);
  }, []);
  const lang = useSyncExternalStore(subscribe, currentLang, (): Lang => 'fr');
  shown = lang;
  return lang;
}

/** Le texte dans la langue courante ; `{nom}` est remplacé par `vars.nom`. */
export function tr(text: string, vars?: Record<string, string | number>): string {
  const translated = shown === 'en' ? en[text] ?? text : text;
  return vars ? translated.replace(/\{(\w+)\}/g, (match, key: string) => (key in vars ? String(vars[key]) : match)) : translated;
}

/** Locale Intl de la langue courante (dates, heures). */
export const intlLocale = () => (shown === 'en' ? 'en-GB' : 'fr-FR');

/** Pluriel simple : `plural(n, 'article', 'articles')` traduit puis choisit. */
export const plural = (count: number, one: string, many: string) => tr(count > 1 ? many : one, {n: count});

/** Un dictionnaire de libellés traduits à la lecture (la langue peut changer après le chargement du module). */
export function translated<T extends Record<string, string>>(map: T): T {
  return new Proxy(map, {
    get: (target, key) => (typeof key === 'string' && Object.prototype.hasOwnProperty.call(target, key) ? tr(target[key]) : Reflect.get(target, key)),
  });
}
