# Download request counting (Convex)

Counts **download requests**: clicks on the website's Windows and Linux download buttons. Not completed
downloads or installations. Not part of the desktop apps, which send nothing. There is no macOS platform.

Production deployment: `notable-cow-268`. The website calls its HTTP actions at
`https://notable-cow-268.convex.site` (the `.convex.cloud` URL is for client SDKs, which are not used).
The project's dev deployment, `colorful-chickadee-319`, is for testing only.

- `schema.ts`: tables and indexes (`downloads`, `visitors`, `dailyCounts`, `counters`).
- `downloads.ts`: the `record` mutation and `stats` query (both internal), version validation.
- `http.ts`: `POST /downloads` (from `website/app.js`) and `GET /stats` (for `website/stats.html`, needs `STATS_TOKEN`).
- `downloads.test.ts`: tests (`npm test`, convex-test + Vitest). The Convex bundler skips this file.

## What is stored

Per request: the platform (`windows` or `linux`), the release version, and the server's time. A random
per-browser ID is added only after the visitor accepted analytics cookies; it estimates unique requesters.
Without consent nothing is stored in the browser and no ID is sent. No IP addresses, user agents, names,
emails, or fingerprints. The dashboard shows totals, the platform split, per day (UTC), per month (UTC, last
12 months), and per version.

## Environment variables (Convex deployment)

| Name | Required | Purpose |
|---|---|---|
| `STATS_TOKEN` | yes | Bearer token for `GET /stats` (24+ random characters). Without it the dashboard answers 503. |
| `ALLOWED_VERSIONS` | recommended | Comma-separated release versions to record, for example `1.0.0,1.0.1`. Others are ignored. When unset, any well-formed version (`1.2.3`, `1.2.3-beta.1`) is recorded. **Add each new release** before its website PR merges, or its requests are dropped. |
| `ALLOWED_ORIGINS` | no | Comma-separated exact origins allowed to record requests. Default: `https://contextsnap.vercel.app,https://context-snap-ashen.vercel.app`. Setting it replaces the default (include those if you still want them). Requests with a missing or `null` Origin are always ignored. |

The Origin check and duplicate window (same browser ID, platform, and version within 10 s) filter noise;
they are not authentication. Anyone can send fake requests that pass them, so counts are estimates.

## Setup (from the repository root)

```
npm ci
npm run typecheck && npm test
npx convex login
npx convex deploy            # always targets production: notable-cow-268
npx convex env set STATS_TOKEN "<a long random value>" --prod
npx convex env set ALLOWED_VERSIONS "1.0.1" --prod
```

To test on the **dev** deployment (`colorful-chickadee-319`) instead, point `.env.local` at
`dev:colorful-chickadee-319`, push with `npx convex dev --once`, and drop `--prod` from the `env set`
commands.

The website reads the deployment's `.convex.site` URL from `<meta name="contextsnap-downloads">` in
`index.html` and `stats.html`; the same URL must be in `connect-src` in `website/vercel.json`. Remove the
meta tag to turn counting off.
