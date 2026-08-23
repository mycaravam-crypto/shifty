import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('../views/Login/LoginView.vue'),
      meta: { public: true },
    },
    { path: '/', name: 'schedule', component: () => import('../views/Schedule/ScheduleView.vue') },
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
