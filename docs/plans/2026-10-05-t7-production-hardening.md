# T7 — Production hardening

Detail plan for the last template phase in
`docs/plans/2026-09-10-storefront-implementation-plan.md` (§4 T7, and the blocking security items
in §8, which the build plan calls "§6" from before T8 was inserted).

T0–T6 and T8 are merged. When T7 is done the template is done, and the tenant track (A0–A4)
starts.

**Exit (from the build plan):** deployed to a staging domain, monitored, restorable, rebuilt from
a clean checkout by CI.

**That exit has a step this plan cannot take.** `.github/copilot-instructions.md` says to keep
the Aspire setup Azure-ready but not deploy to Azure, and nothing here changes that. T7 builds
everything up to the deployment: the Azure topology in the app host, the pipeline, the runbook.
Running the staging deploy is a person's step with a subscription (§2, D1).

**Status: planned.** Branch `t7-production-hardening`, from `80ad9dd`.

---

## 1. What is already wrong

Like T5, T6 and T8, reading the path before building on it found problems. Most are on the API
host, which has had no public exposure yet, so they haven't been tested by anyone hostile.

### 1a. Anyone can create a staff login, and the endpoint says which customers exist

`POST api/User/Register` is `[AllowAnonymous]`. It creates an Identity user with
`EmailConfirmed = true` plus a `dbo.User` staff row, and nothing stops a stranger calling it. The
new login has no role, so it can't do anything yet. But it does join the staff list.

It also answers `409 "A user with this email address already exists"` by looking the address up
with `FindByEmailAsync`, across the whole user store, and customer logins are in that store too.
**So it tells anyone whether an address is a customer of any store on the platform.** T3 made
the storefront form answer the same way either way precisely to prevent this. And because
customer emails aren't unique (one person, two stores), `FindByEmailAsync` throws on a shared
address, so the call returns a 500.

### 1b. `/token` accepts customer passwords, with no limit and no lockout

`TokenController` finds the user by email and calls `CheckPasswordAsync`. A customer's email
matches their own login, so **a customer's storefront password works at `/token`**. The storefront
caps sign-in at twenty attempts per five minutes per IP, and that cap means nothing while the API
accepts the same guess without limit. `CheckPasswordAsync` doesn't count failures toward lockout,
and nothing rate-limits the API.

The token a customer gets has no role, so today it opens nothing. That's one class-level
`[Authorize]` away from opening something, and no test says otherwise. The same `FindByEmailAsync`
also throws for an admin whose email is also a customer's, so that admin can't sign in.

### 1c. The API host still serves ASP.NET Identity's built-in sign-in and registration pages, and an MVC home page

`AddDefaultIdentity` brings the default Identity UI, and `MapRazorPages` serves it:
`/Identity/Account/Register`, `/Login`, `/ForgotPassword` and the rest, on the API's own domain.
Nothing uses them. The portal signs in through `/token`, and so does the WPF app.

The default Login page signs in with `lockoutOnFailure: false`. A customer's login name is
`{SiteKey}|{email}`, which passes `[EmailAddress]` validation. So that page is a third unlimited
password check, and on success it issues the Identity cookie that the `SmartScheme` accepts on
API calls. `HomeController` and `Views/` are the project template's leftovers.

### 1d. CORS is open, and nothing needs it beyond one origin

`OpenCorsPolicy` allows any origin, method and header (build plan §8.1). The build plan expected
a per-site origin list. That isn't needed: `SMStore` renders on the server and never calls the
API from a browser. The only cross-origin caller is the admin portal, because it's served from
a different origin. Serving the portal from the API host removes CORS entirely (D2).

### 1e. Swagger is mapped in every environment (§8.2)

### 1f. Production has no health endpoints

`MapDefaultEndpoints` maps `/health` and `/alive` in Development only. The template's warning
about leaking detail doesn't apply here: the only check registered is `self`, and it writes the
word `Healthy`. In Azure Container Apps, nothing could tell a hung replica from a live one.

### 1g. Nothing handles the proxy's forwarded headers

Neither host configures forwarded headers. Behind Container Apps' ingress, which handles HTTPS
and forwards plain HTTP:

