import {loadInvoice, providerName, type Invoice} from './billing';
import {money} from './format';

const escape = (value: string) =>
  value.replace(/[&<>"']/g, char => ({'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'})[char]!);

const day = (iso: string) => new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'long', year: 'numeric'}).format(new Date(iso));

/** La facture (ou le reçu, sans numéro) en page HTML autonome, prête à imprimer ou à enregistrer en PDF. */
export function invoiceHtml(invoice: Invoice): string {
  const title = invoice.number ? `Facture ${invoice.number}` : 'Reçu de paiement';
  const issuer = invoice.issuer;
  const issuerLines = [issuer.address, issuer.taxId && `Identifiant fiscal : ${issuer.taxId}`, issuer.registrationNumber && `Immatriculation : ${issuer.registrationNumber}`, issuer.email]
    .filter((line): line is string => !!line)
    .map(line => `<div>${escape(line)}</div>`)
    .join('');
  const rows = invoice.lines
    .map(line => `<tr><td>${escape(line.label)}${line.kind === 'AddOn' ? ' <span class="muted">(option)</span>' : ''}</td><td class="num">${line.quantity}</td><td class="num">${escape(money(line.unitAmount, invoice.currencyCode))}</td><td class="num">${escape(money(line.amount, invoice.currencyCode))}</td></tr>`)
    .join('');
  const paidBy = invoice.collectedBy
    ? `Payé en ligne (${escape(providerName[invoice.collectedBy] ?? invoice.collectedBy)}) le ${escape(day(invoice.issuedAtUtc))}.`
    : `Paiement enregistré le ${escape(day(invoice.issuedAtUtc))}.`;
  return `<!doctype html><html lang="fr"><head><meta charset="utf-8"><title>${escape(title)}</title><style>
    body{font-family:system-ui,-apple-system,"Segoe UI",sans-serif;color:#0f172a;margin:40px;font-size:14px}
    header{display:flex;justify-content:space-between;gap:24px;margin-bottom:32px}
    h1{font-size:22px;margin:0 0 4px}.muted{color:#64748b}.box{border:1px solid #e2e8f0;border-radius:8px;padding:12px 16px}
    table{width:100%;border-collapse:collapse;margin-top:24px}th,td{padding:8px;border-bottom:1px solid #e2e8f0;text-align:left}
    th{font-size:12px;color:#64748b;text-transform:uppercase}.num{text-align:right;white-space:nowrap}
    tfoot td{font-weight:700;font-size:16px;border-bottom:none}footer{margin-top:32px;font-size:12px;color:#64748b}
    @media print{body{margin:16mm}button{display:none}}
  </style></head><body>
    <header>
      <div><h1>${escape(issuer.name)}</h1>${issuerLines}</div>
      <div style="text-align:right"><h1>${escape(title)}</h1><div class="muted">Date : ${escape(day(invoice.issuedAtUtc))}</div></div>
    </header>
    <div class="box"><div class="muted">Client</div><strong>${escape(invoice.customerName || '—')}</strong></div>
    <p>Abonnement ${escape(invoice.planName)}, période du ${escape(day(invoice.periodStartUtc))} au ${escape(day(invoice.periodEndUtc))}.</p>
    <table><thead><tr><th>Désignation</th><th class="num">Qté</th><th class="num">Prix unitaire</th><th class="num">Montant</th></tr></thead>
      <tbody>${rows}</tbody>
      <tfoot><tr><td colspan="3" class="num">Total</td><td class="num">${escape(money(invoice.total, invoice.currencyCode))}</td></tr></tfoot></table>
    <p>${paidBy}</p>
    <footer>${issuer.footer ? escape(issuer.footer) : ''}</footer>
    <button onclick="window.print()" style="margin-top:24px;padding:8px 16px">Imprimer ou enregistrer en PDF</button>
  </body></html>`;
}

/**
 * Ouvre la facture dans un nouvel onglet. L'onglet est ouvert tout de suite (dans le clic) pour ne pas être bloqué,
 * puis rempli une fois la facture chargée.
 */
export async function openInvoice(paymentId: string) {
  const target = window.open('', '_blank');
  if (!target) throw new Error('Autorisez l’ouverture d’un nouvel onglet pour afficher la facture.');
  target.document.write('<p style="font-family:system-ui;margin:40px">Chargement de la facture…</p>');
  try {
    const invoice = await loadInvoice(paymentId);
    target.document.open();
    target.document.write(invoiceHtml(invoice));
    target.document.close();
  } catch (error) {
    target.close();
    throw error;
  }
}
