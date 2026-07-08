'use strict';

const { app } = require('electron');
const fs = require('fs');
const path = require('path');

// Base des médecins pré-remplie depuis la liste EUROCARE fournie.
let DOCTORS_SEED = [];
try {
  DOCTORS_SEED = require('./data/doctors.json');
} catch (_) {
  DOCTORS_SEED = [];
}

// Suivi des prestations (Employés/Étudiants × 2025/2026), pré-rempli.
let PRESTATIONS_SEED = {};
try {
  PRESTATIONS_SEED = require('./data/prestations.json');
} catch (_) {
  PRESTATIONS_SEED = {};
}

function settingsPath() {
  return path.join(app.getPath('userData'), 'settings.json');
}

// Réglages par défaut. Les champs « launchers » et « allowedInsecureHosts »
// sont modifiables depuis l'onglet Réglages de l'application.
const DEFAULTS = {
  // Chat IA (compatible OpenAI). La clé n'est jamais versionnée : elle vit
  // uniquement dans le fichier settings.json du profil utilisateur.
  ai: {
    apiKey: '',
    baseUrl: 'https://api.openai.com/v1',
    model: 'gpt-4o-mini',
    systemPrompt:
      "Tu es un assistant pour un cabinet médical belge. Réponds de façon claire, concise et professionnelle, en français."
  },
  // Emplacements des exécutables locaux à lancer par bouton.
  launchers: {
    careconnect: ''
  },
  // Hôtes pour lesquels on tolère un certificat auto-signé (réseau interne).
  allowedInsecureHosts: ['192.168.1.220'],
  // URL de la page « examens du jour » (serveur interne, personnalisable).
  examsUrl: 'https://192.168.1.220/eoWEB/login.php?lang=FR',
  // Adresse e-mail de l'expéditeur pour les modèles de mail (facultatif).
  email: {
    from: ''
  },
  // Modèles de mail proposés dans la liste déroulante.
  emailTemplates: [
    {
      name: 'Rendez-vous — confirmation',
      subject: 'Confirmation de votre rendez-vous',
      body:
        'Bonjour,\n\nNous confirmons votre rendez-vous au cabinet le [DATE] à [HEURE].\n\nEn cas d’empêchement, merci de nous prévenir au moins 24h à l’avance.\n\nBien à vous,\nLe cabinet'
    },
    {
      name: 'Résultats disponibles',
      subject: 'Vos résultats sont disponibles',
      body:
        'Bonjour,\n\nVos résultats sont disponibles. Merci de prendre contact avec le cabinet afin d’en discuter.\n\nBien à vous,\nLe cabinet'
    },
    {
      name: 'Rappel de vaccination',
      subject: 'Rappel de vaccination',
      body:
        'Bonjour,\n\nCeci est un rappel : un vaccin est à renouveler. Merci de prendre rendez-vous à votre convenance.\n\nBien à vous,\nLe cabinet'
    }
  ],
  // Répertoires de départ pour la recherche de fichiers (vide = dossier
  // personnel de l'utilisateur).
  searchRoots: [],
  // Localisation pour la météo du bandeau défilant (défaut : Bruxelles).
  weather: {
    label: 'Bruxelles',
    latitude: 50.8503,
    longitude: 4.3517
  },
  // Modèles de correspondance (mails, rapports, prescriptions, autres),
  // gérés depuis l'onglet « Modèles ».
  correspondence: [
    {
      id: 'm1', category: 'mail', name: 'Rendez-vous — confirmation',
      subject: 'Confirmation de votre rendez-vous',
      body:
        'Bonjour,\n\nNous confirmons votre rendez-vous au cabinet le [DATE] à [HEURE].\n\nEn cas d’empêchement, merci de nous prévenir au moins 24h à l’avance.\n\nBien à vous,\nLe cabinet'
    },
    {
      id: 'm2', category: 'mail', name: 'Résultats disponibles',
      subject: 'Vos résultats sont disponibles',
      body:
        'Bonjour,\n\nVos résultats sont disponibles. Merci de prendre contact avec le cabinet afin d’en discuter.\n\nBien à vous,\nLe cabinet'
    },
    {
      id: 'r1', category: 'rapport', name: 'Rapport de consultation',
      subject: 'Rapport de consultation — [PATIENT]',
      body:
        'Patient : [NOM PRÉNOM]\nDate de naissance : [JJ/MM/AAAA]\nDate de consultation : [DATE]\n\nMotif :\n\nAnamnèse :\n\nExamen clinique :\n\nConclusion :\n\nConduite à tenir :\n\nDr [NOM]'
    },
    {
      id: 'p1', category: 'prescription', name: 'Prescription type',
      subject: 'Prescription — [PATIENT]',
      body:
        'Patient : [NOM PRÉNOM]\nDate de naissance : [JJ/MM/AAAA]\nDate : [DATE]\n\nRp/\n1) [Médicament] [dosage] — [posologie] — [durée]\n2) \n\nDr [NOM]\nN° INAMI : [……]'
    }
  ],
  // Base des médecins (nom, société, adresse, téléphone/n°, NISS),
  // gérée depuis l'onglet « Médecins ». Pré-remplie depuis la liste fournie.
  doctors: DOCTORS_SEED,
  // Suivi des prestations (heures FICHE vs SHYFTER par mois et par personne).
  prestations: PRESTATIONS_SEED
};

function deepMerge(base, override) {
  if (Array.isArray(base)) {
    return Array.isArray(override) ? override : base;
  }
  if (base && typeof base === 'object') {
    const out = { ...base };
    if (override && typeof override === 'object') {
      for (const key of Object.keys(override)) {
        out[key] = deepMerge(base[key], override[key]);
      }
    }
    return out;
  }
  return override === undefined ? base : override;
}

function loadSettings() {
  try {
    const raw = fs.readFileSync(settingsPath(), 'utf8');
    const parsed = JSON.parse(raw);
    return deepMerge(DEFAULTS, parsed);
  } catch (_) {
    return { ...DEFAULTS };
  }
}

function saveSettings(next) {
  const merged = deepMerge(DEFAULTS, next || {});
  const file = settingsPath();
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify(merged, null, 2), 'utf8');
  return merged;
}

module.exports = { loadSettings, saveSettings, DEFAULTS };
