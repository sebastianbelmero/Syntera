import React from 'react';

export interface ColumnProps {
  dataField?: string;
  field?: string;
  caption?: string;
  header?: string;
  width?: number | string;
  alignment?: 'left' | 'center' | 'right';
  sortable?: boolean;
  filter?: boolean;
  allowFiltering?: boolean;
  dataType?: 'string' | 'number' | 'date' | 'boolean';
  validationRules?: Array<{ type: string; message?: string }>;
  lookup?: {
    dataSource: string | any[];
    valueExpr: string;
    displayExpr: string | ((item: any) => string);
  };
  allowEditing?: boolean;
  visibleInGrid?: boolean;
  visibleInForm?: boolean;
  /**
   * Hide this column in the mobile (card) view but keep it visible
   * in the desktop table. Useful for secondary info that doesn't
   * fit cleanly in a stacked card layout. Ignored on desktop.
   */
  hideOnMobile?: boolean;
  formVisible?: (formData: any) => boolean;
  linkToEdit?: boolean;
  editorType?:
    | 'text'
    | 'textarea'
    | 'number'
    | 'combobox'
    | 'switch'
    | 'array'
    | 'tagbox'
    | 'url'
    | 'email'
    | 'color'
    | 'tel';
  formRender?: (
    formData: any,
    onChange: (val: any) => void,
    setFieldValue?: (field: string, val: any) => void
  ) => React.ReactNode;
  columns?: ColumnProps[];
  cellRender?: (info: any) => React.ReactNode;
  body?: (rowData: any) => React.ReactNode;
  placeholder?: string;
  pattern?: string;
  min?: number;
  max?: number;
  step?: number;
  maxLength?: number;
  hint?: string;
  confirmChange?: string;
  onValueChange?: (value: any, formData: any, setFieldValue: (field: string, value: any) => void) => void;
  renderInfo?: (formData: any) => React.ReactNode;
}

/**
 * Declarative column configuration component.
 * This component does NOT render to DOM - it's used as JSX configuration
 * similar to DevExtreme's Column component.
 */
export const Column: React.FC<ColumnProps> = () => {
  return null;
};
