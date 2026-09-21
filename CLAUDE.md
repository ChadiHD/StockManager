# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 10 stock-management solution being built into a **reusable B2B ecommerce platform** — a
template from which multiple storefronts can be stood up, not a one-off site. aclitrade.ie
(Ireland) is the first store to launch on it and the driver of current requirements, but it is
one tenant among future ones, so prefer the generic mechanism over the Ireland-specific shortcut:

- Site-scoped data over hardcoded assumptions. Country, currency, VAT rules, domain and
  distributor feed config belong to a `Site` row, not to constants.
- Branding, catalog taxonomy and content are configuration or per-site assets, never baked into
  components.
- Anything named or worded for aclitrade specifically belongs in that site's configuration or
  content, not in shared code.

Two documents drive the work — read both before adding features to the admin portal, storefront,
quotes, accounts, customer groups or distributor feeds:

- `docs/plans/2026-07-24-aclitrade-b2b-ecommerce-design.md` — the target design
- `docs/plans/2026-09-10-storefront-implementation-plan.md` — the build plan and phase scope

**The template is finished before the first tenant is built.** The plan runs two tracks, T0–T7
for the platform and A0–A4 for aclitrade.ie, and the platform track goes first in full. A
tenant built alongside an unfinished template is how store-specific assumptions get into shared
code, and the whole point of this exercise is that store number two costs days rather than
months. T0–T4 are merged; T3 — customer identity, per
`docs/plans/2026-09-14-t3-customer-identity.md` — covered admin site scoping, the pricing
decision, the schema, registration field sets, `spAccount_Register`, storefront sign-in,
document upload, the mail seam, the approval flow, the account area, email confirmation and
password reset.

T4 — feed reliability, per `docs/plans/2026-09-17-t4-feed-reliability.md` — is merged: the sync
claim, sync history, the nightly scheduler, staleness hiding, failure and staleness alerting,
and the delisted-SKU verification.

**T5 — ordering is in progress**, per `docs/plans/2026-09-17-t5-ordering.md`. Items 1 to 6
of nine are built: the accept claim and the placer, quote lines that record the price the
customer was shown, the server-side basket, the basket pages, submit, and the customer's
own quote and order screens. Admin re-pricing, the documents and the end-to-end pass are
planned. The plan opens with the four decisions that had to be settled before any of it
could be written.

A corollary worth taking literally: **if a tenant task requires editing shared code, that is a
template gap.** Fix the template and let the tenant consume it, rather than special-casing.

.NET Aspire orchestrates everything.

| Project | Role |
| --- | --- |
| `StockManager.AppHost` | Aspire orchestrator. **The entry point** — dashboard on 17291 |
| `StockApi` | ASP.NET Core Web API, HTTPS **pinned to 7042** (clients hardcode it) |
| `SMDataManager.Library` | Dapper + stored-procedure data access. Shared by `StockApi` and `SMStore` |
| `SMDatabase` | SDK-style `.sqlproj` (`Microsoft.Build.Sql`) — tables, procedures, functions |
| `SMStore` | Blazor **Web App** (SSR) customer storefront. Serves every site from one deployment |
| `SMPortal` | Blazor **WebAssembly** admin portal (`/admin/*`) |
| `SMDesktopUI` + `.Library` | WPF POS desktop app (legacy, still shipped) |
| `SMDesktopUI.UITests` | xunit + FlaUI UI automation for the WPF app |
| `StockManager.Identity` | `ApplicationDbContext` — ASP.NET Identity's schema, shared by `StockApi` and `SMStore` |
| `StockManager.ServiceDefaults` | Shared host services: telemetry, health checks, resilience, Data Protection, the document store and the mail seam |
| `SMDataManager.Library.Tests` | xunit + FluentAssertions + NSubstitute. Pricing, staleness, and that a `siteId` reaches the procedure. The T-SQL tests need a database and skip without one |
| `StockApi.Tests` | xunit + FluentAssertions + NSubstitute. Controllers, admin site resolution, the document store |
| `SMStore.Tests` | bUnit + xunit. Component rendering and per-tenant variation, with `SMDataManager.Library` substituted |
| `SMPortal.Tests` | bUnit + xunit. Admin components against a substituted `IAdminDataService` — no HTTP |
| `StockManager.E2ETests` | Playwright + `Aspire.Hosting.Testing` + xunit. Starts the real app host and drives whole journeys. Slow; needs a container runtime |

`SMStore` does **not** call `StockApi`. It renders on the server and reads `SMDatabase` through
`SMDataManager.Library` in process — an HTTP hop to a co-located API would only add a token
exchange to every anonymous catalog page. `SMPortal`, being WebAssembly, still goes through the
API.

## Multi-store rules

Every request resolves to a `dbo.Site` row from its host header, via `SiteResolutionMiddleware`,
before anything else runs. Consequences that are easy to get wrong:

- **Read site values from `ISiteContext`, never from configuration or a constant.** Country,
  currency, locale, ordering mode, registration field set and price visibility all live on the
  site.
- **An unresolved host is a 404, never a fallback to a default store.** Falling back is how one
  tenant's catalog and prices get served on another tenant's domain.
- **Every query over a scoped entity filters on `SiteId`.** Omitting it is a cross-tenant data
  leak, not a display bug. Scoped entities: `Account`, `CustomerGroup`, `Quote`, `Purchase`
  (portal orders only), `DistributorFeed`, `SiteCategory`, `CategoryMapping`, `SiteContent`.
- **A join to a scoped table needs the predicate even when the join path looks safe.** A
  `CategoryMapping` row scoped to site A can name a `SiteCategory` belonging to site B — the
  mapping's own `SiteId` says nothing about the category it points at. The catalog procedures
  filter `SiteCategory` explicitly, and `FK_CategoryMapping_ToSiteCategory` is composite
  (`SiteCategoryId, SiteId`) so the database refuses the mismatched row in the first place.
  Prefer the composite key wherever a scoped row references another scoped row.
- **Nothing outside `SMStore/Ordering/` branches on `Site.OrderMode`.** Ask
  `OrderingModeProvider.Current` instead.
- **`wwwroot/app.css` holds no colour of its own.** It reads custom properties that a theme
  under `wwwroot/sites/{SiteKey}/` defines. `SiteThemeResolver` falls back to the `default`
  theme for any asset a site is missing.
- `Sites:ForceSiteKey` pins every request to one store for local work. `SMStore` refuses to
  start with it set outside Development.

### Scoping is done on writes and not on admin reads

**Read scoping in the admin API is not implemented, and the storefront's is.** `SMStore`
resolves a site per request and every storefront query filters on it. `StockApi` and `SMPortal`
have no site concept at all: no controller, service or data class mentions `SiteId`, and
`spAccount_GetAll`, `spQuote_GetAll`, `spOrder_GetAll`, `spCustomerGroup_GetAll` and
`spDistributorFeed_GetAll` return every store's rows. That is correct only while an admin is
global. It stops being correct the moment an admin belongs to one store — and
`spDistributorFeed_GetAll` returning every tenant's `SecretRef` is the sharpest edge of it,
since `StockApi` holds the key ring that decrypts them. **Deciding whether an admin is global
or per-site is a prerequisite for a second tenant, not a later refinement.**

The write half is done. Every insert over a scoped entity sets `SiteId`: `spAccount_Insert`,
`spCustomerGroup_Insert` and `spDistributorFeed_Insert` take an optional `@SiteId`, while
`spQuote_Insert`, `spOrder_Insert` and `spOrder_ConvertFromQuote` derive it from the account or
quote the row descends from. All six resolve through `dbo.fnSite_Resolve`, which fills in the
only site when a database has exactly one and returns NULL when it has more, so the procedure
throws rather than guessing. This matters more than it looks: `UQ_CustomerGroup_Name`,
`UQ_CustomerGroup_Slug` and `UQ_DistributorFeed_Name` are scoped by site, and SQL Server treats
NULL as one distinct value in a unique constraint — while `SiteId` went unset, the second store
to want a "Reseller" group or a "Main" feed simply could not be created.

### Customer identity spans two databases

ASP.NET Identity lives in `ApiAuthDb` under EF; accounts, contacts and addresses live in
`SMDatabase` under Dapper. There is no transaction across them and there will not be — that
would mean MSDTC, which Azure SQL does not offer. `RegistrationService` therefore creates the
login first, calls `spAccount_Register`, and **deletes the login again if that call fails**.
The procedure is itself all-or-nothing, so the only unrecoverable case is a failed
compensating delete, which is logged at Error with the username because a login with no
account blocks that person from registering again and nothing else would surface it.

- **`ApplicationDbContext` is shared but only `StockApi` migrates it.** It calls
  `Database.Migrate()` at startup and the app host starts both hosts together; two processes
  applying migrations to one database race on the history table. Its migrations stay in
  `StockApi` via `MigrationsAssembly`, so the `dotnet-ef` workflow is unchanged.