- `UseHttpsRedirection` sees HTTP and redirects to HTTPS, every time. The browser loops between
  the ingress and the app.
- Every customer appears to come from the ingress's address, so the storefront's per-IP rate
  limits put the whole internet in one partition. CLAUDE.md has flagged this for T7 since T3.

Aspire's Container Apps output may set `ASPNETCORE_FORWARDEDHEADERS_ENABLED` by itself. Item 10
checks the generated output instead of assuming it does.

### 1h. The Data Protection key ring is stored unencrypted

`AddSharedDataProtection` persists keys to `dbo.DataProtectionKeys` in `ApiAuthDb`, with nothing
encrypting them. A copy of `ApiAuthDb` alone is enough to:

- forge a session cookie for either host,
- decrypt every stored feed credential,
- read every password-reset link still waiting in the outbox.

T6's comment, "a copy of `SMDatabase` alone does not carry the means", is true, but it makes the
key ring the one thing worth stealing.

Production will wrap the keys with Key Vault. This affects keys created after it is switched on,
so it fits the standing rule: **existing key material is not moved or extracted.** No production
key ring exists yet, so production starts with wrapped keys from day one. The development ring
is left alone.

### 1i. There is no production schema path

The only DACPAC deploy is the app host's in-process publish, which uses
`BlockOnPossibleDataLoss = false`. Its own comment says T7 must not carry that setting into
production. CI uploads the DACPAC as an artifact and nothing applies it anywhere.

### 1j. The portal calls `https://localhost:7042`

`SMPortal/wwwroot/appsettings.json` hardcodes the API address. A deployed portal would call the
visitor's own machine.

### 1k. Documents are saved inside the container

`LocalFileDocumentStore` is the only `IDocumentStore`. Each app container has its own filesystem,
and a new revision starts with an empty one. So:

- a document uploaded to `SMStore` isn't visible to `StockApi`,
- every upload is lost on the next deploy.

CLAUDE.md predicted the symptom: a reviewer opens an application and every document returns 404.

### 1l. Staff can't sign in once created, and a fresh deployment can't create the first admin

- The portal's "add user" sends `Register` a random password that it never shows and never
  stores.
- No staff password reset or change exists anywhere.
- So a staff member added from the portal can never sign in.
- A fresh deployment has no admin and no way to make one (CLAUDE.md, SMPortal architecture).

On staging, that means a deployment nobody can use.

### 1m. CI runs no tests

`ci.yml` builds and uploads the DACPAC. Its comment says the four test projects don't exist yet;
they do. The database-backed tests and the E2E suite have only ever run on one developer's
machine.

### 1n. Every storefront page sends the visitor's IP address to Google

The default theme loads its fonts with `@import` from `fonts.googleapis.com`, so every page view
contacts Google. A German court (LG München I, 3 O 17493/20, January 2022) found that a GDPR
breach when done without consent. That ruling is the reason a cookie banner would otherwise be
needed at all.

Product images found by enrichment are linked directly from Icecat's servers, which raises the
same issue. See §5.

---

## 2. Decisions

Each one has a recommendation. Items 1–9 can start before D1 is answered; items 10–12 need it.

### D1. Who deploys — recommend: T7 builds the deployment, a person runs it

The repository instruction is clear, and it's the right one. A deploy creates billable resources
in somebody's subscription. **Claude makes no Azure calls in T7:** no `az login`, no `azd up`,
no `what-if`. T7 delivers:

- the topology in the app host, checked offline with `aspire publish` into a scratch directory,
- a `deploy-staging` workflow that runs only when triggered by hand, gated by a GitHub
  environment that needs a reviewer, signing in to Azure with OIDC (no stored secret),
- a runbook for the first deploy, DNS, and a restore drill.

Needed from you before item 10: subscription and region, the staging hostnames (one store
domain and one admin domain), and who approves the `staging` environment.

### D2. Where the portal lives — recommend: served by `StockApi`

The portal is static WebAssembly files. Served from the API host:

