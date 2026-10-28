import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { Router } from '@angular/router';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const token = auth.accessToken;

  const isAuthCall = req.url.includes('/api/auth/login') || req.url.includes('/api/auth/refresh');
  const authReq = token && !isAuthCall ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(authReq).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 401 && !isAuthCall && auth.refreshToken) {
        return auth.refresh().pipe(
          switchMap(() => next(req.clone({ setHeaders: { Authorization: `Bearer ${auth.accessToken}` } }))),
          catchError(e => { auth.logout(); router.navigate(['/login']); return throwError(() => e); })
        );
      }
      return throwError(() => err);
    })
  );
};
