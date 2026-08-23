// ISO date string ("2026-08-23") -> German display format ("23.08.2026").
// Reorders the parts directly rather than going through Date/toLocaleDateString,
// which would shift the day in timezones behind UTC.
export function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const [year, month, day] = iso.split('-')
  return `${day}.${month}.${year}`
}
