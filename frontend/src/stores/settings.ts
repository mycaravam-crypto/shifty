import { defineStore } from 'pinia'

const DEFAULT_TEAM_KEY = 'shifty:defaultTeamId'

function readDefaultTeamId(): string | null {
  return localStorage.getItem(DEFAULT_TEAM_KEY)
}

// Local-only preferences (no backend concept of per-user settings exists yet) — persisted
// to localStorage so they survive a reload, unlike the Dienstplan's URL-query-string filter
// state (issue #41), which only survives within a session's navigation.
export const useSettingsStore = defineStore('settings', {
  state: () => ({
    defaultTeamId: readDefaultTeamId() as string | null,
  }),
  actions: {
    setDefaultTeamId(teamId: string | null) {
      this.defaultTeamId = teamId
      if (teamId) localStorage.setItem(DEFAULT_TEAM_KEY, teamId)
      else localStorage.removeItem(DEFAULT_TEAM_KEY)
    },
  },
})
