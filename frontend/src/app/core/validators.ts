import { AbstractControl, ValidationErrors, ValidatorFn, FormGroup } from '@angular/forms';

/** Валидатор: только digits и + в начале */
export function phoneValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    if (!control.value) return null;
    const value = control.value.trim();
    if (!/^[+]?[0-9\s()-]*$/.test(value)) {
      return { invalidPhone: true };
    }
    return null;
  };
}

/** Валидатор: только буквы и цифры для кода валюты (3 символа) */
export function currencyCodeValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    if (!control.value) return null;
    const value = control.value.trim().toUpperCase();
    if (!/^[A-Z]{3}$/.test(value)) {
      return { invalidCurrencyCode: true };
    }
    return null;
  };
}

/** Валидатор: только буквы для серии паспорта (2 символа) */
export function passportSeriesValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    if (!control.value) return null;
    const value = control.value.trim().toUpperCase();
    if (!/^[A-Z]{2}$/.test(value)) {
      return { invalidPassportSeries: true };
    }
    return null;
  };
}

/** Валидатор: только цифры для номера паспорта (7 символов) */
export function passportNumberValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    if (!control.value) return null;
    const value = control.value.trim();
    if (!/^[0-9]{7}$/.test(value)) {
      return { invalidPassportNumber: true };
    }
    return null;
  };
}

/** Валидатор: только положительные числа с 1-4 десятичными знаками */
export function positiveDecimalValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    if (control.value === null || control.value === '') return null;
    const value = Number(control.value);
    if (isNaN(value) || value < 0) {
      return { invalidPositiveDecimal: true };
    }
    return null;
  };
}

/** Валидатор: только целые положительные числа */
export function positiveIntValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    if (control.value === null || control.value === '') return null;
    const value = Number(control.value);
    if (!Number.isInteger(value) || value <= 0) {
      return { invalidPositiveInt: true };
    }
    return null;
  };
}

/** Валидатор: неотрицательные числа (0 и больше) */
export function nonNegativeValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    if (control.value === null || control.value === '') return null;
    const value = Number(control.value);
    if (isNaN(value) || value < 0) {
      return { negative: true };
    }
    return null;
  };
}

/** Валидатор для диапазона (from <= to) */
export function rangeValidatorFor(toFieldName: string): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    if (!(control instanceof FormGroup)) return null;
    const fromValue = control.get(Object.keys(control.value)[0])?.value;
    const toValue = control.get(toFieldName)?.value;
    if (fromValue === null || toValue === null || fromValue === '' || toValue === '') return null;
    if (Number(fromValue) > Number(toValue)) {
      return { rangeInvalid: true };
    }
    return null;
  };
}

/** Валидатор для диапазона дат (from <= to) */
export function dateRangeValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    if (!(control instanceof FormGroup)) return null;
    const fromValue = control.get('validFrom')?.value;
    const toValue = control.get('validTo')?.value;
    if (!fromValue || !toValue) return null;
    if (new Date(fromValue) > new Date(toValue)) {
      return { dateRangeInvalid: true };
    }
    return null;
  };
}
