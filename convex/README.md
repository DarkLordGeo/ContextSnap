# Download counting (Convex)

Counts clicks on the website's download buttons. Not part of the desktop apps, which send nothing.

- `schema.ts`: tables and indexes.
- `downloads.ts`: the `record` mutation and `stats` query (both internal).
- `http.ts`: `POST /downloads` (from `website/app.js`) and `GET /stats` (for `website/stats.html`, needs `STATS_TOKEN`).

Setup (from the repository root):

```
npm install
npx convex login
npx convex deploy            # or `npx convex dev` against a dev deployment
npx convex env set STATS_TOKEN <a long random value> --prod
```

The website reads the deployment's `.convex.site` URL from `<meta name="contextsnap-downloads">` in `index.html` and `stats.html`; the same URL must be in `connect-src` in `website/vercel.json`. Remove the meta tag to turn counting off.
