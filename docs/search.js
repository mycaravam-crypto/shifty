window.docwizzSearch = [
  {
    "name": "/",
    "kind": "route",
    "page": "frontend.md",
    "summary": null
  },
  {
    "name": "/dashboard",
    "kind": "route",
    "page": "frontend.md",
    "summary": null
  },
  {
    "name": "/dienstplan/woche/:date",
    "kind": "route",
    "page": "frontend.md",
    "summary": null
  },
  {
    "name": "/employees",
    "kind": "route",
    "page": "frontend.md",
    "summary": null
  },
  {
    "name": "/login",
    "kind": "route",
    "page": "frontend.md",
    "summary": null
  },
  {
    "name": "/settings",
    "kind": "route",
    "page": "frontend.md",
    "summary": null
  },
  {
    "name": "/stammdaten",
    "kind": "route",
    "page": "frontend.md",
    "summary": null
  },
  {
    "name": "Absence",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Employees.md#absence",
    "summary": null
  },
  {
    "name": "Absence",
    "kind": "interface",
    "page": "modules/frontend-src-views-Schedule.md#absence",
    "summary": null
  },
  {
    "name": "AbsenceDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#absencedto",
    "summary": null
  },
  {
    "name": "AbsencesController",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#absencescontroller",
    "summary": null
  },
  {
    "name": "AbsenceType",
    "kind": "enum",
    "page": "modules/src-ShiftPlanner.Domain-Employees.md#absencetype",
    "summary": null
  },
  {
    "name": "AbsenceValidator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Application-Validation.md#absencevalidator",
    "summary": null
  },
  {
    "name": "AccessTokenResponse",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#accesstokenresponse",
    "summary": null
  },
  {
    "name": "AddAbsence",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#addabsence",
    "summary": null
  },
  {
    "name": "AddHoursAdjustments",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#addhoursadjustments",
    "summary": null
  },
  {
    "name": "AddRefreshTokens",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#addrefreshtokens",
    "summary": null
  },
  {
    "name": "AllowedHosts",
    "kind": "config",
    "page": "views/deployment.md#configuration",
    "summary": null
  },
  {
    "name": "ApiKey",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Common.md#apikey",
    "summary": null
  },
  {
    "name": "ApiKeyAuthenticationHandler",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Middleware.md#apikeyauthenticationhandler",
    "summary": null
  },
  {
    "name": "ApiKeyScope",
    "kind": "enum",
    "page": "modules/src-ShiftPlanner.Domain-Common.md#apikeyscope",
    "summary": null
  },
  {
    "name": "ApiWriteRequirement",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Authorization.md#apiwriterequirement",
    "summary": null
  },
  {
    "name": "ApiWriteRequirementHandler",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Authorization.md#apiwriterequirementhandler",
    "summary": null
  },
  {
    "name": "App",
    "kind": "component",
    "page": "modules/frontend-src.md#app",
    "summary": null
  },
  {
    "name": "ApplicationDbContext",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence.md#applicationdbcontext",
    "summary": null
  },
  {
    "name": "ApplicationDbContextModelSnapshot",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#applicationdbcontextmodelsnapshot",
    "summary": null
  },
  {
    "name": "ApplicationUser",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence.md#applicationuser",
    "summary": null
  },
  {
    "name": "AppShell",
    "kind": "component",
    "page": "modules/frontend-src-components.md#appshell",
    "summary": "Global \"Hilfe\" entry point (stakeholder-facing German explanations, data/helpTopics.ts) — reachable from every page, not just the Dienstplan's own separate keyboard-shortcuts panel. Opens pre-selected to the topic matching the page it was opened from."
  },
  {
    "name": "Assignment",
    "kind": "interface",
    "page": "modules/frontend-src-views-Schedule.md#assignment",
    "summary": null
  },
  {
    "name": "AuditLog",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Common.md#auditlog",
    "summary": null
  },
  {
    "name": "AuditLogValueSnapshots",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#auditlogvaluesnapshots",
    "summary": null
  },
  {
    "name": "AuditSaveChangesInterceptor",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence.md#auditsavechangesinterceptor",
    "summary": "Builds AuditLog rows from the DbContext's change tracker on every save (readme.md §23/§1, \"Änderungen nachvollziehbar speichern\"). This used to be a SaveChangesAsync override on `ApplicationDbContext` itself, which had two problems: EF Core has four SaveChanges entry points (SaveChanges(), SaveChanges(bool), SaveChangesAsync(CancellationToken), SaveChangesAsync(bool, CancellationToken)) and a plain override only intercepts the one overload it declares — any caller reaching a different one (notably the sync SaveChanges()) silently skipped the audit write; and it mixed persistence with audit business logic and raw `ClaimsPrincipal` access in the same class as the entity model. A `SaveChangesInterceptor` fixes both: its `SavingChanges`/`SavingChangesAsync` pair is EF Core's single funnel for all four SaveChanges paths, and it lives outside the DbContext entirely."
  },
  {
    "name": "AuthController",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#authcontroller",
    "summary": null
  },
  {
    "name": "AutoFillCommitItem",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#autofillcommititem",
    "summary": null
  },
  {
    "name": "AutoFillCommitRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#autofillcommitrequest",
    "summary": null
  },
  {
    "name": "AutoFillModal",
    "kind": "component",
    "page": "modules/frontend-src-views-Schedule.md#autofillmodal",
    "summary": "Dropped rows are tracked by index rather than spliced out of `proposals` — keeps the \"N vorgeschlagen, M verworfen\" summary stable while the manager reviews."
  },
  {
    "name": "AutoFillProposal",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Suggestions.md#autofillproposal",
    "summary": null
  },
  {
    "name": "AutoFillProposalDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#autofillproposaldto",
    "summary": null
  },
  {
    "name": "BreakMinutesValidator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Application-Validation.md#breakminutesvalidator",
    "summary": null
  },
  {
    "name": "BreakStartTime",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#breakstarttime",
    "summary": null
  },
  {
    "name": "Bundesland",
    "kind": "enum",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#bundesland",
    "summary": null
  },
  {
    "name": "ConfirmDialog",
    "kind": "component",
    "page": "modules/frontend-src-components.md#confirmdialog",
    "summary": null
  },
  {
    "name": "ConnectionStrings:Default",
    "kind": "config",
    "page": "views/deployment.md#configuration",
    "summary": null
  },
  {
    "name": "ConsecutiveDaysValidator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Application-Validation.md#consecutivedaysvalidator",
    "summary": null
  },
  {
    "name": "Contract",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Contracts.md#contract",
    "summary": null
  },
  {
    "name": "Contract",
    "kind": "interface",
    "page": "modules/frontend-src-views-Schedule.md#contract",
    "summary": null
  },
  {
    "name": "ContractDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#contractdto",
    "summary": null
  },
  {
    "name": "ContractHourlyRate",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#contracthourlyrate",
    "summary": null
  },
  {
    "name": "ContractMonthlyHours",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#contractmonthlyhours",
    "summary": null
  },
  {
    "name": "ContractsController",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#contractscontroller",
    "summary": null
  },
  {
    "name": "ContractValidator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Application-Validation.md#contractvalidator",
    "summary": null
  },
  {
    "name": "CopyMonthRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#copymonthrequest",
    "summary": null
  },
  {
    "name": "CopyMonthResponse",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#copymonthresponse",
    "summary": null
  },
  {
    "name": "Cors:FrontendOrigin",
    "kind": "config",
    "page": "views/deployment.md#configuration",
    "summary": null
  },
  {
    "name": "CostBreakdownDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Dashboard.md#costbreakdowndto",
    "summary": null
  },
  {
    "name": "CostOverviewDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Dashboard.md#costoverviewdto",
    "summary": null
  },
  {
    "name": "Coverage",
    "kind": "interface",
    "page": "modules/frontend-src-views-Schedule-composables.md#coverage",
    "summary": null
  },
  {
    "name": "CoverageDayDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Dashboard.md#coveragedaydto",
    "summary": null
  },
  {
    "name": "CoverageStatus",
    "kind": "enum",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#coveragestatus",
    "summary": null
  },
  {
    "name": "CreateAbsenceRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#createabsencerequest",
    "summary": null
  },
  {
    "name": "CreateAssignmentRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#createassignmentrequest",
    "summary": null
  },
  {
    "name": "CreateContractRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#createcontractrequest",
    "summary": null
  },
  {
    "name": "CreateEmployeeRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#createemployeerequest",
    "summary": null
  },
  {
    "name": "CreateHoursAdjustmentRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#createhoursadjustmentrequest",
    "summary": null
  },
  {
    "name": "CreateScheduleRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#createschedulerequest",
    "summary": null
  },
  {
    "name": "CreateShiftTypeRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#createshifttyperequest",
    "summary": null
  },
  {
    "name": "CreateStaffingRequirementRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#createstaffingrequirementrequest",
    "summary": null
  },
  {
    "name": "CreateTeamRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#createteamrequest",
    "summary": null
  },
  {
    "name": "DashboardAggregator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Application-Dashboard.md#dashboardaggregator",
    "summary": null
  },
  {
    "name": "DashboardController",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#dashboardcontroller",
    "summary": null
  },
  {
    "name": "DashboardDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Dashboard.md#dashboarddto",
    "summary": null
  },
  {
    "name": "DashboardKpisDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Dashboard.md#dashboardkpisdto",
    "summary": null
  },
  {
    "name": "DashboardView",
    "kind": "component",
    "page": "modules/frontend-src-views-Dashboard.md#dashboardview",
    "summary": "issue #56: regular/night/Sunday/holiday cost breakdown restored from the dashboard mockup's trimmed scope — mapped onto WageCalculator's actual surcharge types rather than a literal \"overtime\" cost bucket, since there's no separate overtime pay rate anywhere in this app."
  },
  {
    "name": "DELETE /api/absences/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "DELETE /api/assignments/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "DELETE /api/contracts/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "DELETE /api/employees/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "DELETE /api/hours-adjustments/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "DELETE /api/staffing-requirements/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "DragPayload",
    "kind": "interface",
    "page": "modules/frontend-src-views-Schedule-composables.md#dragpayload",
    "summary": null
  },
  {
    "name": "DragState",
    "kind": "interface",
    "page": "modules/frontend-src-views-Schedule-composables.md#dragstate",
    "summary": null
  },
  {
    "name": "EligibilityValidator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Application-Validation.md#eligibilityvalidator",
    "summary": null
  },
  {
    "name": "Employee",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Employees.md#employee",
    "summary": null
  },
  {
    "name": "Employee",
    "kind": "interface",
    "page": "modules/frontend-src-views-Schedule.md#employee",
    "summary": null
  },
  {
    "name": "EmployeeDetailModal",
    "kind": "component",
    "page": "modules/frontend-src-views-Employees.md#employeedetailmodal",
    "summary": "issue #165: HoursAdjustment writes are AdminWrite-only server-side (a manual correction to a computed balance, stricter than the ManagerWrite Absence/Contract use) — gate the create/delete controls the same way client-side; the list itself stays visible to any signed-in Staff user (ApiRead), same read/write split the backend enforces."
  },
  {
    "name": "EmployeeDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#employeedto",
    "summary": null
  },
  {
    "name": "EmployeeId",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Domain-Common.md#employeeid",
    "summary": null
  },
  {
    "name": "EmployeePreferences",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#employeepreferences",
    "summary": null
  },
  {
    "name": "EmployeeScheduleRow",
    "kind": "component",
    "page": "modules/frontend-src-views-Schedule.md#employeeschedulerow",
    "summary": null
  },
  {
    "name": "EmployeesController",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#employeescontroller",
    "summary": null
  },
  {
    "name": "EmployeeShiftTypeEligibility",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#employeeshifttypeeligibility",
    "summary": null
  },
  {
    "name": "EmployeeSidebar",
    "kind": "component",
    "page": "modules/frontend-src-views-Schedule.md#employeesidebar",
    "summary": "The \"Nach Schicht\" view's palette (requested directly — with only a handful of shift types but 20+ employees, dragging shifts onto employee rows is the wrong way round). Employees are listed here instead of as grid rows, each draggable onto a (shiftType, date) cell — the same per-employee Xh/Yh/Übertrag/Lohnkosten readouts EmployeeScheduleRow.vue shows per row move here, since there's no employee row left to show them on in this view. Eligibility hints (requested directly, follow-up): dragging blind meant only finding out an employee couldn't take a shift via a toast after the drop. Each chip now shows a dot per visible ShiftType (dim/ringed when the employee isn't eligible for it — same rule EligibilityValidator itself uses, an empty eligible list means unrestricted) and an \"Abwesend\" badge when they're absent on any day of the displayed period — both purely informational, dropping on a cell the hints flag still works the same as before (the backend validator is still the source of truth), this just surfaces what it would say up front."
  },
  {
    "name": "EmployeesView",
    "kind": "component",
    "page": "modules/frontend-src-views-Employees.md#employeesview",
    "summary": "Set only after a plain delete 409s because the employee still has shifts assigned — offers a second, more explicit confirmation for actually removing the employee *and* that history (e.g. leftover test/demo data nobody wants to keep), rather than requiring the shifts to be removed one by one or the employee kept around deactivated."
  },
  {
    "name": "EmployeeUtilizationDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Dashboard.md#employeeutilizationdto",
    "summary": null
  },
  {
    "name": "EndsNextDayFlag",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#endsnextdayflag",
    "summary": null
  },
  {
    "name": "frontend",
    "kind": "module",
    "page": "modules/frontend.md",
    "summary": null
  },
  {
    "name": "frontend (frontend/package.json)",
    "kind": "project",
    "page": "projects/frontend-da6498268e99.md",
    "summary": "Project overview, modules and components"
  },
  {
    "name": "frontend/src",
    "kind": "module",
    "page": "modules/frontend-src.md",
    "summary": null
  },
  {
    "name": "frontend/src/components",
    "kind": "module",
    "page": "modules/frontend-src-components.md",
    "summary": null
  },
  {
    "name": "frontend/src/data",
    "kind": "module",
    "page": "modules/frontend-src-data.md",
    "summary": null
  },
  {
    "name": "frontend/src/router",
    "kind": "module",
    "page": "modules/frontend-src-router.md",
    "summary": null
  },
  {
    "name": "frontend/src/services",
    "kind": "module",
    "page": "modules/frontend-src-services.md",
    "summary": null
  },
  {
    "name": "frontend/src/stores",
    "kind": "module",
    "page": "modules/frontend-src-stores.md",
    "summary": null
  },
  {
    "name": "frontend/src/utils",
    "kind": "module",
    "page": "modules/frontend-src-utils.md",
    "summary": null
  },
  {
    "name": "frontend/src/views/Dashboard",
    "kind": "module",
    "page": "modules/frontend-src-views-Dashboard.md",
    "summary": null
  },
  {
    "name": "frontend/src/views/Employees",
    "kind": "module",
    "page": "modules/frontend-src-views-Employees.md",
    "summary": null
  },
  {
    "name": "frontend/src/views/Login",
    "kind": "module",
    "page": "modules/frontend-src-views-Login.md",
    "summary": null
  },
  {
    "name": "frontend/src/views/Schedule",
    "kind": "module",
    "page": "modules/frontend-src-views-Schedule.md",
    "summary": null
  },
  {
    "name": "frontend/src/views/Schedule/composables",
    "kind": "module",
    "page": "modules/frontend-src-views-Schedule-composables.md",
    "summary": null
  },
  {
    "name": "frontend/src/views/Settings",
    "kind": "module",
    "page": "modules/frontend-src-views-Settings.md",
    "summary": null
  },
  {
    "name": "frontend/src/views/Stammdaten",
    "kind": "module",
    "page": "modules/frontend-src-views-Stammdaten.md",
    "summary": null
  },
  {
    "name": "GermanPublicHolidays",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#germanpublicholidays",
    "summary": null
  },
  {
    "name": "GET /api/absences/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/contracts/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/dashboard",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/employees",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/employees/{employeeId:guid}/absences",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/employees/{employeeId:guid}/contracts",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/employees/{employeeId:guid}/hours-adjustments",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/employees/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/employees/{id:guid}/eligible-shift-types",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/employees/{id:guid}/hours-balance",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/employees/{id:guid}/hours-report",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/employees/{id:guid}/shift-type-preferences",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/employees/{id:guid}/weekday-preferences",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/hours-adjustments/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/planning-board",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/public-holidays",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/schedules",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/schedules/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/schedules/{id:guid}/auto-fill-preview",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/schedules/{id:guid}/suggestions",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/schedules/{id:guid}/validate",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/shift-types",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/staffing-requirements",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/staffing-requirements/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /api/teams",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GET /health",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "GlobalExceptionMiddleware",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Middleware.md#globalexceptionmiddleware",
    "summary": null
  },
  {
    "name": "HelpBlock",
    "kind": "interface",
    "page": "modules/frontend-src-data.md#helpblock",
    "summary": null
  },
  {
    "name": "HelpModal",
    "kind": "component",
    "page": "modules/frontend-src-components.md#helpmodal",
    "summary": null
  },
  {
    "name": "HelpTopic",
    "kind": "interface",
    "page": "modules/frontend-src-data.md#helptopic",
    "summary": null
  },
  {
    "name": "HoursAdjustment",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Employees.md#hoursadjustment",
    "summary": null
  },
  {
    "name": "HoursAdjustmentDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#hoursadjustmentdto",
    "summary": null
  },
  {
    "name": "HoursAdjustmentsController",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#hoursadjustmentscontroller",
    "summary": null
  },
  {
    "name": "HoursBalanceCalculator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#hoursbalancecalculator",
    "summary": null
  },
  {
    "name": "HoursReportDayDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#hoursreportdaydto",
    "summary": null
  },
  {
    "name": "HoursReportDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#hoursreportdto",
    "summary": null
  },
  {
    "name": "HoursReportModal",
    "kind": "component",
    "page": "modules/frontend-src-views-Employees.md#hoursreportmodal",
    "summary": "issue #165: on-screen month picker for the printable \"Monatsbericht\" — fetches GET /employees/{id}/hours-report?from=&to= for the chosen month and hands it to HoursReportPrintSheet, then triggers window.print(). Deliberately not built on ModalShell: this needs precise control over what's print:hidden vs. what prints (the picker itself must vanish on print, the report sheet must not), which ModalShell's shared overlay doesn't expose."
  },
  {
    "name": "HoursReportPrintSheet",
    "kind": "component",
    "page": "modules/frontend-src-views-Employees.md#hoursreportprintsheet",
    "summary": "issue #165: the printable, signable monthly Ist/Soll report — same \"separate print-only component, its own light/ink-friendly styling\" pattern as views/Schedule/SchedulePrintSheet.vue (see that file's own header comment for why: reusing a dark interactive layout for print fights the theme at every turn). Renders nothing on screen (`hidden print:block`) and is the only thing visible on the page when printing, alongside HoursReportModal's own on-screen picker being `print:hidden`."
  },
  {
    "name": "HoursReportShiftDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#hoursreportshiftdto",
    "summary": null
  },
  {
    "name": "Jwt:Audience",
    "kind": "config",
    "page": "views/deployment.md#configuration",
    "summary": null
  },
  {
    "name": "Jwt:Issuer",
    "kind": "config",
    "page": "views/deployment.md#configuration",
    "summary": null
  },
  {
    "name": "Jwt:SigningKey",
    "kind": "config",
    "page": "views/deployment.md#configuration",
    "summary": null
  },
  {
    "name": "JwtOptions",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Authentication.md#jwtoptions",
    "summary": null
  },
  {
    "name": "JwtOptionsValidator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Authentication.md#jwtoptionsvalidator",
    "summary": null
  },
  {
    "name": "JwtTokenFactory",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Authentication.md#jwttokenfactory",
    "summary": null
  },
  {
    "name": "LaborCostBreakdown",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#laborcostbreakdown",
    "summary": null
  },
  {
    "name": "Logging:LogLevel:Default",
    "kind": "config",
    "page": "views/deployment.md#configuration",
    "summary": null
  },
  {
    "name": "Logging:LogLevel:Microsoft.AspNetCore",
    "kind": "config",
    "page": "views/deployment.md#configuration",
    "summary": null
  },
  {
    "name": "LoginRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#loginrequest",
    "summary": null
  },
  {
    "name": "LoginView",
    "kind": "component",
    "page": "modules/frontend-src-views-Login.md#loginview",
    "summary": null
  },
  {
    "name": "ModalShell",
    "kind": "component",
    "page": "modules/frontend-src-components.md#modalshell",
    "summary": "A tap that opens this modal (e.g. an assignment chip on a touch device) is followed by the browser's synthetic \"click\" compatibility event at the same coordinates — which, once the backdrop exists, lands squarely on it and would immediately close what the tap just opened. Ignore backdrop clicks in the brief window right after mount so only a real outside tap closes the modal."
  },
  {
    "name": "MonthOverviewView",
    "kind": "component",
    "page": "modules/frontend-src-views-Schedule.md#monthoverviewview",
    "summary": "issue #74: the Dienstplan's default landing view — a compact month-wide coverage/absence/ conflict glance, replacing what used to be the single full-month drag-and-drop grid trying to be both an editor and an overview at once. Detailed editing (drag-and-drop, the assignment modal, the grouped validation panel, keyboard shortcuts) now lives in the week-scoped ScheduleView.vue, reached by clicking a week/day header here. `Schedule` itself stays month-scoped server-side — this reuses the exact same usePlanningBoard/usePlanningActions composables the week view uses, just rendered as a dense per-day-per-employee grid instead of full shift chips."
  },
  {
    "name": "PainPointDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Dashboard.md#painpointdto",
    "summary": null
  },
  {
    "name": "PainSeverity",
    "kind": "enum",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#painseverity",
    "summary": null
  },
  {
    "name": "Phase1Foundation",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#phase1foundation",
    "summary": null
  },
  {
    "name": "PlanningBoardAggregator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Application-Planning.md#planningboardaggregator",
    "summary": null
  },
  {
    "name": "PlanningBoardController",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#planningboardcontroller",
    "summary": null
  },
  {
    "name": "PlanningBoardDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#planningboarddto",
    "summary": null
  },
  {
    "name": "PlanningBoardEmployeeDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#planningboardemployeedto",
    "summary": null
  },
  {
    "name": "PlanningBoardEmployeeStats",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Planning.md#planningboardemployeestats",
    "summary": null
  },
  {
    "name": "PlanningGrid",
    "kind": "component",
    "page": "modules/frontend-src-views-Schedule.md#planninggrid",
    "summary": "Auto-scrolls the horizontally-scrolling table while dragging near its edge — otherwise a month with 28+ day columns has no way to reach off-screen days mid-drag. Driven by useScheduleDnD's onAutoScroll callback (via ScheduleView's template ref to this component), so the DOM node this needs stays local to the component that owns it."
  },
  {
    "name": "PlanningStatusDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Dashboard.md#planningstatusdto",
    "summary": null
  },
  {
    "name": "PlanningToolbar",
    "kind": "component",
    "page": "modules/frontend-src-views-Schedule.md#planningtoolbar",
    "summary": "issue #68's ScheduleStatus enum (Domain/Scheduling/Schedule.cs), serialized as its ordinal same as every other backend enum this frontend consumes."
  },
  {
    "name": "POST /api/employees",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/employees/{employeeId:guid}/absences",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/employees/{employeeId:guid}/contracts",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/employees/{employeeId:guid}/hours-adjustments",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/schedules",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/schedules/{id:guid}/archive",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/schedules/{id:guid}/assignments",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/schedules/{id:guid}/auto-fill",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/schedules/{id:guid}/copy",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/schedules/{id:guid}/publish",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/shift-types",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/staffing-requirements",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/teams",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/v1/auth/login",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/v1/auth/logout",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/v1/auth/logout-all",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "POST /api/v1/auth/refresh",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "PreferenceLevel",
    "kind": "enum",
    "page": "modules/src-ShiftPlanner.Domain-Employees.md#preferencelevel",
    "summary": null
  },
  {
    "name": "Program",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api.md#program",
    "summary": null
  },
  {
    "name": "PublicHoliday",
    "kind": "interface",
    "page": "modules/frontend-src-views-Schedule.md#publicholiday",
    "summary": null
  },
  {
    "name": "PublicHoliday",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#publicholiday",
    "summary": null
  },
  {
    "name": "PublicHolidaysController",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#publicholidayscontroller",
    "summary": null
  },
  {
    "name": "PUT /api/absences/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "PUT /api/assignments/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "PUT /api/contracts/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "PUT /api/employees/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "PUT /api/employees/{id:guid}/eligible-shift-types",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "PUT /api/employees/{id:guid}/shift-type-preferences",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "PUT /api/employees/{id:guid}/weekday-preferences",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "PUT /api/hours-adjustments/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "PUT /api/schedules/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "PUT /api/shift-types/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "PUT /api/staffing-requirements/{id:guid}",
    "kind": "endpoint",
    "page": "api.md",
    "summary": null
  },
  {
    "name": "RefreshToken",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Common.md#refreshtoken",
    "summary": null
  },
  {
    "name": "RefreshTokenValidationResult",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Authentication.md#refreshtokenvalidationresult",
    "summary": null
  },
  {
    "name": "RestTimeValidator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Application-Validation.md#resttimevalidator",
    "summary": null
  },
  {
    "name": "RuleOutcome",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Suggestions.md#ruleoutcome",
    "summary": null
  },
  {
    "name": "Schedule",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#schedule",
    "summary": null
  },
  {
    "name": "Schedule",
    "kind": "interface",
    "page": "modules/frontend-src-views-Schedule.md#schedule",
    "summary": null
  },
  {
    "name": "ScheduleDetailDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#scheduledetaildto",
    "summary": null
  },
  {
    "name": "ScheduleDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#scheduledto",
    "summary": null
  },
  {
    "name": "ScheduleId",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Domain-Common.md#scheduleid",
    "summary": null
  },
  {
    "name": "SchedulePlanning",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#scheduleplanning",
    "summary": null
  },
  {
    "name": "SchedulePrintSheet",
    "kind": "component",
    "page": "modules/frontend-src-views-Schedule.md#scheduleprintsheet",
    "summary": "PDF export, redesigned. The interactive grid (PlanningGrid.vue) is dark, glassy, and built for on-screen drag-and-drop — reusing it for print (the previous approach, toggling `print:` utility classes on the same DOM) fought the dark theme, sticky positioning, and chip padding at every turn and still didn't reliably fit on one page. This component is a completely separate, print-only layout with its own light, ink-friendly styling: it renders nothing on screen (`hidden print:block`) and is the only thing visible when printing — PlanningGrid's wrapper is `print:hidden` now. Two layouts, chosen by whether a single employee is being exported (the \"PDF exportieren\" toolbar button vs. a row's own printer icon): - Single employee: a plain day-by-day list (the shape a member actually wants to check \"when do I work this week\" or pin to a fridge) plus an hours summary, not a one-row slice of a multi-employee grid. - Whole team: a compact grid — the day columns are a fixed share of the page width (via `<colgroup>`, `dayColWidth` below) instead of auto-sizing to content, so a full month's 28-31 columns always fits the page width, not just a week's 7. Each cell shows a colored shift-type initial (same abbreviation the interactive month overview uses) rather than the full \"name · time\" text the single-employee list can afford — there's no room for that at month width, and print has no hover tooltip to fall back on, hence the legend below the grid."
  },
  {
    "name": "SchedulePublishInfo",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#schedulepublishinfo",
    "summary": null
  },
  {
    "name": "ScheduleRefDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Dashboard.md#schedulerefdto",
    "summary": null
  },
  {
    "name": "SchedulesController",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#schedulescontroller",
    "summary": null
  },
  {
    "name": "ScheduleStatus",
    "kind": "enum",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#schedulestatus",
    "summary": null
  },
  {
    "name": "ScheduleValidator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Application-Validation.md#schedulevalidator",
    "summary": null
  },
  {
    "name": "ScheduleView",
    "kind": "component",
    "page": "modules/frontend-src-views-Schedule.md#scheduleview",
    "summary": "issue #73: this file is orchestration only now — every DTO/date-math/formatting helper, data-loading concern, drag-and-drop mechanic, and mutation call that used to live here directly has moved into the composables/components imported above. Behavior is unchanged; see each file's own header comment for what it owns. issue #74: this view is week-scoped — the route's `:date` param is any date within the displayed week (MonthOverviewView.vue always passes the week's first visible day). `Schedule` itself stays month-scoped server-side (usePlanningBoard's `days`/`currentSchedule` are unchanged), only the *displayed* grid columns narrow to one week here."
  },
  {
    "name": "SchedulingContext",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Suggestions.md#schedulingcontext",
    "summary": null
  },
  {
    "name": "SeedUser:Email",
    "kind": "config",
    "page": "views/deployment.md#configuration",
    "summary": null
  },
  {
    "name": "SeedUser:Password",
    "kind": "config",
    "page": "views/deployment.md#configuration",
    "summary": null
  },
  {
    "name": "SeedUser:Role",
    "kind": "config",
    "page": "views/deployment.md#configuration",
    "summary": null
  },
  {
    "name": "SetEligibleShiftTypesRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#seteligibleshifttypesrequest",
    "summary": null
  },
  {
    "name": "SettingsView",
    "kind": "component",
    "page": "modules/frontend-src-views-Settings.md#settingsview",
    "summary": null
  },
  {
    "name": "ShiftAssignment",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#shiftassignment",
    "summary": null
  },
  {
    "name": "ShiftAssignmentDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#shiftassignmentdto",
    "summary": null
  },
  {
    "name": "ShiftAssignmentModal",
    "kind": "component",
    "page": "modules/frontend-src-views-Schedule.md#shiftassignmentmodal",
    "summary": "issue #156's optimistic-concurrency 409 always carries this exact backend message — used to tell it apart from every other reason PUT/DELETE .../assignments/{id} can 409 (e.g. the Schedule having been Archived in the meantime), which need their own real message shown instead of this one's \"someone else changed it, reload\" wording."
  },
  {
    "name": "ShiftAssignmentXminConcurrency",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#shiftassignmentxminconcurrency",
    "summary": null
  },
  {
    "name": "ShiftOverlapValidator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Application-Validation.md#shiftoverlapvalidator",
    "summary": null
  },
  {
    "name": "ShiftPalette",
    "kind": "component",
    "page": "modules/frontend-src-views-Schedule.md#shiftpalette",
    "summary": null
  },
  {
    "name": "ShiftPlanner.Api (src/ShiftPlanner.Api/ShiftPlanner.Api.csproj)",
    "kind": "project",
    "page": "projects/ShiftPlanner-Api-b964678aa746.md",
    "summary": "Project overview, modules and components"
  },
  {
    "name": "ShiftPlanner.Application (src/ShiftPlanner.Application/ShiftPlanner.Application.csproj)",
    "kind": "project",
    "page": "projects/ShiftPlanner-Application-bbdb4cab28c5.md",
    "summary": "Project overview, modules and components"
  },
  {
    "name": "ShiftPlanner.Domain (src/ShiftPlanner.Domain/ShiftPlanner.Domain.csproj)",
    "kind": "project",
    "page": "projects/ShiftPlanner-Domain-f2a83529199c.md",
    "summary": "Project overview, modules and components"
  },
  {
    "name": "ShiftPlanner.Infrastructure (src/ShiftPlanner.Infrastructure/ShiftPlanner.Infrastructure.csproj)",
    "kind": "project",
    "page": "projects/ShiftPlanner-Infrastructure-51f58a3062c0.md",
    "summary": "Project overview, modules and components"
  },
  {
    "name": "ShiftPlanner.IntegrationTests (src/ShiftPlanner.IntegrationTests/ShiftPlanner.IntegrationTests.csproj)",
    "kind": "project",
    "page": "projects/ShiftPlanner-IntegrationTests-b8b4157f55b7.md",
    "summary": "Project overview, modules and components"
  },
  {
    "name": "ShiftPlanner.sln",
    "kind": "solution",
    "page": "solutions/ShiftPlanner-34f29b341cc1.md",
    "summary": "Solution and its selected projects"
  },
  {
    "name": "ShiftPlanner.Tests (src/ShiftPlanner.Tests/ShiftPlanner.Tests.csproj)",
    "kind": "project",
    "page": "projects/ShiftPlanner-Tests-2e83a8e8a7d2.md",
    "summary": "Project overview, modules and components"
  },
  {
    "name": "ShiftPlanningGrid",
    "kind": "component",
    "page": "modules/frontend-src-views-Schedule.md#shiftplanninggrid",
    "summary": "The \"Nach Schicht\" view's grid — PlanningGrid.vue's counterpart with the axes swapped (rows = ShiftType, not Employee). Mirrors its sticky-header/sticky-column and edge auto-scroll setup exactly; see PlanningGrid.vue for why both exist (overflow-x-auto forces overflow-y auto too, so a bounded, genuinely-scrolling panel is what sticky positioning actually binds to — issue #76)."
  },
  {
    "name": "ShiftSuggestion",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Suggestions.md#shiftsuggestion",
    "summary": null
  },
  {
    "name": "ShiftSuggestionDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#shiftsuggestiondto",
    "summary": null
  },
  {
    "name": "ShiftSuggestionEngine",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Application-Suggestions.md#shiftsuggestionengine",
    "summary": null
  },
  {
    "name": "ShiftSuggestionModal",
    "kind": "component",
    "page": "modules/frontend-src-views-Schedule.md#shiftsuggestionmodal",
    "summary": "Mirrors ShiftSuggestionEngine.SuggestionReasonCode — used only to pick an icon per reason, the message text itself comes fully formed from the backend."
  },
  {
    "name": "ShiftTiming",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#shifttiming",
    "summary": null
  },
  {
    "name": "ShiftType",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#shifttype",
    "summary": null
  },
  {
    "name": "ShiftType",
    "kind": "interface",
    "page": "modules/frontend-src-views-Schedule.md#shifttype",
    "summary": null
  },
  {
    "name": "ShiftTypeDetailModal",
    "kind": "component",
    "page": "modules/frontend-src-views-Stammdaten.md#shifttypedetailmodal",
    "summary": null
  },
  {
    "name": "ShiftTypeDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#shifttypedto",
    "summary": null
  },
  {
    "name": "ShiftTypeEndsNextDay",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#shifttypeendsnextday",
    "summary": null
  },
  {
    "name": "ShiftTypePreference",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Employees.md#shifttypepreference",
    "summary": null
  },
  {
    "name": "ShiftTypePreferenceDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#shifttypepreferencedto",
    "summary": null
  },
  {
    "name": "ShiftTypeScheduleRow",
    "kind": "component",
    "page": "modules/frontend-src-views-Schedule.md#shifttypeschedulerow",
    "summary": "The \"Nach Schicht\" view's row (requested directly — the counterpart to EmployeeScheduleRow.vue with the axes swapped: one row per ShiftType instead of per Employee, each day cell holding the employees assigned to that shift rather than the shifts assigned to that employee)."
  },
  {
    "name": "ShiftTypesController",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#shifttypescontroller",
    "summary": null
  },
  {
    "name": "ShiftTypeStaffing",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#shifttypestaffing",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Api",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Api.md",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Api/Authentication",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Api-Authentication.md",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Api/Authorization",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Api-Authorization.md",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Api/Controllers",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Api/Middleware",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Api-Middleware.md",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Application/Dashboard",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Application-Dashboard.md",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Application/Planning",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Application-Planning.md",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Application/Suggestions",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Application-Suggestions.md",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Application/Validation",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Application-Validation.md",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Domain/Common",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Domain-Common.md",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Domain/Contracts",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Domain-Contracts.md",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Domain/Employees",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Domain-Employees.md",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Domain/Scheduling",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Infrastructure/Persistence",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence.md",
    "summary": null
  },
  {
    "name": "src/ShiftPlanner.Infrastructure/Persistence/Migrations",
    "kind": "module",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md",
    "summary": null
  },
  {
    "name": "StaffingRequirement",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#staffingrequirement",
    "summary": null
  },
  {
    "name": "StaffingRequirement",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#staffingrequirement",
    "summary": null
  },
  {
    "name": "StaffingRequirementDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#staffingrequirementdto",
    "summary": null
  },
  {
    "name": "StaffingRequirementsController",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#staffingrequirementscontroller",
    "summary": null
  },
  {
    "name": "StaffingValidator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Application-Validation.md#staffingvalidator",
    "summary": null
  },
  {
    "name": "StammdatenView",
    "kind": "component",
    "page": "modules/frontend-src-views-Stammdaten.md#stammdatenview",
    "summary": "issue #57: German labels for the Bundesland enum, indexed by its backend ordinal (Domain/Scheduling/Bundesland.cs) — mirrors AbsenceType's client-side label mapping."
  },
  {
    "name": "SuggestionReason",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Suggestions.md#suggestionreason",
    "summary": null
  },
  {
    "name": "SuggestionReasonCode",
    "kind": "enum",
    "page": "modules/src-ShiftPlanner.Application-Suggestions.md#suggestionreasoncode",
    "summary": null
  },
  {
    "name": "Team",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Employees.md#team",
    "summary": null
  },
  {
    "name": "Team",
    "kind": "interface",
    "page": "modules/frontend-src-views-Schedule.md#team",
    "summary": null
  },
  {
    "name": "TeamBundesland",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Infrastructure-Persistence-Migrations.md#teambundesland",
    "summary": null
  },
  {
    "name": "TeamDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#teamdto",
    "summary": null
  },
  {
    "name": "TeamsController",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#teamscontroller",
    "summary": null
  },
  {
    "name": "Toast",
    "kind": "interface",
    "page": "modules/frontend-src-stores.md#toast",
    "summary": null
  },
  {
    "name": "ToastContainer",
    "kind": "component",
    "page": "modules/frontend-src-components.md#toastcontainer",
    "summary": null
  },
  {
    "name": "ToastKind",
    "kind": "type",
    "page": "modules/frontend-src-stores.md#toastkind",
    "summary": null
  },
  {
    "name": "UpdateAbsenceRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#updateabsencerequest",
    "summary": null
  },
  {
    "name": "UpdateAssignmentRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#updateassignmentrequest",
    "summary": null
  },
  {
    "name": "UpdateContractRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#updatecontractrequest",
    "summary": null
  },
  {
    "name": "UpdateEmployeeRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#updateemployeerequest",
    "summary": null
  },
  {
    "name": "UpdateHoursAdjustmentRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#updatehoursadjustmentrequest",
    "summary": null
  },
  {
    "name": "UpdateScheduleRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#updateschedulerequest",
    "summary": null
  },
  {
    "name": "UpdateShiftTypeRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#updateshifttyperequest",
    "summary": null
  },
  {
    "name": "UpdateStaffingRequirementRequest",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#updatestaffingrequirementrequest",
    "summary": null
  },
  {
    "name": "UtilizationDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Dashboard.md#utilizationdto",
    "summary": null
  },
  {
    "name": "ValidationIssue",
    "kind": "interface",
    "page": "modules/frontend-src-views-Schedule.md#validationissue",
    "summary": null
  },
  {
    "name": "ValidationIssue",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Application-Validation.md#validationissue",
    "summary": null
  },
  {
    "name": "ValidationIssueCode",
    "kind": "enum",
    "page": "modules/src-ShiftPlanner.Application-Validation.md#validationissuecode",
    "summary": null
  },
  {
    "name": "ValidationResult",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Application-Validation.md#validationresult",
    "summary": null
  },
  {
    "name": "ValidationResult",
    "kind": "interface",
    "page": "modules/frontend-src-views-Schedule.md#validationresult",
    "summary": null
  },
  {
    "name": "ValidationSummary",
    "kind": "component",
    "page": "modules/frontend-src-views-Schedule.md#validationsummary",
    "summary": "issue #78: German labels for ScheduleValidator's rule types (Application/Validation/*.cs), used to group the validation panel by rule instead of a flat list."
  },
  {
    "name": "WageCalculator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#wagecalculator",
    "summary": null
  },
  {
    "name": "WeekdayPreference",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Employees.md#weekdaypreference",
    "summary": null
  },
  {
    "name": "WeekdayPreferenceDto",
    "kind": "record",
    "page": "modules/src-ShiftPlanner.Api-Controllers.md#weekdaypreferencedto",
    "summary": null
  },
  {
    "name": "WorkingTimeCalculator",
    "kind": "class",
    "page": "modules/src-ShiftPlanner.Domain-Scheduling.md#workingtimecalculator",
    "summary": null
  }
];
