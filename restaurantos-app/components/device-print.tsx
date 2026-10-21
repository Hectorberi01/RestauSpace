'use client';
import {useEffect, useState} from 'react';
import {Bluetooth, Usb} from 'lucide-react';
import {ApiError, describe} from '@/lib/api';
import {loadSettings, printOnDevice, saveSettings, supports, disconnect, type DevicePrinterSettings, type Transport} from '@/lib/device-printer';
import type {PaperWidth, TicketLine} from '@/lib/escpos';
import {Button, Modal, cx, useToast} from './ui';

/**
 * Imprime sur l'imprimante thermique de l'appareil (Bluetooth ou USB). Au premier usage, demande le type et la largeur du
 * papier, puis l'imprimante (fenêtre du navigateur).
 */
export function DevicePrintButton({lines, label = 'Imprimante de l’appareil', disabled, variant = 'secondary'}: {
  lines: () => TicketLine[];
  label?: string;
  disabled?: boolean;
  variant?: 'secondary' | 'primary';
}) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  const [choosing, setChoosing] = useState(false);
  const [configured, setConfigured] = useState(false);
  useEffect(() => setConfigured(!!loadSettings()), [choosing]);

  const print = async (settings: DevicePrinterSettings) => {
    setBusy(true);
    try {
      const name = await printOnDevice(lines(), settings);
      toast('success', `Envoyé à ${name}.`);
    } catch (error) {
      // Fenêtre de choix fermée sans imprimante : rien à signaler.
      if (!(error instanceof DOMException && error.name === 'NotFoundError')) toast('error', error instanceof ApiError || !(error instanceof Error) ? describe(error) : error.message);
    } finally {
      setBusy(false);
    }
  };

  const start = () => {
    const settings = loadSettings();
    if (settings) void print(settings);
    else setChoosing(true);
  };

  return (
    <>
      <span className="inline-flex items-center gap-1">
        <Button variant={variant} busy={busy} disabled={disabled} onClick={start}>
          <Bluetooth className="size-4" aria-hidden="true" />{label}
        </Button>
        {configured && (
          <button type="button" className="px-1 text-xs font-medium text-brand-700 hover:underline" onClick={() => setChoosing(true)}>Changer</button>
        )}
      </span>
      {choosing && (
        <DevicePrinterDialog
          onClose={() => setChoosing(false)}
          onChosen={settings => {
            saveSettings(settings);
            disconnect();
            setChoosing(false);
            void print(settings);
          }}
        />
      )}
    </>
  );
}

function DevicePrinterDialog({onClose, onChosen}: {onClose: () => void; onChosen: (settings: DevicePrinterSettings) => void}) {
  const initial = loadSettings();
  const [transport, setTransport] = useState<Transport>(initial?.transport ?? 'bluetooth');
  const [paper, setPaper] = useState<PaperWidth>(initial?.paper ?? 58);
  const option = (active: boolean) => cx('flex flex-1 items-center gap-3 rounded-xl border px-4 py-3 text-left text-sm', active ? 'border-brand-500 bg-brand-50 ring-1 ring-brand-500' : 'border-slate-200 hover:bg-slate-50');
  return (
    <Modal
      open
      onClose={onClose}
      title="Imprimante de cet appareil"
      footer={<><Button variant="secondary" onClick={onClose}>Annuler</Button><Button onClick={() => onChosen({transport, paper})} disabled={!supports(transport)}>Choisir l’imprimante</Button></>}
    >
      <div className="space-y-5">
        <p className="text-sm text-ink-500">Imprimante thermique ESC/POS reliée à ce téléphone, cette tablette ou cet ordinateur. Fonctionne avec Chrome ; le choix reste sur cet appareil.</p>
        <div className="flex flex-col gap-2 sm:flex-row" role="radiogroup" aria-label="Liaison">
          {([['bluetooth', 'Bluetooth', Bluetooth], ['usb', 'Câble USB', Usb]] as const).map(([value, name, Icon]) => (
            <button key={value} type="button" role="radio" aria-checked={transport === value} className={option(transport === value)} onClick={() => setTransport(value)}>
              <Icon className="size-5 text-brand-700" aria-hidden="true" />
              <span><span className="block font-semibold">{name}</span>{!supports(value) && <span className="text-xs text-red-700">Non pris en charge par ce navigateur</span>}</span>
            </button>
          ))}
        </div>
        <div className="flex gap-2" role="radiogroup" aria-label="Largeur du papier">
          {([58, 80] as const).map(value => (
            <button key={value} type="button" role="radio" aria-checked={paper === value} className={option(paper === value)} onClick={() => setPaper(value)}>
              <span className="font-semibold">{value} mm</span>
            </button>
          ))}
        </div>
      </div>
    </Modal>
  );
}
