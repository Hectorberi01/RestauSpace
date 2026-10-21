// Imprimante thermique reliée à cet appareil : Bluetooth (BLE) ou USB, depuis Chrome (ordinateur ou Android).
// Aucune donnée ne part vers le serveur : les octets ESC/POS sont produits et envoyés ici. Le choix (type, largeur) est
// gardé dans ce navigateur ; l'appareil lui-même est redemandé au besoin (règle des navigateurs).

import {render, type PaperWidth, type TicketLine} from './escpos';

export type Transport = 'bluetooth' | 'usb';

export interface DevicePrinterSettings {
  transport: Transport;
  paper: PaperWidth;
}

const KEY = 'restaurantos.device-printer';

// Services GATT des imprimantes thermiques BLE courantes (génériques chinoises, Xprinter, Munbyn, Goojprt...).
const SERVICES = [
  '000018f0-0000-1000-8000-00805f9b34fb',
  'e7810a71-73ae-499d-8c15-faa9aef0c3f2',
  '0000ae30-0000-1000-8000-00805f9b34fb',
  '0000ff00-0000-1000-8000-00805f9b34fb',
  '49535343-fe7d-4ae5-8fa9-9fafd205e455',
];

/* Types minimaux des API Web Bluetooth et WebUSB (absentes des définitions DOM de TypeScript). */
interface Characteristic {
  properties: {write: boolean; writeWithoutResponse: boolean};
  writeValue(data: BufferSource): Promise<void>;
  writeValueWithoutResponse?(data: BufferSource): Promise<void>;
}
interface GattService {
  getCharacteristics(): Promise<Characteristic[]>;
}
interface GattServer {
  connected: boolean;
  connect(): Promise<GattServer>;
  getPrimaryServices(): Promise<GattService[]>;
}
interface BluetoothDevice {
  name?: string;
  gatt?: GattServer;
}
interface UsbEndpoint {
  direction: 'in' | 'out';
  type: string;
  endpointNumber: number;
}
interface UsbDevice {
  productName?: string;
  opened: boolean;
  configuration: {interfaces: {interfaceNumber: number; alternate: {interfaceClass: number; endpoints: UsbEndpoint[]}}[]} | null;
  open(): Promise<void>;
  selectConfiguration(value: number): Promise<void>;
  claimInterface(value: number): Promise<void>;
  transferOut(endpoint: number, data: BufferSource): Promise<unknown>;
}
interface NavigatorWithDevices {
  bluetooth?: {requestDevice(options: unknown): Promise<BluetoothDevice>};
  usb?: {requestDevice(options: unknown): Promise<UsbDevice>; getDevices(): Promise<UsbDevice[]>};
}

const devices = () => navigator as unknown as NavigatorWithDevices;

export const supports = (transport: Transport) =>
  typeof navigator !== 'undefined' && (transport === 'bluetooth' ? !!devices().bluetooth : !!devices().usb);

export function loadSettings(): DevicePrinterSettings | null {
  try {
    const value = JSON.parse(localStorage.getItem(KEY) ?? 'null') as DevicePrinterSettings | null;
    return value && (value.transport === 'bluetooth' || value.transport === 'usb') && (value.paper === 58 || value.paper === 80) ? value : null;
  } catch {
    return null;
  }
}

export function saveSettings(settings: DevicePrinterSettings | null) {
  try {
    if (settings) localStorage.setItem(KEY, JSON.stringify(settings));
    else localStorage.removeItem(KEY);
  } catch {
    // Stockage indisponible (navigation privée) : le choix sera redemandé.
  }
}

type Sink = {name: string; write: (data: Uint8Array<ArrayBuffer>) => Promise<void>};

let current: {transport: Transport; sink: Sink} | null = null;

async function bluetooth(): Promise<Sink> {
  const api = devices().bluetooth;
  if (!api) throw new Error('Bluetooth indisponible dans ce navigateur : utilisez Chrome.');
  const device = await api.requestDevice({acceptAllDevices: true, optionalServices: SERVICES});
  const server = await device.gatt?.connect();
  if (!server) throw new Error('Connexion Bluetooth impossible.');
  let target: Characteristic | undefined;
  for (const service of await server.getPrimaryServices()) {
    target = (await service.getCharacteristics()).find(item => item.properties.writeWithoutResponse || item.properties.write);
    if (target) break;
  }
  if (!target) throw new Error('Cette imprimante ne propose pas d’accès en écriture par Bluetooth.');
  const characteristic = target;
  return {
    name: device.name ?? 'Imprimante Bluetooth',
    write: async data => {
      // Les imprimantes BLE acceptent de petits paquets : 100 octets, envoyés l'un après l'autre.
      for (let offset = 0; offset < data.length; offset += 100) {
        const part = data.slice(offset, offset + 100);
        if (characteristic.properties.writeWithoutResponse && characteristic.writeValueWithoutResponse) await characteristic.writeValueWithoutResponse(part);
        else await characteristic.writeValue(part);
      }
    },
  };
}

async function usb(): Promise<Sink> {
  const api = devices().usb;
  if (!api) throw new Error('USB indisponible dans ce navigateur : utilisez Chrome.');
  const known = (await api.getDevices()).find(item => item.configuration?.interfaces.some(entry => entry.alternate.interfaceClass === 7));
  const device = known ?? (await api.requestDevice({filters: [{classCode: 7}]}));
  if (!device.opened) await device.open();
  if (!device.configuration) await device.selectConfiguration(1);
  const entry = device.configuration?.interfaces.find(item => item.alternate.endpoints.some(endpoint => endpoint.direction === 'out' && endpoint.type === 'bulk'));
  const endpoint = entry?.alternate.endpoints.find(item => item.direction === 'out' && item.type === 'bulk');
  if (!entry || !endpoint) throw new Error('Imprimante USB non reconnue.');
  await device.claimInterface(entry.interfaceNumber).catch(() => undefined);
  return {name: device.productName ?? 'Imprimante USB', write: async data => void (await device.transferOut(endpoint.endpointNumber, data))};
}

/** Nom de l'imprimante connectée, ou null. */
export const connected = () => current?.sink.name ?? null;

export function disconnect() {
  current = null;
}

/** Imprime sur l'imprimante de l'appareil ; demande l'appareil au premier usage (geste de l'utilisateur requis). */
export async function printOnDevice(lines: TicketLine[], settings: DevicePrinterSettings): Promise<string> {
  if (!current || current.transport !== settings.transport) {
    current = {transport: settings.transport, sink: settings.transport === 'bluetooth' ? await bluetooth() : await usb()};
  }
  try {
    await current.sink.write(render(lines, settings.paper));
  } catch (error) {
    current = null;
    throw error;
  }
  return current.sink.name;
}
