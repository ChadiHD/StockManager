# Standing up a new store

How a second store — or a tenth — goes live on a deployment that already runs one (T9). None of
it is code. If a step below turns out to need a change to shared code, that is a gap in the
template: fix the template and pull it into the store's repository (§1), never patch it there.

Everything in the portal is done by an **admin of every store**. An admin given only some stores
cannot create, open or close one.

---

## 1. Where a store's files live

This repository is the template. It holds no store's name, brand or domain, and it stays that
way so it can be used for other businesses.

A business running stores on it keeps a repository of its own, downstream:

- A **private copy of this repository**, with this one as its `upstream` remote. GitHub will not
  fork a repository into the account that owns it, and its "template repository" button copies
  without history, which `git merge upstream/main` needs. So create an empty private repository,
  push this `main` to it, then:

  ```bash
  git remote add upstream https://github.com/<owner>/StockManager.git
  ```

- Turn off push to the template from that copy, so a store's commits cannot land upstream by
  accident: `git remote set-url --push upstream no-push`.
- What it adds, and the only things it adds. Every file it owns is new, never an edited
  template file, so a merge from upstream cannot conflict with it. The business picks one
  prefix for its own files, such as `acme-`:
  - `SMStore/wwwroot/sites/{SiteKey}/` for each store: `theme.css`, `logo.svg`,
    `logo-inverse.svg`, `favicon.svg` and `fonts/` (self-hosted; never a font service — see
    CLAUDE.md, *Everything comes from this origin*). A file left out falls back to `default`,
    which is the template's and is not edited downstream.
  - `.github/workflows/acme-deploy-*.yml`: its own copy of `deploy-staging.yml`, carrying the
    domain lines (`docs/runbooks/production.md` §1, *The deploy workflow*). The template's
    *Deploy to staging* is switched off in the copy's Actions settings rather than edited.
    This copy is the one file that follows the template by hand: when upstream changes
    `deploy-staging.yml`, make the same change here.
  - `docs/acme/` for the business's own plans and notes. `CLAUDE.md` is the template's.
- **Template changes come from upstream, by tag.** A gap found while building a store is fixed
  in the template, merged and tagged there (`template-v2`), then merged downstream with
  `git fetch upstream --tags` and `git merge template-v2`, then deployed. Merging tags rather
  than `upstream/main` means a store only ever runs a template version somebody chose. A shared
  file edited downstream is the drift that makes the next merge a fight, and the next store a
  fork.

Its CI should refuse the drift outright. This workflow, as
`.github/workflows/acme-paths.yml`, fails any pull request that touches a file outside the
business's own paths. Name the business's stores in the first alternative, so the `default`
theme stays the template's, and require the check on `main`:

```yaml
name: Own paths

# Shared code belongs in the template (upstream). This repository adds only its own files.
on:
  pull_request:

jobs:
  paths:
    if: ${{ !startsWith(github.event.pull_request.title, 'Merge upstream') }}
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - name: Only this business's files
        run: |
          outside=$(git diff --name-only "origin/${{ github.base_ref }}...HEAD" \
            | grep -vE '^(SMStore/wwwroot/sites/(acme-ie|acme-uk)/|\.github/workflows/acme-|docs/acme/)' || true)
          if [ -n "$outside" ]; then
            echo "::error::Shared code changes belong in the template, not here:"
            echo "$outside"
            exit 1
          fi
```

A merge from upstream touches shared files by design, so the check skips a pull request whose
title starts with "Merge upstream".

## 2. Before anything else: the long lead times

- **The domain.** `.ie` needs IEDR's identity check, which takes days; `.co.uk` is quick.
- **The company.** Its legal name, company number, registered office and VAT number go on every
  document (§4). A store in a new country may be a new company, and VAT registration there can
  take weeks; a store cannot charge VAT before it has a number.
- **The distributor.** An account, the file's specification, a sample file, and SFTP
  credentials. Connecting to their live server is the operator's decision.
- **An accountant** for the country's tax treatment, before the first order.

## 3. Create the store, closed

**Stores → Add store**: name, key, domain, country, currency, locale, registration form and tax
rules.

- **The key is permanent.** It names the theme folder and sits in every admin's stored choice.
  Lower-case letters, digits and hyphens, such as `acme-uk`.
- **The currency is permanent once the store holds any price** — an account, a quote or a feed.
- The store is created **not open**: its storefront answers 404, and only admins of every store
  can act for it.

## 4. Set it up, acting for it

**Stores → Act for it**, then:

1. **Store settings.** Sending address and operator address, the standard tax rate, the store's
   VAT number, legal name, company number and registered office, price display, margin floor and
   staleness.
2. **Categories.** At least one. The address (`/catalog?cat=…`) is fixed once the store opens.
3. **Feeds.** Add the distributor's feed and its credentials, and check every field mapping:
   a blank field is not read at all. Sync it by hand, then map its categories to the store's on
   the Categories screen.
4. **Content pages.** The home page, and at least the terms of sale, privacy and cookies pages.
5. **Email wording**, if the store wants its own.
6. **Customer groups** and their terms, as for any store.

## 5. The theme and the domain

1. Add `SMStore/wwwroot/sites/{SiteKey}/` downstream (§1) and deploy.
2. The domain's DNS records, then its lines in the business's deploy workflow (§1), then its
   certificate, in the order `docs/runbooks/production.md` §3 gives. A store answers on one
   host: choose `www` or not before the DNS.
3. **Mail:** verify the sending domain with Communication Services (same section). Until it is,
   the store's mail is dead-lettered with the reason.

## 6. Open it

**Stores → Open.** The store opens only once it has everything it needs — sending and operator
addresses, a tax rate above zero, a VAT number, a legal name, an active category, and its terms,
privacy and cookies pages. Otherwise the screen lists what is still missing.

The storefront serves it within half a minute. Closing it again is immediate, asks first, and
deletes nothing.

## 7. Staff

**Users & roles**: give the store's own staff that store and no other. Only admins of every store
see the staff screen, and the last admin of every store cannot be demoted.
