import React, { useState, useMemo, useEffect } from 'react';
import {
  useReactTable,
  getCoreRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  getFilteredRowModel,
  getExpandedRowModel,
  flexRender,
  type ColumnDef,
  type SortingState,
  type PaginationState,
  type ColumnFiltersState,
  type ExpandedState,
  type VisibilityState,
} from '@tanstack/react-table';
import {
  Plus,
  Search,
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  ChevronDown,
  ArrowUpDown,
  Pencil,
  Trash2,
  Loader2,
  PackageOpen,
} from 'lucide-react';
import { Column, type ColumnProps } from './Column';
import { useDevExtremeData } from '../../hooks/useDevExtremeData';
import { api as apiClient } from '../../api/client';
import { FilterOperatorDropdown } from './FilterOperatorDropdown';
import { ColumnChooserDropdown } from './ColumnChooserDropdown';
import { HeaderFilterDropdown } from './HeaderFilterDropdown';
import { HighlightText } from './HighlightText';
import { AppDynamicForm } from './AppDynamicForm';
import { Modal } from '../Modal';
import { Drawer } from '../Drawer';
import { Skeleton } from '../ui';

export interface AppGridProps {
  columns?: ColumnProps[];
  dynamicColumns?: ColumnProps[];
  children?: React.ReactNode;
  apiEndpoint?: string;
  staticData?: any[];
  title?: string;
  pageSize?: number;
  enablePagination?: boolean;
  enableSorting?: boolean;
  enableFiltering?: boolean;
  enableColumnChooser?: boolean;
  enableMasterDetail?: boolean;
  enableCrud?: boolean;
  enableRowSelection?: boolean;
  enableStatePersistence?: boolean;
  persistenceKey?: string;
  gridId?: string;
  globalFilterFields?: string[];
  onRowClick?: (row: any) => void;
  onSelectionChange?: (selectedRows: any[]) => void;
  renderMasterDetail?: (row: any, inheritedHighlight?: string) => React.ReactNode;
  DetailTemplate?: (row: any, inheritedHighlight?: string) => React.ReactNode;
  renderCustomActions?: (row: any) => React.ReactNode;
  customActions?: (row: any) => React.ReactNode;
  toolbarContent?: React.ReactNode;
  fetchKey?: string | number;
  initialGlobalFilterValue?: string;
  inheritedHighlightTerm?: string;
  scrollingMode?: 'pagination' | 'infinite';
  showFilterRow?: boolean;
  allowAdd?: boolean;
  allowEdit?: boolean;
  allowDelete?: boolean;
  selectionMode?: 'none' | 'single' | 'multiple';
  data?: any[];
  onDataLoaded?: (data: any[]) => void;
  getAccessToken?: () => string | null;

  // ─── v2.1 New Props ─────────────────────────────────────
  /** Loading state — shows skeleton rows instead of empty table */
  loading?: boolean;
  /** Custom empty state content (shown when data is empty). Defaults to a friendly message with icon. */
  emptyState?: React.ReactNode;
  /** Sticky table header on scroll. Default true. */
  stickyHeader?: boolean;
  /** Max height for the table wrapper (e.g. "400px", "60vh"). Enables internal scroll. */
  maxHeight?: string;
  /** Empty state CTA callback — if provided, shows a "Tambah" button in the empty state */
  onEmptyStateAction?: () => void;
  /** Label for the empty state CTA button */
  emptyStateActionLabel?: string;
}

