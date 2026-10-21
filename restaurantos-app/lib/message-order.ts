/** Lecture d'une commande écrite par un client (WhatsApp, SMS) : quantités et produits reconnus dans le catalogue. */

export interface ParsedLine {
  text: string;
  quantity: number;
  productId: string | null;
}

const words: Record<string, number> = {un: 1, une: 1, deux: 2, trois: 3, quatre: 4, cinq: 5, six: 6, sept: 7, huit: 8, neuf: 9, dix: 10};

const normalize = (value: string) =>
  value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();

const tokens = (value: string) => normalize(value).split(' ').filter(token => token.length > 1).map(token => token.replace(/s$/, ''));

/** Quantité en tête (« 2 », « 2x », « deux ») ou en fin (« x2 ») ; 1 par défaut. */
function quantityOf(fragment: string): {quantity: number; rest: string} {
  const head = fragment.match(/^(\d{1,2})\s*(?:x|×|fois)?\s+(.+)$/i);
  if (head) return {quantity: Number(head[1]), rest: head[2]};
  const tail = fragment.match(/^(.+?)\s*(?:x|×)\s*(\d{1,2})$/i);
  if (tail) return {quantity: Number(tail[2]), rest: tail[1]};
  const word = fragment.match(/^(\p{L}+)\s+(.+)$/u);
  const number = word ? words[normalize(word[1])] : undefined;
  if (word && number) return {quantity: number, rest: word[2]};
  return {quantity: 1, rest: fragment};
}

/** Le produit dont le nom est le mieux retrouvé dans le fragment (la moitié de ses mots au moins). */
function bestProduct(fragment: string, products: {id: string; name: string}[]): string | null {
  const found = new Set(tokens(fragment));
  let best: {id: string; score: number; length: number} | null = null;
  for (const product of products) {
    const name = tokens(product.name);
    if (name.length === 0) continue;
    const matched = name.filter(token => found.has(token)).length;
    const score = matched / name.length;
    if (score >= 0.5 && matched > 0 && (!best || score > best.score || (score === best.score && name.length > best.length))) {
      best = {id: product.id, score, length: name.length};
    }
  }
  return best?.id ?? null;
}

export function parseMessageOrder(message: string, products: {id: string; name: string}[]): ParsedLine[] {
  return message
    .split(/\r?\n|,|;|\s\+\s|\set\s/i)
    .map(part => part.replace(/^[\s\-*•·]+/, '').trim())
    .filter(part => part.length > 1)
    .map(part => {
      const {quantity, rest} = quantityOf(part);
      return {text: part, quantity: Math.min(Math.max(quantity, 1), 99), productId: bestProduct(rest, products)};
    });
}
