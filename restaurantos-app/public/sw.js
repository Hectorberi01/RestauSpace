// Service worker de la console : garde l'interface pour l'ouvrir sans réseau. Les appels à l'API (autre origine) ne
// passent pas ici : la vente hors ligne garde ses propres données (IndexedDB). Rien de personnel n'est mis en cache.
const VERSION = 'restaurantos-v1';
const SHELL = ['/caisse/vente', '/connexion', '/manifest.webmanifest', '/icon-192.png', '/icon-512.png', '/favicon.svg'];

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(VERSION)
      .then(cache => Promise.all(SHELL.map(url => cache.add(new Request(url, {credentials: 'same-origin'})).catch(() => undefined))))
      .then(() => self.skipWaiting()),
  );
});

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(keys.filter(key => key !== VERSION).map(key => caches.delete(key))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener('fetch', event => {
  const request = event.request;
  const url = new URL(request.url);
  if (request.method !== 'GET' || url.origin !== self.location.origin) return;

  // Fichiers versionnés de Next : le cache d'abord, ils ne changent jamais sous le même nom.
  if (url.pathname.startsWith('/_next/static/') || /\.(png|svg|woff2?)$/.test(url.pathname)) {
    event.respondWith(
      caches.match(request).then(cached => cached ?? fetch(request).then(response => {
        if (response.ok) {
          const copy = response.clone();
          void caches.open(VERSION).then(cache => cache.put(request, copy));
        }
        return response;
      })),
    );
    return;
  }

  // Pages : le réseau d'abord ; sans réseau, la dernière version vue, ou la vente rapide.
  if (request.mode === 'navigate') {
    event.respondWith(
      fetch(request)
        .then(response => {
          if (response.ok) {
            const copy = response.clone();
            void caches.open(VERSION).then(cache => cache.put(request, copy));
          }
          return response;
        })
        .catch(() => caches.match(request).then(cached => cached ?? caches.match('/caisse/vente')).then(cached => cached ?? Response.error())),
    );
  }
});
