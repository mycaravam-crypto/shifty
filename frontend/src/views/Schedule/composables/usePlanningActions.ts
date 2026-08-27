import { ref } from 'vue'
import axios from 'axios'
import api from '@/services/api'
import { useToastStore } from '@/stores/toast'
import { addMonths, lastOfMonth, monthFmt, toIso } from '@/views/Schedule/format'
import type { DragPayload } from './useScheduleDnD'
import type { usePlanningBoard } from './usePlanningBoard'

// Create/move/copy mutations for the Dienstplan (issue #73's `usePlanningActions`). Reads and
// mutates the board's state directly (same access pattern ScheduleView.vue used before the
// decomposition) rather than duplicating it.
export function usePlanningActions(board: ReturnType<typeof usePlanningBoard>) {
  const toast = useToastStore()
  const creatingSchedule = ref(false)
  const copyingMonth = ref(false)
  const publishing = ref(false)
  const archiving = ref(false)
  const confirmingArchive = ref(false)

  async function onCreateSchedule() {
    creatingSchedule.value = true
    try {
      await api.post('/schedules', {
        name: board.monthLabel.value,
        startDate: board.monthStartIso.value,
        endDate: board.monthEndIso.value,
      })
      board.schedules.value = (await api.get('/schedules')).data
      toast.success('Dienstplan angelegt.')
    } catch {
      toast.error('Dienstplan konnte nicht angelegt werden.')
    } finally {
      creatingSchedule.value = false
    }
  }

  // issue #68/#79: the button is already disabled while blockingErrorCount > 0, but re-checks
  // the 409 case too (e.g. another manager changed something between page load and this click).
  async function onPublish() {
    if (!board.currentSchedule.value) return
    publishing.value = true
    try {
      const res = await api.post(`/schedules/${board.currentSchedule.value.id}/publish`)
      board.updateCurrentScheduleFrom(res.data)
      toast.success('Dienstplan veröffentlicht.')
    } catch (err) {
      if (axios.isAxiosError(err) && err.response?.status === 409) {
        await board.loadDetail()
        toast.error('Veröffentlichen nicht möglich — es bestehen noch ungelöste Fehler.')
      } else {
        toast.error('Dienstplan konnte nicht veröffentlicht werden.')
      }
    } finally {
      publishing.value = false
    }
  }

  async function onArchiveConfirmed() {
    if (!board.currentSchedule.value) return
    archiving.value = true
    try {
      const res = await api.post(`/schedules/${board.currentSchedule.value.id}/archive`)
      board.updateCurrentScheduleFrom(res.data)
      toast.success('Dienstplan archiviert.')
    } catch {
      toast.error('Dienstplan konnte nicht archiviert werden.')
    } finally {
      archiving.value = false
      confirmingArchive.value = false
    }
  }

  async function onCopyMonth() {
    if (!board.currentSchedule.value) return
    copyingMonth.value = true
    try {
      const nextStart = addMonths(board.anchorDate.value, 1)
      // issue #82: the whole copy (target-schedule creation + every assignment) is computed
      // and applied atomically server-side in one request, instead of a per-assignment POST
      // loop that could leave a partial copy behind if one request in the middle failed.
      const res = await api.post(`/schedules/${board.currentSchedule.value.id}/copy`, {
        targetName: monthFmt.format(nextStart),
        targetStartDate: toIso(nextStart),
        targetEndDate: toIso(lastOfMonth(nextStart)),
      })
      if (!board.schedules.value.some((s) => s.id === res.data.target.id)) {
        board.schedules.value.push(res.data.target)
      }
      toast.success('Monat kopiert.')
      board.nextMonth()
    } catch (err) {
      if (axios.isAxiosError(err) && err.response?.status === 409) {
        board.error.value = 'Nächster Monat hat bereits Schichten — Kopieren abgebrochen.'
        toast.error(board.error.value)
      } else {
        toast.error('Monat konnte nicht kopiert werden.')
      }
    } finally {
      copyingMonth.value = false
    }
  }

  async function performDrop(payload: DragPayload, employeeId: string, dateIso: string) {
    if (!board.currentSchedule.value) return

    try {
      if (payload.kind === 'shiftType') {
        const shiftType = board.shiftTypeById(payload.shiftTypeId!)
        if (!shiftType) return
        await api.post(`/schedules/${board.currentSchedule.value.id}/assignments`, {
          employeeId,
          shiftTypeId: shiftType.id,
          date: dateIso,
          startTime: shiftType.startTime,
          endTime: shiftType.endTime,
          breakMinutes: shiftType.breakMinutes,
        })
      } else if (payload.kind === 'assignment') {
        const assignment = board.assignments.value.find((a) => a.id === payload.assignmentId)
        if (!assignment) return
        await api.put(`/assignments/${assignment.id}`, {
          employeeId,
          shiftTypeId: assignment.shiftTypeId,
          date: dateIso,
          startTime: assignment.startTime,
          endTime: assignment.endTime,
          breakMinutes: assignment.breakMinutes,
          breakStartTime: assignment.breakStartTime,
          rowVersion: assignment.rowVersion,
        })
      }
      await board.loadDetail()
    } catch (err) {
      // issue #156: someone else changed this assignment since the grid last loaded — reload
      // instead of leaving the grid showing a move that didn't actually apply.
      if (axios.isAxiosError(err) && err.response?.status === 409) {
        toast.error('Schicht wurde inzwischen von jemand anderem geändert — Ansicht aktualisiert.')
        await board.loadDetail()
      } else {
        toast.error('Schicht konnte nicht gespeichert werden.')
      }
    }
  }

  async function onAssignmentUpdated() {
    await board.loadDetail()
  }

  return {
    creatingSchedule,
    copyingMonth,
    publishing,
    archiving,
    confirmingArchive,
    onCreateSchedule,
    onPublish,
    onArchiveConfirmed,
    onCopyMonth,
    performDrop,
    onAssignmentUpdated,
  }
}
