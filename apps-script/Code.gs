/**
 * Vérification quotidienne de la synchronisation des agendas
 * (Doctena  ⇄  Google Calendar  ⇄  Doctoranytime)
 *
 * But : détecter les rendez-vous EN DOUBLE pour un même patient et, au choix,
 * déplacer le doublon vers un autre créneau disponible du même médecin.
 *
 * Google Agenda est le point central des deux plateformes : c'est donc ici
 * qu'on détecte les doublons créés par la double synchronisation.
 *
 * ⚠️ IMPORTANT : Google Agenda ne connaît PAS les vraies règles de
 * disponibilité du médecin (heures de consultation, pauses, congés). Le
 * script trouve un créneau « libre » d'après WORK (heures que VOUS configurez)
 * + les événements déjà présents. Vérifiez les propositions par email avant
 * de désactiver le mode simulation.
 */

// ======================== CONFIGURATION ========================
const CONFIG = {
  // ID de l'agenda à vérifier. '' = agenda principal du compte.
  CALENDAR_ID: '',

  // Adresse qui reçoit le rapport quotidien.
  EMAIL: 'ghani.bouali@outlook.com',

  // Fenêtre d'analyse : du passé proche jusqu'à X jours dans le futur.
  LOOKBACK_DAYS: 1,     // on regarde aussi hier (sécurité)
  LOOKAHEAD_DAYS: 60,   // et 60 jours à venir

  // Deux RDV du MÊME patient sont considérés comme doublons s'ils tombent
  // à moins de DUP_WINDOW_HOURS l'un de l'autre. Évite de signaler des
  // suivis légitimes espacés (ex : contrôle dans 1 mois).
  // Mettre 100000 pour signaler TOUS les RDV d'un même patient dans la fenêtre.
  DUP_WINDOW_HOURS: 72,

  // Que faire avec le doublon détecté :
  //   'report' : seulement lister dans l'email (ne touche à rien)
  //   'move'   : déplacer le doublon vers le prochain créneau libre
  //   'delete' : supprimer le doublon (garde le RDV le plus ancien)
  RESOLUTION_MODE: 'move',

  // SIMULATION : true = n'effectue AUCUNE modification, envoie seulement
  // les actions PROPOSÉES par email. Passez à false quand vous avez confiance.
  DRY_RUN: true,

  // Préfixes/mots à retirer du titre pour isoler le nom du patient.
  // Ajoutez ici tout ce que les plateformes collent devant le nom.
  PREFIXES_TO_STRIP: [
    'doctena', 'doctoranytime', 'doctoranytime.be', 'doctena.be',
    'rdv', 'rendez-vous', 'rendez vous', 'consultation', 'consult',
    'patient', 'reservation', 'réservation', 'booking'
  ],

  // Horaires de consultation utilisés pour chercher un créneau libre (mode 'move').
  WORK: {
    START_HOUR: 9,        // début de journée (9h00)
    END_HOUR: 18,         // fin de journée (18h00)
    SLOT_MINUTES: 15,     // granularité de recherche des créneaux
    INCLUDE_SATURDAY: false,
    INCLUDE_SUNDAY: false,
    SKIP_BELGIAN_HOLIDAYS: true,
    MAX_SEARCH_DAYS: 30   // cherche un créneau libre sur 30 jours max
  },

  // Envoyer un email même quand AUCUN doublon n'est trouvé (confirme que ça tourne).
  ALWAYS_NOTIFY: false,

  TIMEZONE: 'Europe/Brussels'
};
// ====================== FIN CONFIGURATION ======================


/**
 * Fonction lancée chaque jour par le déclencheur.
 */
