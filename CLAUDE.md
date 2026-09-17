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
- `docs/plans/2026-09-10-storefront-implementation-plan.md` — the build plan and current state

**The template is finished before the first tenant is built.** The plan runs two tracks, T0–T7
for the platform and A0–A4 for aclitrade.ie, and the platform track goes first in full. A
tenant built alongside an unfinished template is how store-specific assumptions get into shared
code, and the whole point of this exercise is that store number two costs days rather than
months. T0, T1 and T2 are merged. T3 — customer identity, per
`docs/plans/2026-09-14-t3-customer-identity.md` — is built: admin site scoping, the pricing
decision, the schema, registration field sets, `spAccount_Register`, storefront sign-in,
document upload, the mail seam, the approval flow, the account area, email confirmation and
password reset. T4 is next.

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
| `SMDataManager.Library.Tests` | xunit + FluentAssertions + NSubstitute. Pricing, and that a `siteId` reaches the procedure. The parity check needs a database and skips without one |
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

**The schema is published in process, on the database's `ResourceReadyEvent`.** It used to be
a `CommunityToolkit` `AddSqlProject` resource that `stock-api` and `sm-store` both
`WaitForCompletion`'d — and that resource never ran. It reported one `Waiting` snapshot and
never another, so everything waiting on it waited for ever and **`dotnet run` could not start
the apps at all**. Five explanations were checked and none held: the `ExplicitStartupAnnotation`
the toolkit adds (stripping it changes nothing, and so does leaving it in), the resource's own
`WaitAnnotation`s (removing every one changes nothing), `WithSkipWhenDeployed`, the Azure
container app environment, and upgrading Aspire and the toolkit together to 13.5. The DACPAC
was never at fault — `sqlpackage` applies it to the same container in about ten seconds.

So `AppHost.cs` calls `DacServices.Deploy` directly and the toolkit package is gone. Aspire
awaits `ResourceReadyEvent` subscribers before dependents that `WaitFor` the resource proceed,
which is what makes a plain `WaitFor(stockDatabase)` sufficient and is why
`WaitForCompletion(smSchema)` is no longer on either app. Two things went with it: the
dashboard row for the deploy, and the skip-when-unchanged fast path. Ten seconds a launch is
cheaper than an application that will not start.

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

**That filter is what a routine run wants**, and it passes: 251 tests, plus the 2 pricing
parity checks that skip without a database. Both exclusions earn their place. `SMDesktopUI.UITests`
drives a real WPF window and needs an interactive desktop. And the E2E project is in the
solution, so a bare `dotnet test StockManager.sln` discovers it — on any machine that *does*
have a container runtime it will start SQL Server and run, slowly, rather than skip. It skips
only when neither `docker version` nor `podman version` answers, or when Playwright's Chromium
is missing.

**A bUnit JS interop mock must complete its call, not merely be set up.**
`JSInterop.SetupVoid("name", ...)` registers a handler and stops the strict-mode throw; the
task behind the call stays pending until something calls `.SetVoidResult()`. A component that
awaits that call therefore hangs forever, and a click that awaits the component hangs with it.
This cost a day: with collections running sequentially, one such hang stalled everything
scheduled after it, the runner eventually reported a crashed test host, and the run printed
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

**Do not add an explicit `AngleSharp` `PackageReference` to a bUnit project.** bUnit 1.40.0
binds against the AngleSharp it ships with, and pinning 1.8.1 alongside it throws
`MissingMethodException: 'IHtmlCollection<T>.get_Item(Int32)'` from `FindAll(...)[i]` at run
time — three SMPortal tests failed that way and nothing failed to compile. `using
AngleSharp.Dom` works transitively; the package reference adds only the version conflict.

`StockManager.E2ETests/README.md` carries the rest: the Playwright install step and
`E2E_REQUIRE_APPHOST=1` for CI. **All five journeys pass**, in about a minute against a
warm SQL container.

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

`SMDataManager.Library.Tests` holds the one test the pricing design depends on: a parity check
between the net-price expression in `dbo.fnCatalog_VisibleProducts` and `PriceResolver`. The
catalog sorts on the SQL copy and displays the C# one, so this is the tripwire that makes that
duplication safe.

It needs a database — evaluating the SQL half has no other way, and a C# reimplementation
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

- **Unfinished actions disable themselves.** `ProductCard`'s add button binds an
  `EventCallback` and disables when nothing is wired, so later phases supply a handler rather
  than replacing markup. Prefer that to a button that silently does nothing.
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

`CatalogItemModel.Cost` is a buy price and is currently selected on every catalog row.
`ProductCardView` excludes it; the detail page binds the raw model, so it is one field
reference away from publishing margin on a public page. Do not add it to a view record, and
prefer removing it from the projection over relying on review.

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
