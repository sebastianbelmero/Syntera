import React, { useState, useEffect } from 'react';
import { Calendar, Trash2 } from 'lucide-react';
import type { ColumnProps } from './Column';
import type { AxiosInstance } from 'axios';

interface DynamicFieldProps {
  column: ColumnProps & { id: string };
  value: any;
  onChange: (val: any) => void;
  setFieldValue?: (field: string, val: any) => void;
  isRequired?: boolean;
  formData: any;
  apiClient?: AxiosInstance;
}

const DynamicField: React.FC<DynamicFieldProps> = ({
  column,
  value,
  onChange,
  setFieldValue,
  isRequired,
  formData,
  apiClient,
}) => {
  if (column.formRender) {
    return <>{column.formRender(formData, onChange, setFieldValue)}</>;
  }

  const type =
    column.editorType ||
    (column.lookup
      ? 'combobox'
      : column.dataType === 'number'
      ? 'number'
      : column.dataType === 'date'
      ? 'date'
      : 'text');

  const inputClass =
    'p-2 border border-slate-300 dark:border-slate-600 dark:bg-slate-700 dark:text-slate-100 rounded-md text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-200 dark:focus:ring-blue-800 outline-none w-full transition-all';

  if (type === 'textarea') {
    return (
      <textarea
        className={inputClass}
        rows={3}
        required={isRequired}
        value={value || ''}
        onChange={(e) => onChange(e.target.value)}
        placeholder={column.placeholder}
        maxLength={column.maxLength}
      />
    );
  }

  if (type === 'switch') {
    const handleSwitchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
      const newValue = e.target.checked;
      if (column.confirmChange) {
        if (window.confirm(column.confirmChange)) {
          onChange(newValue);
        }
      } else {
        onChange(newValue);
      }
    };
    return (
      <label className="relative inline-flex items-center cursor-pointer mt-1">
        <input
          type="checkbox"
          className="sr-only peer"
          checked={!!value}
          onChange={handleSwitchChange}
        />
        <div className="w-11 h-6 bg-slate-200 dark:bg-slate-600 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 dark:after:border-slate-500 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-blue-600"></div>
        <span className="ml-3 text-sm font-medium text-slate-600 dark:text-slate-300">
          {value ? 'Ya' : 'Tidak'}
        </span>
      </label>
    );
  }

  if (type === 'combobox' && column.lookup) {
    return (
      <LookupSelect
        lookup={column.lookup}
        value={value}
        onChange={onChange}
        isRequired={isRequired}
        inputClass={inputClass}
        apiClient={apiClient}
      />
    );
  }

  if (type === 'tagbox' && column.lookup) {
    return (
      <TagBoxSelect
        lookup={column.lookup}
        value={value || []}
        onChange={onChange}
        isRequired={isRequired}
        inputClass={inputClass}
        apiClient={apiClient}
      />
    );
  }

  if (type === 'array' && column.columns) {
    return (
      <ArrayFieldEditor columns={column.columns} value={value || []} onChange={onChange} />
    );
  }

  if (type === 'date') {
    const dateValue = value ? new Date(value).toISOString().split('T')[0] : '';
    return (
      <div className="relative w-full">
        <Calendar className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <input
          type="date"
          className={`${inputClass} pl-10 cursor-pointer [&::-webkit-calendar-picker-indicator]:absolute [&::-webkit-calendar-picker-indicator]:inset-0 [&::-webkit-calendar-picker-indicator]:w-full [&::-webkit-calendar-picker-indicator]:h-full [&::-webkit-calendar-picker-indicator]:opacity-0`}
          required={isRequired}
          value={dateValue}
          onChange={(e) => onChange(e.target.value)}
          onClick={(e) => {
            const target = e.target as HTMLInputElement;
            if ('showPicker' in target) {
              try {
                target.showPicker();
              } catch (err) {}
            }
          }}
        />
      </div>
    );
  }

  const inputTypeMap: Record<string, string> = {
    number: 'number',
    email: 'email',
    url: 'url',
    tel: 'tel',
    color: 'color',
  };

  const inputType = inputTypeMap[type] || 'text';
  
  if (type === 'number') {
    return (
      <input
        type="number"
        className={inputClass}
        required={isRequired}
        value={value ?? ''}
        onChange={(e) => onChange(Number(e.target.value))}
        placeholder={column.placeholder}
        min={column.min}
        max={column.max}
        step={column.step}
      />
    );
  }

  return (
    <input
      type={inputType}
      className={inputClass}
      required={isRequired}
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value)}
      placeholder={column.placeholder}
      pattern={column.pattern}
      maxLength={column.maxLength}
    />
  );
};

interface LookupSelectProps {
  lookup: {
    dataSource: string | any[];
    valueExpr: string;
    displayExpr: string | ((item: any) => string);
  };
  value: any;
  onChange: (v: any) => void;
  isRequired?: boolean;
  inputClass: string;
  apiClient?: AxiosInstance;
}

