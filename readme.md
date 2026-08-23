# Technisches Konzept: Schichtplaner

## 1. Zielsetzung

Die Anwendung verwaltet Mitarbeiter, Arbeitszeitverträge, Schichten und Wochenpläne. Sie soll automatisch erkennen, ob ein erstellter Plan die vertraglichen Rahmenbedingungen erfüllt und gleichzeitig eine schnelle manuelle Planung ermöglichen.

**Leitprinzipien:**

* **SOLID** für klare Verantwortlichkeiten und Erweiterbarkeit
* **YAGNI**: zunächst nur fachlich notwendige Funktionen
* Mobile/Responsive Web UI
* Planung primär über visuelle Interaktion
* Fachlogik unabhängig von UI und Datenbank
* Vertragsregeln zentral in einer Planning-/Validation-Domain
* Änderungen nachvollziehbar speichern
* Keine unnötige Übermodellierung

---

# 2. Architektur

Für den ersten Stand würde ich einen **modularen Monolithen** verwenden.

```text
┌──────────────────────────────────────────────┐
│                  Frontend                    │
│        Vue / TypeScript / Tailwind           │
│                                              │
│  Mitarbeiter │ Planung │ Kalender │ Settings │
└──────────────────────┬───────────────────────┘
                       │ REST/JSON
┌──────────────────────▼───────────────────────┐
│                  Backend                     │
│              ASP.NET Core API                │
│                                              │
│ ┌────────────┐ ┌────────────┐ ┌────────────┐ │
│ │ Employees  │ │ Scheduling │ │ Validation │ │
│ └────────────┘ └────────────┘ └────────────┘ │
│                                              │
│ ┌──────────────────────────────────────────┐ │
│ │           Domain / Business Rules        │ │
│ └──────────────────────────────────────────┘ │
└──────────────────────┬───────────────────────┘
                       │
              ┌────────▼────────┐
              │   EF Core       │
              │   Database      │
              └─────────────────┘
```

**Keine Microservices.** Für diese Anwendung würden sie hauptsächlich zusätzliche Netzwerkfehler und Deployment-Arbeit erzeugen. Menschen schaffen es schließlich auch ohne Service Mesh, einen Schichtplan zu erstellen.

---

# 3. Fachliche Kernbereiche

Die Anwendung besteht zunächst aus fünf Bereichen:

### Mitarbeiterverwaltung

* Mitarbeiter anlegen/bearbeiten
* Name
* Personalnummer
* Status
* Team/Abteilung
* Vertragsstunden
* Arbeitszeitmodell
* mögliche Schichten
* Abwesenheiten

### Schichtverwaltung

* Früh
* Normal
* Spät
* individuelle Arbeitszeit
* optionale Mindest-/Maximalbesetzung

### Planung

* Wochenplanung
* Mitarbeiter × Tage
* Schichten als verschiebbare Elemente
* parallele Einsätze
* Drag & Drop
* Kopieren von Wochen
* automatische Stundenberechnung

### Validierung

Beispielsweise:

```text
Vertrag:       32 h
Geplant:       36 h
             └─ Fehler/Warnung

Montag:
06:00–14:00
10:00–14:00
             └─ Überlappung

Max. Tageszeit: 8 h
Geplant:        10 h
             └─ Fehler
```

### Stammdaten

* Schichttypen
* Teams
* Abteilungen
* Arbeitszeitmodelle

---

# 4. Wichtigste Entitäten

## Employee

Der Mitarbeiter ist die zentrale Stammdateneinheit.

```text
Employee
──────────────
Id
PersonnelNumber
FirstName
LastName
Email
Active
TeamId
ContractId
```

Beziehung:

```text
Team 1 ─────── N Employee
Employee 1 ─── 1 Contract
Employee 1 ─── N Absence
Employee 1 ─── N ShiftAssignment
```

---

## Contract

Der Vertrag enthält die für die Planung relevanten Arbeitszeitparameter.

```text
Contract
──────────────
Id
EmployeeId
ValidFrom
ValidTo
WeeklyHours
WorkingDaysPerWeek
DailyTargetHours
```

Beispiel:

```text
Max Müller
32 h/Woche
4 Arbeitstage
8 h/Tag
```

Wichtig: Vertragsdaten gehören **nicht direkt in Employee**.

Damit können Vertragsänderungen historisiert werden.

