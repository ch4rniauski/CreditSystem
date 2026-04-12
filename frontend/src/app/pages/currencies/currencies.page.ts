import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService, CurrencyRow } from '../../core/api.service';
import { currencyCodeValidator } from '../../core/validators';

@Component({
  selector: 'app-currencies',
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './currencies.page.html',
  styleUrls: ['./currencies.page.scss'],
})
export default class CurrenciesPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly rows = signal<CurrencyRow[]>([]);
  readonly editingId = signal<number | null>(null);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required, currencyCodeValidator()]],
    name: ['', Validators.required],
  });

  ngOnInit() {
    this.reload();
  }

  reload() {
    this.api.currencies().subscribe({
      next: (r) => {
        this.rows.set(r);
        this.error.set(null);
      },
      error: () => this.error.set('Ошибка загрузки'),
    });
  }

  edit(r: CurrencyRow) {
    this.editingId.set(r.id);
    this.form.patchValue({ code: r.code, name: r.name });
  }

  cancelEdit() {
    this.editingId.set(null);
    this.form.reset();
  }

  save() {
    if (this.form.invalid) {
      return;
    }

    const v = this.form.getRawValue();
    const normalizedCode = v.code.trim().toUpperCase();
    const payload = {
      ...v,
      code: normalizedCode,
    };

    this.form.controls.code.setValue(normalizedCode, { emitEvent: false });

    const id = this.editingId();
    if (id === null) {
      this.api.createCurrency(payload).subscribe({
        next: () => {
          this.cancelEdit();
          this.reload();
        },
        error: (e) => this.error.set(e.error ?? 'Ошибка'),
      });
    } else {
      this.api.updateCurrency(id, payload).subscribe({
        next: () => {
          this.cancelEdit();
          this.reload();
        },
        error: (e) => this.error.set(e.error ?? 'Ошибка'),
      });
    }
  }

  remove(r: CurrencyRow) {
    if (!confirm(`Удалить ${r.code}?`)) {
      return;
    }

    this.api.deleteCurrency(r.id).subscribe({
      next: () => this.reload(),
      error: (e) => this.error.set(typeof e.error === 'string' ? e.error : 'Невозможно удалить'),
    });
  }
}
