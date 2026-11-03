import { v } from "convex/values";
import { internalMutation, internalQuery } from "./_generated/server";
import { platform } from "./schema";

// Inputs come from anyone on the internet (through http.ts), so every field is checked again here.
export const VERSION_PATTERN = /^\d{1,4}\.\d{1,4}\.\d{1,4}(?:-[0-9A-Za-z.-]{1,20})?$/;
export const VISITOR_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
// The same browser clicking the same download again within this window counts once (double clicks).
export const DUPLICATE_WINDOW_MS = 10_000;

const dayOf = (time: number) => new Date(time).toISOString().slice(0, 10);

/** Records one download attempt. The time is the server's; the browser does not send one. */
export const record = internalMutation({
  args: {
    platform,
    version: v.string(),
    visitorId: v.optional(v.string()),
  },
  returns: v.union(v.literal("recorded"), v.literal("duplicate")),
  handler: async (ctx, { platform, version, visitorId }) => {
    if (!VERSION_PATTERN.test(version)) throw new Error("Invalid version");
    if (visitorId !== undefined && !VISITOR_PATTERN.test(visitorId)) throw new Error("Invalid visitor ID");
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
    if (daily) await ctx.db.patch(daily._id, { attempts: daily.attempts + 1 });
    else await ctx.db.insert("dailyCounts", { day, platform, version, attempts: 1 });

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

/** The dashboard's numbers, built from the daily totals. Only reachable through the token check in http.ts. */
export const stats = internalQuery({
  args: { days: v.number() },
  returns: v.object({
    totalAttempts: v.number(),
    uniqueDownloaders: v.number(),
    byPlatform: v.object({ windows: v.number(), linux: v.number() }),
    byVersion: v.array(v.object({ version: v.string(), windows: v.number(), linux: v.number(), total: v.number() })),
    daily: v.array(v.object({ day: v.string(), windows: v.number(), linux: v.number() })),
  }),
  handler: async (ctx, { days }) => {
    const span = Math.min(366, Math.max(1, Math.floor(days)));
    const rows = await ctx.db.query("dailyCounts").collect();
    const counter = await ctx.db.query("counters").withIndex("by_name", (q) => q.eq("name", "uniqueVisitors")).unique();

    const byPlatform = { windows: 0, linux: 0 };
    const versions = new Map<string, { windows: number; linux: number }>();
    const perDay = new Map<string, { windows: number; linux: number }>();
    for (const row of rows) {
      byPlatform[row.platform] += row.attempts;
      const version = versions.get(row.version) ?? { windows: 0, linux: 0 };
      version[row.platform] += row.attempts;
      versions.set(row.version, version);
      const day = perDay.get(row.day) ?? { windows: 0, linux: 0 };
      day[row.platform] += row.attempts;
      perDay.set(row.day, day);
    }

    // Every day of the span, including days without attempts, oldest first.
    const today = Date.parse(`${dayOf(Date.now())}T00:00:00Z`);
    const daily = Array.from({ length: span }, (_, i) => {
      const day = dayOf(today - (span - 1 - i) * 86_400_000);
      return { day, ...(perDay.get(day) ?? { windows: 0, linux: 0 }) };
    });
    const byVersion = [...versions]
      .map(([version, counts]) => ({ version, ...counts, total: counts.windows + counts.linux }))
      .sort((a, b) => b.version.localeCompare(a.version, "en", { numeric: true }));

    return {
      totalAttempts: byPlatform.windows + byPlatform.linux,
      uniqueDownloaders: counter?.value ?? 0,
      byPlatform,
      byVersion,
      daily,
    };
  },
});
