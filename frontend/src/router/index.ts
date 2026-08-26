import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('../views/Login/LoginView.vue'),
      meta: { public: true },
    },
    // issue #74: the Dienstplan is split into a compact month-overview (the default landing
    // view — coverage/absence/conflict glance + navigation) and a week-scoped detail editor
    // carrying the full drag-and-drop/validation experience the single month grid used to try
    // to be all at once. `Schedule` itself stays month-scoped server-side — this is a
    // frontend presentation split only.
    {
      path: '/',
      name: 'schedule',
      component: () => import('../views/Schedule/MonthOverviewView.vue'),
    },
    {
      path: '/dienstplan/woche/:date',
      name: 'schedule-week',
      component: () => import('../views/Schedule/ScheduleView.vue'),
    },
    {
      path: '/dashboard',
      name: 'dashboard',
      component: () => import('../views/Dashboard/DashboardView.vue'),
    },
    {
      path: '/employees',
      name: 'employees',
      component: () => import('../views/Employees/EmployeesView.vue'),
    },
    {
      path: '/stammdaten',
      name: 'stammdaten',
      component: () => import('../views/Stammdaten/StammdatenView.vue'),
    },
    {
      path: '/settings',
      name: 'settings',
      component: () => import('../views/Settings/SettingsView.vue'),
    },
  ],
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  if (!auth.ready) await auth.tryRefresh()
  if (!to.meta.public && !auth.accessToken)
    return { name: 'login', query: { redirect: to.fullPath } }
  if (to.name === 'login' && auth.accessToken) return { name: 'schedule' }
})

export default router