- **A customer login is named `{SiteKey}|{email}`**, and emails are not unique. One person may
  hold accounts at two of the stores run from here, and those are separate businesses that
  must not be able to infer each other's customers. The separator is a pipe because every
  character in Identity's default allow-list can legally appear in an email; both hosts set
  `SiteQualifiedUserName.AllowedUserNameCharacters`, because they share one user store.
- **A valid cookie proves identity, not entitlement.** The shared key ring means a cookie
  issued by either host is readable by both, so site membership is checked per request through
  `Contact` → `Account` → `Site` — `spContact_GetByIdentityUser` takes a `@SiteId` and returns
  nothing for a user belonging to another store.
- **An unconfirmed address cannot sign in.** `RegistrationService` mints an Identity
  email-confirmation token and mails the link; `/confirm-email` calls `ConfirmEmailAsync`, and
  `CustomerAuthEndpoints` refuses a user whose `EmailConfirmed` is false. Creating a login
  proves nothing about who owns the address — an applicant can type somebody else's — and the
  approval mail and password reset both go there. The tokens are Identity's, which is
  what makes them single-use and expiring: confirming changes the user's security stamp, so a
  link cannot be replayed. Do not roll a token here.
  - **Checked at sign-in, not in `CustomerSessionValidator`.** `EmailConfirmed` cannot change
    while a session lives, because a session can only begin at sign-in, so re-reading it per
    request would buy nothing. The security stamp is the opposite case and is checked there;
    see below.
  - **`/resend-confirmation` exists because the gate would otherwise be a dead end.** Someone
    who never received the mail has no other route in, and staff cannot fix it from the portal
    — the flag lives in `ApiAuthDb` and the admin screens read `SMDatabase`. It answers
    identically for an unknown address, another store's address, and one already confirmed:
    an endpoint that only sent mail for real addresses would be a cheaper enumeration oracle
    than the registration form, needing no password and no paperwork.
  - **A confirmation link is checked against the store as well as the token.** A token is
    evidence about an address and says nothing about which tenant issued it, so
    `/confirm-email` compares `SiteQualifiedUserName.BelongsTo(user.UserName, site.SiteKey)`.
    The link itself is built from `Site.Domain`, never from the request host — it lands in a
    customer's inbox, where a link to an attacker's host carrying a valid token is the prize.
    `MailedTokenLink` builds both mailed links for that reason: written twice, the rule drifts.
- **Password reset is Identity's tokens plus two pages, and `PasswordResetService` holds every
  rule worth getting right.** `/forgot-password` posts to `/request-password-reset`, which
  answers identically for every address and returns nothing the caller can read; the reset link
  lands on `/reset-password`, which posts back to itself because a password the store's rules
  refuse needs the rules quoted and the form kept.
  - **A reset ends every session that login had open**, and that is the half a reset page
    cannot do for itself. `ResetPasswordAsync` rotates the security stamp, so
    `CustomerAuthEndpoints` puts the stamp into the cookie as `SecurityStampClaim` and
    `CustomerSessionValidator` compares it per request. Without that, a customer resetting
    because they believe somebody is in their account changes nothing for the somebody, who
    stays signed in for up to fourteen days. It costs one primary-key read on `ApiAuthDb` per
    authenticated request, on top of the contact lookup; a missing claim is refused rather
    than skipped, so sessions predating the check end once.
  - Deliberately not Identity's own `SecurityStampClaimType`. The shared key ring means an
    admin cookie from `StockApi` is readable here, and under Identity's claim name it would
    arrive carrying a stamp that validates.
  - **An unconfirmed address gets no reset mail.** Nobody has shown they own it, so a token
    sent there is a credential handed to whoever typed the address into the registration form.
    `/forgot-password` therefore offers the confirmation route to everybody — offering it only
    to the people it applies to would say which people those are.
  - **A reset link is checked against the store as well as the token**, exactly as a
    confirmation link is, and for the same reason.
  - The success page and the "your password was changed" mail both state that other sessions
    ended. That is true only while the stamp check above exists; remove it and both lie.
- **Registration, sign-in and both halves of password reset are rate limited; nothing else is.**
  `CustomerRateLimiting` partitions a global limiter by caller and path, returning no limiter
  for everything else — a cap that reached the catalog would be a denial-of-service switch
  aimed at the shop window. Registration is ten an hour because it writes across two databases
  and accepts files; asking for a reset link is ten an hour because it fires mail from this
  store's sending domain at an address a stranger chose; sign-in and posting a new password are
  twenty per five minutes each because the storefront authenticates with
  `UserManager.CheckPasswordAsync`, which — unlike `SignInManager` — does not consult
  `IdentityOptions.Lockout`, so nothing else here slows a password guess down.
  - Partitioned by IP only, deliberately: adding the site to the key would hand an attacker
    one allowance per store.
  - **T7 must configure forwarded headers before these mean anything in production**, or every
    customer shares the proxy's partition and the cap protects nobody while throttling
    everybody.
  - The rejection writes a body. `UseStatusCodePagesWithReExecute` re-executes to `/not-found`
    for any bodiless 400–599, so a silent 429 would tell the customer the page does not exist.
- **Registration answers the same way whether or not the address is already registered.**
  Saying otherwise turns the form into an oracle for who buys here. That leaves a genuine
  duplicate applicant with no feedback until the "someone tried to register" email exists, so
  the acknowledgement page must carry a "contact us if you hear nothing" line until then.
  The same rule governs the paperwork: a duplicate applicant gets no `AccountId` back from
  `RegistrationService`, so their upload is not attached to the existing account — doing so
  would confirm to whoever sent it that the account is there.
- **The registration form renders from the field set, not from markup.** Which boxes exist,
  which are required and what they are called are `IRegistrationFieldSet`'s to decide, and a
  `Hidden` field is not rendered, not read and not stored. `Register.razor` reads its posted
  values out of `HttpContext.Request.Form` rather than model-binding, because there is no
  fixed model when the fields vary per store — and because that is what keeps the password
  out of the round trip. Everything echoed back after a validation error is echoed because
  the page chose to; the two password inputs never are.

### Customer documents

The first customer-supplied bytes the platform accepts. `IDocumentStore` holds them and
`dbo.AccountDocument` describes them; the two are always written in that order, because a
row with no file is a broken download a reviewer sees and a file with no row is an orphan
nobody serves.

- **A file's type is decided by reading it.** `DocumentContentTypes.TryDetect` sniffs the
  leading bytes; the declared `Content-Type` and the extension are both chosen by whoever is
  uploading and neither is evidence. What the sniffer returns is what gets stored, served
  back and written to the `ContentType` column. Three types are allowed — PDF, JPEG, PNG —
  and each addition widens the parser surface on a reviewer's machine.
- **The uploaded filename never reaches a filesystem.** `SaveAsync` generates the stored name
  and the original is metadata for display only. `LocalFileDocumentStore` re-checks the shape
  of a stored name on the way back in and that the resolved path is inside the store's own
  container, because the value arriving there came out of a query whose parameters came out
  of a URL.
- **Every `IDocumentStore` call takes the site key**, so the bytes carry the same predicate
  the metadata row does. A row read for the wrong store cannot resolve a file even if the
  query that found it had no site filter at all.
- **There is no static URL for a document and there must never be one.** The bytes live
  outside the web root; `SMStore` serves them from `/account/documents/{id}` after checking
  the row belongs to the session's account, and `StockApi` serves them to a reviewer from
  `AccountDocumentController`. Both answer 404 for "not yours" as well as "not here" — the
  ids are sequential, and a 403 would confirm one exists. Both send
  `Content-Disposition: attachment` and `X-Content-Type-Options: nosniff`: nothing a stranger
  uploaded should render in an origin that holds a session.
- **`AccountDocumentModel.StoredName` must not reach a response.** Only
  `spAccountDocument_GetById` projects it, and only the download path calls that.
  `AccountDocumentController` returns a projection rather than the model for exactly this
  reason — do not "simplify" it back to the model.
- **The 10 MB cap is enforced on the request as well as in code.** A check in code runs after
  the body has been read, which is no protection; `SMStore` sets Kestrel's
  `MaxRequestBodySize` and `FormOptions.MultipartBodyLengthLimit` from
  `DocumentStoreOptions.MaxBytes`, and the in-code check is what produces a useful message
  for a file just over the per-file line. Registration is the storefront's only upload, so
  the limit is global; a second one would make it per-endpoint metadata.
- **Antivirus scanning is not done anywhere.** Recorded as accepted risk in the T3 plan, not
  overlooked. The mitigation is the narrow allow-list and that nothing is executed
  server-side.
- **Both hosts must resolve the same root, and in development they do so by accident.** The
  default is `<content root>/../app-data/documents`, which lands on the repository root for
  both `SMStore` and `StockApi` because they sit side by side. Containers do not, so set
  `Documents:RootPath` on both when they stop sharing a filesystem — the symptom is a
  reviewer opening an application whose documents all 404. `/app-data/` is gitignored:
  real applicants' paperwork must never reach a commit.

### Outbound mail is a seam and nothing more

