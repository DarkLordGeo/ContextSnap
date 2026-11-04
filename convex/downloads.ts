import { v } from "convex/values";
import { internalMutation, internalQuery } from "./_generated/server";
import { platform } from "./schema";

// Inputs come from anyone on the internet (through http.ts), so every field is checked again here.
export const VERSION_PATTERN = /^\d{1,4}\.\d{1,4}\.\d{1,4}(?:-[0-9A-Za-z.-]{1,20})?$/;
export const VISITOR_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
// The same browser requesting the same download again within this window counts once (double clicks).
export const DUPLICATE_WINDOW_MS = 10_000;
export const MONTHS_SHOWN = 12;

/**
 * Release versions that may be recorded, from the deployment's ALLOWED_VERSIONS environment variable
 * (comma separated, for example "1.0.0,1.0.1"). When it is not set, any well-formed version is accepted,
 * so counting keeps working until it is configured. Add each new release before its website PR merges.
 */
export const allowedVersions = (): Set<string> | null => {
  const raw = process.env.ALLOWED_VERSIONS?.trim();
  if (!raw) return null;
  return new Set(raw.split(",").map((item) => item.trim()).filter((item) => VERSION_PATTERN.test(item)));
};

export const isAllowedVersion = (version: string) => {
  if (!VERSION_PATTERN.test(version)) return false;
  const allowed = allowedVersions();
  return allowed === null || allowed.has(version);
};

const dayOf = (time: number) => new Date(time).toISOString().slice(0, 10);
const counts = () => ({ windows: 0, linux: 0 });

/** Records one download request. The time is the server's; the browser does not send one. */
export const record = internalMutation({
  args: {
    platform,
    version: v.string(),
    visitorId: v.optional(v.string()),
  },
  returns: v.union(v.literal("recorded"), v.literal("duplicate"), v.literal("rejected")),
  handler: async (ctx, { platform, version, visitorId }) => {
    if (!isAllowedVersion(version)) return "rejected";
    if (visitorId !== undefined && !VISITOR_PATTERN.test(visitorId)) return "rejected";
    const at = Date.now();

    if (visitorId !== undefined) {
      const recent = await ctx.db
        .query("downloads")
        .withIndex("by_visitor_and_at", (q) => q.eq("visitorId", visitorId).gt("at", at - DUPLICATE_WINDOW_MS))
        .collect();
      if (recent.some((row) => row.platform === platform && row.version === version)) return "duplicate";
    }

    await ctx.db.insert("downloads", { platform, version, at, visitorId });

    const day = dayOf(at);
    const daily = await ctx.db
      .query("dailyCounts")
      .withIndex("by_day_platform_version", (q) => q.eq("day", day).eq("platform", platform).eq("version", version))
      .unique();
    if (daily) await ctx.db.patch(daily._id, { requests: daily.requests + 1 });
    else await ctx.db.insert("dailyCounts", { day, platform, version, requests: 1 });

    if (visitorId !== undefined) {
      const known = await ctx.db.query("visitors").withIndex("by_visitor", (q) => q.eq("visitorId", visitorId)).unique();
      if (!known) {
        await ctx.db.insert("visitors", { visitorId, firstSeen: at });
        const counter = await ctx.db.query("counters").withIndex("by_name", (q) => q.eq("name", "uniqueVisitors")).unique();
        if (counter) await ctx.db.patch(counter._id, { value: counter.value + 1 });
        else await ctx.db.insert("counters", { name: "uniqueVisitors", value: 1 });
      }
    }
    return "recorded";
  },
});

const split = v.object({ windows: v.number(), linux: v.number() });

/** The dashboard's numbers, built from the daily totals. Only reachable through the token check in http.ts. */
export const stats = internalQuery({
  args: { days: v.number() },
  returns: v.object({
    totalRequests: v.number(),
    uniqueRequesters: v.number(),
    byPlatform: split,
    byVersion: v.array(v.object({ version: v.string(), windows: v.number(), linux: v.number(), total: v.number() })),
    daily: v.array(v.object({ day: v.string(), windows: v.number(), linux: v.number() })),
    monthly: v.array(v.object({ month: v.string(), windows: v.number(), linux: v.number() })),
  }),
  handler: async (ctx, { days }) => {
    const span = Math.min(366, Math.max(1, Math.floor(days)));
    // One row per day, platform, and version: small even after years, and the all-time totals need every row.
    const rows = await ctx.db.query("dailyCounts").collect();
    const counter = await ctx.db.query("counters").withIndex("by_name", (q) => q.eq("name", "uniqueVisitors")).unique();

    const byPlatform = counts();
    const versions = new Map<string, { windows: number; linux: number }>();
    const perDay = new Map<string, { windows: number; linux: number }>();
    const perMonth = new Map<string, { windows: number; linux: number }>();
    const add = (map: Map<string, { windows: number; linux: number }>, key: string, row: (typeof rows)[number]) => {
      const entry = map.get(key) ?? counts();
      entry[row.platform] += row.requests;
      map.set(key, entry);
    };
    for (const row of rows) {
      byPlatform[row.platform] += row.requests;
      add(versions, row.version, row);
      add(perDay, row.day, row);
      add(perMonth, row.day.slice(0, 7), row);
    }

    // Every day of the span, including days without requests, oldest first (UTC).
    const today = Date.parse(`${dayOf(Date.now())}T00:00:00Z`);
    const daily = Array.from({ length: span }, (_, i) => {
      const day = dayOf(today - (span - 1 - i) * 86_400_000);
      return { day, ...(perDay.get(day) ?? counts()) };
    });

    // The last MONTHS_SHOWN calendar months (UTC), the current one included, oldest first.
    const now = new Date(today);
    const monthly = Array.from({ length: MONTHS_SHOWN }, (_, i) => {
      const date = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - (MONTHS_SHOWN - 1 - i), 1));
      const month = date.toISOString().slice(0, 7);
      return { month, ...(perMonth.get(month) ?? counts()) };
    });

    const byVersion = [...versions]
      .map(([version, value]) => ({ version, ...value, total: value.windows + value.linux }))
      .sort((a, b) => b.version.localeCompare(a.version, "en", { numeric: true }));

    return {
      totalRequests: byPlatform.windows + byPlatform.linux,
      uniqueRequesters: counter?.value ?? 0,
      byPlatform,
      byVersion,
      daily,
      monthly,
    };
  },
});
