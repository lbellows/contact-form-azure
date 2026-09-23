# Contact Form (Azure Static Web Apps + Azure Functions + ACS Email)

Production-ready contact form hosted on Azure Static Web Apps (SWA) with an Azure Functions API (C#/.NET) that sends notifications via Azure Communication Services (ACS) Email.

## Features
- Static `/form` page with client-side validation
- Azure Functions API `/api/submit`
- ACS Email delivery using official .NET SDK
- In-memory rate limiting per IP
- Allowlisted `site` identifiers for iframe embedding
- Cloudflare Turnstile bot check (strict: fails closed)

## Using the form

Live at `https://proud-grass-0ee553c0f.2.azurestaticapps.net`. Everything runs on Azure; Cloudflare is used only
for the Turnstile check (see [How Turnstile fits in](#how-turnstile-fits-in)).

### Put the form on an existing site
Paste this where the form should appear, replacing `<site>` with the site's id from the
[Embedding](#embedding) table:

```html
<iframe
  title="Contact form"
  src="https://proud-grass-0ee553c0f.2.azurestaticapps.net/form/<site>"
  style="width:100%;min-height:640px;border:0"
  loading="lazy"
></iframe>
```

- The page must be served from one of that site's domains, over `https`. Anywhere else the browser refuses to
  show the frame, which is the point: nobody can reuse your form on their page.
- Leave room for the Turnstile widget (about 65px below the message box). If the bottom of the form is cut off,
  raise `min-height`.
- Messages arrive at `TO_EMAIL` with the subject `[ContactForm][<site>] <subject>`, so you can filter by site.
  Reply-To is set to the visitor's address, so hitting reply answers them directly.

`app/embed-snippet.html` is a responsive version of the same iframe.

### Add a new site or domain
1. Choose a short id (letters, digits, `-`, `_`), e.g. `newshop`.
2. Add it to `ALLOWED_SITES` in the SWA settings (Portal → Static Web Apps → `contact-form-swa` →
   Environment variables), or with the az CLI:
   `az staticwebapp appsettings set -n contact-form-swa --setting-names ALLOWED_SITES=<existing>,newshop`
   (setting it replaces the whole list, so include the existing ids).
3. Copy an existing `/form/<site>` route in `app/staticwebapp.config.json`, rename it, and list the domains that
   may embed it in its `frame-ancestors` header (include `https://www.` for an apex).
4. Commit and push to `main`; the workflow deploys it. Add the row to the table under [Embedding](#embedding).

To let one more domain embed an existing site, only step 3 is needed. Turnstile needs no change for either,
because the widget runs on this app's page, not on the embedding site.

### How Turnstile fits in
1. The form page fetches the public site key from `GET /api/config` and renders Cloudflare's widget. Most
   visitors pass invisibly; a suspicious browser gets a checkbox.
2. The widget's single-use token is sent with the message to `/api/submit`.
3. The Azure function asks Cloudflare's siteverify endpoint whether the token is valid, and only then sends the
   email. A missing, used or invalid token gets `403 captcha_failed`, and the visitor sees a message asking them
   to complete the check again.

**It is strict on purpose.** If Cloudflare cannot be reached, submissions are rejected rather than let through.
Do not change this to fail open: that turns any Cloudflare outage, or anyone able to block it, into a spam
window.

Keys are managed in the Cloudflare dashboard (Turnstile → the widget for this form). The site key is public; the
secret lives only in the SWA settings (`TURNSTILE_SITE_KEY`, `TURNSTILE_SECRET_KEY`). The widget's hostname list
must include the hostname the form is served from (`proud-grass-0ee553c0f.2.azurestaticapps.net`), plus any
custom domain added to the SWA later. To rotate the secret, rotate it in Cloudflare and update
`TURNSTILE_SECRET_KEY`. To turn Turnstile off, remove both settings.

### Check that it works
- `curl https://proud-grass-0ee553c0f.2.azurestaticapps.net/api/config` should return a non-null
  `turnstileSiteKey`.
- A submission with no token should be refused:

  ```bash
  curl -s -X POST https://proud-grass-0ee553c0f.2.azurestaticapps.net/api/submit \
    -H "Content-Type: application/json" \
    -d '{"site":"lb","name":"x","email":"x@example.com","message":"x"}'
  # {"ok":false,"error":"captcha_failed"}
  ```

- For a real end-to-end test, open `/form/<site>` directly in a normal browser, submit, and check the inbox.
  Automated browsers are usually challenged or rejected by Turnstile, so a headless test failing there does not
  mean the form is broken.
- Each IP gets 5 requests per 10 minutes, so repeated testing will hit `429 rate_limited`; wait it out.

## Prerequisites
- Azure subscription
- GitHub repo connected to Azure Static Web Apps
- .NET 8 SDK
- Azure Functions Core Tools (for local API runs)
- Azure Static Web Apps CLI (for `swa start`)

## Azure Communication Services (ACS) setup
Follow the official Azure Communication Services Email “send email” quickstart to:
1. Create an ACS resource with Email capability.
2. Configure a sender identity (verified domain or sender address as required).
3. Copy the ACS connection string.

Required environment variables (set in the SWA Azure portal):
- `ACS_EMAIL_CONNECTION_STRING`
- `ACS_FROM_EMAIL` (must be a configured sender identity)
- `TO_EMAIL` (your inbox)
- `ALLOWED_SITES` (comma-separated, e.g. `siteA,siteB,siteC`)
- `TURNSTILE_SITE_KEY` / `TURNSTILE_SECRET_KEY` (optional) — Cloudflare Turnstile. With both unset the form behaves as before. The site key is public and served by `GET /api/config`; once the secret is set, `/api/submit` requires a valid token (`403 { ok:false, error:"captcha_failed" }` otherwise). The widget's hostname list must include this SWA's hostname, since that is the page the widget renders on, not the site embedding the iframe.

Where to set env vars in SWA:
- Azure Portal → Static Web Apps → your app → Configuration → Application settings.

## Local development
Restore dependencies for the API:

```bash
cd api
dotnet restore
```

Run SWA locally (from repo root):

```bash
swa start app --api-location api
```

Create a local settings file for API environment variables (not committed):

`api/local.settings.json`

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "ACS_EMAIL_CONNECTION_STRING": "<your-acs-connection-string>",
    "ACS_FROM_EMAIL": "sender@yourdomain.example",
    "TO_EMAIL": "you@yourdomain.example",
    "ALLOWED_SITES": "siteA,siteB,siteC"
  }
}
```

## API
### POST `/api/submit`
Payload:

```json
{
  "name": "Jane Doe",
  "email": "jane@example.com",
  "subject": "Hello",
  "message": "Test message",
  "site": "siteA",
  "company": "",
  "turnstileToken": "<token from the widget>"
}
```

`turnstileToken` is required whenever `TURNSTILE_SECRET_KEY` is set.

Responses:
- `200` `{ ok: true }`
- `400` `{ ok:false, error:"validation_error", details:["..."] }`
- `403` `{ ok:false, error:"forbidden_site" }`
- `403` `{ ok:false, error:"captcha_failed" }` (missing, used or invalid Turnstile token, or siteverify unreachable)
- `429` `{ ok:false, error:"rate_limited" }`
- `500` `{ ok:false, error:"email_send_failed" }`

Example test (only succeeds with Turnstile off; see [Check that it works](#check-that-it-works)):

```bash
curl -i -X POST https://<your-swa-domain>/api/submit \
  -H "Content-Type: application/json" \
  -d '{"name":"Jane","email":"jane@example.com","subject":"Hello","message":"Test","site":"siteA","company":""}'
```

## Embedding
Use the iframe snippet at `app/embed-snippet.html`, pointing at `/form/<site>`. Each `/form/<site>` route in
`app/staticwebapp.config.json` sets `Content-Security-Policy: frame-ancestors ...` listing the only domains
allowed to frame that site's form; every other page sends `frame-ancestors 'none'`. `<site>` must also be in
`ALLOWED_SITES`.

| Site | Domains allowed to embed (plus `www.` for apexes) |
|---|---|
| `4leaf` | 4leaf.cc, 4leafelectric.com, 4leafconstruction.com |
| `irondev` | irondeveloper.com, check.irondeveloper.com |
| `parked` | ptreviews.com, swipe.tips, proxyable.com |
| `catholiccoder` | catholiccoder.com |
| `lb` | liambellows.com, blog.liambellows.com |
| `ragstripe` | chat.lcb2.com |

To add a site: add its id to `ALLOWED_SITES` in the SWA settings and a matching route in
`app/staticwebapp.config.json`.

## Security headers / iframe embedding
`app/staticwebapp.config.json` sets:
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Content-Security-Policy: frame-ancestors 'none'` globally, overridden per `/form/<site>` route with that site's domains

Do not set `X-Frame-Options` — `frame-ancestors` is the per-site control.

## Deployment
This repo includes a GitHub Actions workflow for SWA. Connect your repo in Azure Static Web Apps and set the repository secrets as prompted by Azure.