`IEmailSender` in `StockManager.ServiceDefaults/Email/`, registered by `AddEmail()` on both
hosts, with `LoggingEmailSender` writing the whole message to the log. **T6 owns the outbox,
the retry policy and the per-site templates** — T3 wrote the three call sites so T6 has
something to fill rather than something to find: the application acknowledgement in
`SMStore/Registration/RegistrationEmails.cs`, and the approval and rejection in
`StockApi/Accounts/AccountDecisionEmails.cs`.

- **Sending never fails the operation that triggered it.** The write has already committed by
  the time the mail goes out. An approval rolled back because a mail server blinked would
  leave the portal re-issuing a decision that had in fact been made, and landing on the
  conflict response. Failures are logged with the account id and chased operationally.
- **`LoggingEmailSender` logs the body in Development only.** Bodies now carry confirmation
  and password-reset links, and a reset link is a credential — whoever reads the log can take
  the account, and logs are copied, shipped to a telemetry backend and read by people with no
  business signing in as a customer. In Development that logging is exactly what makes the
  feature testable, since the link is read out of the Aspire dashboard, so the line is drawn at
  the environment. It stays registered everywhere rather than being Development-only: with no
  `IEmailSender` at all, the first registration fails to resolve one, and an unsendable message
  is worse than an unsent one.

### Approving an application

`spAccount_Approve` and `spAccount_Reject`, behind `POST api/Account/{id}/Approve` and
`/Reject`. **Not `spAccount_UpdateStatus`**, which still exists for suspending and
reinstating but cannot be the approval path: approval is the one transition that has to leave
evidence, and "someone changed a status" is not an answer to a customer asking why they were
given these terms.

- The approver comes from the authenticated principal. A decider in the request body would be
  an audit trail written by the auditee.
- A rejection reason is required by the procedure, by the controller and by the portal,
  because it is quoted to the applicant verbatim.
- Neither procedure touches an account already in the target state, so a no-op means somebody
  else decided it while the screen was open. That is a `Conflict`, not a success — reporting
  success would show an approval this request did not make.
- The pricing group is assigned at approval rather than after it, because it applies the
  moment the customer signs in. The portal's modal defaults to the group the account already
  carries, so approving without touching the selector cannot silently reprice.

Data Protection keys are shared by `StockApi` and `SMStore` through `AddSharedDataProtection`,
persisted to `dbo.DataProtectionKeys` in `ApiAuthDb`. Both apps must keep the same application
name, or neither can read the other's cookies or the feed credentials in `dbo.DistributorFeed`.

**Moving the key ring orphans everything already encrypted against the old one.** When
`AddSharedDataProtection` replaced the previous per-machine ring
(`%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`), the new database-backed ring started empty, so
the distributor feed password stored months earlier could no longer be decrypted —
`The key {guid} was not found in the key ring`, surfacing as a 502 on sync. There is no
recovery but re-entering the password on the feed, which re-encrypts against the current ring;
`DataProtectionFeedSecretStore.ResolveAsync` now says exactly that instead of surfacing the raw
cryptographic error. Before changing where keys live again, remember that every `SecretRef` in
`dbo.DistributorFeed` is ciphertext bound to the ring that wrote it.

## Build and run

Run everything through the app host:

```bash
dotnet run --project StockManager.AppHost/StockManager.AppHost.csproj
```

Aspire provisions SQL Server as a **persistent container** (`AddAzureSqlServer("sql").RunAsContainer()`
with volume `stockmanager-sql-data`) holding `ApiAuthDb` and `SMDatabase`. The connection strings in
`StockApi/appsettings.json` are overridden by Aspire at run time — the `SITIHAPIB` value there is not
what a running app uses.

### SMDatabase builds with the dotnet CLI

`SMDatabase.sqlproj` is an SDK-style project (`Microsoft.Build.Sql/2.2.0`), so the DACPAC comes
from a plain build and `dotnet build StockManager.sln` works:

```bash
dotnet build SMDatabase/SMDatabase.sqlproj
```

It was classic SSDT until 2026-09-10 and `dotnet build` failed with `MSB4278`. An earlier
conversion attempt on Microsoft.Build.Sql 1.x was reverted because VS 18's SSDT targets pass
`SqlBuildTask` parameters the 1.x task did not declare (`MSB4064` / `MSB4063` on
`AutomaticIndexCompaction` or `AcceleratedDatabaseRecovery`). The 2.x SDK declares them. **If a
Visual Studio build ever reports `MSB4064` on a property name again, that is this collision
returning — look at the SDK version, not the project layout.**

`<TargetFramework>netstandard2.0</TargetFramework>` is pinned in the sqlproj and must stay.
Left unset, the two toolchains disagree: `dotnet build` restores `netstandard2.0` while a build
through Visual Studio evaluates `net472`, because VS still supplies the legacy SSDT
`TargetFrameworkVersion` of `v4.7.2`. The result is
`error NETSDK1005: Assets file ... doesn't have a target for 'net472'`. With it pinned, both
toolchains share one `obj/project.assets.json` and can be alternated without a re-restore.

`StockManager.AppHost` carries a build-only `ProjectReference` to the sqlproj
(`ReferenceOutputAssembly="false" IsAspireProjectResource="false"`), so building the app host
builds the schema too and there is no separate step after touching SQL — just rebuild and restart.

**The app host does not guess where the DACPAC is.** Its `ResolveSMDatabaseDacpacPath` target asks
the SQL project (`GetTargetPath`) and stamps the answer in as `AssemblyMetadata`, which `AppHost.cs`
reads back. The path moves with `$(Configuration)` and again with `$(BaseOutputPath)`, so a
hardcoded `bin\Debug` sent a Release or output-redirected build's guard at a file nothing had
written. Keep the target and the project reference together — `AppHost.cs` throws with an
actionable message if the metadata is absent.

It still **refuses to start** if that DACPAC is missing or older than any `.sql` file under
`SMDatabase/`, which catches a hand-edited `.sql` that was never rebuilt.
`AppendTargetFrameworkToOutputPath` is off in the sqlproj so the output does not nest under a
target-framework folder.

**The schema is published in process, on the database's `ResourceReadyEvent`.** `AppHost.cs`
calls `DacServices.Deploy` directly. Aspire awaits `ResourceReadyEvent` subscribers before
dependents that `WaitFor` the resource proceed, so a plain `WaitFor(stockDatabase)` is
sufficient and neither app needs `WaitForCompletion`. The deploy costs about ten seconds a
launch, has no dashboard row, and has no skip-when-unchanged fast path — all three are the
price of an application that starts.

**Do not reintroduce `CommunityToolkit`'s `AddSqlProject`.** That resource never ran here: it
reported one `Waiting` snapshot and never another, so everything waiting on it waited for
ever and `dotnet run` could not start the apps at all. The package is gone deliberately.

Building `StockManager.AppHost.csproj` **alone** with Visual Studio's MSBuild from a clean `obj`
fails with `CS0246: The type or namespace name 'Projects' could not be found` — the Aspire SDK
generates those classes from the reference graph. Build the solution, or use `dotnet build`.

### Locked bin directories

While the app host, `StockApi.exe` or `devenv.exe` are running, builds fail with `MSB3027` /
`MSB3021` copy errors — the running processes hold the output DLLs. Do not kill the user's processes.
Compile-check into a scratch directory instead:

```bash
dotnet build StockApi/StockApi.csproj -p:BaseOutputPath=<scratch-dir>/bo/ -v q --nologo
```

(`-p:OutputPath=` for the sqlproj.) Compilation errors still surface; only the copy step is skipped.

### Tests

**xunit is the runner everywhere, on v2.** Two pins hold that together and both are commented
in every test csproj: **bunit stays on 1.40.0**, because bUnit 2.x moves to xunit v3 and
upgrading one project would split the runner across the solution; **FluentAssertions stays on
7.2.2**, because 8.0 moved to a licence that is not free for commercial use. NSubstitute
rather than Moq.

The split follows what each layer can be tested through:

- `SMDataManager.Library.Tests`, `StockApi.Tests` — plain unit tests with substituted
  dependencies.
- `SMStore.Tests`, `SMPortal.Tests` — bUnit. `SMStore` substitutes `SMDataManager.Library`;
  `SMPortal` substitutes `IAdminDataService`, which is the one seam every admin page binds to,
  so no HTTP is involved.
- `StockManager.E2ETests` — the real app host under `Aspire.Hosting.Testing`, driven with
  Playwright.

```bash
dotnet test StockManager.sln \
  --filter "FullyQualifiedName!~StockManager.E2ETests&FullyQualifiedName!~SMDesktopUI"
```

**That filter is what a routine run wants**, and it passes green; a further set needs a
database and skips without one. Both exclusions earn their place. `SMDesktopUI.UITests`
drives a real WPF window and needs an interactive desktop. And the E2E project is in the
solution, so a bare `dotnet test StockManager.sln` discovers it — on any machine that *does*
have a container runtime it will start SQL Server and run, slowly, rather than skip. It skips
only when neither `docker version` nor `podman version` answers, or when Playwright's Chromium
is missing.

