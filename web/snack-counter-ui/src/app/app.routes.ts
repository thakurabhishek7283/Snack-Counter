import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', title: 'Snack boxes', loadComponent: () => import('./features/dashboard/dashboard').then(m => m.Dashboard) },
  { path: 'refills/:id', title: 'Refill', loadComponent: () => import('./features/refills/refill-page').then(m => m.RefillPage) },
  { path: 'me', title: 'You', loadComponent: () => import('./features/me/me-page').then(m => m.MePage) },
  { path: 'report', title: 'Monthly spending', loadComponent: () => import('./features/report/report-page').then(m => m.ReportPage) },
  { path: 'demo', title: 'Demo controls', loadComponent: () => import('./features/demo/demo-page').then(m => m.DemoPage) },
  { path: '**', redirectTo: '' },
];