```text
Employee
   │
   ├── Contract 2026
   │
   └── Contract 2027
```

---

# 5. ShiftType

Ein ShiftType beschreibt eine Vorlage.

```text
ShiftType
──────────────
Id
Name
StartTime
EndTime
BreakMinutes
Color
Active
```

Beispielsweise:

```text
Früh       06:00 → 14:00
Normal     08:00 → 16:30
Spät       14:00 → 22:00
```

Der entscheidende Punkt:

**ShiftType ist eine Vorlage, keine konkrete Arbeit.**

---

# 6. ShiftAssignment

Das ist die tatsächliche Zuordnung eines Mitarbeiters zu einer Schicht.

```text
ShiftAssignment
────────────────────
Id
EmployeeId
ShiftTypeId
Date
StartTime
EndTime
BreakMinutes
Status
```

Warum StartTime/EndTime zusätzlich speichern?

Weil eine konkrete Schicht von der Vorlage abweichen kann.

Beispiel:

```text
ShiftType:
Normal 08:00–16:30

Assignment:
Max
2026-08-24
09:00–13:00
```

Dadurch bleibt das Modell flexibel, ohne für jede kleine Abweichung einen neuen Schichttyp zu benötigen.

---

# 7. Schedule / PlanningPeriod

Die Planung sollte eine eigene fachliche Einheit bekommen.

```text
Schedule
──────────────
Id
Name
StartDate
EndDate
Status
```

Beispielsweise:

```text
Schedule
Woche 35
24.08.2026 – 30.08.2026
Status: Draft
```

Damit kann später zwischen

```text
Draft
Published
Archived
```

unterschieden werden.

Ein Schedule enthält:

```text
Schedule 1
   │
   ├── Assignment
   ├── Assignment
   ├── Assignment
   └── ...
```

---

# 8. Absence

Abwesenheiten sollten von Anfang an berücksichtigt werden.

```text
Absence
──────────────
Id
EmployeeId
From
To
Type
Comment
```

Typen könnten zunächst sein:

```text
Vacation
Sick
Training
Other
```

Die eigentliche Planung muss nur wissen:

> Darf dieser Mitarbeiter an diesem Zeitpunkt eingeplant werden?

---

# 9. Team

Eine einfache Organisationsstruktur reicht zunächst.

```text
Team
──────────────
Id
Name
Active
```

Beziehung:

```text
Team
 │
 ├── Employee
 ├── Employee
 └── Employee
```

Eine komplexe Organisationshierarchie würde ich bewusst **nicht** implementieren, solange es keinen fachlichen Bedarf gibt.

---

# 10. Beziehungen

Vereinfacht:

```text
                    ┌──────────────┐
                    │     Team     │
                    └──────┬───────┘
                           │ 1:N
                           ▼
                    ┌──────────────┐
                    │   Employee   │
                    └──────┬───────┘
                       1:N │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
        ┌──────────┐ ┌──────────┐ ┌────────────┐
        │ Contract │ │ Absence  │ │Assignment  │
        └──────────┘ └──────────┘ └──────┬─────┘
                                         │ N:1
                                         ▼
                                  ┌────────────┐
                                  │ ShiftType  │
                                  └────────────┘

                    ┌──────────────┐
                    │   Schedule   │
                    └──────┬───────┘
                           │ 1:N
                           ▼
                    ShiftAssignment
```

---

# 11. Datenbank

Relationale Datenbank, beispielsweise PostgreSQL oder MariaDB.

Wichtige Constraints:

```text
Employee.PersonnelNumber UNIQUE

Contract:
EmployeeId + ValidFrom

Team.Name UNIQUE

ShiftType.Name UNIQUE
```

Foreign Keys konsequent verwenden.

Zeitangaben sollten als **lokale Arbeitszeit** modelliert werden, nicht als UTC-Timestamps.

Für eine klassische Schichtplanung ist:

```text
Date
StartTime
EndTime
```

fachlich sinnvoller als ein globaler UTC-Zeitpunkt.

---

# 12. Domain Layer

Die wichtigste Architekturentscheidung:

**Die Planungslogik darf nicht im Controller liegen.**

Beispielsweise:

```text
ScheduleController
       │
       ▼
ScheduleService
       │
       ▼
PlanningDomain
       │
       ├── WorkingTimeCalculator
       ├── ContractValidator
       ├── ShiftOverlapValidator
       ├── AbsenceValidator
       └── StaffingValidator
```