**A bUnit JS interop mock must complete its call, not merely be set up.**
`JSInterop.SetupVoid("name", ...)` registers a handler and stops the strict-mode throw; the
task behind the call stays pending until something calls `.SetVoidResult()`. A component that
awaits that call therefore hangs forever, and a click that awaits the component hangs with it.
With collections running sequentially, one such hang stalls everything scheduled after it:
the runner eventually reports a crashed test host. One run printed
**31 green with 18 tests never executed**. A partial run that reports success is worse than a
red one, so treat "declared tests equals executed tests" as something to check rather than
assume — `--list-tests` gives the first number.

Some of the reasoning that makes those tests worth keeping is not visible from the assertions:
three of them are guards rather than behaviour checks. `DocumentListItem` must never gain a
`StoredName` property, `ContactListItem` must never gain `IdentityUserId`, and neither the
registration form nor the password-reset form may echo a password back into its own HTML. Each
is a leak that a later "simplification" would reintroduce silently, and each is asserted by
reflection or by searching the rendered markup because no ordinary assertion expresses "this
must stay absent". `PasswordResetServiceTests` is mostly the same shape: what it asserts is the
absence of a difference between a real address and an unknown one.

**A test project that fails to compile is dropped from a solution run, which still exits 0.**
Changing `IDistributorFeedSyncService.SyncAsync` broke `StockApi.Tests`, and
`dotnet test StockManager.sln --filter ...` printed two `error CS7036` lines, ran the other
three projects, printed three green `Passed!` lines and exited 0. Nothing in the summary said a
project was missing. So the count that matters is **one `Passed!` line per project — four for
the routine filter** — and the cheap guard is to build the solution before testing it:

```bash
dotnet build StockManager.sln -v q --nologo && dotnet test StockManager.sln --filter "..."
```

This is the same failure mode as the bUnit interop hang below, reached a different way: a
partial run that reports success. Assume neither the exit code nor a green line is evidence
about a project it does not name.

**Do not add an explicit `AngleSharp` `PackageReference` to a bUnit project.** bUnit 1.40.0
binds against the AngleSharp it ships with, and pinning 1.8.1 alongside it throws
`MissingMethodException: 'IHtmlCollection<T>.get_Item(Int32)'` from `FindAll(...)[i]` at run
time — three SMPortal tests failed that way and nothing failed to compile. `using
AngleSharp.Dom` works transitively; the package reference adds only the version conflict.

`StockManager.E2ETests/README.md` carries the rest: the Playwright install step and
`E2E_REQUIRE_APPHOST=1` for CI. **All seven journeys pass**, in about a minute against a warm
SQL container. The last two are the basket and the quote request, and they earn their place the
way the approval journey did.
A basket change is a form post plus a redirect, so the antiforgery token, the `Set-Cookie` and
the next request's lookup all have to hold at once for a single click to work. And a quote
request spans a basket built under a cookie, the merge that hands it to a contact at sign-in,
and one transaction that writes a quote and deletes the basket. No unit test spans either.

**The end-to-end suite earns its cost, and here is the evidence.** Its first complete run
found a bug that four unit-test projects could not: `Account.ApprovedBy` is a foreign key into
`dbo.User(UserId)`, and `AccountController` was recording `User.Identity.Name` — an email
address — so every approval died with SQL error 547 *after* telling the caller it had
succeeded. The unit test asserted the approver came from `Identity.Name`, which was precisely
the defect. A substituted data access layer cannot fail a foreign key.

```bash
dotnet test SMDesktopUI.UITests/SMDesktopUI.UITests.csproj
dotnet test SMDesktopUI.UITests/SMDesktopUI.UITests.csproj --filter "FullyQualifiedName~MyTest"
```

FlaUI drives a real WPF window, so these need an interactive desktop session. The app host registers
them as `desktop-ui-tests` with `WithExplicitStart()` — they run on demand from the dashboard, never
on launch.

`SMDataManager.Library.Tests` holds the tests that need real T-SQL: the parity check between
the net-price expression in `dbo.fnCatalog_VisibleProducts` and `PriceResolver` (the catalog
sorts on the SQL copy and displays the C# one, so this is the tripwire that makes that
duplication safe), and the staleness predicate in the same function, which has three-valued
logic in it and no failure mode that looks like an error.

**Every database-backed class belongs in `[Collection(DatabaseCollection.Name)]`.** They each
hold an open transaction while inserting a `Site`, a `SiteCategory`, a `CategoryMapping` and
products, and xUnit runs classes in parallel — so the moment a second such class existed, the
two deadlocked on those tables. One collection makes them sequential. Do not "fix" a deadlock
here by retrying: these are not concurrency tests, the contention is an artefact of the
fixtures, and a retry turns a deadlock into an intermittent pass. Classes that touch no
database must stay out of the collection, or the project loses parallelism for nothing.

They need a database — evaluating the SQL half has no other way, and a C# reimplementation
would be a third copy of the thing under test. Point `SMDATABASE_TEST_CONNECTION` at a
development database (the connection string is on the `sql` resource in the Aspire dashboard)
and note the host: **use `127.0.0.1`, not `localhost`** — the container publishes on IPv4 only
and `localhost` resolves to `::1` first, which fails as a connect timeout rather than
anything legible.

```bash
SMDATABASE_TEST_CONNECTION="Server=127.0.0.1,<port>;Database=SMDatabase;User Id=sa;Password=<pw>;TrustServerCertificate=True;Encrypt=False" \
  dotnet test SMDataManager.Library.Tests/SMDataManager.Library.Tests.csproj
```

Without that variable the tests skip rather than fail. Everything they write happens inside a
transaction that is never committed.

**A procedure's `ROLLBACK TRANSACTION` unwinds the test's rollback scope too.** T-SQL has no
nested transactions: a bare `ROLLBACK` goes back to the outermost `BEGIN`, which in these tests
is `TestDatabase.OpenRollbackScope`'s. So a test that provokes a transactional procedure's
`CATCH` — `spOrder_ConvertFromQuote` refusing a claim, for one — must do it **last in its scope**
and touch the connection no further; anything after it runs with `@@TRANCOUNT` at zero and would
commit. Validation errors raised before the procedure opens its transaction are safe anywhere,
and the two kinds are not distinguishable from the call site, so `QuoteAcceptanceTests` says
which is which.

Everything else in that project is a pure unit test and needs nothing.

`.claude/launch.json` has entries for `preview_start`. `sm-portal-standalone` runs the portal alone on
7250, which avoids fighting the app host for ports when iterating on UI.

## Data access

Every business query goes through `ISqlDataAccess`, naming a stored procedure and the connection
string `"SMDatabase"`. There is no EF for business data (the `dotnet-ef` tool exists only for ASP.NET
Identity).

```csharp
_sqlDataAccess.LoadData<QuoteModel, dynamic>("dbo.spQuote_GetByReference", new { Reference = reference }, "SMDatabase");
_sqlDataAccess.SaveData("dbo.spQuote_UpdateStatus", new { Id = quoteId, Status = status }, "SMDatabase");
```

**Output parameters are declared in the sprocs but never read.** `SaveData` passes an anonymous
object, which Dapper cannot write back through. Insert procedures therefore take `@Id int output`,
ignore it, and the caller re-queries for the newest row. To return a value from a mutation, `SELECT`
it and call `LoadData<int, dynamic>` instead — see `QuoteData.DeleteQuoteLine`.

Adding a stored procedure is one step: create the `.sql` file under
`SMDatabase/dbo/Store Procedures/` (or `SMDatabase/dbo/Functions/`, which holds
`fnSite_Resolve`). The SDK-style project globs `**/*.sql`, so there is no longer a
`<Build Include="..." />` list to keep in step — that was the old classic-SSDT footgun, where a
file left out of the project silently never deployed.

**The glob replaced that footgun with a quieter one: Visual Studio evaluates it when it loads the
project and does not rescan.** A `.sql` file created outside the IDE — by an agent, a git pull, a
branch switch — is missing from VS's item list, so a build there produces a DACPAC without it and
the app host deploys schema that silently lacks the new object. The build succeeds and the
staleness guard passes, because the DACPAC *is* newer than the sources. The symptom is a runtime
`Invalid object name` or `Could not find stored procedure` for something that plainly exists on
disk. Reload the project (or build with `dotnet build`, which evaluates the glob fresh) and
confirm before blaming the deploy:

```bash
dotnet build SMDatabase/SMDatabase.sqlproj && \
  unzip -p SMDatabase/bin/Debug/SMDatabase.dacpac model.xml | grep -c YourNewObject
```

**`-p:BaseOutputPath=` redirects the DACPAC too.** The scratch-directory trick for building
around a running app host applies to every project in the solution, `SMDatabase` included — so
`dotnet build StockManager.sln -p:BaseOutputPath=<scratch>` leaves `SMDatabase/bin/Debug/`
holding whatever was there before, and a `sqlpackage` publish from that path cheerfully
deploys the *previous* schema and reports success. The symptom is a procedure that does not
have the column you just added to it, in a database you just published to. Build the sqlproj
on its own before publishing:

```bash
dotnet build SMDatabase/SMDatabase.sqlproj    # no BaseOutputPath
```

**Hand-applying a procedure with `sqlcmd` needs `-I`.** `sqlcmd` defaults `QUOTED_IDENTIFIER`
off, and SQL Server bakes the session's SET options into a procedure at creation time. A
procedure created without it throws `INSERT failed because the following SET options have
incorrect settings` the first time it writes to a table carrying a filtered index — which now
means `Contact` and `Address`. The symptom points at the insert, not at how the procedure was
deployed, and `sqlpackage` gets it right, so this only bites when working around a running app
host. Prefer republishing the DACPAC:

```bash
sqlpackage /Action:Publish /SourceFile:SMDatabase/bin/Debug/SMDatabase.dacpac \
  /TargetConnectionString:"..." /p:BlockOnPossibleDataLoss=false
```

`Scripts/PostDeployment/Seed.sql` runs on **every** publish, so everything in it must be
idempotent. Its job is to guarantee a `dbo.Site` row exists — multi-store scoping means a
database with no site renders nothing — and to backfill `SiteId` on rows that predate it. The
site it seeds is deliberately generic; a real store is inserted as tenant configuration, not
baked into the platform schema.

### `dbo.Purchase` does double duty

`Reference IS NULL` is a desktop POS sale. `Reference IS NOT NULL` is a portal sales order, and only
then are `AccountId`, `QuoteId`, `Currency` and `Status` populated. Every `spOrder_*` procedure filters
on `Reference IS NOT NULL` to keep the two apart — preserve that predicate in anything new, or admin
writes will reach POS history.

Portal orders never touch `dbo.Inventory`; only `spInventory_Insert` writes it. Order-level changes
therefore have no stock side effects, but they do move `spReport_GetSales` and `spActivity_GetRecent`.

**The predicate runs both ways.** `spPurchase_PurchaseReport` — the desktop POS's own report —
filters `Reference IS NULL`. Without that it returns portal orders too, attributed to
whichever admin converted them, and it matters more now `StaffId` is nullable: the report
inner-joins `dbo.[User]`, so a customer-accepted order would drop out of it by accident
rather than on purpose.

### An order records exactly one placer

`Purchase.StaffId` is nullable from T5 and `Purchase.PlacedByContactId` exists beside it, because
`dbo.[User]` holds staff and a customer is a `dbo.Contact`. A customer accepting their own quote
has nothing to put in `StaffId`, and an id-shaped string satisfies the compiler and fails
`FK_Purchase_ToUser` — after the caller has been told it succeeded. That is the third time this
shape has come up here, after `Account.ApprovedBy` in T3 and a test fixture in T4.

- `CK_Purchase_Placer` demands exactly one of the two, on every row. A POS sale has `StaffId`;
  making that column nullable removed the only thing stopping a row with no placer at all.
- `QuoteAcceptance.ByStaff` / `.ByCustomer` are the only ways to build one in C#, so "both" and
  "neither" are unreachable rather than merely wrong.
- `FK_Purchase_ToContact` is composite over `(PlacedByContactId, AccountId)` — which is why
  `UQ_Contact_IdAccount` exists — so another company's buyer cannot place this account's order.
  The procedure says so first, because a foreign-key violation raised inside a transaction
  reaches the API as a 500 rather than as an answer.
- `Purchase.PoNumber` holds the customer's own purchase-order number, captured at acceptance.
  On the order and not on the quote: a quote that was never accepted has none, and holding it
  twice is holding a value that can disagree with itself.

### A quote is claimed before it is converted, and a claim refused is not a failure

`spOrder_ConvertFromQuote` opens its transaction with one atomic `UPDATE` moving the quote to
`Accepted` only while it still reads `Requested` or `Priced`, and throws 50010 on a rowcount of
zero. Before T5 that `UPDATE` sat at the end with no predicate on the current status, so two
callers produced **two orders from one quote** — each with its own `SO-` reference and its own
copy of every line. Reachable then by double-clicking Convert; ordinary once a customer has an
Accept button and both paths land in the same procedure.

- `UQ_Purchase_QuoteId` is the backstop, filtered to `WHERE QuoteId IS NOT NULL` because POS rows
  leave it NULL and SQL Server treats NULL as one distinct value in a unique index.
- **One procedure, not two.** The claim lives here rather than in a separate customer-facing
  accept, because two procedures over one invariant are two guards that drift. Both `Requested`
  and `Priced` pass: the procedure's job is to stop a double conversion, not to decide who may
  convert when. A customer may only accept a *priced* quote, and that rule belongs on the
  customer path where it can be answered with a page.
- A refused claim is a 409 and a third toast, not a failure. `QuoteAcceptanceResult`
  distinguishes it from `Succeeded == false` the whole way out, exactly as
  `DistributorFeedResult.AlreadyRunning` does — an operator told "that failed" about a customer
  accepting their own quote learns to discount the message that matters.
- `OrderData` reads the new order back with `spOrder_GetByQuote`, not as the store's newest
  order. Under two conversions at once, "newest" is the other caller's.
- `CK_Quote_Status` enforces `Requested | Priced | Accepted | Rejected`, which the column had
  carried in a comment since it was written. `QuoteStatus` names them in the library and
  `QuoteController` refuses a fifth with a 400, because a constraint violation raised inside a
  procedure reaches the caller as a 500.

## Auth

`StockApi` runs two schemes and picks per request: a bearer token means JWT, no bearer token means the
Identity application cookie for the Razor UI. `SMPortal` and the WPF app `POST /token`
(`TokenController`) and hold the JWT in local storage under the key from
`SMPortal/wwwroot/appsettings.json`. Admin API controllers are `[Authorize(Roles = "Admin")]`.

`AdminDataService.EnsureAuthHeaderAsync` reads the token straight from storage rather than trusting
`AuthStateProvider` to have set it — on a full page reload the service can run first, and every call
401s otherwise.

## SMStore architecture

Static SSR by default. There is no interactive render mode on any page yet, and adding one
should be a deliberate decision about a specific island rather than a reflex — the mobile menu
is a CSS-only disclosure (hidden checkbox plus a sibling selector) precisely to avoid a circuit
on every page.

The design system came from the Template artboards' `_ds/styles.css` and is split in two:

- **`wwwroot/app.css`** holds the system — blueprint frame, `.btn`, `.card`, `.input`, `.tag`,
  `.table`, `.dialog`, the type scale, `.container` / `.page`, `.breadcrumb`, `.product-grid`,
  `.stub`. It defines **no** colour, space, radius or font. A token added here must be added to
  every theme.
- **`wwwroot/sites/{SiteKey}/theme.css`** holds the `:root` token block and the webfont import.
  A palette and a typeface are brand, not platform.

Component-specific rules live in scoped `.razor.css`. A class used by more than one component
belongs in `app.css` instead — scoped CSS does not reach another component's markup, which is
why `.product-grid` is global. `::deep` reaches markup a child renders, as `FormField` needs to
style the caller's own `.input`.

Themes supply `theme.css`, `logo.svg`, `logo-inverse.svg` and `favicon.svg`.
`SiteThemeResolver` falls back to the `default` theme per missing file. The inverse logo exists
because the footer is painted in `--color-accent-900` and a dark mark disappears into it.

`/_design` renders every primitive against the current theme. Development only — it 404s
elsewhere — and it is the quickest way to see whether a token or component change broke
something.

Two conventions worth keeping:

- **Unfinished actions disable themselves**, and the control goes in before the behaviour
  does. `Basket.razor`'s submit button is the current example: disabled with a title saying
  why, so the control a customer will use is already where they will look for it.

  Disable it with a stated reason rather than wiring an `EventCallback` that cannot fire:
  static SSR has no circuit, so an `@onclick` never arrives. `ProductCard`'s add button is a
  form posting to `BasketEndpoints.AddPath` for that reason. Prefer a form over an event
  callback anywhere a customer is not already on an interactive island.
- **Nothing store-specific belongs in a component.** Navigation is assembled in
  `StoreNavigation`; editorial copy resolves through `ISiteContentSource` from
  `dbo.SiteContent`, and a store with no row gets an explicit empty state rather than another
  store's words. `SiteContent.BodyHtml` renders as `MarkupString`, so rows are staff-authored
  only — nothing originating with a customer may reach that column.

The basket page carries both `@page "/quote"` and `@page "/cart"`; which one a store links to
comes from `OrderingModeProvider`, so neither route 404s and no markup branches on `OrderMode`.

### Catalog and pricing

Pages never touch `ICatalogData` or `IPriceResolver`. Both go through `CatalogPresenter`,
because resolving a price and deciding whether to show one at all are the two things most
easily got subtly wrong in a page.