- it shares the API's origin, so CORS goes (1d),
- its API address is its own base address, so 1j goes,
- the app host loses a resource and the deployment loses a container.

**Cost:** the `sm-portal-standalone` launch profile goes. You iterate on the portal through the
app host, at `https://localhost:7042/`.

The alternative is a static host, Azure Static Web Apps, with one allowed origin from
configuration: one more resource, one more domain, and CORS kept.

### D3. Mail provider — recommend: Azure Communication Services Email

`IEmailSender` gets a second implementation, and only the dispatcher calls it.

- **ACS Email:** Azure's own email service, used through its .NET SDK with the app's managed
  identity, so no password is stored. Every store's domain is verified on one resource (SPF,
  DKIM and DMARC records, a runbook step per tenant). It's in the same Azure account as
  everything else.
- **The alternative is SMTP through MailKit:** it works with any provider, at the cost of a stored
  SMTP credential and a new dependency.

Either way, each store needs its own sender address: a new `Site.MailFromAddress` column, added
at the end of the table. A store with none doesn't send. Its messages are dead-lettered with a
reason, never sent from another store's address.

### D4. Documents — recommend: Blob storage everywhere, with Azurite locally

A `BlobDocumentStore` in a private container, with blob names `{siteKey}/{storedName}` and the
same stored-name checks the file store does today. Locally the app host runs Azurite, Azure's
storage emulator, as a container. Podman runs it like the SQL container. That way the E2E
documents journey exercises the path production uses.

`LocalFileDocumentStore` is deleted. The checks it enforces (stored-name shape, store prefix)
move into a pure helper that both the old tests and the new store use.

Existing development uploads in `app-data/` aren't migrated; it's gitignored test paperwork.

The alternative keeps files locally and Blob only in production. That's two stores, and the
tested one isn't the deployed one.

### D5. Scheduled work — recommend: stays in `StockApi`, which never scales to zero

Five loops will be in one host:

- image enrichment
- feed sync
- the mail dispatcher
- the quote-expiry sweep
- item 5's housekeeping

Every one is claim-based, so more than one replica is already safe. The only thing that breaks
them is zero replicas (T4 §risks), so `stock-api` gets `minReplicas: 1`.

The alternative, Container Apps Jobs on a cron schedule, is five more resources, each with its
own copy of the configuration and secrets, to solve a problem one setting solves.

### D6. Staff passwords — recommend: the admin sets the first one, staff change their own

- The portal's add-user form takes an initial password (with Identity's rules shown).
- The API gets `PUT api/User/Me/Password` (current and new password), and the portal gets a
  "Change password" page.
- For the first admin: `Admin:BootstrapEmail` and `Admin:BootstrapPassword`, set as secret
  app-host parameters. If no user holds `Admin`, `StockApi` creates or promotes that user at
  startup, together with the `dbo.User` row and the three roles. Once any admin exists it does
  nothing, and it logs a Warning while the values are still configured.

The alternative is an emailed invitation link. That needs staff mail, which has no store to send
from (the outbox is per store). It's worth doing once staff aren't global, and they will stop
being global when per-store admins arrive.

---

## 3. The production shape

What `aspire publish` should produce, once the items below are done:

| Resource | From | Notes |
| --- | --- | --- |
| Container Apps environment | `aca-env` (exists) | |
| `stock-api` | Container App | Serves the API and the portal. Custom admin domain. `minReplicas: 1` (D5) |
| `sm-store` | Container App | One custom domain per store, from a `store-domains` parameter, each with a managed certificate |
| Azure SQL | `AddAzureSqlServer` (exists) | Entra-only auth, managed identities. Point-in-time restore is on by default (7 days) |
| Key Vault | new | JWT signing key; the key that wraps the Data Protection keys. Purge protection on |
| Storage | new | Private `documents` blob container (D4) |
| Application Insights | new | Every host exports OpenTelemetry to it through `UseAzureMonitor`, switched on when its connection string is present |
| Communication Services | new, small Bicep module | Email (D3). Sender domains are verified per tenant |

**Schema path:**