function checkDuplicates() {
  const cal = CONFIG.CALENDAR_ID
    ? CalendarApp.getCalendarById(CONFIG.CALENDAR_ID)
    : CalendarApp.getDefaultCalendar();

  if (!cal) {
    Logger.log('Agenda introuvable. Vérifiez CONFIG.CALENDAR_ID.');
    return;
  }

  const now = new Date();
  const from = addDays(now, -CONFIG.LOOKBACK_DAYS);
  const to = addDays(now, CONFIG.LOOKAHEAD_DAYS);

  const events = cal.getEvents(from, to);

  // Regroupe les événements par nom de patient normalisé.
  const byPatient = {};
  events.forEach(function (ev) {
    const key = normalizePatient(ev.getTitle());
    if (!key) return; // titre vide / non exploitable
    (byPatient[key] = byPatient[key] || []).push(ev);
  });

  // Construit les "clusters" de doublons.
  const clusters = [];
  Object.keys(byPatient).forEach(function (key) {
    const list = byPatient[key].sort(function (a, b) {
      return a.getStartTime() - b.getStartTime();
    });
    if (list.length < 2) return;

    // Regroupe les RDV proches dans le temps (fenêtre DUP_WINDOW_HOURS).
    let group = [list[0]];
    for (let i = 1; i < list.length; i++) {
      const gapMs = list[i].getStartTime() - group[group.length - 1].getStartTime();
      if (gapMs <= CONFIG.DUP_WINDOW_HOURS * 3600 * 1000) {
        group.push(list[i]);
      } else {
        if (group.length >= 2) clusters.push({ patient: key, events: group.slice() });
        group = [list[i]];
      }
    }
    if (group.length >= 2) clusters.push({ patient: key, events: group.slice() });
  });

  const actions = [];

  clusters.forEach(function (cluster) {
    // On garde le RDV le plus ancien (créé en premier / plus tôt), on traite les autres.
    const ordered = cluster.events.slice().sort(function (a, b) {
      return a.getStartTime() - b.getStartTime();
    });
    const keep = ordered[0];
    const dups = ordered.slice(1);

    dups.forEach(function (dup) {
      const action = {
        patient: cluster.patient,
        keep: describe(keep),
        dup: describe(dup),
        mode: CONFIG.RESOLUTION_MODE,
        result: ''
      };

      try {
        if (CONFIG.RESOLUTION_MODE === 'delete') {
          if (CONFIG.DRY_RUN) {
            action.result = 'SIMULATION : serait supprimé';
          } else {
            dup.deleteEvent();
            action.result = 'Supprimé';
          }
        } else if (CONFIG.RESOLUTION_MODE === 'move') {
          const durMin = Math.round((dup.getEndTime() - dup.getStartTime()) / 60000);
          const slot = findNextFreeSlot(cal, durMin, addDays(now, 1));
          if (!slot) {
            action.result = 'Aucun créneau libre trouvé sur '
              + CONFIG.WORK.MAX_SEARCH_DAYS + ' jours';
          } else if (CONFIG.DRY_RUN) {
            action.result = 'SIMULATION : serait déplacé vers ' + fmt(slot.start);
          } else {
            dup.setTime(slot.start, slot.end);
            action.result = 'Déplacé vers ' + fmt(slot.start);
          }
        } else {
          action.result = 'Signalé (mode report)';
        }
      } catch (e) {
        action.result = 'ERREUR : ' + e.message;
      }

      actions.push(action);
    });
  });

  Logger.log(actions.length + ' doublon(s) traité(s).');
  sendReport(actions);
}


/**
 * Cherche le prochain créneau libre du médecin pour une durée donnée.
 */
function findNextFreeSlot(cal, durationMin, fromDate) {
  const W = CONFIG.WORK;
  const durMs = durationMin * 60000;
  const holidays = W.SKIP_BELGIAN_HOLIDAYS ? getBelgianHolidayCalendar() : null;

  for (let d = 0; d < W.MAX_SEARCH_DAYS; d++) {
    const day = addDays(stripTime(fromDate), d);
    const dow = day.getDay(); // 0 = dimanche, 6 = samedi
    if (dow === 0 && !W.INCLUDE_SUNDAY) continue;
    if (dow === 6 && !W.INCLUDE_SATURDAY) continue;
    if (holidays && holidays.getEventsForDay(day).length > 0) continue;

    const dayStart = new Date(day); dayStart.setHours(W.START_HOUR, 0, 0, 0);
    const dayEnd = new Date(day); dayEnd.setHours(W.END_HOUR, 0, 0, 0);

    // Événements occupés ce jour-là.
    const busy = cal.getEvents(dayStart, dayEnd);

    for (let t = dayStart.getTime(); t + durMs <= dayEnd.getTime(); t += W.SLOT_MINUTES * 60000) {
      const slotStart = new Date(t);
      const slotEnd = new Date(t + durMs);
      if (slotStart < new Date()) continue; // pas dans le passé
      const overlap = busy.some(function (ev) {
        return ev.getStartTime() < slotEnd && ev.getEndTime() > slotStart;
      });
      if (!overlap) return { start: slotStart, end: slotEnd };
    }
  }
  return null;
}


/**
 * Envoie le rapport par email.
 */
