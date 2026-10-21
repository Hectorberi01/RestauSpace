// Contenu des tickets imprimés depuis l'appareil (reçu, bon de cuisine, ticket Z). Aucune donnée personnelle du client.

import type {CashSession} from './cash';
import {movementLabel} from './cash';
import type {TicketLine} from './escpos';
import {componentLabel} from './formulas';
import {money, orderTypeLabel} from './format';
import {methodLabel, type OrderPayments} from './payments';
import type {Order, Tenant} from './types';

const stamp = (iso: string) =>
  new Intl.DateTimeFormat('fr-FR', {day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit'}).format(new Date(iso));

const text = (value: string, extra: Partial<Extract<TicketLine, {kind: 'text'}>> = {}): TicketLine => ({kind: 'text', text: value, ...extra});
const separator: TicketLine = {kind: 'separator'};
const feed: TicketLine = {kind: 'feed'};
const restaurantName = (tenant: Tenant) => tenant.branding.displayName || tenant.name;

/** Lignes fiscales ajoutées au reçu (mentions légales, code et QR de la facture normalisée), quand il y en a. */
export interface FiscalFooter {
  lines: string[];
  qr: string | null;
}

export function receiptLines(order: Order, payments: OrderPayments | null, tenant: Tenant, location: {name: string; address?: string}, fiscal?: FiscalFooter | null): TicketLine[] {
  const fmt = (value: number) => money(value, order.currencyCode);
  const lines: TicketLine[] = [
    text(restaurantName(tenant), {align: 'center', bold: true, large: true}),
    text(location.name, {align: 'center'}),
  ];
  if (location.address) lines.push(text(location.address, {align: 'center'}));
  lines.push(separator, text('Reçu n°', {right: order.number}), text('Date', {right: stamp(order.completedAtUtc ?? order.placedAtUtc ?? order.createdAtUtc)}), text('Type', {right: orderTypeLabel[order.type]}), separator);
  for (const item of order.items) {
    lines.push(text(`${item.quantity} x ${item.productName}${item.variantName ? ` (${item.variantName})` : ''}`, {right: fmt(item.lineTotal)}));
    for (const component of item.components ?? []) lines.push(text(`   ${componentLabel(component, false)}`));
    if (item.modifiers.length > 0) lines.push(text(`   + ${item.modifiers.map(modifier => modifier.name).join(', ')}`));
  }
  lines.push(separator, text('Sous-total', {right: fmt(order.subTotal)}));
  for (const discount of order.discounts) lines.push(text(`Remise${discount.label ? ` ${discount.label}` : ''}`, {right: `-${fmt(discount.amount)}`}));
  if (order.deliveryFee) lines.push(text('Frais de livraison', {right: fmt(order.deliveryFee)}));
  if (order.taxTotal) lines.push(text(order.priceTaxMode === 'TaxInclusive' ? 'dont taxes' : 'Taxes', {right: fmt(order.taxTotal)}));
  lines.push(text('TOTAL', {bold: true, large: true, right: fmt(order.total)}));
  const captured = (payments?.payments ?? []).filter(payment => payment.capturedAmount > 0);
  if (captured.length > 0) {
    lines.push(feed);
    for (const payment of captured) lines.push(text(methodLabel[payment.method], {right: fmt(payment.capturedAmount)}));
    const refunded = captured.reduce((sum, payment) => sum + payment.refundedAmount, 0);
    if (refunded > 0) lines.push(text('Remboursé', {right: `-${fmt(refunded)}`}));
  }
  if (fiscal) {
    lines.push(separator);
    for (const line of fiscal.lines) lines.push(text(line, {align: 'center'}));
    if (fiscal.qr) lines.push({kind: 'qr', text: fiscal.qr});
  }
  lines.push(feed, text('Merci de votre visite', {align: 'center'}), feed);
  return lines;
}

export function kitchenLines(order: Order): TicketLine[] {
  const lines: TicketLine[] = [
    text('CUISINE', {align: 'center', bold: true}),
    text(order.number, {align: 'center', bold: true, large: true}),
    text(orderTypeLabel[order.type].toUpperCase(), {align: 'center', bold: true}),
    text(stamp(order.placedAtUtc ?? order.createdAtUtc), {align: 'center'}),
    separator,
  ];
  for (const item of order.items) {
    lines.push(text(`${item.quantity} x ${item.productName}`, {bold: true, large: true}));
    if (item.variantName) lines.push(text(`   + ${item.variantName}`));
    for (const component of item.components ?? []) {
      lines.push(text(`   > ${component.productName}${component.variantName ? ` (${component.variantName})` : ''}`, {bold: true}));
      for (const modifier of component.modifiers) lines.push(text(`       + ${modifier.name}`));
    }
    for (const modifier of item.modifiers) lines.push(text(`   + ${modifier.name}`));
    if (item.note) lines.push(text(`   ! ${item.note}`, {bold: true}));
  }
  lines.push(separator, text(`${order.items.reduce((sum, item) => sum + item.quantity, 0)} article(s)`, {align: 'center'}), feed);
  return lines;
}

export function zLines(z: CashSession, tenant: Tenant, locationName: string): TicketLine[] {
  const fmt = (value: number) => money(value, z.currencyCode);
  const signed = (value: number) => (value === 0 ? '0' : `${value > 0 ? '+' : '-'}${fmt(Math.abs(value))}`);
  const lines: TicketLine[] = [
    text(restaurantName(tenant), {align: 'center', bold: true}),
    text(locationName, {align: 'center'}),
    text(`TICKET Z n°${z.number}`, {align: 'center', bold: true, large: true}),
    separator,
    text('Ouverture', {right: stamp(z.openedAtUtc)}),
    text('Clôture', {right: z.closedAtUtc ? stamp(z.closedAtUtc) : '-'}),
    separator,
    text('ESPÈCES', {bold: true}),
    text('Fond de caisse', {right: fmt(z.openingFloat)}),
    text('Ventes nettes', {right: fmt(z.takings.find(item => item.method === 'Cash')?.net ?? 0)}),
    text('Entrées', {right: `+${fmt(z.paidIn)}`}),
    text('Sorties', {right: `-${fmt(z.paidOut)}`}),
    text('Attendu', {bold: true, right: fmt(z.expectedCash)}),
  ];
  for (const movement of z.movements) lines.push(text(`${movementLabel[movement.kind]} : ${movement.reason}`, {right: `${movement.kind === 'PayIn' ? '+' : '-'}${fmt(movement.amount)}`}));
  lines.push(separator, text('COMPTAGE', {bold: true}));
  for (const line of z.lines) {
    lines.push(text(methodLabel[line.method], {bold: true}), text('  attendu', {right: fmt(line.expected)}), text('  compté', {right: fmt(line.counted)}), text('  écart', {right: signed(line.difference)}));
  }
  for (const coin of z.denominations) lines.push(text(`  ${new Intl.NumberFormat('fr-FR').format(coin.value)} x ${coin.count}`, {right: fmt(coin.value * coin.count)}));
  lines.push(separator, text('ÉCART TOTAL', {bold: true, right: signed(z.totalDifference)}));
  const others = z.takings.filter(item => !item.toCount);
  if (others.length > 0) {
    lines.push(separator, text('AUTRES ENCAISSEMENTS', {bold: true}));
    for (const item of others) lines.push(text(methodLabel[item.method], {right: fmt(item.net)}));
  }
  if (z.closingNote) lines.push(separator, text(`Note : ${z.closingNote}`));
  lines.push(feed);
  return lines;
}
