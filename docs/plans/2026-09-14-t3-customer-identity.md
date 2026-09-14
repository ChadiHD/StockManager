# T3 — Customer identity

Detail plan for the third template phase. Sits under
`docs/plans/2026-09-10-storefront-implementation-plan.md` §T3 and replaces its bullet list with
something buildable. T0, T1 and T2 are merged; this is the next phase and nothing in the tenant
track starts until the template track finishes.

**Exit:** a stranger can apply against any site's field set, be approved from the admin portal,
sign in on that site and no other, and see their group's prices and their group's catalog.

**Status: all ten work items built.** What this plan said would not be in T3 still is not —
the quote cart, tax and credit enforcement, the email outbox and templates, per-user admin
site assignment, precomputed pricing and SSO are all still ahead. Two things the plan assumed
would be done by hand are not: §7 records "no test coverage anywhere", and the test suite that
landed alongside this phase is the answer to it.

---

## 1. The pricing decision, first

T3 is what supplies a real `CustomerGroupId`, and that activates two code paths that have never
executed. Deciding how they behave is a prerequisite for writing T3, not a follow-up, because
`spCatalog_Search` gets rewritten either way and it should be rewritten once.

### What actually breaks

`CatalogPresenter` displays `PriceResolver.Resolve(...).NetPrice`; `spCatalog_Search` orders by
`RetailPrice`. The review called this a mismatch, which it is, but the shape matters:

- **A group discount alone does not break the sort.** `net = list × (1 − d)` is monotone in
  `list` for a fixed `d`, so ordering by list and ordering by net give the same sequence.
- **The margin floor breaks it.** `net = max(list × (1 − d), min(cost × (1 + m), list))`
  introduces `cost`, which varies independently of `list`. A thin-margin product floors upward
  and jumps position; a fat-margin one does not.

So the defect is live only when a site sets `MinMarginPct > 0`. Every site is at 0 today, which
is why this can be settled inside T3 rather than ahead of it — but it must be settled inside
T3, because approving the first account is what makes a discount real.

### Options

**A — compute the ordering key in SQL.** `spCatalog_Search` looks up the group's `Discount` and
the site's `MinMarginPct` and orders by the same expression `PriceResolver` evaluates.
Correct sort, one query, `OFFSET/FETCH` unchanged. Cost: the formula exists twice.

**B — page in memory.** Fetch the whole visible set, resolve in C#, sort, page. One
implementation of the rule. Cost: throws away `OFFSET/FETCH` at exactly the scale the template
is meant to survive, and makes facet counts and page counts disagree unless they are recomputed
the same way. Not viable for a platform sized at tens of thousands of SKUs.

**C — precompute a net price per (product, group).** Correct and fast, and the values can be
generated *by* `PriceResolver` during feed sync, so there is genuinely one implementation. Cost:
an invalidation surface — cost changes on every sync, `Discount` changes when an admin edits a
group, `MinMarginPct` changes when an admin edits a site, and a stale row is a wrong price shown
to a customer.

### Recommendation: A

Take the duplication. Three reasons:

1. **The rule is four lines and stable.** It is a discount, a floor, a cap and a rounding mode —
   not a pricing engine. `PriceResolver` has one consumer today (`CatalogPresenter`) and will
   have two after T5 wires quote lines. That is a manageable surface to keep in step, and C's
   invalidation surface is larger and fails silently, whereas a drifted formula fails visibly
   on the next comparison.
2. **B is disqualified on scale**, and the whole point of the template framing is that store
   number two is not a rewrite.
3. **C is the right answer later, not now.** If catalog pricing ever grows per-product overrides
   or tiered break quantities, precomputation becomes necessary and the SQL expression stops
   being four lines. Revisit then; the trigger is the rule gaining a second input that varies
   per row.

Make the duplication safe rather than pretending it is not there:

- **SQL computes an ordering key. `PriceResolver` computes the price the customer is shown.**
  Never render a price that came out of `spCatalog_Search`.
- **Pin them with a differential test.** `SMDataManager.Library` has no test project; T3 adds
  one with a single fixture that drives a matrix of (list, cost, discount, margin) through both
  the C# resolver and the SQL expression and asserts they agree to the cent. This is the first
  automated test in the data layer and it exists because this specific duplication needs a
  tripwire.
- **Round identically.** `Math.Round(x, 2, MidpointRounding.AwayFromZero)` and T-SQL
  `ROUND(x, 2)` agree on positive values. The test is what stops that being a coincidence
  somebody relies on.

### Consequences to build

- `spCatalog_Search` resolves `Discount` from `@CustomerGroupId` and `MinMarginPct` from
  `@SiteId` **inside the procedure**. Neither is a parameter. A caller that could pass a
  discount is a caller that could ask for 90% off.
- **`@CustomerGroupId` must be validated against `@SiteId`.** It currently is not — the review
  flagged it as harmless while nobody signs in, and T3 is what ends that. A group id that does
  not belong to the resolved site is treated as no group, not as an error, so a tampered cookie
  degrades to list price rather than leaking another store's rates.
