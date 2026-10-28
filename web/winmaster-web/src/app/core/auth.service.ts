import { Injectable, signal, computed, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthResponse, User } from './models';

const ACCESS = 'wm_access';
const REFRESH = 'wm_refresh';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private base = environment.apiUrl;

  user = signal<User | null>(null);
  isAuthenticated = computed(() => !!this.user());

  get accessToken(): string | null { return localStorage.getItem(ACCESS); }
  get refreshToken(): string | null { return localStorage.getItem(REFRESH); }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.base}/api/auth/login`, { email, password })
      .pipe(tap(r => this.store(r)));
  }

  refresh(): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.base}/api/auth/refresh`, { refreshToken: this.refreshToken })
      .pipe(tap(r => this.store(r)));
  }

  loadMe(): Observable<User> {
    return this.http.get<User>(`${this.base}/api/auth/me`).pipe(tap(u => this.user.set(u)));
  }

  logout(): void {
    const token = this.refreshToken;
    if (token) this.http.post(`${this.base}/api/auth/logout`, { refreshToken: token }).subscribe({ error: () => {} });
    localStorage.removeItem(ACCESS);
    localStorage.removeItem(REFRESH);
    this.user.set(null);
  }

  hasRole(...roles: string[]): boolean {
    const r = this.user()?.role;
    return !!r && roles.includes(r);
  }

  /** Role hierarchy helper for menu visibility. */
  atLeast(min: string): boolean {
    const order = ['User', 'Reseller', 'Admin', 'SuperAdmin'];
    const r = this.user()?.role;
    return !!r && order.indexOf(r) >= order.indexOf(min);
  }

  private store(r: AuthResponse) {
    localStorage.setItem(ACCESS, r.accessToken);
    localStorage.setItem(REFRESH, r.refreshToken);
    this.user.set(r.user);
  }
}