1. Provision the resources.
2. `sqlpackage /Action:DeployReport` produces the change report, saved as a pipeline artifact.
3. `sqlpackage /Action:Publish /p:BlockOnPossibleDataLoss=true`, signed in with an Entra token.
4. Deploy the containers.

`ApiAuthDb` keeps `Database.Migrate()` at startup. EF Core 9 and later take a migration lock on
SQL Server, so replicas starting together no longer race. That changes CLAUDE.md's
reason-for-only-one-host-migrates, not the rule.

---

## 4. Work items, in order

### 1. Close the API's public surface (1a, 1b, 1c, 1e)

- `AddDefaultIdentity` becomes `AddIdentityCore` with roles, the EF stores and the sign-in
  manager. These go:
  - `Views/`, `Areas/` and `HomeController`,
  - `MapRazorPages` and `AddControllersWithViews`,
  - the `SmartScheme` (JWT becomes the only scheme),
  - the developer exception filters and migrations endpoint.

  The exception handler switches to ProblemDetails.
- `/token` looks users up with `FindByNameAsync`. It refuses any name `SiteQualifiedUserName`
  recognises as a customer's and checks the password with
  `CheckPasswordSignInAsync(lockoutOnFailure: true)`. Every refusal gets the same 400. It's rate
  limited per IP at twenty per five minutes, the storefront sign-in's numbers.
- `POST api/User/Register` becomes `[Authorize(Roles = "Admin")]` and looks up by name, so a
  customer's address can't collide with it and can't be probed through it.
- Swagger is mapped in Development only.
- **Guard:** a reflection test fails for any API action that isn't role-gated, except an
  explicit allowlist (`/token`, `GET api/User`). That makes "a roleless token opens nothing" a
  test instead of a hope.

**Verify:** StockApi.Tests; `/Identity/Account/Login` returns 404; a customer's credentials at
`/token` get 400; E2E 9/9.

### 2. Serve the portal from `StockApi` (D2, 1d, 1j)

- `StockApi` references `SMPortal` and serves its framework files.
- Any URL under `/`, `/login` or `/admin/*` serves the portal's `index.html`, but `/api` and
  `/token` don't, so a mistyped API path still gets a 404 and not a page.
- The portal's API address becomes `HostEnvironment.BaseAddress`.
- CORS is deleted.
- In the app host, the `sm-portal` resource goes. The E2E fixture reads the portal URL from
  `stock-api`. The standalone launch profile goes.

**Verify:** E2E 9/9, with the admin journeys hitting `https://localhost:7042/`.

### 3. Behind a proxy: forwarded headers, health, headers (1f, 1g, 1n)

- **Health:** `/health` and `/alive` are mapped in every environment, and the Container Apps
  probes point at them. On `StockApi` they skip admin site resolution, the way the storefront's
  probes already skip site resolution.
- **Security headers,** in ServiceDefaults for both hosts:
  - `X-Content-Type-Options: nosniff`
  - `Referrer-Policy: strict-origin-when-cross-origin`
  - a Content Security Policy (CSP) per host, with `frame-ancestors 'none'`.
    - **Storefront:** `'self'` only. The two inline `onclick="window.print()"` move to a small
      script file, and `<ImportMap />` goes unless something needs it.
    - **Portal:** adds `'wasm-unsafe-eval'`.
- **Fonts:** self-hosted under `wwwroot/sites/{SiteKey}/fonts/` (Barlow is under the open SIL
  Open Font License), and the Google `@import` is removed. The CSP then needs no third-party host,
  and the storefront makes no third-party request.
- `SMStore` drops `AddInteractiveServerComponents` and `ReconnectModal`. No page is interactive,
  and the `_blazor` hub is an open endpoint serving nothing.
- The forwarded-headers setting itself is checked in item 10, where the generated output can be
  read.

**Verify:** E2E with a console listener that fails on any CSP violation; `curl -I` shows the
headers on both hosts.

### 4. A deployment can have staff (D6, 1l)

Bootstrap at startup, the add-user password, change-own-password, and the portal pages.

**Verify:** unit tests that the bootstrap does nothing once any admin exists, and promotes
rather than duplicates an existing user. One new E2E journey: an admin adds a staff member, who
signs in and changes the password.

