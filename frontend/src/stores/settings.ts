import { defineStore } from 'pinia'

const DEFAULT_TEAM_KEY = 'shifty:defaultTeamId'
const NOTIFICATIONS_ENABLED_KEY = 'shifty:notificationsEnabled'
const LAST_SEEN_AT_KEY = 'shifty:dashboardLastSeenAt'
const SEEN_PAIN_POINTS_KEY = 'shifty:dashboardSeenPainPoints'

function readDefaultTeamId(): string | null {
  return localStorage.getItem(DEFAULT_TEAM_KEY)
}

function readNotificationsEnabled(): boolean {
  const raw = localStorage.getItem(NOTIFICATIONS_ENABLED_KEY)
  return raw === null ? true : raw === 'true'
}

function readLastSeenAt(): string | null {
  return localStorage.getItem(LAST_SEEN_AT_KEY)
}

function readSeenPainPointKeys(): string[] {
  const raw = localStorage.getItem(SEEN_PAIN_POINTS_KEY)
  if (!raw) return []
  try {
    const parsed = JSON.parse(raw)
    return Array.isArray(parsed) ? parsed : []
  } catch {
    return []
  }
}

// Local-only preferences (no backend concept of per-user settings exists yet) — persisted
// to localStorage so they survive a reload, unlike the Dienstplan's URL-query-string filter
// state (issue #41), which only survives within a session's navigation.
export const useSettingsStore = defineStore('settings', {
  state: () => ({
    defaultTeamId: readDefaultTeamId() as string | null,
    // issue #59: "notifications" here means client-side highlighting of Dashboard Pain
    // Points that are new since the last Dashboard visit — NOT email/push (no mail-sending
    // integration exists in this codebase, see CLAUDE.md). Toggle for that highlighting.
    notificationsEnabled: readNotificationsEnabled(),
    // Digest state for the above: when the Dashboard was last viewed, and the identity set
    // of Pain Points seen at that visit (see DashboardView.vue's `painPointKey` for how an
    // identity is derived — PainPointDto has no per-issue timestamp to diff against).
    lastSeenAt: readLastSeenAt() as string | null,
    seenPainPointKeys: readSeenPainPointKeys(),
  }),
  actions: {
    setDefaultTeamId(teamId: string | null) {
      this.defaultTeamId = teamId
      if (teamId) localStorage.setItem(DEFAULT_TEAM_KEY, teamId)
      else localStorage.removeItem(DEFAULT_TEAM_KEY)
    },
    setNotificationsEnabled(enabled: boolean) {
      this.notificationsEnabled = enabled
      localStorage.setItem(NOTIFICATIONS_ENABLED_KEY, String(enabled))
    },
    // Called once per Dashboard load. Callers should read `lastSeenAt`/`seenPainPointKeys`
    // (to diff the just-fetched Pain Points against the *previous* visit) before calling
    // this, since it immediately overwrites both with the current visit's data.
    markDashboardSeen(painPointKeys: string[]) {
      this.lastSeenAt = new Date().toISOString()
      this.seenPainPointKeys = painPointKeys
      localStorage.setItem(LAST_SEEN_AT_KEY, this.lastSeenAt)
      localStorage.setItem(SEEN_PAIN_POINTS_KEY, JSON.stringify(painPointKeys))
    },
  },
})