- `CatalogPresenter.CustomerGroupId` stops returning null and reads from the signed-in
  principal. **It must never read from a query string or a form field.**
- Do the deferred `spCatalog_Search` rewrite in the same change: inline TVF shared with
  `spCatalog_GetFacets`, one `ORDER BY` per sort shape instead of the `CASE` ladder, and
  `OPTION (RECOMPILE)`. All three land on the lines this decision touches, and `sql-pro` has
  already written them.

---

## 2. Three more decisions T3 forces

### 2.1 One Identity user, or one per store?

`StockApi` holds ASP.NET Identity in `ApiAuthDb` as a plain `IdentityDbContext` of
`IdentityUser`. There is no `SiteId` anywhere in it. The moment strangers register, two
questions land at once: can the same person hold accounts at two stores, and can a user
registered at store A sign in at store B?

The second has only one acceptable answer — no — and it is not automatic. A shared key ring
means a cookie issued by one host is readable by the other, which is deliberate and was set up
in T0, so **site membership has to be checked on every request, not just at sign-in.**

The first is a real fork. Identity's default unique username and email make one user per person
globally, which means a customer of two stores shares one credential and one password reset.
That is wrong for a template: the stores are separate businesses, and a person who buys from
both should not have their accounts linked, nor should store A's admin be able to infer that
store B has them as a customer.

**Recommendation: site-qualified usernames.** `UserName = "{SiteKey}|{email}"`, email left
non-unique, `Contact` carrying the `AccountId` that provides the site. Sign-in composes the
username from the resolved site plus the submitted email, so a store A credential simply does
not exist at store B. Authorization additionally checks that the principal's account belongs to
the resolved site, so a stolen cookie is useless cross-store.

Decide this now and not later: it is cheap today and a data migration with live credentials once
users exist.

### 2.2 Is an admin global or per-site?

`StockApi` and `SMPortal` have no site concept at all. Every admin read returns every store's
rows, including `spDistributorFeed_GetAll` handing over every tenant's `SecretRef`.

**Recommendation: a site selector backed by a claim, defaulting to every site for the `Admin`
role.** Admin reads take a `@SiteId` from the selected site rather than from a per-user
assignment. This is the same mechanism per-site admins would need, so constraining who may
select what later is a `UserSite` table and nothing else.

T3 does not strictly require it — with one site the unscoped reads are correct. Do it anyway, as
T3's first work item, because **T3 is when `Account` starts filling from the public**, and
retrofitting scoping onto a populated table with live customer data is materially worse than
doing it against 19 hand-made rows.

### 2.3 How is a registration field set defined?

`Site.RegistrationFieldSet` is `'eu-b2b'` and nothing reads it. The options are data (JSON in a
table) or code (implementations keyed by name).

**Recommendation: code, matching `IOrderingMode`.** Field sets carry validation, not just
layout — a VAT number has a per-country format, an EU registration demands a Chamber of Commerce
document and an export one does not. That is behaviour, and the codebase already has the exact
pattern for per-site behaviour: register every implementation, let a provider pick by the site's
key. `IRegistrationFieldSet` + `RegistrationFieldSetProvider`, alongside `IOrderingMode` +
`OrderingModeProvider`.

---

## 3. Schema

New tables:

- **`Contact`** — `AccountId`, `IdentityUserId`, `FirstName`, `LastName`, `Email`, `Phone`,
  `RoleInAccount` (`Admin` | `Buyer`), `IsPrimary`, `Status`, `CreatedDate`.
  Unique on (`AccountId`, `Email`). This is the join from an Identity user to a trading account,
  and therefore to a site.
- **`Address`** — `AccountId`, `Kind` (`Billing` | `Shipping`), `Line1`, `Line2`, `City`,
  `Region`, `PostCode`, `Country`, `IsDefault`, `CreatedDate`.
- **`AccountDocument`** — `AccountId`, `Kind` (`VatCertificate` | `ChamberOfCommerce` |
  `Other`), `StoredName` (a generated id, never the uploaded name), `OriginalName`,
  `ContentType`, `SizeBytes`, `UploadedByContactId`, `UploadedUtc`, `Status`.

Column additions:

- `Account.VatNumber`, `Account.RegistrationNumber` — the design doc lists VAT on `Account` and
  it is missing.
- `Account.ApprovedUtc`, `Account.ApprovedBy`, `Account.RejectionReason` — the approvals
  workflow currently changes a status and records nothing about who or why.
- `Account.SiteId` **to `NOT NULL`**. The write half of scoping is done and every insert now
  sets it; the column comment already says to tighten it once that is true.

New procedures: `spAccount_Register` (transactional), `spContact_*`, `spAddress_*`,
`spAccountDocument_*`, `spAccount_Approve`, `spAccount_Reject`, and `@SiteId` parameters added to
the existing `spAccount_GetAll` / `spQuote_GetAll` / `spOrder_GetAll` / `spCustomerGroup_GetAll` /
`spDistributorFeed_GetAll` reads.

---

## 4. Work items, in order

**1. Admin site scoping (§2.2).** Site selector, claim, `@SiteId` on every admin read, data
access and controllers threaded through. Done first, against small data.

