#!/usr/bin/env node
// Relais d'impression RestauSpace : relie une imprimante ESC/POS du réseau local (port 9100) à l'API.
// Il interroge l'API toutes les quelques secondes, envoie le ticket à l'imprimante, puis accuse réception.
// Sans dépendance : Node.js 20 ou plus récent.
//
//   RELAY_URL     adresse affichée dans la console (Paramètres, Imprimantes) ; elle contient le jeton : ne pas la diffuser.
//   PRINTER_HOST  adresse IP de l'imprimante sur le réseau local, par exemple 192.168.1.50.
//   PRINTER_PORT  port d'impression brute, 9100 par défaut.
//   POLL_SECONDS  intervalle d'interrogation, 3 secondes par défaut.
//
// Ni l'adresse, ni le jeton, ni le contenu des tickets ne sont écrits dans le journal.

import net from 'node:net';

const relayUrl = (process.env.RELAY_URL ?? '').replace(/\/+$/, '');
const printerHost = process.env.PRINTER_HOST ?? '';
const printerPort = Number(process.env.PRINTER_PORT ?? 9100);
const pollMs = Math.max(1, Number(process.env.POLL_SECONDS ?? 3)) * 1000;
const socketTimeoutMs = 10_000;

if (!/^https?:\/\/.+\/v1\/printing\/relay\/prt_[A-Za-z0-9_-]+$/.test(relayUrl) || !printerHost || !Number.isInteger(printerPort)) {
  console.error('Configuration incomplète : RELAY_URL (adresse de la console) et PRINTER_HOST sont obligatoires.');
  process.exit(1);
}

if (relayUrl.startsWith('http://') && !/^http:\/\/(localhost|127\.0\.0\.1)(:\d+)?\//.test(relayUrl)) {
  console.error('RELAY_URL doit être en https.');
  process.exit(1);
}

const log = message => console.log(`${new Date().toISOString()} ${message}`);
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
let stopping = false;
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => (stopping = true));

/** Envoie les octets à l'imprimante (impression brute TCP) ; rejette sur erreur ou délai dépassé. */
function print(bytes) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection({host: printerHost, port: printerPort});
    socket.setTimeout(socketTimeoutMs);
    socket.once('connect', () => socket.end(bytes));
    socket.once('close', hadError => (hadError ? null : resolve()));
    socket.once('timeout', () => {
      socket.destroy();
      reject(Object.assign(new Error('timeout'), {code: 'printer_timeout'}));
    });
    socket.once('error', error => reject(Object.assign(error, {code: error.code === 'ECONNREFUSED' ? 'printer_refused' : 'printer_unreachable'})));
  });
}

async function acknowledge(jobId, printed, code) {
  const response = await fetch(`${relayUrl}/jobs/${jobId}`, {
    method: 'POST',
    headers: {'Content-Type': 'application/json'},
    body: JSON.stringify({printed, code}),
    signal: AbortSignal.timeout(15_000),
  });
  if (!response.ok) throw new Error(`réponse ${response.status}`);
}

async function once() {
  const response = await fetch(`${relayUrl}/next`, {headers: {Accept: 'application/json'}, signal: AbortSignal.timeout(15_000)});
  if (response.status === 204) return false;
  if (response.status === 401 || response.status === 403) throw Object.assign(new Error('jeton refusé : générez une nouvelle adresse dans la console'), {fatal: true});
  if (!response.ok) throw new Error(`réponse ${response.status}`);

  const job = await response.json();
  const bytes = Buffer.from(job.content, 'base64');
  let code = '200';
  try {
    await print(bytes);
    log(`ticket ${job.jobId.slice(0, 8)} imprimé (${bytes.length} octets)`);
  } catch (error) {
    code = error.code ?? 'printer_error';
    log(`ticket ${job.jobId.slice(0, 8)} non imprimé : ${code}`);
  }

  // Sans accusé, l'API renverra le ticket après 2 minutes : on ne le réimprime pas ici.
  await acknowledge(job.jobId, code === '200', code).catch(error => log(`accusé non transmis : ${error.message}`));
  return true;
}

log(`relais démarré, imprimante ${printerHost}:${printerPort}`);
let failures = 0;
while (!stopping) {
  try {
    const printed = await once();
    failures = 0;
    if (!printed) await sleep(pollMs);
  } catch (error) {
    if (error.fatal) {
      log(error.message);
      process.exit(2);
    }

    failures += 1;
    log(`API injoignable (${error.message}) ; nouvel essai`);
    await sleep(Math.min(60_000, pollMs * 2 ** Math.min(failures, 5)));
  }
}

log('relais arrêté');
