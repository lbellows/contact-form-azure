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
Use the iframe snippet at `app/form/embed-snippet.html`. The query param `site` must match one of the values in `ALLOWED_SITES`.

## Security headers / iframe embedding
`staticwebapp.config.json` sets:
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: strict-origin-when-cross-origin`

Do not set `X-Frame-Options: DENY` because the form is meant to be embedded. If you know the exact domains allowed to embed the form, configure a `Content-Security-Policy` header with `frame-ancestors` for those domains. If you do not know them, leaving it unset is more permissive but less secure.

## Deployment
This repo includes a GitHub Actions workflow for SWA. Connect your repo in Azure Static Web Apps and set the repository secrets as prompted by Azure.
