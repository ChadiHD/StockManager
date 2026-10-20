# T11 — Apparel catalogue: variants, images, description and brand

Detail plan for the eleventh template phase, the second of the B2C track
(`docs/plans/2026-10-15-t10-market-neutrality.md`, §1). The requirement: a clothing store sells
one shirt in several sizes and colours, shows photographs its own staff took, and describes its
products in its own words. T0–T10 sell one SKU per row, show at most one image, and that image is
a URL a content provider supplied. Nothing in the portal can describe or brand a product.

## 1. What reading the code found

1. **No variants anywhere.** One `dbo.Product` row is one SKU. `BasketLine`, `QuoteLine` and
   `PurchaseDetail` all key on `ProductId`, and `UQ_BasketLine_Product` holds one line per
   product per basket.
2. **One image, as a URL.** `Product.ProductImage NVARCHAR(500)` holds an Icecat URL.
   - Nothing uploads an image, and nothing in the portal uses `InputFile`.
   - Icecat never enriches own stock, because its candidates need an MPN or an EAN, and the
     portal cannot enter either.
3. **The portal cannot describe a product or name its brand.** `spProduct_Insert` and
   `spProduct_Update` do not write `Manufacturer`. `AdminDataService.AddProduct` sends
   `Description = ""` and `IsTaxable = true` whatever the form says, and neither form has a
   field for either. A store that sells children's clothes zero-rated could not say so.
4. **No upload limit on `StockApi`.** It configures no request limit at all, so ASP.NET's
   default of about 28.6 MB applies.
5. **`DocumentContentTypes` admits PDF**, which is right for customer paperwork and wrong for a
   photograph. It knows no WebP.

## 2. Decisions

### 2.1 A variant is a `Product` row under a parent

The new column is `Product.ParentProductId`, the last column, a self foreign key. Option values
go in `dbo.ProductOption (ProductId, Name, Value, SortOrder)`, one value per option name per
variant: Size = M, Colour = Navy.

- **Why rows and not a variant table.** A variant has its own SKU, stock and line. As a
  `Product` row it needs nothing new from the basket, the quote, the order, the document, the
  email or the till: every line keeps keying on `ProductId`. A `ProductVariant` table would put
  a second key on four line tables and every procedure that reads them.
- **One level.** A variant has no variants, and `spProduct_AddVariant` refuses a parent that is
  itself a variant.
- **Own stock only.** The feed merge rewrites a feed product's name, cost and stock on every
  sync, so variants of one would be overwritten nightly.
- **A variant inherits from its parent:**
  - its name, description, category, source, cost, price, tax flag and brand;
  - its currency and basis.

  `spProduct_AddVariant` copies these at creation, and `spProduct_Update` cascades them
  whenever the parent changes. The variant's own name is the parent's plus its option values
  ("Linen shirt — M / Navy"), so a basket, a quote, an order and a confirmation mail all name
  the size and colour bought, with no change to any of them.
- **One price per style.** A variant's price is its parent's, cascaded. A listing therefore
  shows the price the customer will pay for any size, and the sort key needs no change.
  Pricing XL above M is not built (§5).
- **A variant's options are fixed once it exists.** To change one, add the new variant and
  remove the old. Removal is refused while a basket, quote or order holds the variant, with a
  message saying to set its stock to zero instead. Lines are never rewritten under a customer.

### 2.2 The catalogue lists parents, and a variant is on sale exactly when its parent is

`fnCatalog_VisibleProducts` gains three output columns and changes two predicates:

- **`ParentProductId`.** The listing, the facets and the featured strip read rows where it is
  NULL. The basket and the detail page read any row.
- **Placement is evaluated on the parent's id** for a variant. A store hides or files a style,
  not a size, so `dbo.SiteProduct` keeps one row per style, and the variant follows it.
- **A style's `Delisted` is not read for its variants.** Only own stock has variants, and
  nothing delists own stock; the join that read it cost every row of the browse.
- **`HasVariants`.** `spBasket_AddLine` refuses a style that has variants, because there is
  nothing to ship until a size is chosen. Its card links to the product page instead of
  posting.
- **`QuantityInStock` is rolled up for a style.** It is the sum of its live variants' stock,
  through a join to the grouped variants, not a subquery per row (the 50,000-product lesson).
  In-stock-only therefore means "some size is in stock".
- **What it costs, measured at 50,000 products** with main's function side by side on one
  database: the browse is level, and the in-stock price sort pays about 10 ms for the roll-up
  (30 to 45 ms of the query, inside a procedure of about a second). A join to the style and a
  per-row label in the admin's snapshot cost it 500 and 700 ms, so the snapshot carries only
  `ParentProductId` and the product page fetches its variants itself.

The search ladder still runs over the parent's own SKU and name. A variant's SKU typed into
search finds nothing (§5).

### 2.3 Options are facets

`spCatalog_Search` and `spCatalog_GetFacets` take an optional `@Options dbo.ProductOptionFilter`
of (Name, Value) pairs.

