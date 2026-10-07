# Production runbook

How the platform gets to Azure and back from a bad day. The topology lives in
`StockManager.AppHost/ProductionTopology.cs` and the pipeline in
`.github/workflows/deploy-staging.yml`; this is the part neither can do for itself.

Everything below is done by a person with rights in the subscription. Nothing in the repository
deploys on its own, and an agent working here does not run these steps (T7 plan, D1).

---

## 1. Before the first deploy

### Azure

1. **A resource group** in the region the tenant's customers are in (`AZURE_LOCATION`,
   `AZURE_RESOURCE_GROUP`).
2. **An Entra app registration for the pipeline** with a federated credential for the
   deploying repository's `staging` environment (subject `repo:<owner>/<repo>:environment:staging`).
   That is the business's own repository (`docs/runbooks/new-store.md` §1), not the template,
   which needs no Azure credential.
   Give it **Owner** on the resource group: the deployment creates role assignments, and it
   becomes the SQL server's Entra administrator, which is how the pipeline publishes the schema.
3. **Communication Services, with Email.** Create the resource and an Email Communication
   Service, connect them, and add each store's sending domain (§3). The endpoint is
   `ACS_ENDPOINT`. After the first deploy, give `stock-api`'s managed identity the
   *Communication and Email Service Owner* role on it — the role assignment needs the identity,
   which the first deploy creates.

### GitHub

Under the repository's **staging** environment, with a required reviewer:

| Kind | Name | Value |
| --- | --- | --- |
| Variable | `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` | from step 2 |
| Variable | `AZURE_LOCATION`, `AZURE_RESOURCE_GROUP` | from step 1 |
| Secret | `JWT_SIGNING_KEY` | 64 random characters (`openssl rand -base64 48`) |
| Variable | `ADMIN_BOOTSTRAP_EMAIL` | the first admin's address |
| Secret | `ADMIN_BOOTSTRAP_PASSWORD` | their first password, which they change at once |
| Variable | `ACS_ENDPOINT` | `https://<name>.communication.azure.com` |
| Variable | `DATAPROTECTION_KEY_URI` | empty for the first deploy; see §2 |
| Variable | `ADMIN_CERTIFICATE_0`, `STORE_CERTIFICATE_0` | empty until §3 |

### The deploy workflow

The hostnames are environment lines in the deploy workflow, beside the parameters:

```yaml
env:
  Deployment__AdminDomain: admin.staging.example.com
  Deployment__StoreDomains__0: staging.shop.example
  Parameters__store-certificate-0: ${{ vars.STORE_CERTIFICATE_0 }}
```

Each store domain needs its own `Deployment__StoreDomains__N` line and a matching
`Parameters__store-certificate-N` line. A business running stores deploys from its own copy of
this workflow, in its own repository (`docs/runbooks/new-store.md` §1). That copy holds the
lines, so the shared `StockManager.AppHost/appsettings.json` is never edited downstream, and
merging the template does not fight over it.

---

## 2. The first deploy

1. **Run the workflow** (*Deploy to staging*, *Run workflow*). It provisions everything,
   deploys both apps, then publishes the schema (there was no server before), then restarts
   both apps so StockApi's first-admin bootstrap runs against a database that has tables.
2. **Create the Data Protection wrapping key**, once, in the vault the deploy made:

   ```bash
   az keyvault key create --vault-name <vault> --name dataprotection --kty RSA --size 3072
   ```

   Set `DATAPROTECTION_KEY_URI` to its URI without the version
   (`https://<vault>.vault.azure.net/keys/dataprotection`) and run the workflow again. From then
   on every new key in the ring is wrapped. The keys created before that are not, and nothing
   re-encrypts them; on a ring this young that is a few hours of sessions, which expire.
