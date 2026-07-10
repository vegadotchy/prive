'use strict';

// Test de vitesse « permanent » : mesure périodiquement la latence et le débit
// de téléchargement via le point de mesure Cloudflare (pas de clé requise).

const DOWN_URL = (bytes) => `https://speed.cloudflare.com/__down?bytes=${bytes}`;
const PING_URL = 'https://speed.cloudflare.com/__down?bytes=0';
const INTERVAL_MS = 30000; // une mesure toutes les 30 s

async function measureLatency() {
  const start = Date.now();
  try {
    await fetch(PING_URL, { cache: 'no-store' });
    return Date.now() - start;
  } catch (_) {
    return null;
  }
}

async function measureDownload(bytes = 5_000_000) {
  const start = Date.now();
  try {
    const res = await fetch(DOWN_URL(bytes), { cache: 'no-store' });
    if (!res.ok) return null;
    const buf = await res.arrayBuffer();
    const seconds = (Date.now() - start) / 1000;
    if (seconds <= 0) return null;
    const bits = buf.byteLength * 8;
    return bits / seconds / 1_000_000; // Mbps
  } catch (_) {
    return null;
  }
}

async function runSpeedTestOnce() {
  const latency = await measureLatency();
  // Si la latence échoue, on est probablement hors ligne : inutile d'insister.
  const download = latency === null ? null : await measureDownload();
  const online = latency !== null;
  return {
    online,
    latencyMs: latency,
    downloadMbps: download,
    timestamp: Date.now()
  };
}

let timer = null;

function startSpeedTest(onResult) {
  const tick = async () => {
    const data = await runSpeedTestOnce();
    try {
      onResult(data);
    } catch (_) {
      /* fenêtre fermée : ignore */
    }
  };
  tick(); // mesure immédiate au démarrage
  timer = setInterval(tick, INTERVAL_MS);
  return () => timer && clearInterval(timer);
}

module.exports = { startSpeedTest, runSpeedTestOnce };