`spCatalog_Search` decides visibility inside the query rather than filtering afterwards — a
product removed from an already-fetched page has still been counted, still shifted the paging,
and has usually already reached the browser. Ranking is a literal ladder (exact SKU, exact MPN,
prefix, contains, description) because the dev SQL Server reports `IsFullTextInstalled = 0`, so
`CONTAINSTABLE` is unavailable. It is deliberately confined to that one procedure, so adopting
full text later is a change in one place.

Three hazards in the query path, all of which have already bitten:

- **A query-string parameter bound as `int?` is a 500 waiting to happen.** Blazor converts the
  value during binding and throws on anything it cannot parse, before any of your validation
  runs — `?page=abc`, or any number past `int.MaxValue`. Bind as `string` and parse. `Catalog`
  is the only page with a numeric query parameter; keep it that way.
- **Clamp page numbers in the procedure as well.** `(@Page - 1) * @PageSize` is int arithmetic,
  and an unbounded page number overflows it into an unauthenticated 500.
- **An empty page carries no total.** The count rides on the rows via `COUNT(*) OVER ()`, so a
  page past the end reports zero matches for a category that is full and drops the pager
  entirely. `CatalogPresenter` re-queries to recover the total and lands on the last page.

**The sort key and the displayed price are two implementations of one rule, and that is
deliberate.** `CatalogPresenter.CustomerGroupId` now reads the signed-in session, so the group
discount is real and the margin floor in `PriceResolver` can bind. A discount alone would not
have broken `ORDER BY RetailPrice` — `net = list × (1 − d)` is monotone in `list` — but the
floor introduces `cost`, which varies independently, so a thin-margin row floors upward and
jumps position. `dbo.fnCatalog_VisibleProducts` therefore computes the same expression and the
procedure sorts on it.

Three rules keep that duplication safe, and none is optional:

- **SQL computes an ordering key; `PriceResolver` computes the price a customer is shown.**
  `spCatalog_Search` does not project `NetPrice`. Never render a price that came out of it.
- **The group's discount and the site's margin are resolved inside the procedure**, never
  passed in. A caller that could pass a discount is a caller that could ask for 90% off, and
  a `@CustomerGroupId` that does not belong to `@SiteId` degrades to no group rather than
  erroring — a tampered cookie lands on list price, not on another store's rates.
- **`CatalogPriceParityTests` is the tripwire.** It drives a matrix of (list, cost, discount,
  margin) through both implementations and asserts they agree to the cent. It is the reason
  this duplication is allowed to exist; do not let it rot.
- **And the price a customer was shown is the price that gets stored.** `spQuoteLine_Insert`
  takes an optional `@NetPrice` and derives one only when it is absent, so the storefront
  records what `CatalogPresenter` rendered while the admin portal keeps typing a list price
  and a discount. Deriving it unconditionally was wrong twice over: a price held up by
  `Site.MinMarginPct` has no integer discount that reproduces it, and the old expression did
  no rounding at all, so 99.99 at 7% stored 92.9907 — `money` carries four decimal places
  and that number reached the quote document. `QuoteLine.DiscountPct` is `DECIMAL(5, 2)` for
  the same reason. `QuoteLinePriceTests` pins the derived path to `PriceResolver` the way
  `CatalogPriceParityTests` pins the sort key.

`CatalogItemModel.Cost` is a buy price and is currently selected on every catalog row.
`ProductCardView` excludes it; the detail page binds the raw model, so it is one field
reference away from publishing margin on a public page. Do not add it to a view record, and
prefer removing it from the projection over relying on review.

### The basket

**`dbo.Basket` is its own table and not a `Quote` with a draft status.** `Quote.AccountId` is
NOT NULL with a composite key to `Account`, so an anonymous visitor cannot have one at all;
`Quote.Reference` comes from a sequence, so every abandoned basket would burn a `QT-` number;
and `spQuote_GetAll` and `spActivity_GetRecent` would both show a request nobody made, which
means a `Status <> 'Draft'` predicate in every query over that table for ever. That is
`dbo.Purchase`'s double duty again.

**A basket is found by its contact when somebody is signed in and by a token in a cookie when
nobody is.** Not `localStorage`: this storefront is static SSR with no JavaScript of its own,
so client storage would mean writing some, plus a render round trip, plus a handoff at sign-in
where the browser posts product ids and prices the server has to distrust anyway. It would also
put the prices somewhere the customer can edit. `BasketService` holds all of it, so no page or
endpoint reasons about cookies and contacts together.

- **The token is a bearer credential, so it is 256 bits from a cryptographic RNG.** Whoever
  holds it holds the basket. `BasketToken.IsWellFormed` rejects anything that is not 43
  base64url characters before it reaches a query, and the cookie is `HttpOnly`, `Secure`,
  `SameSite=Lax` and essential. Lax rather than Strict because a customer arriving from an
  emailed link is on a cross-site navigation, and Strict would hide their basket on exactly
  the page they land on.
- **Reading never creates.** A basket row exists once something has been added, so a crawler
  walking the catalog leaves nothing behind. Only `spBasket_Ensure` creates, and only an add
  calls it.
- **`spBasket_Find` refuses to return a claimed basket to an anonymous caller.** A claimed
  basket keeps its token, so the cookie left after sign-out names a basket that now belongs to
  a customer, and the next person at a shared machine would be shown that list.
  `BasketService.Forget` clears the cookie as well; neither half is sufficient alone. For the
  same reason `spBasket_Ensure` never adopts a token's basket for a signed-in contact —
  adopting is `spBasket_Claim`'s job, which runs once, at sign-in, and merges.
- **The merge keeps the contact's basket, not the browser's**, so its id is stable across
  browsers and a second sign-in is a no-op. Quantities add where both hold the same product,
  and the source basket is deleted in the same transaction: left behind, it would be merged
  again on the next sign-in and double what it contributed.
- **`spBasket_AddLine` checks the product through `dbo.fnCatalog_VisibleProducts`.** A
  `ProductId` arrives in a form post, so the page it came from is not evidence, and without
  the check a basket could be filled with another tenant's catalog and then quoted from it.
  Going through the function means "available" means here what it means on the listing, the
  facet rail and the detail page.
- **A line whose product later vanishes comes back with `Available = 0`, never dropped.** A
  basket that silently loses rows is one the customer cannot reason about, and the submit path
  has to be able to refuse the line explicitly rather than never learn it existed.
- **`BasketLine` holds no price.** The price is resolved at submit, through `PriceResolver`
  like every other. A price stored on a basket line is a price the customer keeps while the
  catalog moves under it.
- Quantities are capped at 9999 in the procedures, because `Quantity * NetPrice` is money
  arithmetic and `int.MaxValue` of anything overflows a line total.
- **Nothing sweeps abandoned anonymous baskets, and that is deliberate.** A row per visitor
  who adds something, robots included, is the cost of the cookie. The sweep is **T7's**, with
  the hosting decision that says where a scheduled job runs: written now it would have been
  a procedure nothing calls, which is what `spProduct_SyncFeeds` was.
  `Basket.UpdatedUtc` is maintained by every write so it has something to key off.
- **Removal is `SetQuantity` with a quantity of zero**, not a procedure of its own — that is
  what a customer typing 0 into the box means, and a second name over the same `DELETE` is
  two things to keep in step.

**Every basket change is a form post, because static SSR has no circuit for an event to
arrive on.** `BasketEndpoints` takes them, validates antiforgery, and redirects to a local
path only — `returnUrl` arrives in a post, so honouring it as given would make every basket
button an open redirect. An add returns to the listing it came from rather than to the
  basket, or browsing becomes a sequence of back buttons.

- **The forms post a SKU, never a product id.** No database id appears in storefront markup,
  and resolving the SKU through `spCatalog_GetBySku` is the first of two visibility checks:
  `spBasket_AddLine` asks the same question again through the same function. A form post says
  nothing about the page it came from, so one check is the minimum and two cost one indexed
  read on a path nobody clicks in a loop.
- **A quantity change resolves its SKU against the basket, not the catalog.** A line whose
  product has since been delisted must still be removable, and `spCatalog_GetBySku` would no
  longer return it.
- **`BasketPresenter` is the basket's `CatalogPresenter`**, and prices go through
  `CatalogPresenter.Resolve` so the basket cannot disagree with the page the customer added
  from. `BasketLineView` has no `Cost` and no `ProductId`: the first is a buy price and this
  is the boundary that keeps it off a public page, the second is what lets the forms post a
  SKU. An unavailable line is rendered and excluded from the value.
- **`FakeCheckoutMode` is what makes `IOrderingMode` a claim rather than a hope.**
  `RfqOrderingMode` is the only mode the platform ships, so every other storefront test
  renders the same words and a page that hard-coded "quote" would pass all of them.
  `BasketPageTests` renders the basket twice under two modes and asserts the RFQ markup says
  "quote" and "Indicative value" while the checkout markup says "cart" and "Total". Add to
  that test when a page gains customer-facing wording.

### Submitting a quote request

