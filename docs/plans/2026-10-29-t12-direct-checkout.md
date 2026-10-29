# T12 — Direct checkout: a cart that becomes an order, for guests and consumers

Detail plan for the twelfth template phase, the third in the B2C track
(`2026-10-15-t10-market-neutrality.md` §1). T10 made prices and words market-neutral and T11
built the apparel catalogue. This phase lets a store take an order straight from its cart,
from a shopper who may have no account at all.

**Status: built** on `t12-direct-checkout`.

## 1. What reading the code found

1. **`DirectCheckout` was named everywhere and built nowhere.** `Site.OrderMode`'s comment,
   `IOrderingMode`'s summary, `/cart` and two test fakes all expected it. `SiteSettingKeys`
   refused it, so no store could choose it.
2. **An order can only come from a quote.** `spOrder_ConvertFromQuote` is the one writer of a
   portal order, and it reads its lines from `QuoteLine`.
3. **An order needs an account and a contact, and that is right.** `CK_Purchase_Placer`, the
   composite keys to `Account` and `Contact`, the account-scoped reads and the admin screens all
   assume both. A guest can have both without loosening any of them: a consumer account of one,
   whose contact has no login.
4. **Nothing records where an order goes.** `Address` belongs to an account, and an account's
   address book can change after the order.
5. **Nothing touches stock.** `QuantityInStock` is written by the admin and by feeds. A cart
   store that sells the last shirt twice has two customers and one shirt.
6. **`IOrderingMode.RequiresApprovedAccount` has no reader.** CLAUDE.md already calls it
   unreachable. A checkout store takes guests, so its one meaning, "a checkout store cannot take
   an order from just anyone", is now false too.
7. **The tax arithmetic for an order line is written once, in `spOrder_ConvertFromQuote`.** A
   second writer of orders would be its second copy.
8. **Registration always creates a `Pending` trade application,** with paperwork and an
   approval queue. A consumer has neither.
9. **The quote status `Awaiting payment` already exists on `Purchase`.** An order placed from a
   cart starts there, and T13's payment moves it on.

## 2. Decisions

### 2.1 The mode

- **`DirectCheckoutOrderingMode`** says "Add to cart", lives at `/cart`, and shows a payable
  total.
- **`TakesQuotes`** replaces `RequiresApprovedAccount`.
  - Under RFQ the basket submits a quote request, and the account area lists quotes.
  - Under checkout the basket links to `/checkout`, and the quote panel is gone.
  - Both pages read the property and never the mode's key.
- **The test fakes go.** `FakeCheckoutMode` and `TenantVariationTests`' fake stood in for a mode
  that did not exist. The real one now does, so the tests render it.

### 2.2 The buyer

- **Every order has an account and a contact**, as before.
  - **A signed-in customer** orders on their own account.
  - **A guest** gets a new consumer account per order. It is `Approved` and `Prepaid`, with one
    contact who has no login, and it is written in the order's own transaction.
- **A guest is never merged into an existing account by email.** A merge would tell whoever
  typed an address whether its owner shops here. It would also file the order under somebody
  else's account.
- **`Account.Kind`** is `Business` or `Consumer`, and defaults to `Business`. A consumer's
  `Company` holds their name. The kind says it is a person and not a firm, which the portal shows
  and T14's fiscal document needs.
- **The optional account** is the `b2c` registration field set.
  - It asks for a name, an email address and a password, and nothing else: no company, no VAT
    number and no paperwork.
  - `spAccount_Register` creates a consumer account `Approved`, because there is nothing for
    anyone to review and it extends no credit.
  - **Email confirmation stays mandatory**, for T3's reasons.

### 2.3 Placing the order

- **`spOrder_PlaceFromBasket`, in one transaction**, does the following:
  - It checks the stock.
  - It writes the guest's account and contact.
  - It takes the stock.
  - It writes the order at `Awaiting payment`, with its lines and delivery address.
  - It deletes the cart.

  Its rules are `spQuote_SubmitRequest`'s:
  - The site and the contact are in every predicate.
  - The currency is the store's.
  - Every product is re-checked against the store's placement.
