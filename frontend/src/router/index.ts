import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', name: 'schedule', component: () => import('../views/Schedule/ScheduleView.vue') },
    {
      path: '/employees',
      name: 'employees',
      component: () => import('../views/Employees/EmployeesView.vue'),
    },
    {
      path: '/settings',
      name: 'settings',
      component: () => import('../views/Settings/SettingsView.vue'),
    },
  ],
})

export default router
