import { ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSettingsStore } from '@/stores/settings'
import type { Schedule, Team } from '@/views/Schedule/types'

// issue #41: search/team filter persisted to the URL query string, so navigating away and
// back (or bookmarking/sharing a filtered view) doesn't lose the selection. Also resolves
// the issue #43 Settings default-team-filter and the issue #30/#31 dashboard `?scheduleId=`
// deep link, both of which interact with the same query-string state.
export function useScheduleFilters() {
  const route = useRoute()
  const router = useRouter()
  const settings = useSettingsStore()

  const search = ref('')
  const teamFilter = ref('')
  const searchInputRef = ref<HTMLInputElement | null>(null)

  function filterQuery(): Record<string, string> {
    const query: Record<string, string> = {}
    if (search.value) query.q = search.value
    if (teamFilter.value) query.team = teamFilter.value
    return query
  }
  watch([search, teamFilter], () => {
    router.replace({ query: filterQuery() })
  })

  // Reads ?q=&team= from the URL into local state. Returns whether ?team= was present, so
  // the caller can decide whether the Settings-configured default team should still apply.
  function applyRouteQuery(): boolean {
    const hadTeamQuery = typeof route.query.team === 'string'
    if (typeof route.query.q === 'string') search.value = route.query.q
    if (hadTeamQuery) teamFilter.value = route.query.team as string
    return hadTeamQuery
  }

  // issue #43: fall back to the Settings-configured default team filter, but only when the
  // URL didn't already specify one — an explicit ?team= (e.g. a shared/bookmarked link)
  // always wins over the user's own default.
  function applyDefaultTeamId(hadTeamQuery: boolean, teams: Team[]) {
    if (!hadTeamQuery && settings.defaultTeamId) {
      if (teams.some((t) => t.id === settings.defaultTeamId)) {
        teamFilter.value = settings.defaultTeamId
      }
    }
  }

  // Deep link from the dashboard's pain-point/planning-status links (issue #30/#31): find the
  // linked Schedule (if any) and strip `scheduleId` from the URL once consumed, keeping the
  // q/team filter params intact. Returns the matched Schedule so the caller can jump
  // `anchorDate` to its month.
  function consumeScheduleDeepLink(schedules: Schedule[]): Schedule | undefined {
    const linkedId = route.query.scheduleId
    if (typeof linkedId !== 'string') return undefined
    const linked = schedules.find((s) => s.id === linkedId)
    router.replace({ query: filterQuery() })
    return linked
  }

  return {
    search,
    teamFilter,
    searchInputRef,
    filterQuery,
    applyRouteQuery,
    applyDefaultTeamId,
    consumeScheduleDeepLink,
  }
}
