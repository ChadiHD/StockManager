# T6 — Tax, terms, transactional email

Detail plan for the sixth template phase. Sits under
`docs/plans/2026-09-10-storefront-implementation-plan.md` §T6 and replaces its bullet list with
something buildable. T0–T5 are merged; this is the next phase and nothing in the tenant track
starts until the template track finishes.

**Exit:** correct tax on every document for any configured jurisdiction; every state change
sends the right mail.

**Status: planned.** Nothing built.

**This document is not tax advice.** The design doc says the reverse-charge treatment must be
confirmed with an accountant before go-live, and that has not happened. What this phase builds
is an engine that applies whichever rules a store configures, and the evidence that it applies
them consistently. Which rules are correct for Ireland is somebody else's signature.

---

## 1. What is already wrong, and has to be settled first

T5 opened with three defects found by reading the accept path before building on it. The same
reading of the tax path finds four, and they are worse in kind: T5's were races and rounding,
these are **three different numbers for the same tax on the same order**, all of them shipped.

### 1a. The portal computes 23%, the database stores zero, and the report prints both

`spOrder_ConvertFromQuote` writes `VAT = 0` and `FinalPrice = SubTotal`, deliberately and with
a comment saying T6 owns the rule engine. Nothing else got that memo:

| Where | What it shows |
| --- | --- |
| `SMPortal/Pages/Admin/Quotes/QuoteDetail.razor:134,260` | `VAT (23%)`, computed in the page as `_sub * 0.23m` |
| `SMPortal/Pages/Admin/Orders/OrderDetail.razor:52,131` | the same, on the order |
| `SMPortal/Pages/Admin/Reports/Reports.razor:30` | "VAT collected", summing the stored `Purchase.VAT` — always zero — under the label "At 23% standard rate" |
| `SMStore` customer document | `SubTotal` and `Total` from the model, so no VAT line at all |

An admin looking at an order sees a total 23% higher than the customer looking at the same
order, and the dashboard reports €0.00 of VAT collected on both. Every one of those numbers is
rendered with confidence. **This is the defect T6 exists to remove, and it is worth stating as
a defect rather than as missing work**: the pages do not say "tax is not calculated yet", they
say `VAT (23%)` and then show a figure the database will not agree with.

### 1b. The POS charges 8.75%

`PurchaseData.SavePurchases` reads `taxRate` from configuration, and
`SMDesktopUI/appsettings.json` sets it to `8.75`. That is a US sales-tax rate left over from
the codebase's origins, applied to `Product.IsTaxable` rows in the desktop till, in euro, in
Ireland. It has nothing to do with the 23% the portal displays.

The POS path is explicitly out of scope for the retirement work — the implementation plan says
so, and the WPF app still ships — but "out of scope" and "correct" are different claims, and a
reader of this repository should not have to discover the 8.75 for themselves.

### 1c. `ConfigHelper.GetTaxRate()` has no callers at all

The implementation plan says to retire it "on the portal path only — the WPF POS still uses
it, so do not remove it outright". **The WPF POS does not use it.** Nothing does:
`git grep GetTaxRate` finds the definition and that sentence, and no call site. The POS reads
the same `taxRate` setting through `IConfiguration` in `PurchaseData` instead.

So the instruction to keep it is based on a premise that is false. It is dead code, in the
shared library rather than the desktop one, under the namespace `SMDesktopUI.Library` while
living in `SMDataManager.Library`, and its one error path throws
`ConfigurationErrorsException("The tax rate is not")` — a sentence that stops mid-clause.

**It goes.** Recorded here rather than done quietly, because the plan above says the opposite
and a future reader deserves to know which of the two is right.

### 1d. `Account.CreditLimit` is collected, displayed, and never read

The column exists, the approval modal sets it, `AccountDetail` renders it, and no code path
anywhere compares it to anything. A customer on Net 30 with a €10,000 limit can accept a
€90,000 quote, and the order is raised. The credit-terms half of this phase is therefore
greenfield rather than a tightening — there is no existing check to preserve the behaviour of.

---

## 2. The first decision: where a tax rule lives

