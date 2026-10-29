# T13 — Payment and shipping: Stripe Checkout, flat rates, and an order that ships

Detail plan for the thirteenth template phase, the fourth in the B2C track
(`2026-10-15-t10-market-neutrality.md` §1). T12 placed an order from the cart and left it at
`Awaiting payment`. This phase takes the payment, charges for delivery, tells the customer when
it ships, and lets a checkout store open only once all of that is set up.

**Status: in progress** on `t13-payment-shipping`.

## 1. What reading the code found

1. **Nothing can take money.** An order waits at `Awaiting payment` until an admin moves it.
2. **The storefront's CSP says `form-action 'self'`.** Chrome applies `form-action` to the
   redirect that follows a form post, so a checkout redirecting to a hosted payment page is
   refused.
3. **There is no delivery charge**, and nothing records one on an order.
4. **Stock taken by an unpaid order is held until somebody cancels it.**
5. **Nothing tells a customer their order has gone.** "Mark fulfilled" changes a status and
   sends nothing.
6. **The open checklist knows nothing about a checkout store.** It would open one with no way to
   pay and no way to deliver.
7. **The feed password's Data Protection pattern is the one to copy for a store's payment keys.**
   Both hosts share the key ring, so the storefront can read what the portal writes.

## 2. Decisions

### 2.1 Stripe Checkout, behind a seam

- **`IPaymentProvider`**, with one implementation, `StripeCheckout`. The seam is built ahead of a
  second provider on purpose: a Brazilian store will want Mercado Pago or the like. It is also
  what lets the storefront's tests and the journey run without Stripe.
- **No Stripe SDK.** The two calls (create a session, expire one) and the webhook signature are
  about eighty lines over `HttpClient` and `HMACSHA256`. That is less to audit than a package that
  wraps the whole API.
  - Requests pin `Stripe-Version`, and carry an `Idempotency-Key`. The resilience handler may
    retry a POST, and the key makes a retry the same session.
  - **The API base and the checkout origin are configuration** (`Payments:Stripe`), defaulting to
    Stripe's own. The journey points them at a stub.
- **No `payment_method_types`.** The store's own Stripe dashboard decides what is offered: cards
  everywhere, and Pix or boleto in Brazil.
- **Amounts go in minor units**, with Stripe's zero-decimal currencies sent whole.
- **The line items add up to the order's total exactly.**
  - On an inclusive store, each line goes at its price and the delivery at its price.
  - On an exclusive store, the lines and the delivery go net, and the tax is one line of its own.

### 2.2 Per-store credentials

- **A store's secret key and webhook signing secret are `Site.PaymentSecretRef` and
  `Site.PaymentWebhookSecretRef`**, encrypted with Data Protection under one purpose, as a
  feed's password is. Each store is its own Stripe account and its own legal entity.
- **Write-only.** They are set at `/admin/store` and never sent back: the screen says whether they
  are set. `spSite_GetPaymentSecrets` is the only read, and only the storefront calls it.

### 2.3 The flow

1. The checkout posts, and `spOrder_PlaceFromBasket` writes the order (T12), now with its
   delivery charge.
2. **When the store takes payment**, the storefront creates a Checkout Session for the order,
   records its id on the order, and redirects to Stripe. The session expires after an hour.
3. **Stripe returns the customer to their order page**, which says it is confirming the payment
   and refreshes itself. **It never trusts the redirect.** Only the webhook marks an order paid.
4. **`POST /webhooks/stripe`**, resolved to its store by the host, checks `Stripe-Signature`
   against that store's secret. It handles four events:
   - `checkout.session.completed` and `async_payment_succeeded` mark the order **`Paid`**, and
     queue `order.paid`.
   - `async_payment_failed` and `checkout.session.expired` cancel the order and return its stock.
     They act only for the order's *current* session, so expiring an old one changes nothing.
   - **A completed session whose payment is still pending** (boleto, Pix) waits for the async
     event.
5. **Every event is recorded once**, in `dbo.PaymentEvent`, keyed on Stripe's event id. That
   happens in the transaction that applies it, so a replayed event changes nothing.
6. **"Pay now"** on an unpaid order starts a fresh session, expiring the previous one first. A
   session that could not be created, or was abandoned, is therefore not a dead end. Expiring the
   old one is also what stops a customer paying twice.

### 2.4 Shipping

- **`dbo.ShippingRate`** belongs to a store. Each rate has a name, the countries it covers (or
  every country), a price on the store's basis, an optional free-over threshold, and whether it is
  active. Rates are edited at `/admin/shipping`.
- **The cheapest active rate that covers the delivery country is charged**, by
  `ShippingQuote.Choose`. A store with rates that cover nowhere the customer is going refuses the
  checkout and names the country. A store with no rates at all charges nothing, and cannot open as
  a checkout store.
- **On the order, not as a line.** `PurchaseDetail.ProductId` is a product. The order records
  `ShippingName`, `ShippingPrice` and `ShippingVat`, and its totals include them. The delivery is
  taxed at the order's rate, through `fnOrder_LineTax`.
- **Carrier quotes** by weight or postcode (Correios, Melhor Envio) are not built.

### 2.5 Fulfilment

- **"Mark fulfilled" takes an optional tracking number** (`Purchase.TrackingNumber`). On an order
  from the cart it queues `order.shipped`, from `spOrder_UpdateStatus` in the same transaction.
- `order.paid` and `order.shipped` carry no link, so procedures queue them. The guest already has
  their link, from `order.placed`.

### 2.6 Opening a checkout store

- **`spSite_SetActive` also requires a checkout store to have** payment keys, an active shipping
  rate, and a `returns` page. For the `returns` page, Brazil's CDC gives seven days to withdraw,
  and the EU gives fourteen.
- **The list is the library's** (`SiteContentKeys.RequiredToOpenForCheckout`), passed in like
  the other pages.

## 3. Exit

- A checkout store with Stripe keys:
  - redirects the checkout to Stripe, with line items that add up to the order;
  - lands the customer back on their order page, which waits for the webhook.
- A signed `checkout.session.completed` marks the order `Paid` and queues `order.paid`.
  - Replayed, it changes nothing.
  - Unsigned, or signed for another store, it is refused.
- An expired session cancels the order and returns its stock.
- Delivery is charged from the store's rates and taxed with the order. A country no rate covers is
  refused.
- Marking a cart order fulfilled with a tracking number queues `order.shipped`.
- A checkout store does not open without payment keys, a shipping rate and a returns page.
- The RFQ stores and the fifteen journeys are unchanged.

## 4. Order of work

1. The schema, the shipping and payment procedures, and the status changes.
2. The provider, the webhook, the payment start and "Pay now".
3. Shipping at checkout, the CSP and the open checklist.
4. The portal: payment keys, shipping rates and the tracking number.
5. A journey that pays through a stub of Stripe's API and a signed webhook.

## 5. Not in T13

- **Refunds.** They are made in the Stripe dashboard, and an admin records them by cancelling.
  The `charge.refunded` webhook is not handled.
- **Carrier rates and shipping labels.**
- **Destination-based tax** (EU OSS, US sales tax, Stripe Tax).
- **A second provider.** The seam is there for it.
- **A payment taken after an order was cancelled** is logged for a person to deal with, not
  reconciled.