Dadurch kann dieselbe Logik verwendet werden von:

```text
REST API
   │
   ├── Web UI
   ├── Import
   └── zukünftige automatische Planung
```

---

# 13. Validierungsmodell

Eine besonders wichtige Designentscheidung wäre, Validierung nicht als simples `bool` zu implementieren.

Stattdessen:

```text
ValidationResult

IsValid
Warnings[]
Errors[]
```

Beispiel:

```json
{
  "isValid": false,
  "errors": [
    {
      "type": "ContractHoursExceeded",
      "employeeId": 42,
      "plannedHours": 36,
      "contractHours": 32
    }
  ],
  "warnings": [
    {
      "type": "ShiftOverlap"
    }
  ]
}
```

Dadurch kann die UI konkrete Hinweise anzeigen:

> ⚠ Max Müller ist diese Woche 4 h über seinem Vertrag.

statt des intellektuell besonders hilfreichen:

> ❌ Invalid.

---

# 14. Stundenberechnung

Eine zentrale Komponente:

```text
WorkingTimeCalculator
```

Berechnet:

```text
ShiftAssignment
       ↓
Net working hours
       ↓
Employee weekly hours
```

Beispiel:

```text
08:00 → 16:30
Pause: 30 min

= 8 h Arbeitszeit
```

Dann:

```text
Soll:    32 h
Geplant: 28 h
Differenz: -4 h
```

Diese Berechnung sollte **eine einzige Quelle der Wahrheit** besitzen.

Nicht:

```text
Frontend rechnet 8 h
Backend rechnet 7.99 h
Excel rechnet 8.5 h
```

Denn irgendwann sitzt jemand um 23:47 Uhr vor einem Schichtplan und fragt sich, warum Lisa angeblich 37 Minuten Urlaub verdient hat.

---

# 15. UI-Konzept

Die wichtigste Ansicht ist die **Wochenplanung**.

```text
             Mo      Di      Mi      Do      Fr

Anna        Früh    Normal   Früh    Normal  Spät
            06-14   08-16    06-14   08-16   14-22

Max         Normal  Normal   Spät    Spät
            08-16   08-16    14-22   14-22

Lisa                Früh     Normal  Normal  Spät
                    06-10    08-12   08-12   14-18
```

Die Mitarbeiterzeile zeigt zusätzlich:

```text
40 h / 40 h
████████████████████
```

oder:

```text
32 h / 36 h
██████████████████████ ⚠
```

---

# 16. Interaktion

Die Planung sollte möglichst wenig Formulare benötigen.

Primärer Workflow:

```text
Mitarbeiter
     │
     ▼
Tag auswählen
     │
     ▼
Schicht per Drag & Drop
     │
     ▼
Plan aktualisiert
     │
     ▼
Validierung
```

Zusätzlich:

* Schicht verschieben
* Schicht kopieren
* Schicht löschen
* ganze Woche kopieren
* Mitarbeiter filtern
* Team filtern
* nur Konflikte anzeigen

Ein Klick auf eine Schicht öffnet ein kleines Detail-Popup:

```text
Normal

08:00 – 16:30
Pause 30 min

[Zeit ändern]
[Schicht ändern]
[Löschen]
```

---

# 17. Mitarbeiterverwaltung

Separate Ansicht:

```text
Mitarbeiter
─────────────────────────────────────

Name             Team          Vertrag
Anna Schmidt     Entwicklung   40 h
Max Müller       Entwicklung   32 h
Lisa Weber       Produktion    20 h
Jonas Fischer    Produktion    40 h

                           [+ Mitarbeiter]
```

Detailansicht:

```text
┌─────────────────────────────────────┐
│ Anna Schmidt                        │
│                                     │
│ Personalnummer  4711                │
│ Team             Entwicklung        │
│ Status           Aktiv              │
│                                     │
│ Arbeitsvertrag                      │
│ 40 h / Woche                        │
│ 5 Arbeitstage                       │
│ 8 h / Tag                           │
│                                     │
│ [Speichern]                         │
└─────────────────────────────────────┘
```

Später können hier Arbeitszeitpräferenzen ergänzt werden.

---

# 18. REST API

Minimaler API-Schnitt:

```text
GET    /api/employees
POST   /api/employees
GET    /api/employees/{id}
PUT    /api/employees/{id}
DELETE /api/employees/{id}

GET    /api/teams
POST   /api/teams

GET    /api/shift-types
POST   /api/shift-types
PUT    /api/shift-types/{id}

GET    /api/schedules
GET    /api/schedules/{id}

POST   /api/schedules/{id}/assignments
PUT    /api/assignments/{id}
DELETE /api/assignments/{id}

POST   /api/schedules/{id}/validate
POST   /api/schedules/{id}/publish
```

Nicht sofort:

```text
/api/v1/super/advanced/planning/optimization/...
```

YAGNI.

---

# 19. Projektstruktur

Für ASP.NET Core:

```text
src/
├── ShiftPlanner.Api/
│   ├── Controllers/
│   ├── Middleware/
│   └── Program.cs
│
├── ShiftPlanner.Application/
│   ├── Employees/
│   ├── Scheduling/
│   ├── Validation/
│   └── Common/
│
├── ShiftPlanner.Domain/
│   ├── Employees/
│   ├── Scheduling/
│   ├── Contracts/
│   └── Common/
│
├── ShiftPlanner.Infrastructure/
│   ├── Persistence/
│   ├── Repositories/
│   └── Services/
│
└── ShiftPlanner.Tests/
    ├── Domain/
    ├── Application/
    └── Integration/
```

Frontend:

```text
src/
├── components/
├── views/
│   ├── Schedule/
│   ├── Employees/
│   └── Settings/
├── services/
├── stores/
├── models/
└── composables/
```

---

# 20. SOLID konkret angewendet

### Single Responsibility

Nicht:

```text
ScheduleService
```

mit 2.000 Zeilen für alles.

Stattdessen:

```text
ScheduleService
WorkingTimeCalculator
ContractValidator
AbsenceValidator
OverlapValidator
```

### Open/Closed

Neue Validierungsregel:

```text
IPlanningRule
```

implementieren, ohne bestehende Regeln umzubauen.

### Dependency Inversion

Domain kennt keine:

```text
EF Core
HTTP
Vue
MariaDB
```

---

# 21. Was bewusst zunächst NICHT enthalten ist

Für die erste Version würde ich vermeiden:

* automatische KI-Schichtplanung
* komplexe Optimierungsalgorithmen
* Payroll
* Zeiterfassung
* Urlaubsgenehmigungsworkflow
* Schichttausch zwischen Mitarbeitern
* Push Notifications
* Multi-Tenant-Architektur
* Microservices
* komplexe Rollen-/Rechteverwaltung
* Mobile App

Die Architektur sollte diese Dinge **später ermöglichen**, aber das Datenmodell nicht heute schon mit hypothetischen Problemen aufblasen.

---

# 22. Sinnvolle Entwicklungsreihenfolge

**Phase 1: Foundation**

```text
Employee
Team
Contract
ShiftType
Database
REST API
```

**Phase 2: Planung**

```text
Schedule
ShiftAssignment
Wochenansicht
Drag & Drop
Stundenberechnung
```

**Phase 3: Validierung**

```text
Vertragsstunden
Überlappungen
Abwesenheiten
Tagesstunden
Wochenstunden
Konfliktanzeige
```

**Phase 4: Usability**

```text
Wochen kopieren
Filter
Suche
Shortcuts
Optimiertes Drag & Drop
```

**Phase 5: Erweiterungen**

Erst anhand echter Nutzung entscheiden, was tatsächlich gebraucht wird.

---

## Kernmodell

Wenn man das ganze Konzept auf das Wesentliche reduziert, sind eigentlich nur diese Objekte für den ersten brauchbaren Release notwendig:

```text
Team
  │
  ▼
Employee ─────── Contract
  │
  ├────────────── Absence
  │
  └────────────── ShiftAssignment ───── ShiftType
                         │
                         ▼
                      Schedule
```

---

# 23. Security & Login