- **Prices are resolved at checkout, through `CatalogPresenter`**, exactly as a quote request
  resolves them. The browser posts an address, never a price.
- **Tax is assessed on the delivery country.** A consumer is taxed where the goods go. A
  signed-in business keeps its VAT number for `eu-b2b`'s reverse charge.
- **The line tax is one inline function, `fnOrder_LineTax`**, which both order writers now call.
  It implements T10's inclusive back-out as well as the exclusive case.
- **Unavailable lines are dropped and named**, as a quote request drops them. The checkout page
  lists what will be ordered before anyone presses the button.

### 2.4 Stock

- **Only own stock is held**: no feed, and not marked `Distributor`, which is
  `fnSite_ProductSellable`'s definition. A feed's quantity is the distributor's, and the next
  sync overwrites it.
- **Checked before anything is written, under `UPDLOCK, HOLDLOCK`,** so two checkouts for the
  last shirt cannot both pass. A short order writes nothing, and returns the SKUs it is short of,
  so the page can name them.
- **Cancelling a cart order puts its stock back**, in `spOrder_UpdateStatus` and in the same
  statement that claims the status change. **A cancelled cart order cannot be reopened.** That
  would take the stock a second time with no check, and so it is a 409. An order from a quote
  never held stock, and reopens as before.
- **An unpaid order holds its stock until somebody cancels it.** T13's expired checkout session
  is what cancels it on its own.

### 2.5 After the order

- **A guest's order is at `/orders/{token}`.**
  - The token is 256 bits, the `BasketToken` shape, and it is the only way in.
  - **Only its SHA-256 is stored** (`Purchase.GuestTokenHash`), so a copy of the database does
    not open anybody's order page with their address on it.
  - "Not yours" and "not here" are one answer.
- **A signed-in customer lands on `/account/orders/{reference}`**, which already carries the
  account predicate.
- **`order.placed` is queued by the caller through `IEmailOutbox`**, not by the procedure.
  - It carries the guest's link, which is a credential, so its payload is encrypted. A procedure
    can only enqueue plain JSON.
  - That leaves the one-insert window that registration and password reset already accept.
- **The delivery address is a snapshot**, in `dbo.PurchaseAddress`, one row per order. The
  customer, the guest and the admin each see the same row.

## 3. Exit

- A store set to `DirectCheckout` says "cart" and shows a total. Its cart leads to a checkout
  page, which takes a guest's name, email address and delivery address.
- Placing the order:
  - writes a consumer account, a contact with no login, an order at `Awaiting payment`, its lines
    taxed on the store's basis, and its address;
  - takes the stock, and deletes the cart;
  - lands on a page only the token opens, and queues `order.placed`.
- A checkout for more than the store holds is refused, names the product, and writes nothing.
- Cancelling that order in the portal puts the stock back. Reopening it is refused.
- A `b2c` store's registration asks for no company and no paperwork. Its account signs in once
  the address is confirmed, and lists its orders.
- The RFQ stores, the quote path and the thirteen B2B journeys are unchanged.

## 4. Order of work

1. The schema, `fnOrder_LineTax`, `spOrder_PlaceFromBasket`, the status release, and the reads.
2. The mode, the checkout service and page, and the guest order page.
3. Consumer registration.
4. The portal's delivery address and the consumer label.
5. A journey in which a guest buys the last shirt and a second guest is refused it.

## 5. Not in T12

- **Payment.** An order waits at `Awaiting payment`. Stripe Checkout, its webhook and `Paid` are
  T13's.
- **Shipping rates, and a total that includes them.** These are T13's, and so is the open
  checklist's knowledge of a checkout store.
- **Releasing an unpaid order's stock on a timer.** T13 does this, from the checkout session's
  expiry.
- **Country-specific checkout fields**, such as a CPF and a CEP. The NF-e is the only thing that
  needs them, so they move to T14. The checkout takes a generic international address, with the
  region and postcode optional, like `Address`.
- **A separate billing address.** For a card, the payment provider collects it.
- **Linking a guest's orders to an account registered later** with the same address. This is
  the merge that §2.2 refuses.
- **Order notes at checkout.**