const LookupSelect: React.FC<LookupSelectProps> = ({
  lookup,
  value,
  onChange,
  isRequired,
  inputClass,
  apiClient,
}) => {
  const [options, setOptions] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (Array.isArray(lookup.dataSource)) {
      setOptions(lookup.dataSource);
    } else if (typeof lookup.dataSource === 'string' && apiClient) {
      setLoading(true);
      apiClient
        .get(lookup.dataSource)
        .then((res) => {
          setOptions(res.data?.data || res.data);
          setLoading(false);
        })
        .catch((err) => {
          console.error('Failed fetching lookup:', err);
          setLoading(false);
        });
    }
  }, [lookup.dataSource, apiClient]);

  const getDisplayValue = (item: any) => {
    if (typeof lookup.displayExpr === 'function') {
      return lookup.displayExpr(item);
    }
    return item[lookup.displayExpr];
  };

  return (
    <select
      className={inputClass}
      required={isRequired}
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value)}
      disabled={loading}
    >
      <option value="">{loading ? 'Loading...' : '-- Pilih --'}</option>
      {options.map((item) => (
        <option key={item[lookup.valueExpr]} value={item[lookup.valueExpr]}>
          {getDisplayValue(item)}
        </option>
      ))}
    </select>
  );
};

interface TagBoxSelectProps {
  lookup: {
    dataSource: string | any[];
    valueExpr: string;
    displayExpr: string | ((item: any) => string);
  };
  value: any[];
  onChange: (v: any[]) => void;
  isRequired?: boolean;
  inputClass: string;
  apiClient?: AxiosInstance;
}

