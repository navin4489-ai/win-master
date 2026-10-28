import { Injectable, signal } from '@angular/core';

export interface Toast { id: number; type: 'success' | 'error' | 'info'; text: string; }

@Injectable({ providedIn: 'root' })
export class ToastService {
  toasts = signal<Toast[]>([]);
  private id = 0;

  show(text: string, type: Toast['type'] = 'info') {
    const t: Toast = { id: ++this.id, type, text };
    this.toasts.update(list => [...list, t]);
    setTimeout(() => this.dismiss(t.id), 4000);
  }
  success(t: string) { this.show(t, 'success'); }
  error(t: string) { this.show(t, 'error'); }
  dismiss(id: number) { this.toasts.update(l => l.filter(x => x.id !== id)); }
}