**`spQuote_SubmitRequest` writes the quote, its lines and the deletion of the basket in one
transaction.** The halfway states are all wrong: a quote with no lines is a request sales
cannot answer, lines with no quote are orphans, and a basket left full after a successful
submit sits there inviting the customer to send the same request again. The lines arrive as
`dbo.QuoteRequestLine` — a basket is small, but a per-line round trip inside a transaction
holds it open across the network for as many turns as the customer has products.

- **The account is derived from the contact and the currency from the account.** Neither is
  accepted from the caller: a session proves a contact, and everything else follows from it.
- **Prices are resolved at submit, through `CatalogPresenter`, never posted by the browser.**
  A price in a form is a client's opinion about what things cost, and the basket page may
  have been open for hours. The resolved *effective* discount is what gets stored, which is
  not the group's rate whenever the margin floor bound — see `QuoteLine.DiscountPct`.
- **An unavailable line is dropped and named, never dropped silently.** The basket page has
  already warned about it, and a submit that refuses until the customer tidies up puts the
  store's supply problem in their way at the moment they were ready to buy. A basket where
  nothing can be supplied produces no quote and says so.
- **`spQuote_SubmitRequest` re-checks that every product is sold by this store**, through
  `CategoryMapping`, even though the caller resolved the lines through
  `fnCatalog_VisibleProducts` and `spBasket_AddLine` refused anything else. One atomic write
  is checked on its own terms. The basket `DELETE` is scoped to the site *and* the contact
  for the same reason: `@BasketId` arrives from the caller.
- **Submitting needs a session, and `IOrderingMode.RequiresApprovedAccount` is currently
  unreachable.** It reads false for RFQ, meaning a store may take a request from an
  unapproved account. Nothing can reach an unapproved session: `CustomerAuthEndpoints` and
  `CustomerSessionValidator` both admit Approved accounts only. The property stays because a
  store that wants a pending applicant to ask for a price will change the sign-in gate, not
  the submit. An anonymous submit redirects to sign-in and the basket follows, through
  `spBasket_Claim`.
- **The acknowledgement page shows the reference and nothing else.** References come from a
  sequence and are guessable, so a page that rendered lines or prices from a `?ref=` would be
  readable by anyone who changed a digit. The account-scoped view is the account area's.
- **Submitting is rate limited even though it is authenticated** — the only path here that
  is. One submit writes a quote and a line per product, burns a `QT-` number, and lands in a
  queue a human works through.

### A reference is not an authorisation

**Every customer-facing read of a quote or an order carries the account in its predicate as
well as the site.** References come from `dbo.QuoteReferenceSequence` and
`dbo.OrderReferenceSequence` and read `QT-0041` and `SO-0012`, so scoping by site alone — which
is right for an admin, who may see every quote in their store, and is what
`spQuote_GetByReference` does — would let any signed-in customer read any other customer's
lines and prices by changing a digit. Hence a second set of procedures rather than a parameter
on the first: one shared procedure means one caller passing NULL for the predicate that protects
the other.

- `spQuote_GetByAccount`, `spQuote_GetForAccount`, `spQuoteLine_GetForAccount`, and the three
  `spOrder_*` equivalents. **The line reads repeat the predicate** rather than relying on the
  caller having resolved the parent first: defence that depends on call order survives until
  somebody adds a second caller.
- **"Not yours" and "not here" are one answer.** Empty, and the pages render the same "not
  found" for both, for the reason `AccountDocumentController` answers 404 rather than 403.
- The account comes from `ICustomerContext`, never from a route. Same rule as the absent
  `/account/{id}`, applied to documents that carry prices.
- `CustomerDocumentScopeTests` is what says the predicates are there. It has no failure mode
  that looks like an error: leave the account out and every other test still passes, and the
  only symptom is that the wrong person can read a price.

### What a customer may decide

**`QuoteDecisionService` requires a quote to be `Priced` and unexpired, and
`spOrder_ConvertFromQuote` does not.** That is deliberate rather than an inconsistency: the
procedure's job is to stop a double conversion, and an admin converting an unpriced quote
because the customer rang up is a deliberate act. A customer accepting a price nobody has set
is not.

- **Expiry blocks.** `ExpiresDate` is a statement the store already made in writing, and
  honouring it past its date is the store's choice rather than a button's. A quote with no
  `ExpiresDate` is decidable — no date means the store did not set one, not that it has
  passed. **The delisted-line warning is the other half of that rule and is not built**:
  nothing on `DocumentLineView` carries availability, so the customer is not told and the
  order does not record it. A delisted line does not block a decision either way.
- **An acceptance records `PlacedByContactId` and no `StaffId`**, through the same procedure the
  admin's Convert button uses. One procedure, one guard.
- **A rejection requires a reason**, in the procedure as well as on the page, for the reason
  `spAccount_Reject` requires one. It is a claim like the accept — one atomic `UPDATE` over the
  two undecided statuses — so a reject racing an accept cannot both succeed, and a rowcount of
  zero means a colleague decided it first, which for a company with two buyers is an ordinary
  Tuesday rather than a fault.
- **`Quote.CustomerNote` and `Quote.RejectedReason` originate with a customer**, so the
  `SiteContent.BodyHtml` rule applies in reverse: neither may ever reach a `MarkupString`.
  `AccountDocumentPageTests` asserts the note is escaped.
- **The message after a decision comes from an allow-list.** `?decision=` arrives in a URL
  anyone can write, inside a session; echoing it would put attacker-chosen text on a page beside
  the customer's own prices. Same rule as the basket's `?basket=` notices.

## Distributor feeds and image enrichment

A feed sync imports thousands of rows, so anything per-product and outbound happens out of
band. `DistributorFeedSyncService` does the import; `ProductImageBackgroundService` fills in
images afterwards, a batch at a time.

- **A finished sync signals the worker through `IImageEnrichmentSignal`** rather than leaving
  new products to wait out the idle sweep. The signal is a flag plus one replaceable
  `TaskCompletionSource`, **not** a `SemaphoreSlim` — the worker waits on it with `WhenAny`
  against a timer, and a semaphore strands the losing `WaitAsync` on its queue, where the next
  `Release()` feeds an orphan instead of the live waiter. That bug made Sync work exactly once
  per process. The `WhenAny` loser is cancelled through a linked token source, not abandoned.
- **An `HttpClient` timeout is an `OperationCanceledException` too.** Every catch around
  outbound work must filter on `stoppingToken.IsCancellationRequested`, or one slow response
  ends the worker silently for the life of the process.
- **`Considered` counts candidates fetched, not progressed.** Only a lookup that reaches a
  verdict stamps `ImageLookupUtc` and drops the row from the candidate set, so a batch where
  every call threw leaves the same rows waiting. Progress is `Failed < Considered`; a batch
  that clears nothing backs off by doubling rather than retrying at the batch pause.
- **`dbo.BrandAlias` translates feed brand vocabulary for the content provider.** A feed files
  HP Inc. under `HPINC`, which matches nothing at Icecat; `UNIVERSAL` is not a manufacturer at
  all, and a NULL `IcecatBrand` suppresses the brand lookup so only the EAN fallback runs. An
  unmapped brand passes through unchanged, because most of them are already right.
- **`BrandAliasResolver` is a singleton and takes a loader delegate, not its data access.** A
  singleton's factory closes over the *root* provider, so resolving a transient `IDisposable`
  from it leaks one per refresh for the life of the host. The composition root scopes each
  refresh instead.

**A feed is claimed before it is fetched, and a claim refused is not a failure.**
`spDistributorFeed_ClaimForSync` sets `DistributorFeed.SyncStartedUtc` in one atomic `UPDATE`
whose `WHERE` and `SET` share a row lock, so two callers arriving together cannot both take it;
`spDistributorFeed_RecordSync` releases it, writes `dbo.DistributorFeedSyncLog` and stamps the
feed's last-run fields in one transaction. The claim expires on an hour lease, because a host
killed mid-sync would otherwise take that feed out of service permanently and silently.
`DistributorFeedResult.AlreadyRunning` is distinct from `Succeeded = false` all the way out to
a 409 and a different toast — once feeds sync nightly, an operator pressing Sync in that window
is the ordinary case, and a red banner there teaches them to ignore the one that matters.

**The nightly sync is `DistributorFeedSyncBackgroundService`, in `StockApi`, off by default.**
`Feeds:SyncEnabled` and `Feeds:SyncAtUtc` (one time of day, UTC, every store). It lives in
`StockApi` because that host holds the Data Protection ring that decrypts `SecretRef`, it loops
active sites so a second country is a `Site` row rather than new sync code, and a host that
starts after the hour waits for tomorrow — otherwise a restart loop re-imports every feed.
Nothing guards against two replicas because the claim already does.

**Stale stock hides from the storefront when a store asks, and `Delisted` is a different
thing.** Delisted means the distributor said it no longer supplies the product; stale means the
distributor has said nothing at all for longer than `Site.FeedStaleAfterHours`, so the quantity
and price on the row are whatever they were when the file last arrived. `Site.HideStaleProducts`
turns hiding on, both default to off, and `dbo.fnSite_StaleBeforeUtc` resolves the cutoff.

