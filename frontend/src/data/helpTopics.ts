// German in-app user documentation, shown by components/HelpModal.vue. Content lives in its
// own data file (rather than inline in the modal) so it can grow without bloating the
// component — this is a stakeholder-facing explanation layer, not developer documentation
// (that's CLAUDE.md/readme.md), so wording stays non-technical and describes what a manager
// sees and can do, not how it's implemented.

export interface HelpBlock {
  h?: string
  p?: string
  ul?: string[]
}

export interface HelpTopic {
  id: string
  title: string
  // Route names (router/index.ts) this topic should auto-open for when Hilfe is opened from
  // that page. A topic with no match anywhere is still reachable via the topic list.
  routeNames?: string[]
  blocks: HelpBlock[]
}

export const helpTopics: HelpTopic[] = [
  {
    id: 'allgemein',
    title: 'Allgemein',
    blocks: [
      {
        h: 'Was ist ein Dienstplan?',
        p: 'Ein Dienstplan gilt immer für einen ganzen Kalendermonat und enthält die Schichten aller Mitarbeiter in diesem Monat. Für jeden Monat wird bei Bedarf ein eigener Dienstplan angelegt ("Diesen Monat anlegen") — es gibt also z. B. einen Dienstplan für August 2026 und einen eigenen für September 2026.',
      },
      {
        h: 'Status: Entwurf, Veröffentlicht, Archiviert',
        p: 'Jeder Dienstplan durchläuft drei Zustände:',
        ul: [
          'Entwurf — der normale Bearbeitungszustand. Schichten können frei per Drag & Drop angelegt, verschoben oder gelöscht werden.',
          'Veröffentlicht — der Dienstplan wurde final freigegeben. Ab diesem Zeitpunkt ist er schreibgeschützt: keine neuen Schichten, kein Verschieben, kein Löschen. Veröffentlichen ist nur möglich, wenn keine blockierenden Fehler mehr offen sind.',
          'Archiviert — ein veröffentlichter Dienstplan, dessen Zeitraum abgeschlossen ist. Bleibt weiterhin einsehbar (z. B. für Auswertungen), ist aber ebenfalls nicht mehr bearbeitbar.',
        ],
      },
      {
        h: 'Rollen',
        p: 'Es gibt drei Rollen: Admin (voller Zugriff), Manager (kann Mitarbeiter, Verträge und Dienstpläne bearbeiten) und Employee (nur Ansicht, für spätere Selbstbedienungsfunktionen vorgesehen). Wer welche Aktionen sieht, hängt vom eigenen Konto ab — die Konto-E-Mail und Rolle stehen unten links in der Seitenleiste bzw. unter Einstellungen.',
      },
      {
        h: 'Fehler und Warnungen',
        p: 'Die Anwendung prüft Dienstpläne fortlaufend gegen eine Reihe von Regeln und zeigt Verstöße in einem Validierungs-Panel an (❌ Fehler, ⚠ Warnung):',
        ul: [
          'Ruhezeit unterschritten — weniger als 11 Stunden Pause zwischen zwei Schichten derselben Person (ArbZG §5).',
          'Zu viele Arbeitstage in Folge — mehr als 6 Arbeitstage ohne Ruhetag (ArbZG).',
          'Pause unterschritten — gesetzliche Mindestpause nicht eingehalten (30 Min. ab 6 Std., 45 Min. ab 9 Std., ArbZG §4).',
          'Vertragsstunden überschritten — die geplanten Stunden liegen über den vertraglich vereinbarten Wochenstunden (auf den Monat hochgerechnet, Abwesenheitstage werden abgezogen).',
          'Nicht freigegebene Schichtart — die Person ist für diese Schichtart nicht als "mögliche Schicht" hinterlegt.',
          'Einsatz während Abwesenheit — die Schicht fällt in einen eingetragenen Urlaub/Krankheit/Fortbildung/Sonstiges-Zeitraum.',
          'Überlappende Schichten — dieselbe Person hat zwei sich zeitlich überschneidende Schichten am selben Tag (nur eine Warnung, kein Fehler).',
          'Unterbesetzung / Überbesetzung — die für eine Schichtart hinterlegte Mindest- bzw. Maximalbesetzung wird an diesem Tag nicht eingehalten.',
        ],
      },
      {
        h: 'Diese Hilfe',
        p: 'Dieses Fenster ist über den "?"-Button in der Seitenleiste (bzw. oben rechts auf schmalen Bildschirmen) von jeder Seite aus erreichbar und öffnet automatisch das zur aktuellen Seite passende Thema. Die Liste links zeigt alle Themen.',
      },
    ],
  },
  {
    id: 'uebersicht',
    title: 'Übersicht (Dashboard)',
    routeNames: ['dashboard'],
    blocks: [
      {
        p: 'Die Übersicht fasst den Planungsstand für einen wählbaren Zeitraum zusammen (Datumsfelder oben) und lässt sich zusätzlich nach Team und Schichtart filtern.',
      },
      {
        h: 'Die sechs Kennzahlen-Kacheln',
        ul: [
          'Besetzung — wie viel Prozent der benötigten Mindestbesetzung im Zeitraum tatsächlich verplant ist (grün ≥ 95 %, gelb ≥ 85 %, sonst rot).',
          'Auslastung — geplante Stunden im Verhältnis zu den vertraglich verfügbaren Stunden aller Mitarbeiter.',
          'Lohnkosten — Summe der Lohnkosten im Zeitraum inkl. Veränderung zum Vorzeitraum, aufgeschlüsselt nach Regulär / Nachtzuschlag / Sonntagszuschlag / Feiertagszuschlag (Balken darunter).',
          'Planung — Anteil veröffentlichter Dienstpläne im Zeitraum.',
          'Offene Probleme — Anzahl aller Validierungs-Fehler/-Warnungen, davon "kritisch" (echte Fehler).',
          'Überstunden — Summe der über die Vertragsstunden hinaus geplanten Stunden.',
        ],
      },
      {
        h: 'Besetzungsgrad und Planungsstatus',
        p: 'Die linke Liste zeigt pro Tag und Schichtart, wie viele Personen im Verhältnis zur Mindestbesetzung eingeplant sind. Rechts steht, wie viele Dienstpläne im Zeitraum veröffentlicht bzw. noch Entwurf sind und wie viele Konflikte (Fehler) bestehen — ein Klick auf einen betroffenen Dienstplan springt direkt in dessen Dienstplan-Ansicht.',
      },
      {
        h: 'Auslastung nach Mitarbeiter',
        p: 'Tabelle mit Soll- und Ist-Stunden, Auslastung in Prozent und Überstunden pro Mitarbeiter für den gewählten Zeitraum.',
      },
      {
        h: 'Pain Points und Handlungsbedarf',
        p: 'Pain Points listet alle offenen Validierungsprobleme im Zeitraum auf. Handlungsbedarf zeigt daraus die (bis zu 8) wichtigsten, Fehler vor Warnungen, mit einem "Öffnen"-Button zum betroffenen Dienstplan. Ein blaues "Neu"-Badge markiert Probleme, die seit dem letzten Besuch der Übersicht neu hinzugekommen sind — das lässt sich unter Einstellungen abschalten.',
      },
    ],
  },
  {
    id: 'dienstplan-monat',
    title: 'Dienstplan — Monatsübersicht',
    routeNames: ['schedule'],
    blocks: [
      {
        p: 'Die Monatsübersicht ist die Startseite des Dienstplans: eine kompakte Tabelle mit allen Mitarbeitern (Zeilen) und allen Tagen des Monats (Spalten), gedacht für einen schnellen Blick auf Besetzung, Abwesenheiten und Konflikte über den ganzen Monat.',
      },
      {
        h: 'Zellen lesen',
        ul: [
          'Farbiges Kästchen mit Buchstabe = eine zugewiesene Schicht (Farbe/Kürzel wie in den Stammdaten hinterlegt).',
          'Gestrichelter Kreis mit "A" = Abwesenheit (Urlaub, Krankheit, …) an diesem Tag.',
          'Kleiner Punkt unten rechts in der Zelle = rot für einen Fehler, gelb für eine Warnung an diesem Tag für diese Person.',
          'Zahl neben dem Namen = Anzahl Probleme, die nicht an einem einzelnen Tag hängen (z. B. zu viele Vertragsstunden über den ganzen Monat).',
          'Bernsteinfarbener Punkt in der Kopfzeile = gesetzlicher Feiertag (Name als Tooltip).',
        ],
      },
      {
        h: 'Navigation',
        p: 'Ein Klick auf eine Wochen-Kopfzeile, eine Tagesspalte oder eine Zelle öffnet die Wochenansicht (den eigentlichen Editor) für die entsprechende Woche. Die Pfeile oben wechseln den angezeigten Monat.',
      },
      {
        h: 'Monat kopieren',
        p: 'Überträgt alle Schichten des aktuell sichtbaren Monats auf denselben Tag im Folgemonat (der 31. wird dabei automatisch auf den letzten Tag kürzerer Monate begrenzt). Existiert der Zielmonat noch nicht, wird er dabei automatisch angelegt. Hat der Zielmonat bereits Schichten, wird die Aktion abgebrochen, damit nichts überschrieben wird.',
      },
      {
        h: 'Automatisch füllen',
        p: 'Sucht alle unterbesetzten Schichten (wo die Mindestbesetzung einer Schichtart nicht erreicht ist) im sichtbaren Monat und schlägt dafür passende Mitarbeiter vor — basierend auf Verfügbarkeit, Präferenzen und wer aktuell am weitesten unter seinem Vertragssoll liegt. Die Vorschläge werden zunächst nur angezeigt (Vorschau); einzelne Zeilen lassen sich vor der Übernahme verwerfen. Erst "Bestätigen" legt die verbliebenen Schichten tatsächlich an.',
      },
      {
        h: 'Suche und Team-Filter',
        p: 'Die Suche filtert die angezeigten Mitarbeiterzeilen nach Namen, der Team-Filter nach zugeordnetem Team. Ein Standard-Team-Filter lässt sich unter Einstellungen hinterlegen.',
      },
    ],
  },
  {
    id: 'dienstplan-woche',
    title: 'Dienstplan — Wochenansicht',
    routeNames: ['schedule-week'],
    blocks: [
      {
        p: 'Die Wochenansicht ist der eigentliche Editor: eine Woche im Detail, mit allen Schichten als farbige Kacheln. Erreichbar über die Monatsübersicht (Klick auf eine Woche/Zelle), zurück geht es über "Monatsübersicht" oben links.',
      },
      {
        h: 'Schichten anlegen und verschieben (Drag & Drop)',
        ul: [
          'Eine Schichttyp-Kachel aus der Palette oben auf eine leere Zelle ziehen → legt eine neue Schicht mit den hinterlegten Standardzeiten dieser Schichtart an.',
          'Eine bereits zugewiesene Schicht auf eine andere Zelle ziehen → verschiebt sie auf den neuen Mitarbeiter/Tag.',
          'Ein einfacher Klick (ohne zu ziehen) auf eine bestehende Schicht öffnet sie zum Bearbeiten (Zeiten, Pause, Pausenbeginn) oder Löschen.',
          'Funktioniert per Maus und per Touch (Tablet/Smartphone).',
          'Beim Ziehen an den rechten/linken Rand der Tabelle scrollt die Ansicht automatisch weiter.',
        ],
      },
      {
        h: 'Vorschlagen (✨-Symbol)',
        p: 'Das Funken-Symbol an einer Schichttyp-Kachel öffnet eine Liste geeigneter Mitarbeiter für ein bestimmtes Datum dieser Schichtart — sortiert nach Eignung (✓/✗) und einem Punktwert, mit Begründung (z. B. bevorzugte Schichtart, noch unter Vertragssoll, bereits an diesem Tag eingeplant). "Zuweisen" legt die Schicht direkt an.',
      },
      {
        h: 'Validierungs-Panel',
        p: 'Oberhalb der Palette zeigt ein Panel alle Fehler und Warnungen für die sichtbare Woche, gruppiert nach Regel (z. B. "Ruhezeit unterschritten (2)") und aufklappbar. Ein Klick auf eine einzelne Meldung springt zur betroffenen Zeile/Zelle und hebt sie kurz hervor. Siehe auch das Thema "Allgemein" für die Bedeutung der einzelnen Regeln.',
      },
      {
        h: 'Stundenanzeige je Mitarbeiter',
        p: 'Neben jedem Namen steht "Xh / Yh" (geplante / vertraglich vorgesehene Stunden für den Monat) mit Fortschrittsbalken, darunter bei Bedarf ein Übertrag ("+Xh"/"-Xh") aus bereits abgeschlossenen Vormonaten sowie die Lohnkosten dieser Person, sofern ein Stundenlohn im Vertrag hinterlegt ist.',
      },
      {
        h: 'Veröffentlichen / Archivieren',
        p: 'Solange der Dienstplan im Entwurf ist, kann er über "Veröffentlichen" freigegeben werden — der Button ist deaktiviert (mit Begründung als Tooltip), solange noch blockierende Fehler offen sind. Nach der Veröffentlichung wird die Ansicht schreibgeschützt: Schichten lassen sich nur noch ansehen, die Palette verliert Ziehen/Vorschlagen, "Automatisch füllen" ist ausgeblendet. "Archivieren" markiert einen veröffentlichten Dienstplan als abgeschlossen; er bleibt einsehbar.',
      },
      {
        h: 'PDF-Export und Tastenkürzel',
        p: '"PDF exportieren" (bzw. das Drucker-Symbol an einer Mitarbeiterzeile für nur diese Person) öffnet den Druckdialog des Browsers — dort kann als PDF gespeichert werden. Über das "?"-Symbol in der Werkzeugleiste (oder die Taste "?") öffnet sich eine Liste aller Tastenkürzel dieser Ansicht.',
      },
    ],
  },
  {
    id: 'mitarbeiter',
    title: 'Mitarbeiter',
    routeNames: ['employees'],
    blocks: [
      {
        p: 'Liste aller Mitarbeiter mit Personalnummer, Team und Status. "Mitarbeiter" oben legt einen neuen an; ein Klick auf eine Zeile öffnet die Detailansicht.',
      },
      {
        h: 'In der Detailansicht',
        ul: [
          'Stammdaten — Name, Personalnummer, Team, E-Mail, Telefon, Aktiv/Inaktiv.',
          'Mögliche Schichten — welche Schichtarten diese Person überhaupt übernehmen darf. Ist hier nichts ausgewählt, gilt die Person als für alle Schichtarten einsetzbar.',
          'Präferenzen — pro Schichtart und Wochentag lässt sich per Klick "bevorzugt" (👍) oder "vermeiden" (👎) einstellen. Fließt in "Vorschlagen" / "Automatisch füllen" als Bonus/Malus ein, blockiert aber nichts.',
          'Verträge — Gültigkeitszeitraum, Wochenstunden, Arbeitstage/Woche, Soll-Stunden/Tag und optional ein Stundenlohn (€/Std) für die Lohnkostenberechnung. Ein Mitarbeiter kann mehrere, zeitlich aufeinanderfolgende Verträge haben (z. B. bei einer Stundenänderung).',
          'Abwesenheiten — Urlaub, Krankheit, Fortbildung oder Sonstiges mit Zeitraum und optionalem Kommentar. Schichten, die in eine Abwesenheit fallen, werden im Dienstplan als Fehler markiert.',
        ],
      },
    ],
  },
  {
    id: 'stammdaten',
    title: 'Stammdaten',
    routeNames: ['stammdaten'],
    blocks: [
      {
        h: 'Teams',
        p: 'Gruppierung von Mitarbeitern, u. a. für den Team-Filter im Dienstplan und in der Übersicht. Optional lässt sich ein Bundesland hinterlegen — dann werden bei der Feiertags- und Lohnzuschlagsberechnung für diese Teammitglieder zusätzlich die regionalen Feiertage dieses Bundeslands berücksichtigt (z. B. Fronleichnam), nicht nur die neun bundesweiten.',
      },
      {
        h: 'Schichttypen',
        p: 'Vorlagen für Schichten: Name, Start-/Endzeit, Pause (Minuten), Farbe (zur Wiedererkennung im Dienstplan) sowie optional eine Mindest- und Maximalbesetzung. Diese Besetzungswerte sind die Grundlage für die Unterbesetzt/Überbesetzt-Warnungen und für "Automatisch füllen".',
      },
    ],
  },
  {
    id: 'einstellungen',
    title: 'Einstellungen',
    routeNames: ['settings'],
    blocks: [
      {
        p: 'Kontoinformationen (E-Mail, Rolle) sowie zwei persönliche Voreinstellungen für den eigenen Browser:',
        ul: [
          'Standard-Team-Filter — wird beim Öffnen des Dienstplans automatisch als Team-Filter vorausgewählt, solange kein anderer Filter über einen Link vorgegeben ist.',
          'Benachrichtigungen für neue Probleme — steuert die blauen "Neu"-Badges in der Übersicht bei neu hinzugekommenen Pain Points. Rein clientseitig (im Browser gespeichert), kein E-Mail- oder Push-Versand.',
        ],
      },
      {
        h: 'Alle Sitzungen abmelden',
        p: 'Meldet dieses Konto auf allen Geräten ab, inklusive der aktuellen Sitzung — sinnvoll z. B. bei einem verlorenen Gerät oder wenn ein Mitarbeiterkonto vollständig abgemeldet werden soll.',
      },
    ],
  },
]

export function topicIdForRoute(routeName: unknown): string {
  const name = typeof routeName === 'string' ? routeName : ''
  return helpTopics.find((t) => t.routeNames?.includes(name))?.id ?? 'allgemein'
}