A rate is data. **Reverse charge is not a rate, it is a decision tree** — whether the customer
is in the store's own country, whether they are elsewhere in the EU, whether they produced a
VAT number, whether the goods leave the union — and each branch carries a different legend
that has to appear on the document. Putting that in a column means putting a program in a
column.

### Not `Site.VatRules` as JSON

The design doc sketches `Site.VatRules`. A JSON blob in a column can express rates; it cannot
express "zero-rate only if the VAT number validates and the country differs and the goods
ship", and the moment it tries it is a rules engine with no type checking, no test, and no
compiler. The catalog's margin floor is a single decimal and belongs on the site. This does
not.

### Not a rate table alone

A `dbo.TaxRate` table keyed by country answers "what rate" and says nothing about "which
treatment", which is the part that is actually hard and the part an auditor asks about.

### Decision: a named rule set in code, selected by the site, reading rates from the site

The platform already does this twice — `IRegistrationFieldSet` keyed by
`Site.RegistrationFieldSet`, `IOrderingMode` keyed by `Site.OrderMode` — and both exist because
the same question came up: per-tenant behaviour that is logic rather than configuration.

- `ITaxRuleSet` with one implementation to start, `EuB2bTaxRuleSet`, selected by a new
  `Site.TaxRuleSet` column defaulting to `eu-b2b`.
- `TaxRuleSetProvider` resolves it, exactly as `OrderingModeProvider` does, and **nothing
  outside the tax namespace branches on `Site.TaxRuleSet`** — the same rule CLAUDE.md already
  states for `Site.OrderMode`.
- Rates are site data, because they change by statute without the logic changing:
  `Site.StandardTaxRatePct` and `Site.TaxRegistrationNumber`, both on the row.
- The rule set takes what it needs and returns a verdict, not a number:

  ```
  TaxAssessment Assess(TaxContext context)
  // context:  site country, site rate, account country, account VAT number,
  //           whether the line's product is taxable
  // verdict:  treatment, rate to apply, legend text, and why
  ```

**A second store in a second country is then a `Site` row plus, at most, a second rule set** —
which is the whole test this platform is built against.

### The reverse-charge decision, stated once

The rule set is the only place this is written down:

| Account country | VAT number | Treatment | Rate |
| --- | --- | --- | --- |
| Same as the site | any | Domestic standard | `Site.StandardTaxRatePct` |
| Elsewhere in the EU | present | Intra-EU reverse charge | 0 |
| Elsewhere in the EU | absent | Domestic standard | `Site.StandardTaxRatePct` |
| Outside the EU | any | Export | 0 |
| Any, product not `IsTaxable` | any | Not taxable | 0 |

**A VAT number is not validated against VIES in this phase.** It is recorded, it decides the
treatment, and the store carries the risk that it is wrong — which is what stores did before
VIES had an API and what the reverse-charge rules already expect of a supplier acting in good
faith. VIES lookup is named in §10 as not-in-T6, with the reason: it is a network call to a
service with no availability guarantee, on the path that raises an order, and designing its
failure mode is a phase of its own.

**`Product.IsTaxable` already exists** and is honoured by the POS. The rule set reads it too,
so one product cannot be taxable in the till and exempt on the portal.

---

## 3. The second decision: what an order snapshots

T5 settled the same question for price and the answer holds here: **the treatment the customer
was shown is the treatment that gets stored.** A rate can change between acceptance and the
customer opening the document; a rule set can be corrected; a store can move country. None of
that may silently rewrite a document somebody has already acted on.

So an order does not re-derive its tax. It carries it:

- `PurchaseDetail.TaxRatePct` — per line, because `IsTaxable` varies per product and a mixed
  basket has two rates on one order.
- `Purchase.TaxTreatment` — the verdict, one of a known set, so a report can group by it and
  a constraint can refuse a fifth.
- `Purchase.TaxLegend` — the sentence printed on the document. Snapshotted rather than
  re-rendered from the treatment, because the wording is a legal statement and improving it
  next year must not restate what last year's invoices said.

`Purchase.VAT` and `PurchaseDetail.VAT` already exist and keep their meaning: the money. What
is new is why that money is what it is.

