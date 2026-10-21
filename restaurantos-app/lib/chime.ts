/**
 * Sonnerie des nouvelles commandes en ligne, jouée par le navigateur (Web Audio, aucun fichier).
 * Un navigateur n'autorise le son qu'après une interaction : le contexte audio est débloqué au premier clic ou à la
 * première touche sur la page.
 */
let context: AudioContext | null = null;

export function armChime() {
  if (typeof window === 'undefined') return () => {};
  const unlock = () => {
    try {
      context ??= new AudioContext();
      void context.resume();
    } catch {
      context = null;
    }
  };
  window.addEventListener('pointerdown', unlock);
  window.addEventListener('keydown', unlock);
  return () => {
    window.removeEventListener('pointerdown', unlock);
    window.removeEventListener('keydown', unlock);
  };
}

/** Deux notes courtes ; sans effet si le son n'est pas encore débloqué. */
export function playChime() {
  if (!context || context.state !== 'running') return false;
  const start = context.currentTime;
  [880, 1320].forEach((frequency, index) => {
    const oscillator = context!.createOscillator();
    const gain = context!.createGain();
    oscillator.type = 'sine';
    oscillator.frequency.value = frequency;
    const at = start + index * 0.18;
    gain.gain.setValueAtTime(0.0001, at);
    gain.gain.exponentialRampToValueAtTime(0.3, at + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, at + 0.16);
    oscillator.connect(gain).connect(context!.destination);
    oscillator.start(at);
    oscillator.stop(at + 0.18);
  });
  return true;
}
