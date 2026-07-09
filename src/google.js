'use strict';

// Connexion à Google Agenda (OAuth 2.0 « desktop » avec redirection loopback)
// et insertion d'événements via l'API Google Calendar.
// Nécessite un identifiant OAuth (Client ID + Secret) créé par l'utilisateur
// dans Google Cloud (type « Application de bureau »).

const http = require('http');
const { shell } = require('electron');

const SCOPE = 'https://www.googleapis.com/auth/calendar.events';
const TZ = 'Europe/Brussels';

function connect(settings, saveSettings) {
  const g = settings.google || {};
  if (!g.clientId || !g.clientSecret) {
    return Promise.resolve({ ok: false, error: 'Renseignez d’abord le Client ID et le Secret Google dans Réglages.' });
  }

  return new Promise((resolve) => {
    let settled = false;
    const done = (v) => { if (!settled) { settled = true; resolve(v); } };

    const server = http.createServer(async (req, res) => {
      try {
        const u = new URL(req.url, 'http://127.0.0.1');
        const code = u.searchParams.get('code');
        const err = u.searchParams.get('error');
        res.setHeader('Content-Type', 'text/html; charset=utf-8');
        if (err || !code) {
          res.end('<body style="font-family:sans-serif">Connexion annulée. Vous pouvez fermer cet onglet.</body>');
          server.close(); done({ ok: false, error: err || 'Aucun code reçu.' });
          return;
        }
        const port = server.address().port;
        const redirect = `http://127.0.0.1:${port}`;
        const body = new URLSearchParams({
          code, client_id: g.clientId, client_secret: g.clientSecret,
          redirect_uri: redirect, grant_type: 'authorization_code'
        });
        const tr = await fetch('https://oauth2.googleapis.com/token', {
          method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body
        });
        const data = await tr.json();
        if (!tr.ok || !data.refresh_token) {
          res.end('<body style="font-family:sans-serif">Échec de connexion. Fermez cet onglet et réessayez.</body>');
          server.close();
          done({ ok: false, error: data.error_description || data.error || 'Pas de refresh_token (réautorisez avec « prompt=consent »).' });
          return;
        }
        settings.google = { ...g, refreshToken: data.refresh_token };
        saveSettings(settings);
        res.end('<body style="font-family:sans-serif;background:#0b1220;color:#eee;padding:40px"><h2>✅ Google Agenda connecté</h2><p>Vous pouvez fermer cet onglet et revenir dans IATECH-CONTROL PRO.</p></body>');
        server.close();
        done({ ok: true });
      } catch (e) {
        try { res.end('Erreur.'); } catch (_) {}
        server.close(); done({ ok: false, error: e.message });
      }
    });

    server.listen(0, '127.0.0.1', () => {
      const port = server.address().port;
      const redirect = `http://127.0.0.1:${port}`;
      const authUrl = 'https://accounts.google.com/o/oauth2/v2/auth?' + new URLSearchParams({
        client_id: g.clientId, redirect_uri: redirect, response_type: 'code',
        scope: SCOPE, access_type: 'offline', prompt: 'consent'
      }).toString();
      shell.openExternal(authUrl);
    });

    // Abandon au bout de 3 minutes.
    setTimeout(() => { try { server.close(); } catch (_) {} done({ ok: false, error: 'Délai dépassé.' }); }, 180000);
  });
}

async function accessToken(g) {
  const body = new URLSearchParams({
    client_id: g.clientId, client_secret: g.clientSecret,
    refresh_token: g.refreshToken, grant_type: 'refresh_token'
  });
  const r = await fetch('https://oauth2.googleapis.com/token', {
    method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body
  });
  const data = await r.json();
  if (!r.ok || !data.access_token) throw new Error(data.error_description || data.error || 'Jeton refusé.');
  return data.access_token;
}

async function addEvent(settings, ev) {
  const g = settings.google || {};
  if (!g.refreshToken) return { ok: false, error: 'Google Agenda non connecté.' };
  try {
    const token = await accessToken(g);
    const label = { rdv: 'Rendez-vous', tache: 'Tâche', rappel: 'Rappel' }[ev.type] || '';
    const fmt = (d) => `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}` +
      `T${pad2(d.getHours())}:${pad2(d.getMinutes())}:00`;
    const start = new Date(`${ev.date}T${(ev.time || '09:00')}:00`);
    const end = new Date(start.getTime() + 30 * 60000);
    const startDate = fmt(start);
    const endDate = fmt(end);
    const cal = (settings.calendar && settings.calendar.email) || 'primary';
    const r = await fetch(`https://www.googleapis.com/calendar/v3/calendars/${encodeURIComponent(cal)}/events`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({
        summary: `${label} : ${ev.title}`,
        description: ev.note || '',
        start: { dateTime: startDate, timeZone: TZ },
        end: { dateTime: endDate, timeZone: TZ }
      })
    });
    const data = await r.json();
    // Repli sur l'agenda principal si l'adresse liée n'est pas un calendrier accessible.
    if (!r.ok && cal !== 'primary') {
      const r2 = await fetch('https://www.googleapis.com/calendar/v3/calendars/primary/events', {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({
          summary: `${label} : ${ev.title}`, description: ev.note || '',
          start: { dateTime: startDate, timeZone: TZ }, end: { dateTime: endDate, timeZone: TZ }
        })
      });
      if (r2.ok) return { ok: true };
    }
    if (!r.ok) return { ok: false, error: (data.error && data.error.message) || 'Erreur API Agenda.' };
    return { ok: true, link: data.htmlLink };
  } catch (e) {
    return { ok: false, error: e.message };
  }
}

function pad2(n) { return String(n).padStart(2, '0'); }

function status(settings) {
  const g = settings.google || {};
  return { connected: Boolean(g.refreshToken), hasCreds: Boolean(g.clientId && g.clientSecret) };
}

module.exports = { connect, addEvent, status };
