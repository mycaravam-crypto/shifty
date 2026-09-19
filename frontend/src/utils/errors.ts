import axios from 'axios'

// Every controller in this app returns a plain string body on BadRequest(...)/Conflict(...)
// (e.g. "Mitarbeiter kann nicht gelöscht werden: ..."), which axios hands back as
// `e.response.data` already being that string — this was already being extracted ad hoc in a
// few create/update flows (`axios.isAxiosError(e) && e.response?.data ? e.response.data : ...`)
// but not in most delete flows, so a delete failure always showed a generic hardcoded message
// even once the backend started returning a specific reason (e.g. issue behind "delete a user
// fails with no significant error message" — EmployeesController now 409s with a clear reason,
// but the UI needs to actually surface it). Centralized here so every catch block gets the
// backend's real reason when there is one, a friendly message for the cases where there
// structurally can't be one (network failure, permissions), and only falls back to its own
// generic text as a last resort.
// ASP.NET Core's automatic model-validation 400 (e.g. a missing [Required] field) returns this
// shape instead of a plain string — pulled into its own helper just to keep
// extractErrorMessage's branching within the linter's cognitive-complexity budget.
function messageFromProblemDetails(data: unknown): string | null {
  if (!data || typeof data !== 'object') return null
  const problem = data as { title?: string; detail?: string; errors?: Record<string, string[]> }
  const messages = problem.errors ? Object.values(problem.errors).flat() : []
  if (messages.length) return messages.join(' ')
  return problem.detail ?? problem.title ?? null
}

function messageForStatus(status: number): string | null {
  if (status === 401) return 'Sitzung abgelaufen. Bitte erneut anmelden.'
  if (status === 403) return 'Keine Berechtigung für diese Aktion.'
  return null
}

export function extractErrorMessage(e: unknown, fallback: string): string {
  if (!axios.isAxiosError(e)) return fallback

  if (!e.response) {
    return 'Keine Verbindung zum Server möglich. Bitte Internetverbindung prüfen und erneut versuchen.'
  }

  const data: unknown = e.response.data
  if (typeof data === 'string' && data.trim()) return data

  return messageFromProblemDetails(data) ?? messageForStatus(e.response.status) ?? fallback
}
