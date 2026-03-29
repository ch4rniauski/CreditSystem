import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'contracts', pathMatch: 'full' },
  { path: 'currencies', loadComponent: () => import('./pages/currencies/currencies.page').then((m) => m.default) },
  { path: 'refinance', loadComponent: () => import('./pages/refinance/refinance.page').then((m) => m.default) },
  { path: 'credit-products', loadComponent: () => import('./pages/credit-products/credit-products.page').then((m) => m.default) },
  { path: 'clients', loadComponent: () => import('./pages/clients/clients.page').then((m) => m.default) },
  { path: 'contracts', loadComponent: () => import('./pages/contracts/contracts.page').then((m) => m.default) },
  { path: 'guarantors', loadComponent: () => import('./pages/guarantors/guarantors.page').then((m) => m.default) },
  { path: 'pledges', loadComponent: () => import('./pages/pledges/pledges.page').then((m) => m.default) },
  { path: 'payments', loadComponent: () => import('./pages/payments/payments.page').then((m) => m.default) },
  { path: 'reports', loadComponent: () => import('./pages/reports/reports.page').then((m) => m.default) },
];