**A quote is not an order.** A quote shows an indicative tax line and stores nothing, because
the treatment depends on the account and the date and the quote may never be accepted. The
snapshot happens once, inside `spOrder_ConvertFromQuote`, in the transaction that already
claims the quote.

---

## 4. The third decision: terms as a string, or as days

`Account.PaymentTerms` is `NVARCHAR(50)` holding `Prepaid`, `Net 14`, `Net 30`, `Net 45` or
`Net 60` — free text with no constraint, and the portal's `_creditTerms` array is the only
thing that says which values exist. The design doc asked for `PaymentTermsDays`, an integer.

A due date needs arithmetic. Parsing `"Net 30"` into `30` works until somebody types
`"net 30"`, `"NET30"`, `"30 days"` or `"Net 30 EOM"`, and it has to be parsed everywhere a due
date is shown.

**Decision: add `Account.PaymentTermsDays INT NOT NULL DEFAULT 0` and keep the label.** Zero
means prepaid. The label stays for display because "Net 30" is what a buyer's finance
department recognises, and the integer is what the code does maths with. A `CHECK` keeps the
two from disagreeing: days zero if and only if the label is `Prepaid`.

Migrating the existing rows is a backfill in the post-deployment script, which already runs on
every publish and already backfills `SiteId`.

---

## 5. What a credit check refuses, and when

### Inside the acceptance transaction, not before it

The check belongs in `spOrder_ConvertFromQuote`, after the status claim and before the insert,
for the reason the claim itself is there: **two quotes accepted at once by two buyers at the
same company would each pass a check made outside the transaction**, and the account ends up
over its limit by the value of the smaller one. The claim already gives this procedure the
lock it needs.

### What counts against the limit

Exposure is the sum of orders already raised and not yet settled, plus the one being raised.
That needs a definition of settled, and `Purchase.Status` is currently free text
(`Awaiting payment`, `Fulfilled`). This phase pins it: a `CK_Purchase_Status` constraint and a
named set, the same treatment `CK_Quote_Status` got in T5 and for the same reason — a status
nobody constrained is a status a typo can invent.

### A refusal is an answer, not an error

Refused acceptance throws a named number in the 500xx range that the customer path turns into
a page and the admin path turns into a 409 — the pattern `spOrder_ConvertFromQuote` already
uses for a refused claim, and `QuoteAcceptanceResult` already carries the shape. The customer
sees what they can do about it: the order value, their limit, their exposure, and who to
contact.

**A prepaid account has no limit to exceed** and skips the check entirely, rather than being
given a limit of zero and refused everything.

### The admin is not blocked

An admin converting a quote for a customer who rang up may exceed the limit deliberately —
that is a commercial decision somebody is making with their name on it. Same shape as T5's
`Priced`-and-unexpired rule: **the gate is on the customer path, not in the procedure**, and
the procedure records that the limit was exceeded rather than refusing. One more reason the
check is a parameter to the procedure rather than an unconditional guard inside it.

---

## 6. The outbox: what "sent" means

`IEmailSender` exists with a logging implementation and three call sites written in T3 so this
phase has something to fill. What is missing is everything behind it.

### The write and the intent to send commit together

Today a send is fire-and-forget after the write, and a process that dies between them loses
the message with nothing recording that it was owed. An outbox row written **in the same
transaction as the state change** gives both halves of the rule the seam already states: the
operation cannot be failed by a mail server, and the message cannot be lost by a restart.

- `dbo.EmailOutbox` — site, recipient, template key, a JSON payload of the values the template
  needs, status, attempt count, next attempt, last error.
- Written by the procedure that makes the change, where there is one, so there is no window.
- **The payload is values, not rendered text.** A template fixed after the row was written
  should apply to a message that has not gone yet; a rendered body freezes the bug in.

### The dispatcher claims, exactly like a feed sync

`spEmailOutbox_Claim` takes a batch in one atomic `UPDATE` whose `WHERE` and `SET` share a row
lock, with a lease that expires — the same shape as `spDistributorFeed_ClaimForSync`, for the
same reasons, including that a host killed mid-send must not take the queue out of service
permanently and silently. Two replicas are then safe without a leader.

