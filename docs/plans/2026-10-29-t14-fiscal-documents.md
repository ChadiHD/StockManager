# T14 — The fiscal-document seam: an outbox for invoices a tax authority must see

Detail plan for the fourteenth template phase, the last in the B2C track
(`2026-10-15-t10-market-neutrality.md` §1). T13 took the payment. In Brazil a sale also needs an
**NF-e**, an electronic invoice authorised by the tax authority through a provider, and other
countries have their own (Italy's SdI, Mexico's CFDI, India's e-invoicing). This phase gives the
template the place such a document is issued from, without choosing a provider or a country.

**Status: built** on `t14-fiscal-documents`.

## 1. What reading the code found

1. **Nothing happens after an order is paid** except `order.paid`. There is no hook a country's
   invoicing could hang from.
2. **The mail outbox is the shape to copy.** An issuer is a remote service that is sometimes
   down, sometimes refuses, and must be called exactly once per sale. `dbo.EmailOutbox` already
   solves that: a row written in the transaction that made the change, an atomic claim with a
   lease, retries with backoff, a dead letter, and an operator alert.
3. **An order can be paid two ways.** The webhook (`spOrder_ApplyPaymentEvent`), and an admin
   choosing `Paid` on the order screen (`spOrder_UpdateStatus`). An order on terms can also go
   straight to `Fulfilled` without ever reading `Paid`.
4. **Keyed settings have one list and a test.** `SiteSettingKeys` and the keys-versus-
   implementations tests are how an ordering mode or a tax rule set is added; an issuer is one
   more.

## 2. Decisions

### 2.1 A setting, a seam and two keys

- **`Site.FiscalIssuer`**, a keyed setting saved through `spSite_Update` like `TaxRuleSet`, and
  listed in `SiteSettingKeys.FiscalIssuers`.
  - **`none`**, the default, queues nothing. Every store before T14 is unchanged.
  - **`logging`** issues nothing and logs the order. It is for development and for proving a
    store's set-up, and the settings screen says so.
- **`IFiscalDocumentIssuer`** in `StockApi`, with `Key` and `IssueAsync`. It is handed the order,
  its lines, its delivery address and the store, all models that already exist, and returns the
  document's number and, if there is one, a link to it.
- **A real provider is a second implementation**, built when the Brazilian store chooses one
  (NFE.io, Focus NFe, eNotas). It goes in this repository, not the tenant's, because the next
  Brazilian store needs it too. Its credentials will be a store's, encrypted like the payment
  keys; that is the adapter's to add.

### 2.2 The outbox

- **`dbo.FiscalDocument`**: one row per order (`UQ_FiscalDocument_PurchaseId`), with the issuer
  the store had when the row was written, a status (`Pending`, `Issuing`, `Issued`,
  `DeadLettered`), attempts, a lease and a claim token, and the number, link and last error.
  - **The issuer is on the row.** A document is owed under the set-up the sale was made under,
    and switching a store to `none` must not silently drop what it already owes.
- **`spFiscalDocument_Enqueue`** writes the row when the store's issuer is not `none` and the
  order has none yet. It is called inside the transaction that makes the order owe one:
  - **`spOrder_ApplyPaymentEvent`**, when a payment marks the order `Paid`;
  - **`spOrder_UpdateStatus`**, when an admin sets `Paid`, or `Fulfilled` on an order nobody
    marked paid. A sale shipped on terms is still a sale.
- **The claim, the lease and the claim token are `spEmailOutbox_Claim`'s**, for the same reasons.
  Retries follow `OutboxRetryPolicy`: eight attempts over about two hours.
- **`FiscalDocumentRefusedException`** is how an issuer says no retry can help, for instance the
  authority rejecting the document's data. It dead-letters at once, as
  `PermanentEmailFailureException` does.
- **A dead letter mails the operator** (`operator.fiscal-failed`), through the mail outbox, and
  shows on the order in the portal with its error and a **Retry** button
  (`spFiscalDocument_Retry`). An invoice the law requires cannot just be given up on: somebody
  fixes the cause and sends it again.

### 2.3 The dispatcher

- **`FiscalDocumentDispatcher`** in `StockApi`, beside the mail dispatcher: claim, load the order,
  find the row's issuer, issue, record. An issuer this build does not know is retried, in case a
  newer host arrives, as an unknown mail template is.
- **`FiscalDocumentBackgroundService`** runs it every thirty seconds, **on by default**
  (`Fiscal:DispatchEnabled`, `Fiscal:DispatchIntervalSeconds`). With every store on `none` there
  is nothing to claim.

### 2.4 What the portal shows

- The settings screen offers the issuer.
- The order screen shows the document: pending, issued with its number and link, or failed with
  its error and Retry.

## 3. Exit

- A store on `none` queues nothing, and every existing store is on it.
- A store on `logging` queues one document when an order is paid, by webhook or by an admin, and
  one when an order is fulfilled unpaid. A replayed webhook queues no second.
- The dispatcher issues each document once, records its number, retries a failure, and
  dead-letters a refusal at once, with an operator mail.
- A dead-lettered document can be sent again from the portal.
- The sixteenth journey's paid order is issued by the logging issuer, and its number shows in
  the portal.

## 4. Order of work

1. The schema: `Site.FiscalIssuer`, `dbo.FiscalDocument` and its procedures, and the two enqueue
   points.
2. The seam, the logging issuer, the dispatcher and its loop.
3. The setting and the order panel in the portal.
4. The database tests, the dispatcher tests, and the journey.

## 5. Not in T14

- **A real provider.** The tenant picks one; its adapter follows the seam.
- **Cancelling an issued document.** An NF-e can be cancelled within a window, and a cancelled
  order with an issued document needs one. Until an adapter exists, cancelling such an order is a
  person's job in the provider's dashboard.
- **The customer's copy.** Providers mail the DANFE themselves. Showing the link on the
  customer's order page waits for a provider that needs it.
- **The buyer's tax id (CPF).** `b2c` asks for none. Whether an NF-e for a consumer needs one
  depends on the value and the state; the adapter decides, and adds the field to a field set.
