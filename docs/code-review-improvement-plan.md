# C#/.NET Verbesserungsplan — Backend (`src/`)

Review-Ergebnis eines vollständigen Durchgangs durch `src/ShiftPlanner.Api`,
`src/ShiftPlanner.Application`, `src/ShiftPlanner.Domain`,
`src/ShiftPlanner.Infrastructure` und `src/ShiftPlanner.Tests` gegen das
projektinterne C#/.NET-Regelwerk (Verständlichkeit, Verantwortlichkeit,
Typsicherheit, ungültige Zustände, explizite Abhängigkeiten, Fehlerbehandlung,
Async, Datenzugriff, Tests, Abstraktion, Kommentare, API-Oberfläche).

Frontend (`frontend/`) ist bewusst außen vor — das Regelwerk ist C#/.NET-spezifisch.

## Zusammenfassung

Der Code ist insgesamt sauber kommentiert (viele nachvollziehbare "Warum"-
Kommentare mit Issue-Referenzen), es gibt keine Async-Antipatterns
(`.Result`/`.Wait()`), keine leeren `catch`-Blöcke und keine globale
mutable State. Die bewusste Architekturentscheidung "keine Service-/
Repository-Schicht, Controller sprechen direkt mit `ApplicationDbContext`"
funktioniert für einfache CRUD-Controller gut, stößt aber bei
`DashboardController` an ihre Grenze, wo sie zu einer nicht isoliert
testbaren Aggregations-Logik von >200 Zeilen geführt hat.

Wiederkehrende Themen über alle Schichten:

1. **Der Draft/Published/Archived-Lebenszyklus von `Schedule`** (issue #68)
   ist im Controller korrekt durchgesetzt, aber im Domain-Modell selbst nur
   durch einen Kommentar geschützt — `Status`/`PublishedAt`/`PublishedBy`
   haben öffentliche Setter und lassen sich unabhängig voneinander und ohne
   Zustandsprüfung setzen. Für genau diese Übergänge existiert zudem kein
   einziger Test.
2. **"Aktiver Vertrag zum Stichtag"** ist eine zentrale Geschäftsregel, die
   an fünf Stellen unabhängig kopiert wurde, statt (wie bei
   `WorkingTimeCalculator.ExpectedHours`/`OverlapDays` bereits vorexerziert)
   einmal extrahiert zu werden.
3. **Primitive Obsession bei `Guid`**: Jede Domänen-ID (`EmployeeId`,
   `ScheduleId`, `TeamId`, `ContractId`, `ShiftTypeId`) ist austauschbarer
   `Guid`, in mehreren Kernsignaturen (`ShiftSuggestionEngine.Suggest`/
   `AutoFill`, `WageCalculator.LaborCost`) direkt hintereinander.
4. **Cross-Midnight-Schichten** (`EndTime &lt;= StartTime`) werden an vier
   unabhängigen Stellen still gefiltert/übersprungen statt zentral und
   sichtbar als Fehler behandelt — und dieser Fall ist nirgends getestet.
5. **Keine Integrationstests**: `ShiftPlanner.Tests` deckt ausschließlich
   Domain/Application-POCOs ab; die komplette Controller-Schicht (409-
   Konfliktlogik, Statusübergänge, Datumsbereichsprüfungen) läuft ohne
   jede automatisierte Testabsicherung.

Verteilung der Findings: **12 Hoch**, **15 Mittel**, **8 Niedrig**.

---

## Priorität: Hoch

### H1. `DashboardController` — Aggregationslogik nicht testbar in der Controller-Klasse

**Datei:** `src/ShiftPlanner.Api/Controllers/DashboardController.cs:72-301`

**Beobachtung:** `BuildCoverage`, `BuildPainPoints`, `BuildPlanningStatus`,
`BuildCostBreakdown`, `BuildEmployeeUtilization` sind private statische
Methoden direkt in der Controller-Klasse — ca. 230 Zeilen reine
Aggregations-Fachlogik neben DB-Zugriff und HTTP-Mapping.

**Auswirkung:** Diese zentrale KPI-Logik ist nicht isoliert unit-testbar
(nur über den Controller erreichbar). Änderungen an einer Kennzahl
riskieren unbemerkte Seiteneffekte auf andere; die Klasse wächst mit jedem
neuen Dashboard-KPI unkontrolliert weiter.

**Empfehlung:** Die `Build*`-Methoden in eine reine, DB-freie Klasse
(z. B. `DashboardAggregator` in `ShiftPlanner.Application`) extrahieren,
die Rohdaten entgegennimmt und DTOs zurückgibt — analog zu
`ScheduleValidator`/`WageCalculator`.

**Priorität:** Hoch

---

### H2. Dashboard-Kostenberechnung ignoriert Bundesland-Feiertage

**Datei:** `src/ShiftPlanner.Api/Controllers/DashboardController.cs:96, 122, 250-271`

**Beobachtung:** `BuildCostBreakdown` verwendet einen einzigen bundesweiten
Feiertagskalender (`GermanPublicHolidays.InRange(...)` ohne `Bundesland`)
für alle Mitarbeiter; `employees` wird ohne `Include(e => e.Team)` geladen.
`SchedulesController` löst dagegen bewusst pro Mitarbeiter-Team das
jeweilige Bundesland auf (issue #57).

**Auswirkung:** Für Teams mit landesspezifischen Zusatzfeiertagen wird der
Feiertagszuschlag im Dashboard zu niedrig berechnet, während dieselbe
Schicht in der Schedule-Detailansicht korrekt ausgewiesen wird — zwei
Endpunkte liefern widersprüchliche Kostenzahlen für dieselben Daten.

**Empfehlung:** `employees` mit `.Include(e => e.Team)` laden und wie in
`SchedulesController` pro Bundesland einen eigenen Feiertags-Set aufbauen,
bevor `BuildCostBreakdown`/`BuildEmployeeUtilization` ihn verwenden.

**Priorität:** Hoch

---

### H3. Contract-Historisierung erlaubt überlappende Gültigkeitszeiträume

**Datei:** `src/ShiftPlanner.Api/Controllers/ContractsController.cs:65, 93`

**Beobachtung:** Die Konfliktprüfung beim Anlegen/Ändern eines Vertrags
prüft nur exakt identisches `ValidFrom`, nicht echte Zeitraumüberlappung.
Zwei Verträge mit überschneidenden Gültigkeiten (z. B. 01.01.–01.06. und
01.03.–unbefristet) lassen sich anlegen.

**Auswirkung:** An mehreren Stellen (`SchedulesController.HourlyRateOn`,
`DashboardController.ActiveContract`, `HoursBalanceCalculator`,
`ContractValidator`, `ShiftSuggestionEngine`) wird bei Überlappung
stillschweigend per `MaxBy(ValidFrom)` "der neueste" Vertrag gewählt —
Dateneingabefehler bleiben unbemerkt und führen zu falschen Stundensätzen/
Kosten ohne jede Fehlermeldung an den Nutzer.

**Empfehlung:** Beim Anlegen/Ändern echte Intervallüberlappung prüfen
(`ValidFrom <= otherValidTo && otherValidFrom <= ValidTo`) und bei Konflikt
`409 Conflict` zurückgeben statt nur exakte Datumsgleichheit abzufangen.

**Priorität:** Hoch

---

### H4. `Schedule`-Zustandsmaschine nur durch Konvention geschützt, Übergänge ungetestet

**Dateien:** `src/ShiftPlanner.Domain/Scheduling/Schedule.cs:12,17-18`;
`src/ShiftPlanner.Api/Controllers/SchedulesController.cs:147-166,204-245,
424-425,478-479,533-534,572-573`

**Beobachtung:** `Status`, `PublishedAt` und `PublishedBy` haben alle
öffentliche Setter. Laut Kommentar sollen sie ausschließlich zusammen über
den `Publish`-Endpunkt gesetzt werden — das ist reine Konvention, nicht im
Typsystem erzwungen. Gleichzeitig prüft kein einziger Test die komplette
Zustandsmaschine (Draft→Published nur mit bestandener Validierung,
Published→Archived, verbotene Übergänge → 409, Schreibsperre auf
Assignments außerhalb Draft).

**Auswirkung:** Jeder künftige Code-Pfad (Migration, Testdaten, neuer
Endpoint) kann `Status = Published` setzen, ohne `PublishedAt`/
`PublishedBy` zu setzen, oder umgekehrt — ein laut Doku unmöglicher
Zustand wird real erreichbar. Da genau dieser Lebenszyklus laut CLAUDE.md
"der wirkungsvollste Integritäts-Fix im gesamten Projekt" ist (issue #68),
ist das Fehlen von Tests hier besonders riskant: ein Regressions-Bug, der
z. B. das Bearbeiten eines bereits veröffentlichten Schedules wieder
erlaubt, würde nicht auffallen.

**Empfehlung:**
1. Setter auf `private set` umstellen; eine `Publish(string publishedBy, DateTimeOffset at)`/`Archive()`-Methode auf `Schedule` selbst anbieten, die Zustand und Metadaten atomar setzt und den Ausgangszustand prüft.
2. `WebApplicationFactory`-basierte Integrationstests für `SchedulesController.Publish`/`Archive`/`Update` ergänzen: Draft→Published mit/ohne blockierende Errors, Published→Archived, jeweils verbotene Übergänge → 409, Schreibversuch auf Assignment außerhalb Draft → 409.

**Priorität:** Hoch

---

### H5. `ApplicationDbContext` — Audit-Log umgehbar bei synchronem `SaveChanges`, Verantwortlichkeiten vermischt

**Datei:** `src/ShiftPlanner.Infrastructure/Persistence/ApplicationDbContext.cs:128-135,137-202`

**Beobachtung:** Nur `SaveChangesAsync(CancellationToken)` ist überschrieben.
EF Core hat drei weitere, davon unabhängige Overloads
(`SaveChanges()`, `SaveChanges(bool)`, `SaveChangesAsync(bool, CancellationToken)`),
die intern nicht auf die überschriebene Methode zurückfallen. Zusätzlich
bauen `BuildAuditLogs()`/`CurrentUserId()` JSON-Snapshots, lesen
`ClaimsPrincipal` über `IHttpContextAccessor` und schreiben Audit-Entities
direkt im `DbContext`.

**Auswirkung:** Jeder Aufrufer, der (versehentlich oder durch eine
Bibliothek/ein künftiges Feature) `SaveChanges()` statt `SaveChangesAsync()`
aufruft, erzeugt **keinen** AuditLog-Eintrag — ein stiller, schwer zu
entdeckender Compliance-Bug (readme.md §23 fordert Nachvollziehbarkeit).
Die Vermischung von Persistenz, Audit-Fachlogik und HTTP-Claims-Zugriff in
einer Klasse macht diese zusätzlich unnötig groß und schwerer isoliert
testbar.

**Empfehlung:** Auf einen EF Core `SaveChangesInterceptor` umstellen — das
fängt alle vier `SaveChanges`-Pfade zentral ab und trennt die
Audit-Fachlogik (`BuildAuditLogs`) sauber vom `DbContext` selbst.

**Priorität:** Hoch

---

### H6. `ShiftSuggestionEngine` — mehrdeutige gleichartige Parameter in geschäftskritischer Kernlogik

**Datei:** `src/ShiftPlanner.Application/Suggestions/ShiftSuggestionEngine.cs:44-55` (`Suggest`), `:188-200` (`AutoFill`)

**Beobachtung:** `Suggest` hat u. a. `historyAssignments` und
`scheduleAssignments` (beide `IReadOnlyList<ShiftAssignment>`, fachlich
unterschiedlich: komplette Historie vs. nur aktueller Schedule) sowie drei
`DateOnly`-Parameter (`date`, `scheduleStart`, `scheduleEnd`). `AutoFill`
hat zwölf Parameter, viele davon gleichen Typs.

**Auswirkung:** Der Compiler kann eine Vertauschung von
`historyAssignments`/`scheduleAssignments` oder der drei `DateOnly`-Werte
nicht verhindern — das würde die Kernlogik der Vorschlags-Engine
(Eligibility, Ruhezeit, aufeinanderfolgende Tage) unbemerkt falsch
berechnen, mit direkter Auswirkung auf reale Personaleinsatzplanung.

**Empfehlung:** Fachlich zusammengehörige Parameter in einen Kontext-Typ
bündeln, z. B. `record SchedulingContext(DateOnly Start, DateOnly End,
IReadOnlyList<ShiftAssignment> ScheduleAssignments,
IReadOnlyList<ShiftAssignment> HistoryAssignments, ...)` — reduziert
gleichzeitig die Parameterzahl.

**Priorität:** Hoch

---

### H7. `WageCalculator` — viele positionale Parameter gleichen Typs in der Lohnkostenberechnung

**Datei:** `src/ShiftPlanner.Domain/Scheduling/WageCalculator.cs:27-29,36-37`

**Beobachtung:** `LaborCost`/`Breakdown` nehmen zwei Pflicht-`TimeOnly`
(`startTime`, `endTime`), zwei `decimal`-Werte (`netHours`, `hourlyRate`)
und ein optionales `TimeOnly?` (`breakStartTime`) in Folge entgegen.
Zusätzlich existieren zwei Overloads namens `LaborCost` mit stark
unterschiedlicher Semantik (einfache Multiplikation vs. volle
Zuschlagsberechnung).

**Auswirkung:** Ein vertauschtes `startTime`/`endTime` oder
`netHours`/`hourlyRate` kompiliert klaglos und würde Lohnkosten falsch
berechnen — dies ist laut Code-Kommentar "single source of truth for
labor cost", also unmittelbar geschäftskritisch.

**Empfehlung:** Parameter-Objekt (`record ShiftTiming(TimeOnly Start,
TimeOnly End, int BreakMinutes, TimeOnly? BreakStart)`) einführen; die
beiden `LaborCost`-Overloads klarer benennen (z. B. `SimpleLaborCost` vs.
`LaborCostWithSurcharges`), um Verwechslung beim Aufruf zu vermeiden.

**Priorität:** Hoch

---

### H8. Cross-Midnight-Schichten (`EndTime <= StartTime`) werden inkonsistent behandelt und sind ungetestet

**Dateien:** `src/ShiftPlanner.Application/Validation/BreakMinutesValidator.cs:13-14`,
`RestTimeValidator.cs:16`, `src/ShiftPlanner.Domain/Scheduling/WorkingTimeCalculator.cs:9-13`,
`ShiftSuggestionEngine.cs:87`; Controller-seitige Ablehnung in
`src/ShiftPlanner.Api/Controllers/SchedulesController.cs:493-494,545-546`,
`ShiftTypesController.cs:56-57,89-90`

**Beobachtung:** "Schichten über Mitternacht nicht unterstützt" (issue #11)
wird an vier unabhängigen Stellen in Domain/Application still per
`continue`/`.Where(...)`-Filter behandelt, statt durch einen zentralen
Validator, der das als Fehler meldet. `WorkingTimeCalculator.NetHours`
liefert bei negativer Differenz sogar `Math.Max(0, minuten)` — also `0`
Stunden — zurück, statt einen Fehler zu signalisieren. Die eigentliche
Durchsetzung liegt laut Code-Kommentar bewusst nur im Controller
(`EndTime <= StartTime` → 400), nicht in Domain/Application. Kein Test in
`ShiftPlanner.Tests` prüft diesen Ablehnungspfad, und
`BreakMinutesValidatorTests.ZeroLengthShift_SkippedDefensively` deckt
ausdrücklich nur den harmlosen Defensiv-Fall ab.

**Auswirkung:** Eine `ShiftAssignment` mit `EndTime <= StartTime` (z. B.
durch einen künftigen Bulk-Import oder eine Datenmigration, die den
Controller umgeht) würde heute unbemerkt durch `ScheduleValidator.Validate`
laufen — keine Fehlermeldung, dafür in Pausen- und Ruhezeitprüfung
stillschweigend ignoriert und mit 0 Stunden verrechnet. Zusätzlich ist die
tatsächlich sicherheitsrelevante Ablehnung im Controller komplett
ungetestet — ein versehentliches Entfernen der Prüfung in einem der vier
Endpunkte bliebe unbemerkt.

**Empfehlung:**
1. Einen zentralen Validator/eine Entity-Invariante einführen, der `EndTime <= StartTime` explizit als `Error` meldet, statt das Problem in jedem Consumer separat still zu filtern.
2. Je einen Controller-Test pro betroffenem Endpunkt ergänzen, der `EndTime <= StartTime` sendet und den 400 erwartet (siehe H10 zur fehlenden Integrationstest-Infrastruktur).

**Priorität:** Hoch

---

### H9. Duplizierte "aktiver Vertrag zum Stichtag"-Logik an fünf Stellen

**Dateien:** `src/ShiftPlanner.Api/Controllers/SchedulesController.cs:80-83`,
`src/ShiftPlanner.Api/Controllers/DashboardController.cs:243-245`,
`src/ShiftPlanner.Domain/Scheduling/HoursBalanceCalculator.cs:23-26`,
`src/ShiftPlanner.Application/Validation/ContractValidator.cs:24-27`,
`src/ShiftPlanner.Application/Suggestions/ShiftSuggestionEngine.cs:148-150`

**Beobachtung:** Das exakt gleiche Muster
`contracts.Where(c => c.EmployeeId == X && c.ValidFrom <= Y &&
(c.ValidTo is null || c.ValidTo >= Y)).MaxBy(c => c.ValidFrom)` ist fünfmal
unabhängig kopiert — obwohl an anderer Stelle im selben Projekt
(`WorkingTimeCalculator.ExpectedHours`/`OverlapDays`) genau diese Art
Duplikation bereits bewusst durch Extraktion vermieden wurde, als sie den
dritten Aufrufer erreichte.

**Auswirkung:** Ändert sich die Auswahlregel (z. B. Tie-Breaking oder als
Teil des Fixes für H3), muss sie an fünf Stellen synchron angepasst
werden. Vergisst man eine, driften Schedule-Detail, Dashboard-Kosten,
Überstunden-Saldo, Vertragsvalidierung und Schichtvorschläge fachlich
auseinander — mit dem Risiko, dass z. B. der Dashboard-Stundensatz vom
tatsächlich in der Wochenansicht angezeigten abweicht.

**Empfehlung:** Eine gemeinsame Methode `Contract.ActiveOn(
IReadOnlyList<Contract> contracts, Guid employeeId, DateOnly date)` in der
Domain-Schicht extrahieren (neben `WorkingTimeCalculator`) und an allen
fünf Stellen verwenden.

**Priorität:** Hoch

---

### H10. Vollständiges Fehlen von Controller-/Integrationstests

**Bereich:** gesamtes `src/ShiftPlanner.Api/Controllers/` — kein
Gegenstück in `src/ShiftPlanner.Tests/`

**Beobachtung:** Bestätigt per Grep: kein Testfile referenziert
`Microsoft.AspNetCore.Mvc.Testing`, `WebApplicationFactory` oder
`HttpClient`. `ShiftPlanner.Tests` besteht ausschließlich aus reinen
Unit-Tests über Domain-/Application-POCOs. Die Controller enthalten aber
erhebliche eigene Logik (Statusübergänge, 409-Konfliktbehandlung,
Datumsbereichsprüfungen, Aggregation über mehrere Entitäten), die in
keinem Unit-Test der unteren Schichten abgedeckt wird, weil sie dort gar
nicht liegt.

**Auswirkung:** Genau die Stellen, an denen Nutzereingaben zuerst auf
echte Geschäftsregeln treffen ("darf ich diesen Status wechseln?",
"liegt das Datum im Schedule-Zeitraum?"), können unbemerkt regressieren.
Ein Refactoring eines Controllers oder ein ORM-Update würde von der
Testsuite nicht erkannt.

**Empfehlung:** Mindestens für `SchedulesController` (Publish/Archive/
CopyMonth/CreateAssignment) `WebApplicationFactory`-basierte
Integrationstests mit einer In-Memory- oder Testcontainer-Postgres
einführen, die Statusmaschine und Konfliktfälle end-to-end prüfen (deckt
H4 und H8 mit ab).

**Priorität:** Hoch

---

### H11. `CopyMonth`-Tageskappung ungetestet — Risiko für stillen Datenverlust

**Datei:** `src/ShiftPlanner.Api/Controllers/SchedulesController.cs:283-300`

**Beobachtung:** `CopyMonth` berechnet
`day = Math.Min(a.Date.Day, daysInTargetMonth)` und kann beim Kopieren in
einen kürzeren Zielmonat mehrere Quelltage (28./29./30./31.) auf denselben
Zieltag mappen. Kein Unit- oder Integrationstest prüft dieses Clamping
oder eine daraus resultierende Kollision.

**Auswirkung:** Ein Monatswechsel von 31 auf 28 Tage könnte mehrere
`ShiftAssignment`s auf denselben Tag kopieren, ohne dass dies erkannt oder
verhindert wird — ein Business-relevanter Edge Case mit echtem
Datenrisiko, gerade weil issue #82 diese Operation bewusst atomar/
transaktional gemacht hat (ein Fehler würde also nicht einmal durch eine
Teilkopie auffallen).

**Empfehlung:** Testfall mit Quellmonat 31 Tage → Zielmonat Februar
(28/29 Tage) ergänzen und das erwartete Verhalten (Kappung vs.
Überspringen vs. Fehler bei Kollision) explizit festlegen und verifizieren.

**Priorität:** Hoch

---

### H12. `Employee` — mutable Listen öffentlich exponiert

**Datei:** `src/ShiftPlanner.Domain/Employees/Employee.cs:20,25,26`

**Beobachtung:** `EligibleShiftTypes`, `ShiftTypePreferences`,
`WeekdayPreferences` sind `public List<T> { get; set; }` — vollständig
austauschbar und von außen beliebig mutierbar, obwohl sie direkt in
`EligibilityValidator`, `ShiftSuggestionEngine` und mehreren Controllern
als Entscheidungsgrundlage für Personaleinsatzplanung dienen.

**Auswirkung:** Jeder Aufrufer kann die Liste komplett austauschen oder
ungefiltert mutieren (`employee.EligibleShiftTypes.Clear()`), ohne dass
`Employee` das kontrollieren oder darauf reagieren kann. Da diese Listen
in Validierungs- und Vorschlagslogik gelesen werden, kann ein
versehentlicher Seiteneffekt (z. B. gemeinsam referenzierte Liste in
einem Test-Setup) fachlich falsche Ergebnisse erzeugen, ohne dass der
Compiler warnt.

**Empfehlung:** Als `IReadOnlyCollection<T>` mit privatem Backing-Feld
exponieren und gezielte Methoden (`AddEligibleShiftType`/
`RemoveEligibleShiftType`) anbieten — EF Core kann private Setter/
Backing-Fields problemlos mappen.

**Priorität:** Hoch

---

## Priorität: Mittel

### M1. N+1-Query in `AutoFillCommit`

**Datei:** `src/ShiftPlanner.Api/Controllers/SchedulesController.cs:432-441`

**Beobachtung:** In der Schleife über `request.Assignments` wird pro Item
ein eigenes `await db.Employees.AnyAsync(e => e.Id == item.EmployeeId)`
ausgeführt, während `shiftTypesById` korrekt vorab als Dictionary geladen
wird.

**Auswirkung:** Bei einem größeren Auto-Fill-Commit (z. B. 50 Zuweisungen
für einen Monat) entstehen 50 einzelne DB-Roundtrips statt einem —
unnötige Latenz, die mit der Commit-Größe linear wächst.

**Empfehlung:** Alle `EmployeeId`s vorab per einer Abfrage laden
(`db.Employees.Where(e => ids.Contains(e.Id)).Select(e => e.Id).ToHashSetAsync()`)
und in der Schleife nur im Set nachschlagen.

**Priorität:** Mittel

---

### M2. Magic Strings statt Enum für Severity/Status im Dashboard

**Datei:** `src/ShiftPlanner.Api/Controllers/DashboardController.cs:33,41-43,155,187,217,233`

**Beobachtung:** `PainPointDto.Severity`/`.Type` sind `string`, ebenso
`CoverageDayDto.Status` ("Green"/"Yellow"/"Red"). Der Vergleich
`p.Severity == "Error"` taucht mehrfach wortwörtlich auf.

**Auswirkung:** Ein Tippfehler in einem der String-Literale wird vom
Compiler nicht erkannt und würde KPI-Zählungen (`CriticalIssuesCount`)
und Filterlogik still falsch berechnen lassen.

**Empfehlung:** `enum PainSeverity { Warning, Error }` und
`enum CoverageStatus { Green, Yellow, Red }` einführen und in den DTOs
verwenden.

**Priorität:** Mittel

---

### M3. Inkonsistente Modellierung geschlossener Wertemengen: `ValidationIssue.Type` als Magic String vs. sauberes Enum in `ShiftSuggestionEngine`

**Dateien:** `src/ShiftPlanner.Application/Validation/BreakMinutesValidator.cs:24-27`
("InsufficientBreak"), `ContractValidator.cs:36-38` ("ContractHoursExceeded"),
u. a. vs. `ShiftSuggestionEngine.cs:7-19` (`SuggestionReasonCode`-Enum)

**Beobachtung:** `ValidationIssue.Type` ist in jedem der sieben Validatoren
ein frei getippter String ohne gemeinsame Quelle, während dieselbe Art
Information in `ShiftSuggestionEngine` bereits sauber als Enum modelliert
ist.

**Auswirkung:** Tippfehler in den Type-Strings fallen erst zur Laufzeit
auf (z. B. beim Frontend-Mapping in `ScheduleView.vue`'s
`ISSUE_TYPE_LABELS`), und es gibt keine zentrale, kompilierzeitgeprüfte
Übersicht aller möglichen Validierungscodes.

**Empfehlung:** Einen `ValidationIssueCode`-Enum analog zu
`SuggestionReasonCode` einführen und `ValidationIssue.Type` darauf
umstellen (Frontend-Mapping entsprechend anpassen).

**Priorität:** Mittel

---

### M4. Primitive Obsession: `Guid` für alle Domänen-IDs, `decimal` für alle Geld-/Ratenwerte

**Dateien:** durchgängig, u. a. `src/ShiftPlanner.Domain/Employees/Employee.cs:7`,
`src/ShiftPlanner.Domain/Scheduling/Schedule.cs:8`, `ShiftAssignment.cs:6-9`
(drei `Guid`-Properties direkt hintereinander), `src/ShiftPlanner.Domain/Contracts/Contract.cs:7-8,11-17`

**Beobachtung:** `EmployeeId`, `ScheduleId`, `ShiftTypeId`, `ContractId`,
`TeamId` sind durchgängig austauschbare `Guid`; `WeeklyHours`,
`HourlyRate`, alle Zuschlagssätze sind austauschbare `decimal`.

**Auswirkung:** Der Compiler kann eine vertauschte `Guid` (z. B.
`ShiftTypeId` statt `EmployeeId`) an keiner Stelle im Projekt erkennen —
dieses Risiko zieht sich durch praktisch jede Methode mit mehreren
ID-Parametern, insbesondere die bereits unter H6/H7 genannten
Kernmethoden.

**Empfehlung:** Kein Big-Bang-Refactoring; stattdessen an den
fehleranfälligsten Hotspots (Methoden mit mehreren aufeinanderfolgenden
`Guid`-Parametern, z. B. `DashboardController`-Hilfsmethoden,
`ShiftSuggestionEngine`) `readonly record struct`-Wrapper für mindestens
`EmployeeId`/`ScheduleId` einführen und schrittweise ausweiten.

**Priorität:** Mittel

---

### M5. `ValidationIssue` — zwei gleichartige optionale `Guid`-Parameter, nur positional aufgerufen

**Dateien:** `src/ShiftPlanner.Application/Validation/ValidationResult.cs:5`,
Aufrufbeispiel `AbsenceValidator.cs:27-30`

**Beobachtung:** `ValidationIssue(string Type, string Message,
Guid? EmployeeId = null, Guid? ShiftAssignmentId = null)` wird an allen
Aufrufstellen positional aufgerufen.

**Auswirkung:** Ein vertauschtes `EmployeeId`/`ShiftAssignmentId` fällt
weder beim Kompilieren noch typischerweise beim Testen auf (beide sind
`Guid?`) — im UI würde dann der falsche Datensatz markiert/verlinkt.

**Empfehlung:** Benannte Argumente an den Aufrufstellen erzwingen (Roslyn-
Analyzer-Regel) oder auf Objekt-Initialisierer umstellen.

**Priorität:** Mittel

---

### M6. Fehlendes `AsNoTracking()` bei reinen Lesezugriffen

**Dateien:** durchgängig, u. a. `DashboardController.cs:82-120`,
`SchedulesController.cs:88,173-186`, `EmployeesController.cs:44-47`

**Beobachtung:** Kein Controller verwendet `.AsNoTracking()` — auch nicht
bei ausschließlich lesenden GET-Endpunkten wie `DashboardController.Get`,
das in einem Request Schedules, Assignments, Employees, Contracts,
Absences und ShiftTypes vollständig getrackt lädt.

**Auswirkung:** Unnötiger Change-Tracking-Overhead (Snapshot-Erstellung
pro Entity) bei potenziell großen Ergebnismengen — reine
Performance-Verschwendung ohne fachlichen Nutzen.

**Empfehlung:** Für alle reinen GET-Abfragen `.AsNoTracking()` ergänzen;
alternativ `ChangeTracker.QueryTrackingBehavior =
QueryTrackingBehavior.NoTracking` als Default im `ApplicationDbContext`
für Read-Only-Kontexte setzen.

**Priorität:** Mittel

---

### M7. Fehlende Pagination auf Listen-Endpunkten

**Dateien:** `EmployeesController.cs:41-48`, `SchedulesController.cs:85-90`,
`ShiftTypesController.cs:38-47`, `TeamsController.cs:22-30`

**Beobachtung:** `GetAll()` lädt in allen vier Controllern uneingeschränkt
alle Datensätze der Tabelle.

**Auswirkung:** Mit wachsender Mitarbeiter-/Schedule-Anzahl steigen
Antwortzeit und Speicherverbrauch pro Request unbegrenzt; heute
vermutlich unkritisch, wird aber irgendwann zum Performance-Problem ohne
Vorwarnung.

**Empfehlung:** Zumindest für `Employees` und `Schedules` (am stärksten
wachsend) `skip`/`take`-Parameter oder Keyset-Pagination einführen.

**Priorität:** Mittel

---

### M8. JWT-Konfigurationsschlüssel als verstreute Magic Strings

**Dateien:** `Program.cs:18-19`, `Authentication/JwtTokenFactory.cs:74,95`

**Beobachtung:** `"Jwt:SigningKey"`, `"Jwt:Issuer"`, `"Jwt:Audience"`
werden an drei unabhängigen Stellen per `IConfiguration`-Indexer
abgefragt statt über eine gebundene Optionsklasse.

**Auswirkung:** Ein Tippfehler in einem der drei Vorkommen fällt nicht
zur Compile-Zeit auf, sondern führt erst zur Laufzeit zu einer
`InvalidOperationException` oder — schlimmer — zu inkonsistentem
Verhalten an nur einer der drei Stellen.

**Empfehlung:** `JwtOptions`-Klasse mit
`builder.Services.AddOptions<JwtOptions>().BindConfiguration("Jwt").ValidateOnStart()`
einführen und per DI injizieren statt `IConfiguration` durchzureichen.

**Priorität:** Mittel

---

### M9. `DashboardController.ResolvePeriod` validiert `from`/`to` nicht

**Datei:** `src/ShiftPlanner.Api/Controllers/DashboardController.cs:75-77,163-171`

**Beobachtung:** Anders als `PublicHolidaysController`
(`end < start` → BadRequest) oder `AbsencesController`/`ContractsController`
prüft `DashboardController.Get` nicht, ob `to >= from`, bevor daraus
`periodDays`, `prevTo`, `prevFrom` berechnet werden.

**Auswirkung:** Ein Aufruf mit vertauschten Parametern liefert keinen
Fehler, sondern rechnerisch unsinnige Vorperioden-Grenzen und damit
potenziell verzerrte Delta-Prozentwerte — ein stiller Fehler statt eines
klaren 400.

**Empfehlung:** Analog zu den anderen Controllern zu Beginn
`if (to < from) return BadRequest(...)` ergänzen.

**Priorität:** Mittel

---

### M10. Falscher `Location`-Header bei `CreatedAtAction`

**Dateien:** `src/ShiftPlanner.Api/Controllers/ShiftTypesController.cs:75`,
`src/ShiftPlanner.Api/Controllers/TeamsController.cs:44`

**Beobachtung:** Beide `Create`-Actions rufen
`CreatedAtAction(nameof(GetAll), new { id = ... }, dto)` auf — `GetAll()`
hat weder einen Routen- noch einen Query-Parameter `id`; für keine der
Ressourcen existiert ein GetById-Endpoint.

**Auswirkung:** Der `Location`-Header eines `201 Created` zeigt fälschlich
auf die Collection statt auf die neue Ressource — ein Verstoß gegen
REST-Semantik, der API-Clients, die dem Header folgen, in die Irre führt.

**Empfehlung:** Entweder einen echten `GetById`-Endpoint ergänzen und
referenzieren, oder bewusst `StatusCode(201, dto)`/`Ok(dto)` verwenden,
statt einen irreführenden Header vorzutäuschen.

**Priorität:** Mittel

---

### M11. `ShiftSuggestionEngine.Suggest` — magische Scoring-Zahlen

**Datei:** `src/ShiftPlanner.Application/Suggestions/ShiftSuggestionEngine.cs:117,124,129,136,141`

**Beobachtung:** `score -= 3`, `score += 2`, `score -= 2`, `score += 1`,
`score -= 1` sind inline codiert, während im selben Typ
`MinRestHours`/`MaxConsecutiveDays` bereits als benannte Konstanten
geführt werden.

**Auswirkung:** Inkonsistenter Stil; die Gewichtung der Scoring-Regeln ist
nicht an einer Stelle überblickbar und schwerer zu tunen oder gezielt zu
testen.

**Empfehlung:** Benannte Konstanten (`AlreadyAssignedPenalty`,
`ShiftTypePreferredBonus`, …) einführen, analog zu den bereits
vorhandenen.

**Priorität:** Mittel

---

### M12. `ShiftSuggestionEngine.Suggest` — eine sehr große Methode mit vielen unabhängigen Regeln

**Datei:** `src/ShiftPlanner.Application/Suggestions/ShiftSuggestionEngine.cs:44-171`

**Beobachtung:** Eligibility-, Absence-, Ruhezeit-, Konsekutiv-Tage-,
Overlap-, zwei Präferenz- und Vertrags-Ziel-Prüfung sind alle inline in
einer ~130-Zeilen-Methode je Kandidat.

**Auswirkung:** Jede Einzelregel ist nur im Rahmen des Gesamtaufrufs
testbar, nicht isoliert — bei Punktevergabe fällt eine falsch gewichtete
Einzelregel nicht sofort auf.

**Empfehlung:** Einzelne Regeln als private, unabhängig testbare Methoden
extrahieren (z. B. `EvaluateRestTime(...)`, `EvaluateConsecutiveDays(...)`),
die jeweils `(bool eligible, decimal scoreDelta, SuggestionReason? reason)`
zurückgeben — Klasse bleibt eine Einheit (hohe Kohäsion bleibt gewahrt),
nur die Methode wird unterteilt.

**Priorität:** Mittel

---

### M13. Entities ohne Invarianten — rein anämisches Modell für Datumsbereiche

**Dateien:** `src/ShiftPlanner.Domain/Contracts/Contract.cs:9-10`
(`ValidFrom`/`ValidTo`), `src/ShiftPlanner.Domain/Employees/Absence.cs:18-19`
(`From`/`To`), `src/ShiftPlanner.Domain/Scheduling/ShiftAssignment.cs:11-12`
(`StartTime`/`EndTime`, siehe auch H8)

**Beobachtung:** Alle Entities sind reine Property-Bags mit öffentlichen
Settern; nichts verhindert auf Objekt-Ebene z. B. `ValidFrom > ValidTo`
oder `From > To`. Diese Prüfungen existieren nur (teilweise) in
nachgelagerten Application-Validatoren.

**Auswirkung:** Jeder direkte Konsument der Domain-Klassen (Tests,
Seed-Skripte, künftiger Code, der die Validatoren nicht durchläuft) kann
fachlich unsinnige Objekte erzeugen, ohne dass das sichtbar wird.

**Empfehlung:** Punktuell dort, wo es echten Nutzen bringt, eine einfache
Validierung im Konstruktor/einer Factory-Methode ergänzen — kein
flächendeckendes Redesign nötig; `ShiftAssignment` (H8) hat hier die
höchste Priorität.

**Priorität:** Mittel

---

### M14 (Test). `ScheduleValidatorTests` — Komposition deckt nicht alle sieben Regeln gemeinsam ab

**Datei:** `src/ShiftPlanner.Tests/Application/ScheduleValidatorTests.cs` (gesamt, 4 Tests)

**Beobachtung:** `CombinesMultipleRuleViolationsInOneResult` kombiniert
nur drei von acht Regeln (`InsufficientBreak` + `ContractHoursExceeded` +
`Understaffed`). `Eligibility`, `ShiftOverlap`, `ConsecutiveDays` werden
nie gemeinsam mit anderen Regeln in einem realistischen Szenario geprüft,
nur isoliert in ihren eigenen Testklassen.

**Auswirkung:** Es ist nicht abgesichert, dass sich die Regeln bei
realistischen, "unordentlichen" Schedules nicht gegenseitig maskieren.

**Empfehlung:** Einen "Kitchen-Sink"-Testfall ergänzen, der bewusst 5+
Regeln gleichzeitig verletzt und alle erwarteten Fehler/Warnungen per
`Assert.Contains` prüft.

**Priorität:** Mittel

---

### M15 (Test). Grenzwerte und Defensivzweige mehrerer Validatoren ungetestet

**Dateien und Details:**

- `src/ShiftPlanner.Application/Validation/BreakMinutesValidator.cs:16-21` — exakt 6h/9h-Bruttodauer (strikte `>`-Schwelle) nie getestet, nur 5h/7h/10h.
- `src/ShiftPlanner.Application/Validation/AbsenceValidator.cs:24-26` — Fallback-Platzhaltername "Mitarbeiter" bei unbekanntem Employee-Dictionary-Eintrag ungetestet.
- `src/ShiftPlanner.Domain/Scheduling/WageCalculator.cs:66` — `Math.Min(breakStartMinute + breakMinutes, 24*60)`-Clamp für eine über Mitternacht reichende Pause ungetestet.
- `src/ShiftPlanner.Application/Validation/StaffingValidator.cs:17-18` — fehlender `ShiftType`-Dictionary-Eintrag (`continue`) ungetestet.
- `src/ShiftPlanner.Application/Validation/RestTimeValidator.cs:16` — stiller Ausschluss von Cross-Midnight-Assignments aus der Ruhezeitprüfung ungetestet (Regressionsrisiko, siehe H8).

**Auswirkung:** Diese Grenzwerte/Defensivzweige sind echter Produktionscode
mit fachlicher Bedeutung; ein "off-by-one" (z. B. `>=` statt `>`) oder ein
verändertes Fallback-Verhalten würde von der Testsuite nicht erkannt.

**Empfehlung:** Je einen gezielten Testfall pro genanntem Zweig ergänzen —
kleiner, klar abgegrenzter Aufwand mit direktem Nutzen für die
Regressionssicherheit der Kernvalidierung.

**Priorität:** Mittel

---

## Priorität: Niedrig

### N1. Fehlende `CancellationToken`-Propagation

**Dateien:** durchgängig alle Controller-Actions, am relevantesten
`DashboardController.cs:75`, `SchedulesController.cs:171`
(`ValidateScheduleAsync`)

**Beobachtung:** Keine Action nimmt einen `CancellationToken`-Parameter
entgegen und reicht ihn an `ToListAsync`/`SaveChangesAsync` weiter.

**Auswirkung:** Bei den teuersten Endpunkten laufen mehrere sequentielle
DB-Abfragen auch dann weiter, wenn der Client die Verbindung längst
getrennt hat.

**Empfehlung:** Bei den datenintensiven Endpunkten (`Dashboard.Get`,
`ValidateScheduleAsync`, `AutoFillPreview`) `CancellationToken ct`
ergänzen und durchreichen.

**Priorität:** Niedrig

---

### N2. Ungetypter `List<Guid>`-Body-Parameter

**Datei:** `src/ShiftPlanner.Api/Controllers/EmployeesController.cs:158`

**Beobachtung:** `SetEligibleShiftTypes(Guid id, List<Guid> shiftTypeIds)`
nimmt eine nackte Liste als Body entgegen statt eines benannten
Request-DTOs, anders als praktisch jeder andere Write-Endpoint im
Projekt.

**Auswirkung:** In der generierten Swagger-Doku erscheint der Body ohne
erkennbaren Feldnamen — inkonsistent zum Rest der API.

**Empfehlung:** `record SetEligibleShiftTypesRequest(List<Guid> ShiftTypeIds)`
einführen.

**Priorität:** Niedrig

---

### N3. `WageCalculator` — unklares Kommentar-Tag "ponytail:"

**Datei:** `src/ShiftPlanner.Domain/Scheduling/WageCalculator.cs:10`

**Beobachtung:** `// ponytail: hardcoded, move to appsettings if a
deployment needs different rates.` verwendet ein unübliches Tag statt
`TODO:`/`NOTE:` und ist inhaltlich redundant zum ausführlichen
Warum-Kommentar direkt darüber.

**Auswirkung:** Verwirrt neue Teammitglieder, die die Konvention hinter
"ponytail" nicht kennen; kein funktionales Problem.

**Empfehlung:** Auf `// TODO:` vereinheitlichen oder entfernen, da der
Kontext bereits vollständig darüber steht.

**Priorität:** Niedrig

---

### N4. `PreferenceLevel` — explizite Zahlenwerte werden nirgends genutzt

**Datei:** `src/ShiftPlanner.Domain/Employees/PreferenceLevel.cs:8-9`

**Beobachtung:** `Avoid = -1, Preferred = 1` legt eine arithmetische
Nutzung nahe (z. B. direkt als Score-Delta), tatsächlich vergleicht
`ShiftSuggestionEngine` aber nur per `==` und nutzt eigene, unabhängige
Gewichte (siehe M11).

**Auswirkung:** Leichtes Missverständnis-Potenzial für Leser, die
annehmen, die Enum-Werte flössen direkt ins Scoring ein.

**Empfehlung:** Entweder die Enum-Werte tatsächlich im Scoring verwenden,
oder auf ein reines `enum PreferenceLevel { Avoid, Preferred }` ohne
explizite Werte umstellen.

**Priorität:** Niedrig

---

### N5. `AutoFill` — `Guid.Empty` als Sentinel-Wert

**Datei:** `src/ShiftPlanner.Application/Suggestions/ShiftSuggestionEngine.cs:245`

**Beobachtung:** Die hypothetische, nicht persistierte `ShiftAssignment`
bekommt `ScheduleId = Guid.Empty` als Markierung "nicht real" — kommentiert,
aber nichts im Domain-Modell verbietet, dass ein echter `Schedule.Id`
jemals `Guid.Empty` sein könnte.

**Auswirkung:** Rein theoretisches Risiko, aktuell durch Kommentar und
lokal begrenzten Gültigkeitsbereich gut abgesichert.

**Empfehlung:** Nur bei Bedarf ändern, z. B. falls diese Liste jemals
außerhalb der Methode weiterverwendet wird.

**Priorität:** Niedrig

---

### N6 (Test). `TestFactory.Contract()` hartkodiert `WorkingDaysPerWeek = 5`

**Datei:** `src/ShiftPlanner.Tests/Application/TestFactory.cs:46`

**Beobachtung:** Der Builder setzt `WorkingDaysPerWeek = 5` fest,
unabhängig vom übergebenen `weeklyHours`-Parameter; kein Test variiert
diesen Wert.

**Auswirkung:** Sollte `WorkingDaysPerWeek` künftig in einer Berechnung
verwendet werden, würde keine Testabdeckung existieren.

**Empfehlung:** Kurzer Kommentar im Builder, warum der Wert fix ist, oder
ihn parametrisierbar machen, sobald er geschäftsrelevant wird.

**Priorität:** Niedrig

---

### N7 (Test). Testnamen teils sehr lang / grenzwertig lesbar

**Datei:** z. B. `src/ShiftPlanner.Tests/Application/ContractValidatorTests.cs:70`
(`AbsenceDays_ReduceExpectedHours_SoUnscaledPassButScaledFails`)

**Beobachtung:** Einige Testnamen versuchen, das gesamte Szenario inkl.
Kontrastfall im Namen zu kodieren.

**Auswirkung:** Rein kosmetisch, erschwert aber das schnelle Scannen der
Testübersicht.

**Empfehlung:** Details eher in Kommentaren im Testkörper belassen (wie
größtenteils bereits praktiziert) statt im Methodennamen zu häufen.

**Priorität:** Niedrig

---

### N8 (Test). Ergebnisvergleich per `Math.Round` statt festem Erwartungswert

**Datei:** `src/ShiftPlanner.Tests/Domain/WorkingTimeCalculatorTests.cs:77`

**Beobachtung:** `Assert.Equal(Math.Round(40m * 31 / 7, 2),
Math.Round(hours, 2))` berechnet den Erwartungswert aus derselben Formel
wie die Produktionslogik, statt eine unabhängig vorab berechnete Konstante
zu verwenden.

**Auswirkung:** Verringert die Aussagekraft leicht (Anti-Pattern
"Test dupliziert Implementierung"), hier aber geringes Risiko, da die
Formel simpel ist.

**Empfehlung:** Konstante (`177.14m`) fest im Test hinterlegen statt die
Formel zu duplizieren.

**Priorität:** Niedrig

---

## Empfohlene Umsetzungsreihenfolge

Die Reihenfolge optimiert auf Risiko × Aufwand — kleine, klar umrissene
Fixes mit hohem Impact zuerst, größere strukturelle Arbeit danach.

**Phase 1 — Datenintegrität schließen (klein, hoher Impact):**
H3 (Contract-Überlappung), H2 (Dashboard-Feiertage), M9 (Dashboard
from/to-Validierung), M10 (Location-Header). Diese vier sind jeweils
lokal begrenzte Fixes mit unmittelbarer Korrektheitswirkung.

**Phase 2 — Schedule-Lebenszyklus hart machen:**
H4 (Status/PublishedAt kapseln + Integrationstests), H8 (Cross-Midnight
zentral validieren + Tests), H11 (CopyMonth-Kappung testen) — diese drei
hängen eng zusammen und profitieren von derselben neuen
Integrationstest-Infrastruktur (H10).

**Phase 3 — Zentrale Geschäftsregeln konsolidieren:**
H9 (aktiver Vertrag extrahieren, Voraussetzung für einen sauberen Fix von
H3), H1 (Dashboard-Aggregation extrahieren), H5 (SaveChangesInterceptor),
H12 (Employee-Listen kapseln).

**Phase 4 — Signaturen und Typsicherheit härten:**
H6/H7 (Parameter-Objekte für Suggestion-Engine/WageCalculator), M4
(Guid-Wrapper an Hotspots), M5 (benannte Argumente für ValidationIssue),
M2/M3 (Enums statt Magic Strings).

**Phase 5 — Testlücken systematisch schließen:**
M14/M15 sowie die übrigen Test-Findings (N6–N8) — am besten begleitend zu
den jeweiligen Phase-2/3-Fixes statt als separater Block, da die meisten
Testlücken genau die dort geänderten Stellen betreffen.

**Laufend / bei Gelegenheit:** M1 (N+1 in AutoFillCommit), M6
(AsNoTracking), M7 (Pagination), M8 (JWT-Options), M11–M13, N1–N5 — echte
Findings, aber ohne Abhängigkeiten zu den größeren Blöcken; können
unabhängig und opportunistisch (z. B. im Rahmen ohnehin anstehender
Änderungen an derselben Datei) abgearbeitet werden.