- **It is resolved into a variable and passed in, never called per row.**
  `fnCatalog_VisibleProducts` is inline and cannot `DECLARE`, so `@StaleBeforeUtc` arrives
  pre-computed exactly as `@DiscountPct` does — and a scalar function in a `WHERE` runs per row
  and defeats the plan. All three callers (`spCatalog_Search`, `spCatalog_GetFacets`,
  `spCatalog_GetBySku`) must resolve the same value, or the facet counts disagree with the page
  they filter to, which is the bug that function was extracted to kill.
- **Own stock is never stale, and that needs saying twice.** `Source` is nullable, so
  `Source <> 'Distributor'` is UNKNOWN for a row that predates the column — the predicate names
  `IS NULL` explicitly. A plain `LastSynced >= @cutoff` hides the whole own-brand catalog the
  first time a store sets a threshold.
**An operator is told twice or not at all.** `FeedAlertService` mails
`Site.OperatorEmail` — the push half — and `/admin/feeds` renders two banners plus a per-feed
history modal, which is the pull half. Neither is enough alone: nobody watches a screen at
02:00, and a mail about a failure that has since been fixed is worse than no mail.

- **Only the scheduler alerts.** An operator who pressed Sync is reading the toast, and mailing
  them about a failure they are already looking at is how a channel stops being believed.
- **A failure mails on the transition, not on every failing run.** `IsNewFailure` reads the
  second row of `DistributorFeedSyncLog` — seven identical mails get the eighth filtered, and
  that filter is still in place when the next real failure happens. A history it cannot read
  counts as new: one message too many beats silence.
- **Staleness is a separate alert because it is a separate condition.** A feed nobody attempted
  — scheduler off, host down — leaves nothing in the history to fail.
  `spDistributorFeed_GetStale` keys off `FeedStaleAfterHours` alone, not `HideStaleProducts`: a
  store that keeps selling while it chases the distributor still wants the mail. A feed that
  failed in this pass is excluded, or a repeat failure silenced on one path would mail on the
  other.
- **`Site.OperatorEmail` has no platform-wide fallback.** The message names this store's
  distributor and quotes its status text; delivering that to another tenant's operator because
  a column was blank would be a disclosure. NULL logs at Warning instead.
- **`Site` has no admin screen**, so `OperatorEmail`, `FeedStaleAfterHours`,
  `HideStaleProducts`, `MinMarginPct` and `PriceDisplay` are all set by updating the row. That
  is a gap, not a design: a tenant cannot configure its own staleness policy without database
  access.

**Delisting is flagged, never deleted, and `DelistedProductHistoryTests` is what holds that.**
A product the distributor dropped still resolves on the quote that already contains it —
`spQuoteLine_GetByQuote` joins `Product` with no `Delisted` predicate, and
`spOrder_ConvertFromQuote` copies its lines with none either. That is one well-meant
`AND p.Delisted = 0` away from a customer opening a six-month-old quote and finding blank
lines, and the edit would read as a tightening rather than a regression. The staleness
predicate must not reach a quote line for the same reason: hiding stock from the shop window
is not the same as withdrawing a price already quoted.

- **Expiry blocks, delisting warns.** A delisted line does not stop an acceptance: the price
  was quoted, and withdrawing it at that moment pushes a supply problem the store owns onto
  the customer, who would otherwise be stuck behind a button that cannot succeed until
  somebody notices. The line is flagged to the customer and on the order instead. Both gates
  belong on the customer accept path rather than in `spOrder_ConvertFromQuote`, which an
  admin uses deliberately. See **What a customer may decide** for which half is built.

- **`spProduct_SyncFeeds` was deleted, not kept for later.** It stamped
  `LastSynced = SYSUTCDATETIME()` on every distributor-sourced product **without fetching
  anything** — a placeholder from before the feed client existed, called by nothing since
  `Catalog/Sync` became real. With staleness live it would mark the entire catalog fresh while
  making the data no newer.

**Icecat's free tier covers 3.8% of this catalog** — a full pass over 1,676 live products
matched 63. The feed carries an Icecat id but no image URLs. Treat images as an unsolved
sourcing problem, not a code problem, and do not build storefront features that assume a
product has one.

## SMPortal architecture

`IAdminDataService` is the single seam the admin pages bind to. Its **reads are synchronous** against
an in-memory snapshot that `EnsureLoadedAsync` fills, which `AdminLayout` awaits before rendering.
Mutations are `Task`-returning (a WASM client cannot block on HTTP) and end with `RefreshAsync()`.

Consequence: after any mutation, re-read the entity from the service — the object captured on page load
is stale. Detail pages do this in a `Recalc()` that starts with `_q = Data.GetQuote(Id)`.

The UI models key on human references (`"QT-0041"`, `"SO-0012"`, SKUs), not database ints. Mutations
resolve those to ids through `_accountIds` / `_groupSlugs` lookups or by re-fetching the catalog.
When a UI model needs a database id — as `QuoteLine.LineId` does so a row can be deleted — it has to
be added to the model *and* to the `Map*` projection.

**`GetContacts`, `GetAddresses` and `GetDocuments` are the exceptions to the synchronous
rule.** They are per account and fetched on opening one, because a reviewer opens one
application at a time and pulling every customer's staff list and paperwork into the snapshot
would be the wrong trade. A page using them needs `OnParametersSetAsync`, not
`OnParametersSet`.

**`ProductDetail`'s category dropdown is a hardcoded seven-item list and does not know what
decides storefront visibility.** `_cats` is `Servers, Networking, Laptops, Components,
Security, Power, Peripherals`, with no relationship to `dbo.CategoryMapping` or
`dbo.SiteCategory` — and it is the mapping, joined in `fnCatalog_VisibleProducts`, that
decides whether a product appears on a storefront at all. Saving that form on a product whose
real `Category` string is not one of the seven silently rewrites it to one that may have no
mapping, and the product disappears from every store with no warning anywhere. Do not add
features on top of that control; it needs to read the site's own taxonomy first.

**A fresh deployment has no admin and no way to make one.** `POST /api/User/Admin/AddRole` is
`[Authorize(Roles = "Admin")]`, and the only anonymous endpoint, `POST /api/User/Register`,
grants no role. Standing up tenant number two therefore means someone writing an
`AspNetUserRoles` row by hand. `StockManager.E2ETests` bootstraps its operator directly
against `ApiAuthDb` for exactly this reason — that is a workaround for a gap, not a pattern to
copy, and the gap should close before a second store ships.

**Nothing in the portal may invent data.** `AccountDetail` used to fabricate contacts, three
documents and an approval timeline — plausible-looking panels of nothing, rendered beside the
button that opens a company's credit account. Panels now render what the API returned or an
explicit empty state. A stub belongs behind a "lands with T5" note, never disguised as a
record.

### Blazor traps that have already cost time here

- **`Value="_search"` on a `string` parameter passes the literal text `"_search"`.** The `@` is not
  optional: write `Value="@_search"`. This was live at 8 call sites and made `SearchBox`'s parent-sync
  logic permanently dead.
- **Dirty inputs stop following the `value` attribute.** Once a user types, Blazor writing the
  attribute no longer updates the DOM property, so setting text in code appears to do nothing. Force a
  new element with `@key` — see `_textStamp` in `Shared/Admin/Combo.razor`.
- **One UI thread.** Fuzzy search over the catalogue blocks paint. The pattern is: raise the busy flag,
  `StateHasChanged()`, `await Task.Delay(80)` (16ms is not enough for a frame), *then* apply the term
  and do the work. Results are memoised in `SearchCache`.
- **Rebuilding SMPortal under a running dev server breaks SRI**: `Failed to find a valid digest in the
  'integrity' attribute`. Stop and restart the preview after a build.
- **CSS order:** `index.html` loads `app.css` then `admin.css`, so admin wins equal-specificity ties.
  Scoped `.razor.css` gains a `[b-xxxxx]` attribute and outranks both — use it to beat a global rule.
  `::deep` is needed to reach markup a child component renders (e.g. `NavLink`'s own `<a>`).

## Conventions

Comments in this codebase explain **why**, not what — usually the constraint or the bug that forced
the shape of the code. Match that; a comment restating the line below it does not belong.

Search is `FuzzySearch.Rank(source, term, textFields, codeFields)`. It runs literal matching first and
only falls back to FuzzySharp for a single-token term. **Code fields (SKU, MPN, EAN) get no fuzzy
pass** — same-shaped sibling identifiers scored above threshold and returned unrelated rows.

From `.github/copilot-instructions.md`:

- Keep the Aspire setup Azure-ready (publishes as Azure Container Apps) but **do not deploy to Azure**.
- Keep local orchestration on a persistent SQL Server container for both databases.
- Keep `StockApi` on HTTPS port 7042 — the Blazor and WPF clients depend on it.