Retry with exponential backoff, and a dead-letter status after a fixed number of attempts so a
permanently bad address stops consuming the queue. **A dead-lettered message is an operator
alert**, through the same `Site.OperatorEmail` path `FeedAlertService` uses — and yes, the
alert about undeliverable mail is itself mail, so the failure to send *that* is logged at
Error and goes no further. One turn of that wheel is enough.

### Eight templates, per site

Registration received, approved, rejected, quote received, quote priced, quote expiring, order
confirmed, password reset. Per site because the from-address, the signature and the sender
reputation are per site — which `EmailMessage.SiteKey` already carries, and which is the reason
it does.

`SiteContent` is the precedent for per-site copy: rows are staff-authored, and a store with no
row gets the platform default rather than another store's words. **Templates are not
`MarkupString` territory.** A customer's own words reach some of these messages —
`Quote.CustomerNote`, `Account.RejectionReason` — so a template renders values into text, and
the escaping rule that governs `SiteContent.BodyHtml` runs in reverse here exactly as it does
on the printed document.

### Quote expiring is the one that needs a clock

Seven of the eight are triggered by a state change. "Your quote expires in three days" is
triggered by nothing happening, so it needs a scheduled sweep — and this repository already
has that argument on record: the abandoned-basket sweep was deferred to T7 *with the hosting
decision that says where a scheduled job runs*, because writing it early produces a procedure
nothing calls.

`DistributorFeedSyncBackgroundService` is the counter-example: a scheduled job that does run,
in `StockApi`, off by default. So the machinery exists and the precedent is set.
**Decision: build it, in the same host, off by default**, and let T7 move both sweeps together
if the hosting decision says they belong elsewhere.

---

## 7. The renderer decision T5 deferred, and why it can be deferred once more

T5 built the document and left the renderer open, on the grounds that all three candidates
render the same markup and the thing that forces a choice is an *attachable file*. T6 is the
phase that sends mail, so this is where the choice was expected to land.

It does not have to. **The order-confirmed message can link to the document rather than
attach it.** The customer is signed in, the route exists, the account predicate is already on
it, and a link costs nothing to build and nothing to deploy.

- **For:** no licence question, no browser in the deployment, no PDF generation on the path
  that confirms an order. T7 weighs the hosting cost of headless Chromium once, with
  everything else it is weighing.
- **Against:** some finance departments file the attachment and will not click through, and a
  link expires with the account rather than with the document.

**Recommendation: link, and revisit when a customer asks for the attachment.** If the answer
is instead "attach from day one", the choice is QuestPDF's revenue-conditional Community
licence — the ground FluentAssertions 8 was rejected on, so it needs a deliberate business
answer rather than a developer's — or headless Chromium via the Playwright already in the
solution, which is a browser in the deployment.

**This is the one open question in this plan.**

---

## 8. Schema

```
Site
  + TaxRuleSet            NVARCHAR(50)  NOT NULL DEFAULT 'eu-b2b'
  + StandardTaxRatePct    DECIMAL(5,2)  NOT NULL DEFAULT 0
  + TaxRegistrationNumber NVARCHAR(30)  NULL

Account
  + PaymentTermsDays      INT           NOT NULL DEFAULT 0
  + CK_Account_Terms      days = 0 if and only if PaymentTerms = 'Prepaid'

Purchase
  + TaxTreatment          NVARCHAR(30)  NULL     -- NULL on POS rows
  + TaxLegend             NVARCHAR(200) NULL
  + DueDate               DATE          NULL     -- PurchaseDate + PaymentTermsDays
  + CreditLimitExceeded   BIT           NOT NULL DEFAULT 0
  + CK_Purchase_Status    the named set, replacing free text

PurchaseDetail
  + TaxRatePct            DECIMAL(5,2)  NOT NULL DEFAULT 0

EmailOutbox                                      -- new
    Id, SiteId, ToAddress, ToName, TemplateKey,
    PayloadJson, Status, Attempts, NextAttemptUtc,
    LastError, CreatedUtc, SentUtc
  + UQ / index on (Status, NextAttemptUtc) for the claim
```