const TagBoxSelect: React.FC<TagBoxSelectProps> = ({
  lookup,
  value,
  onChange,
  inputClass,
  apiClient,
}) => {
  const [options, setOptions] = useState<any[]>([]);
  const [, setLoading] = useState(false);
  const [isOpen, setIsOpen] = useState(false);
  const [searchTerm, setSearchTerm] = useState('');

  useEffect(() => {
    if (Array.isArray(lookup.dataSource)) {
      setOptions(lookup.dataSource);
    } else if (typeof lookup.dataSource === 'string' && apiClient) {
      setLoading(true);
      apiClient
        .get(lookup.dataSource)
        .then((res) => {
          setOptions(res.data?.data || res.data);
          setLoading(false);
        })
        .catch((err) => {
          console.error('Failed fetching tagbox:', err);
          setLoading(false);
        });
    }
  }, [lookup.dataSource, apiClient]);

  const getDisplayValue = (item: any) => {
    if (typeof lookup.displayExpr === 'function') {
      return lookup.displayExpr(item);
    }
    return item[lookup.displayExpr];
  };

  const filteredOptions = options.filter((item) =>
    getDisplayValue(item).toLowerCase().includes(searchTerm.toLowerCase())
  );

  const handleToggle = (val: any) => {
    if (value.includes(val)) {
      onChange(value.filter((v) => v !== val));
    } else {
      onChange([...value, val]);
    }
  };

  return (
    <div className="relative">
      <div
        className={`${inputClass} min-h-[38px] flex items-center flex-wrap gap-1 cursor-pointer`}
        onClick={() => setIsOpen(!isOpen)}
      >
        {value.length === 0 ? (
          <span className="text-slate-400">Pilih...</span>
        ) : (
          value.map((val) => {
            const item = options.find((o) => o[lookup.valueExpr] === val);
            return item ? (
              <span
                key={val}
                className="inline-flex items-center px-2 py-0.5 rounded text-xs bg-blue-100 dark:bg-blue-900 text-blue-700 dark:text-blue-200"
              >
                {getDisplayValue(item)}
                <button
                  type="button"
                  className="ml-1 text-blue-500 hover:text-blue-700"
                  onClick={(e) => {
                    e.stopPropagation();
                    handleToggle(val);
                  }}
                >
                  ×
                </button>
              </span>
            ) : null;
          })
        )}
      </div>

      {isOpen && (
        <div className="absolute z-10 mt-1 w-full bg-white dark:bg-slate-700 border border-slate-300 dark:border-slate-600 rounded-md shadow-lg max-h-60 overflow-y-auto">
          <input
            type="text"
            placeholder="Cari..."
            className="w-full p-2 border-b border-slate-200 dark:border-slate-600 dark:bg-slate-700 dark:text-slate-100 text-sm"
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
          />
          {filteredOptions.map((item) => (
            <div
              key={item[lookup.valueExpr]}
              className={`px-3 py-2 cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-600 dark:text-slate-100 ${
                value.includes(item[lookup.valueExpr]) ? 'bg-blue-50 dark:bg-blue-900' : ''
              }`}
              onClick={() => handleToggle(item[lookup.valueExpr])}
            >
              {getDisplayValue(item)}
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

interface ArrayFieldEditorProps {
  columns: ColumnProps[];
  value: any[];
  onChange: (v: any[]) => void;
}

const ArrayFieldEditor: React.FC<ArrayFieldEditorProps> = ({ columns, value, onChange }) => {
  const handleAdd = () => {
    onChange([...value, {}]);
  };

  const handleRemove = (index: number) => {
    onChange(value.filter((_, i) => i !== index));
  };

  const handleChange = (index: number, field: string, val: any) => {
    const newValue = [...value];
    newValue[index] = { ...newValue[index], [field]: val };
    onChange(newValue);
  };

  return (
    <div className="space-y-2">
      {value.map((item, index) => (
        <div key={index} className="flex gap-2 items-start p-2 border border-slate-200 dark:border-slate-600 rounded">
          <div className="flex-1 grid grid-cols-2 gap-2">
            {columns.map((col) => {
              const field = col.dataField || col.field || '';
              return (
                <div key={field}>
                  <label className="text-xs text-slate-600 dark:text-slate-300">
                    {col.caption || col.header}
                  </label>
                  <input
                    type="text"
                    className="p-1 border border-slate-300 dark:border-slate-600 dark:bg-slate-700 dark:text-slate-100 rounded text-sm w-full"
                    value={item[field] || ''}
                    onChange={(e) => handleChange(index, field, e.target.value)}
                  />
                </div>
              );
            })}
          </div>
          <button
            type="button"
            className="text-red-500 dark:text-red-400 hover:text-red-700 dark:hover:text-red-300"
            onClick={() => handleRemove(index)}
          >
            <Trash2 className="size-3.5" />
          </button>
        </div>
      ))}
      <button
        type="button"
        className="text-sm text-blue-600 dark:text-blue-400 hover:text-blue-800 dark:hover:text-blue-300"
        onClick={handleAdd}
      >
        + Tambah
      </button>
    </div>
  );
};

export interface AppDynamicFormProps {
  columns: (ColumnProps & { id: string })[];
  initialData: any;
  mode: 'add' | 'edit';
  onSubmit: (data: any) => Promise<void>;
  onCancel: () => void;
  apiClient?: AxiosInstance;
}

export const AppDynamicForm: React.FC<AppDynamicFormProps> = ({
  columns,
  initialData,
  onSubmit,
  apiClient,
}) => {
  const [formData, setFormData] = useState<any>(initialData || {});
  const [, setIsSubmitting] = useState(false);

  const handleChange = (field: string, value: any) => {
    setFormData((prev: any) => ({ ...prev, [field]: value }));
  };

  const handleChangeWithCallback = (col: ColumnProps, fieldName: string, value: any) => {
    handleChange(fieldName, value);
    if (col.onValueChange) {
      col.onValueChange(value, formData, handleChange);
    }
  };

  const handleFormSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSubmitting(true);
    try {
      await onSubmit(formData);
    } finally {
      setIsSubmitting(false);
    }
  };

  const formColumns = columns.filter((col) => {
    if (['id', 'Id', '_actions', 'selection', 'expander'].includes(col.id)) return false;
    if (col.visibleInForm === false) return false;
    if (col.formVisible && !col.formVisible(formData)) return false;
    return true;
  });

  return (
    <div className="flex flex-col flex-1 h-full">
      <form
        id="app-dynamic-form"
        onSubmit={handleFormSubmit}
        className="flex flex-col gap-4 flex-1 overflow-y-visible"
      >
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {formColumns.map((col) => {
            const isRequired = col.validationRules?.some((r) => r.type === 'required');
            const fieldName = col.dataField || col.field || col.id;
            const value = formData[fieldName];
            const header = col.caption || col.header || fieldName;

            const isFullWidth =
              col.editorType === 'textarea' || col.editorType === 'array' || col.formRender;

            return (
              <div
                key={col.id}
                className={`flex flex-col gap-1.5 ${isFullWidth ? 'md:col-span-2' : ''}`}
              >
                <label className="text-[13px] font-bold text-slate-700 dark:text-slate-200">
                  {header} {isRequired && <span className="text-red-500 dark:text-red-400">*</span>}
                </label>
                <DynamicField
                  column={col}
                  value={value}
                  onChange={(v) => handleChangeWithCallback(col, fieldName, v)}
                  setFieldValue={handleChange}
                  isRequired={isRequired}
                  formData={formData}
                  apiClient={apiClient}
                />
                {col.renderInfo && col.renderInfo(formData)}
                {col.hint && (
                  <span className="text-xs text-slate-500 dark:text-slate-400 italic">{col.hint}</span>
                )}
              </div>
            );
          })}
        </div>
      </form>
    </div>
  );
};
