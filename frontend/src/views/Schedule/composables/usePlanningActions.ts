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
        })
      }
      await board.loadDetail()
    } catch {
      toast.error('Schicht konnte nicht gespeichert werden.')
    }
  }

  async function onAssignmentUpdated() {
    await board.loadDetail()
  }

  return {
    creatingSchedule,
    copyingMonth,
    onCreateSchedule,
    onCopyMonth,
    performDrop,
    onAssignmentUpdated,
  }
}