### 5. Housekeeping sweeps

Owed since T5 (baskets) and T6 (sent mail).

- `spBasket_SweepAbandoned` deletes anonymous baskets (`ContactId IS NULL`) that haven't changed
  since a cutoff, in batches of 5,000, so a large backlog never holds a long lock.
- `spEmailOutbox_SweepSent` deletes `Sent` rows past a cutoff. Dead letters stay, because an
  operator has to see them.
- One `HousekeepingBackgroundService` in `StockApi`, run once a day through `DailySchedule`. It's
  **on by default** (`Housekeeping:Enabled`, `AbandonedBasketDays` 30, `SentMailDays` 90).
  - Unlike the expiry sweep, it writes to nobody.
  - The development database grows with every E2E run.
  - Deleting an abandoned anonymous basket from a copy of production harms no one.

**Verify:** database tests: the cutoff is respected, a contact's basket is never swept, dead
letters survive.

### 6. Documents in Blob storage (D4, 1k)

`BlobDocumentStore`, Azurite in the app host, the shared name checks, and `LocalFileDocumentStore`
deleted. Downloads stream from the blob and still send `Content-Disposition: attachment` and
`nosniff`.

**Verify:** the existing document tests against the helper; Blob-backed tests skip without
Azurite, as the DB tests skip without SQL. The E2E registration and approval journeys exercise
upload and review.

### 7. A real mail transport (D3)

- `AcsEmailSender`, registered when `Email:Transport` is `Acs`. Development keeps
  `LoggingEmailSender`.
- `EmailMessage` gains the sender address and name. The dispatcher fills them from the store's
  `Site` row (`MailFromAddress`, `Name`).
- A store with no sender address has its messages dead-lettered at once with that reason. A retry
  can't fix a missing address, as it can't fix an unreadable payload.

**Verify:** dispatcher unit tests: the sender address comes from the store, and a missing one
dead-letters. `TransportCallerTests` still pass, because only the dispatcher holds the sender.

### 8. Cookies and legal pages

The terms and privacy pages already exist (`/terms` and `/privacy`, from `SiteContent`). Add:

- `/cookies` as a fourth `ContentPage` route, with a footer link.
- **No consent banner.** The storefront sets only strictly necessary cookies: the customer
  session, the basket and antiforgery. Those are exempt from consent under ePrivacy Art. 5(3).
  With item 3 done, the storefront also contacts no third party.
- **Guard:** an E2E step lists every cookie the browser holds after the quote journey. It fails
  on any name outside those three, with a message saying a new cookie needs a consent decision.
  A second step fails on any request a storefront page makes to another host.

**Verify:** the guard steps pass; a deliberately added cookie fails them.

### 9. Load check

Done on a separate database, `SMDatabaseLoad`, in the development container. It has the same
DACPAC, 50,000 products, two stores, 40 categories each, and 2,000 per-store overrides. Every run
is repeated fifty times:

- `spCatalog_Search`:
  - the default sort (featured),
  - a text search,
  - a category filter,
  - a price sort.
- `spCatalog_GetFacets`
- `spCatalog_GetBySku`
- the admin portal's `GetCatalog` snapshot: its size, and the time from request to a rendered
  products page.

**Target:** each procedure under 100 ms at the 95th percentile.

The portal number is measured, not targeted. T8 §7 flagged the whole-catalog snapshot, and
whether it holds at this scale is the question. Results go in §7 of this plan.

A failing procedure is fixed here if an index fixes it. A failing snapshot becomes a decision
for you, not a rewrite slipped in.

### 10. Azure topology in the app host (D1, §3)

Key Vault, Storage, Application Insights, the Communication Services module, per-store custom
domains, `minReplicas`, probes, and Key Vault wrapping for the Data Protection keys (in publish
mode only). The in-process DACPAC publish is wrapped in `IsRunMode` explicitly. The forwarded-
headers setting is confirmed in the generated output and set if absent.

**Verify:** `aspire publish` into a scratch directory succeeds, and the output is read for the
settings above. No Azure calls.

### 11. CI (1m)