Angelehnt an das Deployment-Vorbild [vanspace3d](https://github.com/mycaravam-crypto/vanspace3d), das JWT bereits im eigenen Konzept vorsieht, aber noch nicht umgesetzt hat.

**Accounts:** ASP.NET Core Identity — nicht selbst gebaut, Passwort-Hashing, Token-Handling etc. kommen fertig aus dem Framework.

Drei feste Rollen, keine dynamische Rechteverwaltung (siehe §21 — das bleibt bewusst so):

```text
Admin      Stammdaten, Benutzer, API-Keys
Manager    Planung, Mitarbeiter, Validierung
Employee   nur Lesezugriff auf eigenen Plan
```

**Wichtig:** Employee-Login ist in v1 **nicht** enthalten — nur Admin/Manager melden sich an. Das reine `Employee`-Datenmodell (§4) braucht dafür keinen Identity-Account. Self-Service kommt erst in Frage, wenn die Planung selbst läuft (§22 Phase 5).

**Token-Flow:**

```text
Login (Email/Passwort)
       │
       ▼
Access Token (JWT, kurzlebig, ~15 min)
Refresh Token (httpOnly Cookie, rotierend)
       │
       ▼
Jeder Request: Authorization: Bearer <token>
```

**Weitere Maßnahmen:**

* HTTPS ausschließlich über Caddy (§26), HSTS aktiv
* Rate Limiting auf `/auth/*` (ASP.NET Core `RateLimiter`, kein zusätzliches Paket)
* Kein Secret im Repo — JWT-Signing-Key und DB-Passwort ausschließlich über Umgebungsvariablen (§25)
* Audit-Log (wer hat wann welche Zuweisung geändert) — erfüllt §1's "Änderungen nachvollziehbar speichern" ohne eigenes Event-Sourcing

```text
AuditLog
──────────────
Id
UserId
Action
EntityType
EntityId
Timestamp
```

---

# 24. Externe API-Integration

Ziel: andere Programme (Skripte, Auswertungen, spätere Integrationen) sollen die REST-API aus §18 nutzen können, **ohne** sich als Mitarbeiter einzuloggen.

**Zwei Auth-Schemes parallel:**

```text
Vue-Frontend        →  JWT (Login, §23)
Externes Programm   →  X-Api-Key Header
```

```text
ApiKey
──────────────
Id
Name
HashedKey
Scope        (ReadOnly | ReadWrite)
CreatedAt
RevokedAt
```

Der Key wird gehasht gespeichert (wie ein Passwort), nie im Klartext. Vergabe/Widerruf nur durch `Admin`, über einen eigenen Endpoint — kein UI-Formular nötig, das ist ein seltener Vorgang.

**Sonst:**

* OpenAPI/Swagger unter `/swagger` — das ist die Dokumentation für Fremdprogramme, keine zusätzliche Doku-Pflege nötig
* Endpoints bleiben unter `/api/v1/...` (§18 gilt weiter, keine Änderung am Schnitt)
* CORS-Allowlist nur für die Vue-Origin — Server-zu-Server-Aufrufe mit API-Key brauchen kein CORS

---

# 25. Docker & Datenbank

Entscheidung aus §11 (PostgreSQL oder MariaDB) fällt zugunsten **PostgreSQL**, containerisiert:

```text
postgres:16-alpine
  └── Volume: pgdata (persistent)
```

EF Core mit Npgsql-Provider. Migrationen laufen beim Deploy (§26), nicht bei jedem Container-Start — ein Container-Restart soll nicht versehentlich Schema-Änderungen auslösen.

Zugangsdaten ausschließlich über `.env` (nicht versioniert, `.env.example` im Repo als Vorlage).

---

# 26. Deployment

Gleiches Muster wie [vanspace3d](https://github.com/mycaravam-crypto/vanspace3d): gleicher VPS (`vi0lins.de`), **Caddy** übernimmt TLS automatisch (Let's Encrypt, kein certbot), Deploy per **GitHub Actions → SSH**. Unterschied zu vanspace3d: dort werden nur statische Dateien per rsync synchronisiert, hier braucht es einen echten Backend- und DB-Container — deshalb **Docker Compose** statt rsync/Symlink-Releases.

```text
docker-compose.yml
├── db    postgres:16-alpine, Volume pgdata
├── api   ASP.NET Core (Dockerfile in src/ShiftPlanner.Api)
└── web   Caddy: serviert Vue-Build (static) + reverse_proxy /api/* → api
```

```text
GitHub Actions (push auf main)
       │
       ▼
SSH auf vi0lins.de
       │
       ▼
docker compose build && up -d
       │
       ▼
dotnet ef database update (im api-Container)
```

Rollback: vorherige Git-Revision auschecken, erneut deployen — Docker-Images sind bereits versioniert, das ersetzt vanspace3d's `releases/<timestamp>`-Symlink-Trick.

