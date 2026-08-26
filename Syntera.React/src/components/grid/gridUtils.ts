import type { SortingState, PaginationState, ColumnFiltersState } from '@tanstack/react-table';
import type { DevExtremeLazyState } from '../../lib/devextreme';

/**
 * Converts TanStack Table state into the DevExtreme query payload
 * consumed by `useDevExtremeData`.
 */
export function buildTanstackToLazyState(
  pagination: PaginationState,
  sorting: SortingState,
  columnFilters: ColumnFiltersState,
  filterModes: Record<string, string>,
  globalFilterValue: string,
  globalFilterFields: string[]
): DevExtremeLazyState {
  const filters: Record<string, any> = {};
  columnFilters.forEach((filter) => {
    if (Array.isArray(filter.value)) {
      filters[filter.id] = {
        value: filter.value,
        matchMode: 'in',
      };
    } else {
      filters[filter.id] = {
        value: filter.value,
        matchMode: filterModes[filter.id] || 'contains',
      };
    }
  });

  const state: any = {
    first: pagination.pageIndex * pagination.pageSize,
    rows: pagination.pageSize,
    page: pagination.pageIndex,
    globalFilterValue,
    globalFilterFields,
    filters,
  };

  if (sorting.length > 0) {
    if (sorting.length === 1) {
      state.sortField = sorting[0].id;
      state.sortOrder = sorting[0].desc ? -1 : 1;
    } else {
      state.multiSortMeta = sorting.map((s) => ({
        field: s.id,
        order: s.desc ? -1 : 1,
      }));
    }
  }

  return state;
}