function sendReport(actions) {
  if (actions.length === 0 && !CONFIG.ALWAYS_NOTIFY) return;

  const subject = actions.length === 0
    ? '✅ Sync agendas : aucun doublon (' + fmtDate(new Date()) + ')'
    : '⚠️ Sync agendas : ' + actions.length + ' doublon(s) — ' + fmtDate(new Date());

  let body = 'Vérification de la synchronisation Doctena ⇄ Google Agenda ⇄ Doctoranytime\n';
  body += 'Date : ' + fmt(new Date()) + '\n';
  body += 'Mode : ' + CONFIG.RESOLUTION_MODE
    + (CONFIG.DRY_RUN ? ' (SIMULATION — rien n\'a été modifié)' : ' (ACTIF)') + '\n';
  body += '----------------------------------------------------\n\n';

  if (actions.length === 0) {
    body += 'Aucun rendez-vous en double détecté. 👍\n';
  } else {
    actions.forEach(function (a, i) {
      body += (i + 1) + ') Patient : ' + a.patient + '\n';
      body += '   • RDV conservé : ' + a.keep + '\n';
      body += '   • Doublon      : ' + a.dup + '\n';
      body += '   → Action       : ' + a.result + '\n\n';
    });
    if (CONFIG.DRY_RUN) {
      body += '----------------------------------------------------\n';
      body += 'ℹ️ Mode SIMULATION actif : aucune modification réelle.\n';
      body += 'Pour activer les actions réelles, mettez CONFIG.DRY_RUN = false.\n';
    }
  }

  MailApp.sendEmail(CONFIG.EMAIL, subject, body);
}


// ======================== INSTALLATION ========================

/**
 * À lancer UNE FOIS pour programmer la vérification quotidienne (vers 7h).
 */
function installDailyTrigger() {
  // Supprime les anciens déclencheurs de checkDuplicates pour éviter les doublons de triggers.
  ScriptApp.getProjectTriggers().forEach(function (t) {
    if (t.getHandlerFunction() === 'checkDuplicates') ScriptApp.deleteTrigger(t);
  });

  ScriptApp.newTrigger('checkDuplicates')
    .timeBased()
    .everyDays(1)
    .atHour(7)
    .inTimezone(CONFIG.TIMEZONE)
    .create();

  Logger.log('Déclencheur quotidien installé (≈ 7h, ' + CONFIG.TIMEZONE + ').');
}

/**
 * Test manuel : lance la vérification tout de suite.
 */
function runNow() {
  checkDuplicates();
}


// ======================== UTILITAIRES ========================

function normalizePatient(title) {
  if (!title) return '';
  let s = title.toLowerCase();
  // retire les accents
  s = s.normalize('NFD').replace(/[̀-ͯ]/g, '');
  // retire les préfixes connus
  CONFIG.PREFIXES_TO_STRIP.forEach(function (p) {
    const pn = p.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
    s = s.replace(new RegExp('\\b' + escapeRegex(pn) + '\\b', 'g'), ' ');
  });
  // retire ponctuation et chiffres isolés (numéros de dossier, etc.)
  s = s.replace(/[^a-z\s]/g, ' ');
  // compacte les espaces
  s = s.replace(/\s+/g, ' ').trim();
  return s;
}

function escapeRegex(s) {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function describe(ev) {
  return ev.getTitle() + ' — ' + fmt(ev.getStartTime());
}

function fmt(d) {
  return Utilities.formatDate(d, CONFIG.TIMEZONE, 'EEE dd/MM/yyyy HH:mm');
}

function fmtDate(d) {
  return Utilities.formatDate(d, CONFIG.TIMEZONE, 'dd/MM/yyyy');
}

function addDays(d, n) {
  const r = new Date(d);
  r.setDate(r.getDate() + n);
  return r;
}

function stripTime(d) {
  const r = new Date(d);
  r.setHours(0, 0, 0, 0);
  return r;
}

let _beHolidays = null;
function getBelgianHolidayCalendar() {
  if (_beHolidays !== null) return _beHolidays;
  const cals = CalendarApp.getCalendarsByName('Jours fériés en Belgique');
  _beHolidays = cals && cals.length ? cals[0] : null;
  if (!_beHolidays) {
    try {
      _beHolidays = CalendarApp.getCalendarById('fr.be#holiday@group.v.calendar.google.com');
    } catch (e) { _beHolidays = null; }
  }
  return _beHolidays;
}
