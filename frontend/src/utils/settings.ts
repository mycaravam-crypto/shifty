// Shared localStorage-backed user settings — kept in one place so the key name isn't
// duplicated (and liable to drift) between SettingsView (writes it) and ScheduleView (reads it
// as the fallback when no filter has been persisted yet, per issue #41).
const DEFAULT_TEAM_STORAGE_KEY = 'schichtplaner.defaultTeamFilter'

export function getDefaultTeamFilter(): string {
  try {
    return localStorage.getItem(DEFAULT_TEAM_STORAGE_KEY) ?? ''
  } catch {
    return ''
  }
}

export function setDefaultTeamFilter(teamId: string) {
  try {
    if (teamId) localStorage.setItem(DEFAULT_TEAM_STORAGE_KEY, teamId)
    else localStorage.removeItem(DEFAULT_TEAM_STORAGE_KEY)
  } catch {
    // Private-browsing/storage-full edge cases: the setting just won't stick — not worth
    // surfacing to the user over.
  }
}