export const AppGrid: React.FC<AppGridProps> = ({
  columns,
  dynamicColumns,
  children,
  apiEndpoint,
  staticData = [],
  title,
  pageSize = 10,
  enablePagination = true,
  enableSorting = true,
  enableFiltering = true,
  enableColumnChooser = true,
  enableMasterDetail = false,
  enableCrud = false,
  enableRowSelection = false,
  enableStatePersistence = false,
  persistenceKey,
  gridId,
  globalFilterFields = [],
  onRowClick,
  onSelectionChange,
  renderMasterDetail,
  DetailTemplate,
  renderCustomActions,
  customActions,
  toolbarContent,
  fetchKey,
  initialGlobalFilterValue = '',
  inheritedHighlightTerm,
  scrollingMode: _scrollingMode = 'pagination',
  showFilterRow: _showFilterRow = true,
  allowAdd = false,
  allowEdit = false,
  allowDelete = false,
  selectionMode = 'none',
  data: dataProp,
  onDataLoaded,
  getAccessToken,

  // v2.1 props
  loading = false,
  emptyState,
  stickyHeader = true,
  maxHeight,
  onEmptyStateAction,
  emptyStateActionLabel = 'Tambah Data',
}) => {
  // Resolve columns from multiple sources
  const resolvedColumns = useMemo(() => {
    if (dynamicColumns) return dynamicColumns;
    if (columns) return columns;
    if (children) {
      const cols: ColumnProps[] = [];
      React.Children.forEach(children, (child) => {
        if (React.isValidElement(child) && child.type === Column) {
          cols.push(child.props as ColumnProps);
        }
      });
      return cols;
    }
    return [];
  }, [columns, dynamicColumns, children]);

  const resolvedCustomActions = renderCustomActions || customActions;
  const resolvedMasterDetail = renderMasterDetail || DetailTemplate;
  void resolvedMasterDetail;

  // Derive CRUD endpoint from grid endpoint. The AppGrid
  // uses `apiEndpoint` for BOTH data load (the DevExtreme /grid route)
  // AND CRUD (POST/PUT/DELETE to the entity root). To keep both
  // working in Syntera — where grid data lives at `/api/{entity}/grid`
  // but CRUD lives at `/api/{entity}` — we strip the `/grid` suffix
  // to recover the CRUD endpoint. If the caller passes a non-`/grid`
  // endpoint, we fall back to the original value (backward compat).
  const crudEndpoint = apiEndpoint && apiEndpoint.endsWith('/grid')
    ? apiEndpoint.slice(0, -'/grid'.length)
    : apiEndpoint;

  const canAdd = enableCrud || allowAdd;
  const canEdit = enableCrud || allowEdit;
  const canDelete = enableCrud || allowDelete;
  const effectiveRowSelection = selectionMode !== 'none' ? true : enableRowSelection;

  // Stabilize the array identity (keyed on contents) so the data-loading
  // effect below does not refire on every render when callers pass an
  // inline array literal. Rebuilt only when the field list actually changes.
  const globalFilterFieldsKey = JSON.stringify(globalFilterFields);
  const stableGlobalFilterFields = useMemo(
    () => JSON.parse(globalFilterFieldsKey) as string[],
    [globalFilterFieldsKey]
  );

  // State
  const [data, setData] = useState<any[]>(dataProp || staticData);
  const [sorting, setSorting] = useState<SortingState>([]);
  const [pagination, setPagination] = useState<PaginationState>({ pageIndex: 0, pageSize });
  const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([]);
  const [columnVisibility, setColumnVisibility] = useState<VisibilityState>({});
  const [expanded, setExpanded] = useState<ExpandedState>({});
  const [rowSelection, setRowSelection] = useState({});
  const [globalFilter, setGlobalFilter] = useState(initialGlobalFilterValue);
  const [filterModes, setFilterModes] = useState<Record<string, string>>({});

  const [, setDebouncedFilters] = useState(columnFilters);
  const [debouncedGlobal, setDebouncedGlobal] = useState(globalFilter);

  useEffect(() => {
    const handler = setTimeout(() => {
      setDebouncedFilters(columnFilters);
      setDebouncedGlobal(globalFilter);
    }, 400);
    return () => clearTimeout(handler);
  }, [columnFilters, globalFilter]);

  const activeHighlight = debouncedGlobal || inheritedHighlightTerm || '';

  useEffect(() => {
    if (activeHighlight && activeHighlight.trim().length > 0) {
      setExpanded(true);
    } else {
      setExpanded({});
    }
  }, [activeHighlight]);

  // CRUD state
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [formMode, setFormMode] = useState<'add' | 'edit'>('add');
  const [editingRow, setEditingRow] = useState<any>(null);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [deletingRow, setDeletingRow] = useState<any>(null);
  const [isLoading, setIsLoading] = useState(false);

  // Persist state
  useEffect(() => {
    const storageKey = persistenceKey || gridId;
    if ((enableStatePersistence || gridId) && storageKey) {
      const saved = localStorage.getItem(`appgrid-${storageKey}`);
      if (saved) {
        try {
          const parsed = JSON.parse(saved);
          if (parsed.sorting) setSorting(parsed.sorting);
          if (parsed.columnVisibility) setColumnVisibility(parsed.columnVisibility);
          if (parsed.pagination) setPagination(parsed.pagination);
        } catch { /* ignore */ }
      }
    }
  }, [persistenceKey, gridId, enableStatePersistence]);

  useEffect(() => {
    const storageKey = persistenceKey || gridId;
    if ((enableStatePersistence || gridId) && storageKey) {
      localStorage.setItem(
        `appgrid-${storageKey}`,
        JSON.stringify({ sorting, columnVisibility, pagination })
      );
    }
  }, [sorting, columnVisibility, pagination, persistenceKey, gridId, enableStatePersistence]);

  // Sync data prop
  useEffect(() => {
    if (dataProp !== undefined) {
      setData(dataProp);
    }
  }, [dataProp]);

  // DevExtreme data loading
  const { data: apiData, totalRecords, loading: apiLoading, loadData } = useDevExtremeData<any>({
    endpoint: apiEndpoint || '',
    getAccessToken,
    loadMethod: "GET",
  });

  useEffect(() => {
    if (apiEndpoint) {
      loadData({
        skip: pagination.pageIndex * pagination.pageSize,
        take: pagination.pageSize,
        sort: sorting.map((s) => ({ selector: s.id, desc: s.desc })),
        filter: columnFilters.length > 0
          ? columnFilters.map((f) => [f.id, 'contains', f.value])
          : undefined,
        globalFilterValue: globalFilter,
        globalFilterFields: stableGlobalFilterFields,
      } as any);
    }
  }, [apiEndpoint, pagination, sorting, columnFilters, globalFilter, stableGlobalFilterFields, loadData]);

  useEffect(() => {
    // Only update data from API response when apiEndpoint is set.
    // Without this guard, initial apiData=[] overwrites dataProp/staticData.
    if (apiEndpoint && apiData && apiData.length >= 0) {
      setData(apiData);
      if (onDataLoaded) onDataLoaded(apiData);
    }
  }, [apiData, onDataLoaded, apiEndpoint]);

  useEffect(() => {
    if (fetchKey !== undefined) {
      loadData({
        skip: pagination.pageIndex * pagination.pageSize,
        take: pagination.pageSize,
      } as any);
    }
  }, [fetchKey]); // eslint-disable-line react-hooks/exhaustive-deps

  // Column definitions
  const columnDefs = useMemo(() => {
    const defs: ColumnDef<any>[] = [];

    // Expand column (master-detail)
    if (enableMasterDetail && renderMasterDetail) {
      defs.push({
        id: '__expand',
        header: () => null,
        cell: ({ row }) => (
          <button
            onClick={() => row.toggleExpanded()}
            className="rounded p-1 transition-colors hover:bg-muted"
            aria-label={row.getIsExpanded() ? 'Collapse' : 'Expand'}
          >
            <ChevronDown
              className={`size-4 text-muted-foreground transition-transform duration-200 ${
                row.getIsExpanded() ? 'rotate-0' : '-rotate-90'
              }`}
            />
          </button>
        ),
        enableSorting: false,
        enableColumnFilter: false,
        size: 40,
      });
    }

    // Data columns
    resolvedColumns.forEach((col) => {
      const accessor = col.dataField || col.field || '';
      if (!accessor) return;
      if (col.visibleInGrid === false) return;

      defs.push({
        id: accessor,
        accessorKey: accessor,
        header: col.caption || col.header || accessor,
        cell: (info) => {
          const value = info.getValue();
          const rowData = info.row.original;
          if (col.body) return col.body(rowData);
          if (col.cellRender) {
            return col.cellRender({ value, row: rowData, column: col });
          }
          if (col.dataType === 'boolean') {
            return (
              <div className={`h-5 w-9 rounded-full transition-colors ${value ? 'bg-primary' : 'bg-muted'}`} />
            );
          }
          if (col.dataType === 'date' && value) {
            return new Date(value as string).toLocaleDateString('id-ID');
          }
          if (col.dataType === 'number' && value !== null && value !== undefined) {
            return new Intl.NumberFormat('id-ID').format(value as number);
          }
          if (Array.isArray(value)) {
            return value.length > 0 ? `${value.length} item(s)` : '-';
          }
          if (value !== null && value !== undefined && typeof value === 'object') {
            return '-';
          }
          if (globalFilter && typeof value === 'string') {
            return <HighlightText text={value} highlight={globalFilter} />;
          }
          return value ?? '-';
        },
        enableSorting: enableSorting && col.sortable !== false,
        enableColumnFilter: enableFiltering && col.allowFiltering !== false,
        size: col.width ? parseInt(String(col.width)) : undefined,
      });
    });

    // Actions column
    if (canEdit || canDelete || resolvedCustomActions) {
      defs.push({
        id: '__actions',
        header: 'Aksi',
        cell: ({ row }) => (
          <div className="flex justify-center gap-2">
            {resolvedCustomActions && resolvedCustomActions(row.original)}
            {canEdit && (
              <button
                onClick={() => handleEdit(row.original)}
                className="rounded p-1.5 text-primary transition-colors hover:bg-primary/10"
                title="Edit"
              >
                <Pencil className="size-3.5" />
              </button>
            )}
            {canDelete && (
              <button
                onClick={() => handleDelete(row.original)}
                className="rounded p-1.5 text-danger transition-colors hover:bg-danger/10"
                title="Delete"
              >
                <Trash2 className="size-3.5" />
              </button>
            )}
          </div>
        ),
        enableSorting: false,
        enableColumnFilter: false,
        size: resolvedCustomActions ? 150 : 100,
      });
    }

    return defs;
  }, [resolvedColumns, enableSorting, enableFiltering, enableMasterDetail, canEdit, canDelete, resolvedCustomActions, globalFilter, renderMasterDetail]);

  // Table instance
  // oxlint-disable-next-line react/incompatible-library -- TanStack Table's useReactTable() intentionally returns unstable function references; memoizing them would freeze the grid UI. The React Compiler already skips this component.
  const table = useReactTable({
    data,
    columns: columnDefs,
    state: { sorting, pagination, columnFilters, columnVisibility, expanded, rowSelection, globalFilter },
    onSortingChange: setSorting,
    onPaginationChange: setPagination,
    onColumnFiltersChange: setColumnFilters,
    onColumnVisibilityChange: setColumnVisibility,
    onExpandedChange: setExpanded,
    onRowSelectionChange: (updater) => {
      setRowSelection(updater);
      if (onSelectionChange) {
        const selectedRows = table?.getSelectedRowModel?.()?.rows?.map((r: any) => r.original) || [];
        onSelectionChange(selectedRows);
      }
    },
    onGlobalFilterChange: setGlobalFilter,
    getCoreRowModel: getCoreRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getExpandedRowModel: getExpandedRowModel(),
    getRowId: (row: any, index) => row?.id?.toString?.() || row?.Id?.toString?.() || `row-${index}`,
    manualPagination: !!apiEndpoint,
    manualSorting: !!apiEndpoint,
    manualFiltering: !!apiEndpoint,
    pageCount: apiEndpoint && totalRecords ? Math.ceil(totalRecords / pageSize) : undefined,
    enableRowSelection: effectiveRowSelection,
    enableExpanding: enableMasterDetail,
  });

  // CRUD handlers
  const handleAdd = () => {
    setFormMode('add');
    setEditingRow(null);
    setIsFormOpen(true);
  };

  const handleEdit = (row: any) => {
    setFormMode('edit');
    setEditingRow(row);
    setIsFormOpen(true);
  };

  const handleDelete = (row: any) => {
    setDeletingRow(row);
    setIsDeleteModalOpen(true);
  };

  const handleFormSubmit = async (formData: any) => {
    if (!crudEndpoint) { setIsFormOpen(false); return; }
    setIsLoading(true);
    try {
      if (formMode === 'add') {
        await apiClient.post(crudEndpoint, formData);
      } else {
        const id = editingRow?.id || editingRow?.Id;
        await apiClient.put(`${crudEndpoint}/${id}`, formData);
      }
      setIsFormOpen(false);
      setEditingRow(null);
      loadData({
        skip: pagination.pageIndex * pagination.pageSize,
        take: pagination.pageSize,
      } as any);
    } catch (error) {
      console.error('Failed to save data', error);
      alert('Gagal menyimpan data');
    } finally {
      setIsLoading(false);
    }
  };

  const handleDeleteConfirm = async () => {
    if (!crudEndpoint || !deletingRow) return;
    setIsLoading(true);
    try {
      const id = deletingRow.id || deletingRow.Id;
      await apiClient.delete(`${crudEndpoint}/${id}`);
      setIsDeleteModalOpen(false);
      setDeletingRow(null);
      if (apiData) setData(apiData);
    } catch (error) {
      console.error('Failed to delete data', error);
      alert('Gagal menghapus data');
    } finally {
      setIsLoading(false);
    }
  };

  // v2.1: Compute pagination info
  const totalDataCount = apiEndpoint ? totalRecords : data.length;
  const startRow = totalDataCount === 0 ? 0 : pagination.pageIndex * pagination.pageSize + 1;
  const endRow = Math.min(startRow + table.getRowModel().rows.length - 1, totalDataCount);
  const showLoading = loading || apiLoading;
  const showEmptyState = !showLoading && table.getRowModel().rows.length === 0;

  // v2.1: Default empty state
  const defaultEmptyState = (
    <div className="flex flex-col items-center justify-center py-12 text-center">
      <PackageOpen className="mb-3 size-12 text-muted-foreground/50" />
      <p className="mb-1 text-sm font-medium text-foreground">Tidak ada data</p>
      <p className="mb-4 text-xs text-muted-foreground">
        {globalFilter ? 'Tidak ada hasil yang cocok dengan pencarian Anda.' : 'Belum ada data tersedia.'}
      </p>
      {onEmptyStateAction && (
        <button onClick={onEmptyStateAction} className="app-grid-btn app-grid-btn-primary">
          <Plus className="mr-1.5 size-3.5" />
          {emptyStateActionLabel}
        </button>
      )}
    </div>
  );

  return (
    <div className="app-grid-container">
      {/* Toolbar */}
      <div className="app-grid-toolbar">
        {title && <h3 className="app-grid-title">{title}</h3>}
        {toolbarContent && <div className="app-grid-toolbar-custom">{toolbarContent}</div>}
        <div className="app-grid-toolbar-actions">
          {canAdd && (
            <button onClick={handleAdd} className="app-grid-btn app-grid-btn-primary">
              <Plus className="mr-1.5 size-3.5" />
              Tambah
            </button>
          )}
          {enableColumnChooser && <ColumnChooserDropdown table={table} />}
          {enableFiltering && globalFilterFields.length > 0 && (
            <div className="app-grid-search">
              <Search className="size-3.5 text-muted-foreground" />
              <input
                type="text"
                placeholder="Cari..."
                value={globalFilter}
                onChange={(e) => setGlobalFilter(e.target.value)}
                className="app-grid-search-input"
              />
            </div>
          )}
        </div>
      </div>

      {/* Desktop table view (hidden on mobile) */}
      <div className="app-grid-table-wrapper hidden md:block" style={maxHeight ? { maxHeight, overflowY: 'auto' } : undefined}>
        <table className="app-grid-table">
          <thead className={stickyHeader ? 'sticky top-0 z-10' : ''}>
            {table.getHeaderGroups().map((headerGroup) => (
              <tr key={headerGroup.id}>
                {headerGroup.headers.map((header) => {
                  const canFilter = header.column.getCanFilter();
                  const canSort = header.column.getCanSort();
                  const isSorted = header.column.getIsSorted();

                  return (
                    <th key={header.id} colSpan={header.colSpan}>
                      <div className="app-grid-th-content">
                        <div
                          onClick={canSort ? header.column.getToggleSortingHandler() : undefined}
                          className={`flex items-center gap-1 ${canSort ? 'cursor-pointer select-none hover:text-foreground' : ''}`}
                        >
                          {header.isPlaceholder
                            ? null
                            : flexRender(header.column.columnDef.header, header.getContext())}
                          {canSort && (
                            <span className="text-muted-foreground">
                              {isSorted === 'asc' ? (
                                <ChevronDown className="size-3 rotate-180" />
                              ) : isSorted === 'desc' ? (
                                <ChevronDown className="size-3" />
                              ) : (
                                <ArrowUpDown className="size-3 opacity-40" />
                              )}
                            </span>
                          )}
                        </div>
                        {canFilter && (
                          <HeaderFilterDropdown
                            column={header.column}
                            apiEndpoint={apiEndpoint}
                            apiClient={apiClient}
                          />
                        )}
                      </div>
                      {canFilter && (
                        <div className="app-grid-filter-row">
                          <FilterOperatorDropdown
                            mode={filterModes[header.id] || 'contains'}
                            onChange={(mode) =>
                              setFilterModes((prev) => ({ ...prev, [header.id]: mode }))
                            }
                          />
                          <input
                            type="text"
                            value={(header.column.getFilterValue() as string) ?? ''}
                            onChange={(e) => header.column.setFilterValue(e.target.value)}
                            placeholder="Filter..."
                            className="app-grid-filter-input"
                          />
                        </div>
                      )}
                    </th>
                  );
                })}
              </tr>
            ))}
          </thead>
          <tbody>
            {/* Loading skeleton */}
            {showLoading && (
              <>
                {Array.from({ length: 5 }).map((_, idx) => (
                  <tr key={`skeleton-${idx}`}>
                    {columnDefs.map((col) => (
                      <td key={col.id}>
                        <Skeleton className="h-4 w-full max-w-[120px]" />
                      </td>
                    ))}
                  </tr>
                ))}
              </>
            )}

            {/* Data rows */}
            {!showLoading && table.getRowModel().rows.map((row) => (
              <React.Fragment key={row.id}>
                <tr
                  onClick={(e) => {
                    // Don't toggle if user clicked a button/link inside the row
                    const target = e.target as HTMLElement;
                    if (target.closest('button, a, input, select, [role="button"]')) return;

                    // Toggle expand if master-detail is enabled
                    if (enableMasterDetail && renderMasterDetail) {
                      row.toggleExpanded();
                    }
                    // Also call onRowClick if provided
                    onRowClick?.(row.original);
                  }}
                  className={`transition-colors ${
                    (enableMasterDetail && renderMasterDetail) || onRowClick
                      ? 'cursor-pointer hover:bg-muted/50'
                      : 'hover:bg-muted/30'
                  } ${row.getIsExpanded() ? 'bg-muted/30' : ''}`}
                >
                  {row.getVisibleCells().map((cell) => (
                    <td key={cell.id}>
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </td>
                  ))}
                </tr>
                {row.getIsExpanded() && renderMasterDetail && (
                  <tr className="bg-muted/20">
                    <td colSpan={row.getVisibleCells().length} className="border-l-4 border-primary p-0">
                      <div className="p-4">{renderMasterDetail(row.original)}</div>
                    </td>
                  </tr>
                )}
              </React.Fragment>
            ))}

            {/* Empty state */}
            {showEmptyState && (
              <tr>
                <td colSpan={table.getAllColumns().length || 1} className="p-0">
                  {emptyState || defaultEmptyState}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Mobile card view (visible < md). Each row collapses to a stacked
          card — first column becomes the title, remaining columns render
          as label/value pairs. Columns with `hideOnMobile: true` are
          dropped to keep the card compact. Master-detail is rendered
          as a <details> element so the chevron pattern stays familiar. */}
      <div className="app-grid-mobile md:hidden">
        {showLoading && Array.from({ length: 3 }).map((_, idx) => (
          <div key={`m-skel-${idx}`} className="app-grid-mobile-card">
            <Skeleton className="mb-2 h-4 w-2/3" />
            <Skeleton className="h-3 w-full" />
            <Skeleton className="mt-1 h-3 w-3/4" />
          </div>
        ))}
        {!showLoading && table.getRowModel().rows.length === 0 && (
          <div className="p-4">{emptyState || defaultEmptyState}</div>
        )}
        {!showLoading && table.getRowModel().rows.map((row) => {
          const cols = resolvedColumns.filter(c => c.visibleInGrid !== false);
          const firstCol = cols.find(c => !c.hideOnMobile);
          const restCols = cols.filter((c, i) => c !== firstCol && !c.hideOnMobile && i > 0);
          const renderColValue = (col: ColumnProps) => {
            const accessor = col.dataField || col.field || '';
            const value = row.original[accessor];
            if (col.body) return col.body(row.original);
            if (col.cellRender) return col.cellRender({ value, row: row.original, column: col });
            if (value === null || value === undefined) return '-';
            if (typeof value === 'object') return '-';
            return String(value);
          };
          return (
            <div key={row.id} className="app-grid-mobile-card">
              {firstCol && (
                <div className="app-grid-mobile-card-title">{renderColValue(firstCol)}</div>
              )}
              <div className="app-grid-mobile-card-body">
                {restCols.map((col) => {
                  const accessor = col.dataField || col.field || '';
                  return (
                    <div key={accessor} className="app-grid-mobile-card-row">
                      <span className="app-grid-mobile-card-label">{col.caption || col.header || accessor}</span>
                      <span className="app-grid-mobile-card-value">{renderColValue(col)}</span>
                    </div>
                  );
                })}
              </div>
              {enableMasterDetail && renderMasterDetail && (
                <details className="app-grid-mobile-detail">
                  <summary className="app-grid-mobile-detail-summary">Detail</summary>
                  <div className="app-grid-mobile-detail-body">{renderMasterDetail(row.original, activeHighlight)}</div>
                </details>
              )}
              {(canEdit || canDelete || resolvedCustomActions) && (
                <div className="app-grid-mobile-card-actions">
                  {resolvedCustomActions && resolvedCustomActions(row.original)}
                  {canEdit && (
                    <button onClick={() => handleEdit(row.original)} className="app-grid-btn app-grid-btn-primary" aria-label="Edit">
                      <Pencil className="mr-1 size-3.5" /> Edit
                    </button>
                  )}
                  {canDelete && (
                    <button onClick={() => handleDelete(row.original)} className="app-grid-btn app-grid-btn-danger" aria-label="Hapus">
                      <Trash2 className="mr-1 size-3.5" /> Hapus
                    </button>
                  )}
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* Pagination */}
      {enablePagination && (
        <div className="app-grid-pager">
          <div className="app-grid-pager-info">
            {totalDataCount === 0
              ? '0 data'
              : `${startRow}-${endRow} dari ${totalDataCount} data`}
          </div>
          <div className="app-grid-pager-controls">
            <button
              onClick={() => table.setPageIndex(0)}
              disabled={!table.getCanPreviousPage()}
              className="app-grid-pager-btn"
              aria-label="First page"
            >
              <ChevronsLeft className="size-4" />
            </button>
            <button
              onClick={() => table.previousPage()}
              disabled={!table.getCanPreviousPage()}
              className="app-grid-pager-btn"
              aria-label="Previous page"
            >
              <ChevronLeft className="size-4" />
            </button>
            <span className="app-grid-pager-current">
              {table.getState().pagination.pageIndex + 1} / {table.getPageCount() || 1}
            </span>
            <button
              onClick={() => table.nextPage()}
              disabled={!table.getCanNextPage()}
              className="app-grid-pager-btn"
              aria-label="Next page"
            >
              <ChevronRight className="size-4" />
            </button>
            <button
              onClick={() => table.setPageIndex(table.getPageCount() - 1)}
              disabled={!table.getCanNextPage()}
              className="app-grid-pager-btn"
              aria-label="Last page"
            >
              <ChevronsRight className="size-4" />
            </button>
            <span className="text-xs text-muted-foreground">per halaman</span>
            <select
              value={table.getState().pagination.pageSize}
              onChange={(e) => table.setPageSize(Number(e.target.value))}
              className="app-grid-pager-select"
            >
              {[10, 20, 30, 50, 100].map((size) => (
                <option key={size} value={size}>{size}</option>
              ))}
            </select>
          </div>
        </div>
      )}

      {/* CRUD Form Drawer */}
      {canAdd && canEdit && (
        <Drawer
          isOpen={isFormOpen}
          onClose={() => setIsFormOpen(false)}
          title={formMode === 'add' ? 'Tambah Data' : 'Edit Data'}
        >
          <AppDynamicForm
            columns={resolvedColumns.map((col, idx) => ({ ...col, id: col.dataField || col.field || `col-${idx}` }))}
            initialData={editingRow || {}}
            mode={formMode}
            onSubmit={handleFormSubmit}
            onCancel={() => setIsFormOpen(false)}
          />
        </Drawer>
      )}

      {/* Delete Confirmation Modal */}
      <Modal
        isOpen={isDeleteModalOpen}
        onClose={() => { setIsDeleteModalOpen(false); setDeletingRow(null); }}
        title="Konfirmasi Hapus"
        footer={
          <div className="flex justify-end gap-2">
            <button
              onClick={() => { setIsDeleteModalOpen(false); setDeletingRow(null); }}
              className="app-grid-btn"
              disabled={isLoading}
            >
              Batal
            </button>
            <button
              onClick={handleDeleteConfirm}
              className="app-grid-btn app-grid-btn-danger"
              disabled={isLoading}
            >
              {isLoading ? <Loader2 className="mr-1.5 size-3.5 animate-spin" /> : null}
              Hapus
            </button>
          </div>
        }
      >
        <p>Apakah Anda yakin ingin menghapus data ini?</p>
      </Modal>
    </div>
  );
};
