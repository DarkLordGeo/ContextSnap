import { httpRouter } from "convex/server";
import { httpAction } from "./_generated/server";
import { internal } from "./_generated/api";
import { isAllowedVersion, VISITOR_PATTERN } from "./downloads";

// HTTP endpoints on the deployment's .convex.site URL:
//   POST /downloads  one download request, sent by website/app.js with fetch (mode "cors", keepalive,
//                    text/plain JSON, so no CORS preflight). Always answers 204 so nothing on the page
//                    depends on it, including when the request is ignored.
//   GET  /stats      the dashboard's numbers (website/stats.html), only with the STATS_TOKEN set in this
//                    deployment's environment variables: `npx convex env set STATS_TOKEN <random value>`.
const http = httpRouter();

const MAX_BODY = 1024;
const noContent = () => new Response(null, { status: 204, headers: { "Access-Control-Allow-Origin": "*" } });

// Pages allowed to record requests. ALLOWED_ORIGINS (comma separated, exact origins such as
// "https://contextsnap.vercel.app") replaces this list, for example to add http://localhost:8080 for
// testing. A missing or "null" Origin header is never accepted. This filters casual noise from other
// sites and scripts that do not fake the header; it is not authentication, so counts stay estimates.
export const DEFAULT_ORIGINS = ["https://contextsnap.vercel.app", "https://context-snap-ashen.vercel.app"];
const allowedOrigins = () => {
  const raw = process.env.ALLOWED_ORIGINS?.trim();
  const list = raw ? raw.split(",").map((item) => item.trim().replace(/\/+$/, "")).filter(Boolean) : DEFAULT_ORIGINS;
  return new Set(list.map((item) => item.toLowerCase()));
};

http.route({
  path: "/downloads",
  method: "POST",
  handler: httpAction(async (ctx, request) => {
    const origin = request.headers.get("Origin")?.toLowerCase();
    if (!origin || !allowedOrigins().has(origin)) return noContent();
    const text = await request.text();
    if (text.length > MAX_BODY) return noContent();
    let body: unknown;
    try {
      body = JSON.parse(text);
    } catch {
      return noContent();
    }
    if (typeof body !== "object" || body === null || Array.isArray(body)) return noContent();
    const { platform, version, visitorId } = body as Record<string, unknown>;
    if (platform !== "windows" && platform !== "linux") return noContent();
    if (typeof version !== "string" || !isAllowedVersion(version)) return noContent();
    if (visitorId !== undefined && (typeof visitorId !== "string" || !VISITOR_PATTERN.test(visitorId))) return noContent();
    await ctx.runMutation(internal.downloads.record, { platform, version, visitorId });
    return noContent();
  }),
});

// The token is sent in a header, never a cookie, so allowing any origin exposes nothing without it.
const statsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "Authorization",
  "Access-Control-Allow-Methods": "GET, OPTIONS",
  "Cache-Control": "no-store",
  Vary: "Authorization",
};

// Compares in time that does not depend on where the strings first differ.
const sameText = (a: string, b: string) => {
  const left = new TextEncoder().encode(a);
  const right = new TextEncoder().encode(b);
  let difference = left.length ^ right.length;
  for (let i = 0; i < Math.max(left.length, right.length); i++) difference |= (left[i] ?? 0) ^ (right[i] ?? 0);
  return difference === 0;
};

http.route({
  path: "/stats",
  method: "OPTIONS",
  handler: httpAction(async () => new Response(null, { status: 204, headers: { ...statsHeaders, "Access-Control-Max-Age": "600" } })),
});

http.route({
  path: "/stats",
  method: "GET",
  handler: httpAction(async (ctx, request) => {
    const expected = process.env.STATS_TOKEN;
    if (!expected || expected.length < 24) {
      return Response.json({ error: "The dashboard is not set up: STATS_TOKEN is missing or shorter than 24 characters." }, { status: 503, headers: statsHeaders });
    }
    const given = (request.headers.get("Authorization") ?? "").replace(/^Bearer\s+/i, "");
    if (!sameText(given, expected)) return Response.json({ error: "Not authorized." }, { status: 401, headers: statsHeaders });
    const days = Number(new URL(request.url).searchParams.get("days") ?? 30);
    const stats = await ctx.runQuery(internal.downloads.stats, { days: Number.isFinite(days) ? days : 30 });
    return Response.json(stats, { headers: statsHeaders });
  }),
});

export default http;