3. **Sign in to the portal** at the admin domain (or the container app's own URL) as the
   bootstrap admin, and change the password (*Change password*, top bar).
4. **Remove the bootstrap values**: set `ADMIN_BOOTSTRAP_EMAIL` and
   `ADMIN_BOOTSTRAP_PASSWORD` to empty and run the workflow again. StockApi warns in its log
   until they are gone; they do nothing once an admin exists.
5. **Give `stock-api` its mail role** (§1, step 3).

### Each deploy after that

Run the workflow. It publishes the schema first — so new code never starts against procedures
the database lacks — with `BlockOnPossibleDataLoss` on, after writing a deploy report that is
kept as an artifact. A change the publish refuses as data loss stops the deploy before any code
moves. That is the point: read the report, decide, and if the loss is intended, write the
pre-deployment step that keeps the data (CLAUDE.md, *Data access*), never turn the flag off.

---

## 3. A store's DNS

Per store domain, at whoever holds the zone:

**One host per store.** A store answers on its `Site.Domain` and nowhere else; any other
hostname is a 404, deliberately (CLAUDE.md, *Multi-store rules*). Choose `www.shop.example` or
`shop.example`, use only that one below, and have the registrar redirect the other to it.

**The storefront.**

1. A `CNAME` from the store's hostname to the `sm-store` container app's default FQDN. An
   apex domain (no `www`) cannot hold a `CNAME`: give it an `A` record to the environment's
   static IP instead (`az containerapp env show ... --query properties.staticIp`).
2. A `TXT` record `asuid.<hostname>` with the Container Apps environment's custom-domain
   verification ID (`az containerapp env show ... --query properties.customDomainConfiguration.customDomainVerificationId`).
3. Deploy once with the certificate parameter empty; the domain is added with binding disabled.
4. Create a managed certificate for it in the environment
   (`az containerapp env certificate create --hostname <host> --validation-method CNAME ...`;
   `HTTP` for an apex domain), set `STORE_CERTIFICATE_N` to its name, and deploy again. The
   binding turns on.

The admin domain is the same with `stock-api` and `ADMIN_CERTIFICATE_0`.

**Mail.** In the Email Communication Service, add the domain and publish the records it asks
for: the verification `TXT`, `SPF` (`include:spf.protection.outlook.com`), and two `DKIM`
`CNAME`s. Add a `DMARC` record of your own (`_dmarc.<domain>`, start at `p=none`). Then set the
store's sender — **Store settings → Mail is sent from**, acting for that store (since T9; it was
an `UPDATE dbo.Site` before). A whole new store is `docs/runbooks/new-store.md`.

Until it is set, that store's mail is dead-lettered with the reason, and the operator alert
for it is too — check `dbo.EmailOutbox` for `DeadLettered` rows after setting it.

---

## 4. Restoring

Azure SQL keeps seven days of point-in-time restore by default. **Practise this on staging
once before launch**; a restore nobody has done is a hope.

1. **Restore both databases to the same moment**, as new databases:

   ```bash
   az sql db restore -g <rg> -s <server> -n SMDatabase   --dest-name SMDatabase-restored   --time "2026-10-05T10:00:00Z"
   az sql db restore -g <rg> -s <server> -n ApiAuthDb    --dest-name ApiAuthDb-restored    --time "2026-10-05T10:00:00Z"
   ```

   The same timestamp, because they refer to each other: a contact names an Identity user, a
   staff profile names a login. Restoring one without the other leaves people who exist in one
   database and not the other.
2. **Swap them in**: rename the damaged databases aside and the restored ones into place (or
   point the apps' connection strings at the restored names), then restart both apps.
3. **Know what a restore costs.** The Data Protection key ring lives in `ApiAuthDb`. Restoring
   it to before a key was created loses that key, and with it every session cookie, every
   stored feed credential and every queued reset link encrypted with it since. Sessions sign
   in again; a feed whose credential was re-entered after the restore point needs it entered
   again (*Distributor feeds* in the portal); a reset link needs requesting again.
4. **Documents are not in either database.** They are blobs in the storage account, which has
   its own soft delete. A row restored from before a document was uploaded simply lacks it; a
   blob deleted after the restore point is gone unless soft delete caught it.

**The Key Vault key that wraps the ring must never be purged.** Purge protection is on, so it
cannot be; do not turn it off. Lose that key and every wrapped key in the ring is unreadable
with it, and no restore brings it back.

---

## 5. Rotating the JWT signing key

Set a new `JWT_SIGNING_KEY` and deploy. Every token the portal and the POS hold stops working
at once, and staff sign in again. There is no overlap window, and for a handful of staff that is
the right trade against the complexity of two valid keys.
