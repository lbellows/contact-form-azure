# Contact Form (Azure Static Web Apps + Azure Functions + ACS Email)

Production-ready contact form hosted on Azure Static Web Apps (SWA) with an Azure Functions API (C#/.NET) that sends notifications via Azure Communication Services (ACS) Email.

## Features
- Static `/form` page with client-side validation
- Azure Functions API `/api/submit`
- ACS Email delivery using official .NET SDK
- In-memory rate limiting per IP
- Allowlisted `site` identifiers for iframe embedding

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
  "company": ""
}
```

Responses:
- `200` `{ ok: true }`
- `400` `{ ok:false, error:"validation_error", details:["..."] }`
- `403` `{ ok:false, error:"forbidden_site" }`
- `429` `{ ok:false, error:"rate_limited" }`
- `500` `{ ok:false, error:"email_send_failed" }`

Example test:

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