- **Windows job:** build, then the routine unit filter. The log has to show four `Passed!`
  lines, because a project that fails to compile drops out silently.
- **Linux `database` job:** SQL Server as a service container. It publishes the DACPAC with
  `sqlpackage` and runs the database tests, and **fails if any test skipped**: a broken
  connection string would otherwise skip all of them and pass.
- **Linux `e2e` job:** a container runtime, Playwright's Chromium, and
  `E2E_REQUIRE_APPHOST=1`. The app host's `sm-desktop` resource is registered on Windows only.
- **`deploy-staging.yml`:** manual trigger, `staging` environment, OIDC, then provision,
  database report, database publish, deploy. Written and reviewed, not run (D1).

**Verify:** all three jobs green on the PR.

### 12. Runbook and docs

`docs/runbooks/production.md` covers:

- **The first deploy:**
  - the OIDC federated credential,
  - the GitHub environment,
  - the parameters: JWT key, bootstrap admin, domains.
- **Per-store DNS:**
  - the Container Apps CNAME and `asuid` TXT record,
  - the ACS SPF, DKIM and DMARC records.
- **Rotating the JWT key.**
- **The restore drill:**
  - **Both databases are restored to the same point in time.** Contacts reference Identity users,
    so restoring one without the other leaves contacts pointing at missing users.
  - **The Data Protection key ring lives in `ApiAuthDb`,** so restoring it to before a key was
    created makes cookies, feed credentials and outbox payloads encrypted since then unreadable.
  - **The Key Vault key that wraps the ring must never be purged.**

CLAUDE.md is updated wherever this phase changes the rules.

**Exit for this branch:** items 1–12 done, CI green. **Exit for T7:** you run `deploy-staging`,
bind the staging domains, and run the restore drill once from the runbook.

---

## 5. Risks

- **Removing the Identity UI and the cookie scheme is a breaking change for anything that used
  them.** Nothing in the repository does; something outside it could. The API is JWT-only
  afterwards.
- **Lockout is a lever for an attacker.** Five failures lock an admin out for fifteen minutes,
  and anyone who knows an admin's address can trigger it. The per-IP limit narrows that; it
  doesn't remove it. That's the usual trade, and for a handful of staff the right one.
- **A Content Security Policy breaks things quietly.** A blocked script or style is a console
  line, not an exception. That's why item 3's verification is the E2E suite with a listener that
  fails on violations, not a page load.
- **Icecat images are linked from Icecat's servers,** so a product with one leaks the visitor's
  IP address the way the fonts did. It's 63 products today. Copying images into Blob storage
  during enrichment closes it, and is listed under §6 for A4 to decide before launch.
- **The JWT lives in `localStorage` for a day.** Script injection on the portal's origin can read
  it. The CSP is the mitigation. Moving to a cookie would mean CSRF protection on the API, which
  is out of scope.
- **Five background loops in one host** (D5). That's fine at this volume, and it's the first
  thing to split if `stock-api` starts scaling on load.
- **The whole-catalog portal snapshot** may not hold at 50,000 products (item 9). If it doesn't,
  the fix is server-side paging for the products list. That's real work and gets its own
  decision.

## 6. Not in T7

- **The staging deploy itself:** D1.
- **Per-store admins:** a prerequisite for a second tenant, which is out of v1. The change is
  still the one check in `AdminSiteResolutionMiddleware` that CLAUDE.md describes.
- **A `Site` settings screen and `SiteCategory` management:** staff still set these with SQL.
  They're real gaps, but feature work rather than hardening.
- **A PDF renderer:** T6 chose a link to the printable page over an attachment, and nothing in
  T7 needs a file.
- **Bounce and complaint handling from ACS:** needs Event Grid and a suppression list. Until then
  a hard bounce dead-letters like any other failure.
- **Antivirus scanning of uploads:** still the accepted risk recorded in T3.
- **Copying Icecat images into Blob storage:** §5. A4 decides before launch.
- **Long-term backup retention** beyond the default seven-day point-in-time restore. That's a
  retention-policy decision for A4's GDPR work, not a platform default.

## 7. Results

Filled in as items land.
