'use strict';

const fs = require('fs');
const fsp = fs.promises;
const os = require('os');
const path = require('path');

// Dossiers à ignorer : bruit et volumes système qui ralentissent la recherche.
const SKIP_DIRS = new Set([
  'node_modules',
  '$Recycle.Bin',
  'System Volume Information',
  'Windows',
  'AppData',
  '.git',
  'Application Data',
  'ProgramData'
]);

const CONTENT_MAX_BYTES = 2 * 1024 * 1024; // 2 Mo : au-delà on ne lit pas le contenu
const CONTENT_EXTS = new Set([
  '.txt', '.md', '.csv', '.log', '.json', '.xml', '.html', '.htm',
  '.rtf', '.js', '.ts', '.css', '.ini', '.cfg', '.yml', '.yaml'
]);

/**
 * Recherche récursive de fichiers/dossiers par nom (et éventuellement contenu).
 *
 * @param {object} opts
 * @param {string} opts.query          Terme recherché (insensible à la casse)
 * @param {string[]} [opts.roots]      Racines de départ (défaut : dossier perso)
 * @param {boolean} [opts.searchContent] Cherche aussi dans le contenu des fichiers texte
 * @param {number} [opts.maxResults]   Nombre max de résultats (défaut 500)
 * @param {number} [opts.timeoutMs]    Durée max de la recherche (défaut 20s)
 */
async function searchFiles(opts) {
  const query = String(opts.query || '').trim().toLowerCase();
  if (!query) return { results: [], truncated: false, error: 'Terme de recherche vide.' };

  const roots =
    Array.isArray(opts.roots) && opts.roots.length ? opts.roots : [os.homedir()];
  const searchContent = Boolean(opts.searchContent);
  const maxResults = Number(opts.maxResults) || 500;
  const timeoutMs = Number(opts.timeoutMs) || 20000;

  const deadline = Date.now() + timeoutMs;
  const results = [];
  let truncated = false;

  const queue = [...roots];

  while (queue.length) {
    if (results.length >= maxResults) {
      truncated = true;
      break;
    }
    if (Date.now() > deadline) {
      truncated = true;
      break;
    }

    const dir = queue.shift();
    let entries;
    try {
      entries = await fsp.readdir(dir, { withFileTypes: true });
    } catch (_) {
      continue; // dossier illisible (droits, lien mort…) : on passe
    }

    for (const entry of entries) {
      if (results.length >= maxResults) {
        truncated = true;
        break;
      }
      const name = entry.name;
      const full = path.join(dir, name);

      let isDir = entry.isDirectory();
      // Résout les liens symboliques prudemment.
      if (entry.isSymbolicLink()) {
        try {
          isDir = (await fsp.stat(full)).isDirectory();
        } catch (_) {
          continue;
        }
      }

      const nameMatch = name.toLowerCase().includes(query);
      if (nameMatch) {
        results.push({ path: full, name, type: isDir ? 'folder' : 'file' });
      }

      if (isDir) {
        if (!SKIP_DIRS.has(name) && !name.startsWith('.')) {
          queue.push(full);
        }
      } else if (searchContent && !nameMatch) {
        const ext = path.extname(name).toLowerCase();
        if (CONTENT_EXTS.has(ext)) {
          try {
            const st = await fsp.stat(full);
            if (st.size <= CONTENT_MAX_BYTES) {
              const text = await fsp.readFile(full, 'utf8');
              if (text.toLowerCase().includes(query)) {
                results.push({ path: full, name, type: 'file', contentMatch: true });
              }
            }
          } catch (_) {
            /* fichier illisible : on ignore */
          }
        }
      }
    }
  }

  return { results, truncated };
}

module.exports = { searchFiles };
