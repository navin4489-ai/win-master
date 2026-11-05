import { environment } from '../../environments/environment';

// Where the UI sends its API calls. Users can switch this on the login page between the
// PC (served via the Cloudflare tunnel) and the always-on Cloud (Render) backend.
// The chosen value is remembered in localStorage so it survives reloads.

const KEY = 'wm_api_base';

export interface ApiTarget { id: string; label: string; url: string; }

// Available backends. 'default' uses the build-time environment URL (api.wintechinov.com),
// which normally routes to the PC when it's on and Render when it's off.
export const API_TARGETS: ApiTarget[] = [
  { id: 'pc', label: 'My PC', url: 'https://api.wintechinov.com' },
  { id: 'cloud', label: 'Cloud', url: 'https://win-master.onrender.com' },
];

/** The API base URL the UI should use right now (saved choice, else the build default). */
export function apiBase(): string {
  try {
    const saved = localStorage.getItem(KEY);
    if (saved) return saved.replace(/\/+$/, '');
  } catch { /* localStorage unavailable */ }
  return (environment.apiUrl || '').replace(/\/+$/, '');
}

/** Persist the chosen backend. Pass an empty string to clear back to the build default. */
export function setApiBase(url: string): void {
  try {
    if (url) localStorage.setItem(KEY, url.replace(/\/+$/, ''));
    else localStorage.removeItem(KEY);
  } catch { /* ignore */ }
}

/** The id of the currently selected target (for highlighting the toggle). */
export function currentTargetId(): string {
  const b = apiBase();
  const match = API_TARGETS.find(t => t.url.replace(/\/+$/, '') === b);
  return match ? match.id : 'default';
}