`Purchase.TaxTreatment` is nullable because `dbo.Purchase` does double duty and a POS row has
no account to assess — the same reason `AccountId`, `QuoteId` and `Currency` are nullable
there. The `Reference IS NOT NULL` predicate that separates the two already exists on every
`spOrder_*` procedure and must stay.

---

## 9. Work items, in order

**1. Clear the ground.** Delete `ConfigHelper` (§1c: it has no callers). Constrain
`Purchase.Status`. Add `PaymentTermsDays` with its check and its backfill. No behaviour change
and nothing depends on it, which is what makes it first.

**2. The rule set.** `ITaxRuleSet`, `EuB2bTaxRuleSet`, `TaxRuleSetProvider`, the `Site`
columns. Pure logic against the table in §2, so the tests are a matrix and need no database.

**3. Assessment at acceptance.** `spOrder_ConvertFromQuote` takes the assessed rate and
treatment and writes the snapshot; the caller assesses. The procedure stops writing zeros.
Requires the T-SQL tests that T5's acceptance work already has a fixture for.

**4. The credit check.** Exposure, the named refusal, the customer page that explains it, and
the admin path that records rather than refuses.

**5. Tax on every surface.** The three portal pages stop computing 23% in markup and read the
stored value; the customer's document and print sheet gain a tax line and the legend; the
report groups by treatment instead of labelling everything "23% standard rate". **This is the
item that closes §1a**, and it is deliberately after item 3 — the pages cannot show the truth
until the truth is stored.

**6. The outbox.** Table, claim procedure, dispatcher, retry, dead-letter, operator alert.
Wired to the three existing call sites first, because they already exist and prove the path
before five more are added.

**7. The remaining templates**, and the per-site copy resolution.

**8. The expiry sweep.** Scheduled, off by default, in `StockApi` beside the feed scheduler.

**9. End to end.** One journey: an EU customer with a VAT number accepts a quote, the order
carries zero VAT and the reverse-charge legend, the confirmation mail is in the outbox, the
dispatcher sends it, and a second customer without a VAT number gets 23% on the same catalog.
T5's journey is the model — the assertion that spans every hop, not a screenshot of each.

---

## 10. Not in T6

- **VIES validation** of a customer's VAT number. §2 says why: a network call with no
  availability guarantee on the path that raises an order.
- **Invoices as a document type.** An order confirmation is not an invoice; an invoice has a
  number series, a due date, a payment state and a credit-note counterpart. The due date lands
  here because credit terms need it; the rest is its own phase.
- **Payment collection.** Card capture, gateway, reconciliation. `PaymentMethod` stays a label.
- **The POS tax path.** §1b records that it charges 8.75%; changing the WPF till's tax
  behaviour is not this phase, and pretending otherwise would put a desktop regression inside
  a storefront phase.
- **Multi-rate catalogs.** Reduced and second-reduced rates exist in Ireland and every product
  here is standard-rated or exempt. `PurchaseDetail.TaxRatePct` is per line so the schema does
  not have to change when that stops being true, but no UI sets a per-product rate.

## 11. Risks

- **The rules may be wrong.** Stated at the top and repeated here: this is an engine, and the
  design doc's instruction to have the treatment confirmed by an accountant before go-live is
  unresolved. The mitigation is that the treatment is snapshotted and named on every order, so
  a correction is auditable rather than archaeological.
- **Two more scheduled jobs.** The feed sync, the expiry sweep, the outbox dispatcher, and
  T7's basket sweep. Each is off-by-default and claim-based, but four background loops in one
  host is a hosting decision arriving by accretion. Worth naming to T7 rather than discovering.
- **The outbox makes failures visible that were previously silent.** Today a failed send is a
  log line nobody reads. After this, it is a row with a status, and somebody has to look at
  it. That is the point, and it is also work.
- **Changing `Purchase.Status` to a constrained set touches POS history.** The column is
  nullable and POS rows leave it NULL, so the constraint has to admit NULL as well as the
  named set — and the live rows must be surveyed before it is written, or the publish fails
  on data nobody remembers writing. `Site` carries no `CHECK` constraints at all today, so
  `OrderMode` and `RegistrationFieldSet` are unconstrained strings by the same omission;
  `TaxRuleSet` will join them unless that is fixed deliberately.