**2. Pricing (§1).** SQL ordering key, group and margin looked up in-procedure,
`@CustomerGroupId` validated against `@SiteId`, the deferred `spCatalog_Search` rewrite, and the
differential test project.

**3. Schema and data access.** §3, plus `Account.SiteId` to `NOT NULL`.

**4. Registration field sets.** `IRegistrationFieldSet`, `RegistrationFieldSetProvider`, an
`eu-b2b` implementation. Field metadata drives the form; validation lives with the field set.

**5. `spAccount_Register`.** One transaction creating `Account` (Pending), primary `Contact`,
`Address`, and the Identity user. Identity is EF and the rest is Dapper, so the Identity user is
created first and the procedure rolls back against it on failure — a created user with no
account must not survive.

**6. Storefront auth.** `AddIdentityCore` plus cookie authentication in `SMStore` against the
same `ApplicationDbContext`. **`SMStore` must never call `Database.Migrate()`** — `StockApi`
owns Identity migrations, and two hosts migrating one database is a race. Distinct cookie name
from the admin cookie. Site membership checked per request.

**7. Document upload.** See §5.

**8. Email seam.** `IEmailSender` with a development implementation that writes to a log.
Confirmation, approval and rejection mail. **T6 owns the outbox and the templates** — T3 defines
the seam and nothing more, so T6 has something to fill rather than something to unpick.

**9. Approval flow.** `/admin/accounts` approve and reject, recording who and why, assigning a
`CustomerGroupId`, sending the mail, enabling sign-in.

**10. Account area.** `Account.razor`, `Login.razor` and `Register.razor` already exist as T1
shells. Profile, contacts, addresses.

---

## 5. Security requirements

Concentrated here because T3 is the first phase that accepts input from strangers.

**Document upload is the first customer-supplied bytes in the system.**

- Allow-list content types (PDF, JPEG, PNG) and **verify by sniffing the leading bytes**, not by
  trusting the declared type or the extension.
- Cap at 10 MB, enforced at the request-body limit as well as in code. The artboard says 128 MB;
  ignore it.
- Store outside the web root, behind an `IDocumentStore` seam — local filesystem in development,
  blob in production. A tenant must not be able to read another tenant's container path.
- **Serve only through an authorized endpoint** that re-checks the document belongs to an account
  the caller may see. No guessable static URL, ever.
- Never use the uploaded filename on disk. Generate the stored name; keep the original as
  metadata for display only, and encode it on the way out.
- Antivirus scanning is out of scope. Note it as accepted risk rather than assuming it.

**Everything else:**

- `CustomerGroupId` comes from the signed-in principal only.
- A group id not belonging to the resolved site degrades to no group, silently.
- Registration is unauthenticated and writes to the database: rate-limit it, and do not reveal
  whether an email is already registered.
- Password reset and email confirmation tokens are single-use and expiring — Identity's defaults
  are fine, but the reset page must not leak account existence either.
- `Account.Status` gates sign-in. A `Pending`, `Rejected` or `Suspended` account authenticates to
  nothing.
- Admin approval is a state transition on a scoped entity; it needs the same `@SiteId` predicate
  as the read that found it.

---

## 6. Not in T3

- The quote cart and quote submission — T5.
- Tax treatment and credit-limit enforcement — T6. `Account.CreditLimit` exists and stays unread.
- The email outbox, dispatcher and per-site templates — T6.
- Per-user admin site assignment. The selector ships; the `UserSite` restriction does not.
- Precomputed pricing (§1 option C).
- Social or SSO sign-in.

---

## 7. Risks

**The Identity username decision is one-way.** §2.1 is cheap now and a credential migration
later. It is the single highest-regret item in this phase.

**Two hosts, one Identity database.** Migration ownership must stay with `StockApi`. A stray
`Database.Migrate()` in `SMStore` will eventually run concurrently with the API's and corrupt
the migration history.

**The shared key ring cuts both ways.** It was set up in T0 so a cookie is portable between
hosts, which is what makes storefront sign-in simple — and it means an admin cookie presents
successfully at the storefront. Role checks and the site-membership check are what keep that
safe; neither is optional.

**`spAccount_Register` spans EF and Dapper.** Two connections, so no ambient transaction unless
one is deliberately created. Getting this wrong produces orphaned Identity users, which then
collide with the retry.

**No test coverage anywhere.** T3 adds one differential test for pricing and nothing else. Every
other item in this phase is verified by hand, and the phase is large.

---

## 8. Estimate

The plan's T3 says two weeks. That was written before admin site scoping, the pricing decision
and the `spCatalog_Search` rewrite were folded in, and before the Identity-per-site question
surfaced.

Realistically **3–4 weeks**: roughly half a week for admin scoping, half for pricing and the
rewrite, one for schema, registration and field sets, one for auth, upload and email, and half
for the approval flow and account area.

Items 1 and 2 are independent of the rest and are worth landing as their own PR — they are
corrections to merged work, and holding them behind a registration engine delays fixes that
stand on their own.
