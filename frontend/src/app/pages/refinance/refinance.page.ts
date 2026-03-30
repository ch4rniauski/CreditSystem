import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService, RefinanceRateRow } from '../../core/api.service';

@Component({
  selector: 'app-refinance',
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './refinance.page.html',
  styleUrls: ['./refinance.page.scss'],
})
export default class RefinancePage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly rows = signal<RefinanceRateRow[]>([]);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    validFromDate: ['', Validators.required],
    validToDate: [''],
    ratePercent: [0, [Validators.required, Validators.min(0)]],
  });

  ngOnInit() {
    this.reload();
  }

  reload() {
    this.api.refinanceRates().subscribe({
      next: (r) => this.rows.set(r),
      error: () => this.error.set('Ошибка загрузки'),
    });
  }

  add() {
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    this.api
      .createRefinanceRate({
        validFromDate: v.validFromDate,
        validToDate: v.validToDate || null,
        ratePercent: v.ratePercent,
      })
      .subscribe({
        next: () => {
          this.form.reset({ ratePercent: 0 });
          this.error.set(null);
          this.reload();
        },
        error: (e) => {
          let errorMsg = 'Ошибка при создании ставки';
          if (e.error) {
            // Если error — строка (plain text)
            if (typeof e.error === 'string') {
              errorMsg = e.error;
            }
            // Если error — объект с текстом
            else if (e.error.message) {
              errorMsg = e.error.message;
            }
          } else if (e.message) {
            errorMsg = e.message;
          }
          this.error.set(errorMsg);
        },
      });
  }
}
