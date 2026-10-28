import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastService } from './core/toast.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  template: `
    <router-outlet></router-outlet>
    <div class="toast-wrap">
      @for (t of toast.toasts(); track t.id) {
        <div class="toast {{t.type}}" (click)="toast.dismiss(t.id)">{{ t.text }}</div>
      }
    </div>
  `
})
export class AppComponent {
  toast = inject(ToastService);
}
