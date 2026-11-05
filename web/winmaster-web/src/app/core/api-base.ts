// Where the UI sends its API calls. Users can switch this on the login page between the
// always-on Cloud (Render) backend and the PC (served via the Cloudflare tunnel).
// The chosen value is remembered in localStorage so it survives reloads. Default: Cloud.

const KEY = 'wm_api_base';

export interface ApiTarget { id: string; label: string; url: string; }

// Available backends. When nothing is saved, the UI defaults to Cloud (always-on Render).
// Users switch to 'My PC' on the login page when their PC is on (needed for WhatsApp sending).
export const API_TARGETS: ApiTarget[] = [
  { id: 'cloud', label: 'Cloud', url: 'https://win-master.onrender.com' },
  { id: 'pc', label: 'My PC', url: 'https://api.wintechinov.com' },
];

/** The default backend used when the user hasn't chosen one yet. */
export const DEFAULT_TARGET = API_TARGETS[0]; // Cloud

/** The API base URL the UI should use right now (saved choice, else the Cloud default). */
export function apiBase(): string {
  try {
    const saved = localStorage.getItem(KEY);
    if (saved) return saved.replace(/\/+$/, '');
  } catch { /* localStorage unavailable */ }
  return DEFAULT_TARGET.url.replace(/\/+$/, '');
}

/** Persist the chosen backend. Pass an empty string to clear back to the build default. */
export function setApiBase(url: string): void {
  try {
    if (url) localStorage.setItem(KEY, url.replace(/\/+$/, ''));
    else localStorage.removeItem(KEY);
  } catch { /* ignore */ }
}

/** The id of the currently selected target (for highlighting the toggle). Defaults to Cloud. */
export function currentTargetId(): string {
  const b = apiBase();
  const match = API_TARGETS.find(t => t.url.replace(/\/+$/, '') === b);
  return match ? match.id : DEFAULT_TARGET.id;
}
