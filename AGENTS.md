# Coding Agent Instructions

Project: Azure Static Web Apps + Azure Functions contact form using ACS Email.

## Repository Layout
- Static site: `app/`
  - Form page: `app/index.html`
  - Client script: `app/form.js`
  - Embed snippet: `app/embed-snippet.html`
- Azure Functions API: `api/`
  - Function host: `api/Program.cs`
  - Function project: `api/ContactFormApi.csproj`
  - Submit function: `api/submit/SubmitFunction.cs`
- SWA config: `staticwebapp.config.json`
- Workflow: `.github/workflows/azure-static-web-apps.yml`
- Docs: `README.md`

## Behavior Requirements (Do Not Break)
- Form hosted at `/form` and submits to `/api/submit` (POST).
- Payload includes `site` from query param and `company` honeypot.
- Server validation:
  - Required: `name`, `email`, `message`.
  - Max lengths: name 100, email 254, subject 150, message 4000, site 50.
  - Reject non-empty `company` (honeypot).
- Allowlist:
  - `ALLOWED_SITES` env var (comma-separated).
  - If missing or not allowed → `403 { ok:false, error:"forbidden_site" }`.
- Rate limiting:
  - Per-IP, 5 requests per 10 minutes, in-memory sliding window.
  - On limit → `429 { ok:false, error:"rate_limited" }`.
- ACS Email:
  - Use `Azure.Communication.Email` EmailClient with connection string.
  - Env vars: `ACS_EMAIL_CONNECTION_STRING`, `ACS_FROM_EMAIL`, `TO_EMAIL`.
  - Subject: `[ContactForm][${site}] ${subject || "(no subject)"}`
  - Include name/email/subject/message/site/timestamp/IP/user-agent.
  - Don’t log message content; log message length only.
- Responses:
  - 200 `{ ok:true }`
  - 400 `{ ok:false, error:"validation_error", details:[...] }`
  - 403 `{ ok:false, error:"forbidden_site" }`
  - 429 `{ ok:false, error:"rate_limited" }`
  - 500 `{ ok:false, error:"email_send_failed" }` or `server_error` when misconfigured.

## SWA / Security Headers
- Keep `/form` route in `staticwebapp.config.json`.
- Headers set:
  - `X-Content-Type-Options: nosniff`
  - `Referrer-Policy: strict-origin-when-cross-origin`
- Do not add `X-Frame-Options: DENY`.
- If you add CSP `frame-ancestors`, ensure it aligns with README guidance.

## Style / Code Quality
- C#/.NET 8 isolated Functions.
- Keep dependencies minimal.
- Add small helper methods for validation, JSON parsing, and rate limiting.
- Avoid non-ASCII unless already in file.

## Local Dev
- API deps: `cd api && dotnet restore`
- SWA local: `swa start app --api-location api` (requires SWA CLI + Functions Core Tools).

## Testing Tips
- Use curl to POST to `/api/submit`.
- Verify rate limit and allowlist behaviors.