- **The match rule.** A style matches when one of its live variants carries, for every option
  name selected, one of the values selected for that name. Size M or L, and Navy, means an M or
  L that is Navy. With in-stock-only on, the matching variant must also be in stock.
- **One function holds the rule**, `fnProduct_MatchesOptions`, which both procedures read, for
  the reason `fnCatalog_VisibleProducts` exists: the facet counts must agree with the page.
- **A third facet grid** counts styles per (option, value) over the filtered set.
- **The URL** carries `?opt=Size:M&opt=Colour:Navy`, bound as `string[]` and parsed. An option
  name is the store's own word, so nothing in the platform names Size or Colour.

### 2.4 Images are blobs, served from each host's own origin

- **Storage.** A new blob container, `product-images`, beside `documents` in the same storage
  account. It is registered as a keyed client, because both hosts already hold one unkeyed
  `BlobContainerClient` for documents. `IProductImageStore` and `BlobProductImageStore` mirror
  `IDocumentStore`, without the site key: `dbo.Product` is shared, so its photographs are too.
- **What a stored image is.**
  - The type is decided by reading the bytes, through `ProductImageTypes.TryDetect`: JPEG, PNG
    or WebP, and nothing else, PDF included.
  - The stored name is generated, `{guid}.{ext}`, and its shape is checked on the way back in.
  - The content type rides on the blob, so serving an image reads no row.
- **Serving.** `GET /img/{name}`, mapped by `MapProductImages` in ServiceDefaults, on both hosts.
  - The storefront's CSP stays `img-src 'self'`. The portal, on `StockApi`'s origin, can show
    the same photograph. A bearer token cannot ride on an `<img>`, and these are public
    photographs anyway.
  - Each response carries `Cache-Control: public, max-age=31536000, immutable`, because a name
    is never reused.
  - Every refusal is a 404.
- **Upload.** `POST api/Product/Catalog/{sku}/Images`, Admin, capped at 5 MB on the request.
  - The bytes are written first and the row second, then the blob is deleted if the row fails:
    the document store's order and the document store's reason.
  - `dbo.ProductImage (ProductId, StoredName, ContentType, SortOrder)` describes them.
- **The first image is the product's `ProductImage`**, written as `/img/{name}` by the image
  procedures. So the card, the detail page and the portal render it with no change. A
  provider's URL already there is replaced by the first upload, and comes back only if every
  upload is removed and enrichment runs again.
- **The detail page shows every image**, in order, as a gallery. It is static SSR, so there is
  no carousel.

### 2.5 The portal describes and brands a product

- **The fields.** `spProduct_Insert` and `spProduct_Update` take `@Manufacturer`. Both product
  forms gain a description, a brand, and "exempt from tax", and `AddProduct` sends what the
  form says.
- **Brand is editable on own stock only.** The feed overwrites a feed product's brand on every
  sync, so editing it there would quietly not stick.
- **The product page gains two panels:**
  - **Variants**: list, add, set stock, and remove where nothing holds the variant.
  - **Images**: upload, list and remove.

  Both are fetched when the page opens, the `GetContacts` precedent, not held in the snapshot.
- **The products list shows styles**, with a count of their variants.

## 3. Exit

- **An admin builds a style in the portal.** They create a shirt, give it a description, a
  brand and two photographs, and add M/Navy, L/Navy and M/White with stock.
- **The listing shows the shirt once:**
  - at its price;
  - with the first photograph;
  - "in stock" while any variant is;
  - "Choose options" rather than an add button.
- **The product page:**
  - shows every photograph;
  - offers the three variants in one select;
  - adds the chosen one to the basket, where it reads "Shirt — L / Navy".
- **Size and colour facets** narrow the listing, and their counts agree with the page they
  filter to.
- **A post of the style's own SKU** to the basket is refused.
- **Hiding the style on a store** hides all three variants there.
- **Existing behaviour is unchanged.** Every existing test passes, the T-SQL suite included,
  as do the 13 journeys. A second publish reports nothing, an upgrade from main rebuilds no
  table, and the 50,000-product load check is no slower than on main.

## 4. Order of work, one commit each

1. Variants: the schema, the catalogue function, the listing, the basket refusal, and the
   presenter, card and product page.
2. Option facets.
3. Product images: the store, the procedures, upload, serving and the gallery.
4. The portal: description, brand, tax flag, the variants panel and the images panel.
5. A journey that builds a style in the portal and buys a variant of it.

## 5. Not in T11

- **Stock reserved by an order.** It belongs to the order path T12 builds, which is its only
  caller. Shipping the procedure first would be a guard nothing exercises.
- **A price per variant.** One price per style for now; the listing's "from" price and its sort
  key need deciding together.
- **Images per colour.** Choosing a colour cannot swap a photograph without a link per colour or
  a script, and the gallery shows them all.
- **Searching by a variant's SKU.** The ladder ranks styles.
- **Facet counts that exclude their own dimension.** Option counts are taken over the set
  already narrowed by the selected options, unlike the brand and category grids.
- **Thumbnails and resizing.** An image is served as uploaded, up to 5 MB. A CDN or a resizer
  is a hosting decision.
