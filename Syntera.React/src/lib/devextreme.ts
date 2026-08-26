/**
 * DevExtreme data protocol adapter.
 *
 * Translates TanStack Table state (pagination, sorting, column filters,
 * global search) into the DevExtreme ASP.NET Data query format understood
 * by the Syntera.Api grid endpoints (`/api/{entity}/grid`), which use the
 * `DevExtreme.AspNet.Data` NuGet package with `DataSourceLoadOptions`.
 */

/**
 * Table state compatible with DevExtreme protocol
 */
export interface DevExtremeLazyState {
  first?: number;
  rows?: number;
  page?: number;
  sortField?: string;
  sortOrder?: number;
  multiSortMeta?: Array<{ field: string; order: number }>;
  filters?: Record<string, { value: any; matchMode: string }>;
  globalFilterValue?: string;
  globalFilterFields?: string[];
  customQueryParams?: Record<string, any>;
}

/**
 * Convert React Table state to DevExtreme query format
 * @param event - Table state event
 * @returns DevExtreme query object
 */
export const buildDevExtremeQuery = (event: DevExtremeLazyState): Record<string, any> => {
  const query: Record<string, any> = {
    requireTotalCount: true,
  };

  // Pagination
  if (event.first !== undefined && event.rows) {
    query.skip = event.first;
    query.take = event.rows;
  } else if (event.first === 0 && event.rows) {
    query.skip = 0;
    query.take = event.rows;
  }

  // Sorting
  if (event.sortField) {
    const desc = event.sortOrder === -1;
    query.sort = [{ selector: event.sortField, desc }];
  } else if (event.multiSortMeta && event.multiSortMeta.length > 0) {
    const sortArr = event.multiSortMeta.map((s) => ({
      selector: s.field,
      desc: s.order === -1,
    }));
    query.sort = sortArr;
  }

  const finalFilters: any[] = [];

  // Global Search
  if (event.globalFilterValue && event.globalFilterFields && event.globalFilterFields.length > 0) {
    query.customQueryParams = { searchTerm: event.globalFilterValue };

    const hasComplexSearch = event.globalFilterFields.some((field) => field.includes('|'));

    if (!hasComplexSearch) {
      const globalSearchFilters: any[] = [];

      event.globalFilterFields.forEach((field) => {
        if (globalSearchFilters.length > 0) {
          globalSearchFilters.push('or');
        }
        globalSearchFilters.push([field, 'contains', event.globalFilterValue]);
      });

      if (globalSearchFilters.length > 0) {
        finalFilters.push(globalSearchFilters);
      }
    }
  }

  // Column Filters
  if (event.filters) {
    const devExpressFilters: any[] = [];

    Object.keys(event.filters).forEach((key) => {
      const filterObj: any = event.filters![key];
      if (filterObj && filterObj.value !== null && filterObj.value !== '') {
        let op = 'contains';
        switch (filterObj.matchMode) {
          case 'contains':
            op = 'contains';
            break;
          case 'startsWith':
            op = 'startswith';
            break;
          case 'endsWith':
            op = 'endswith';
            break;
          case 'equals':
            op = '=';
            break;
          case 'in':
            op = 'in';
            break;
        }

        if (op === 'in' && Array.isArray(filterObj.value)) {
          const inCondition: any[] = [];
          filterObj.value.forEach((val: any) => {
            if (inCondition.length > 0) inCondition.push('or');
            inCondition.push([key, '=', val]);
          });

          if (inCondition.length > 0) {
            if (devExpressFilters.length > 0) devExpressFilters.push('and');
            devExpressFilters.push(inCondition);
          }
        } else {
          const condition = [key, op, filterObj.value];

          if (devExpressFilters.length > 0) {
            devExpressFilters.push('and');
          }
          devExpressFilters.push(condition);
        }
      }
    });

    if (devExpressFilters.length > 0) {
      if (finalFilters.length > 0) {
        const tempGlobal = [...finalFilters];
        finalFilters.length = 0;
        finalFilters.push(
          tempGlobal,
          'and',
          devExpressFilters.length === 1 ? devExpressFilters[0] : devExpressFilters
        );
      } else {
        if (devExpressFilters.length === 1) {
          finalFilters.push(...devExpressFilters[0]);
        } else {
          finalFilters.push(...devExpressFilters);
        }
      }
    }
  }

  if (finalFilters.length > 0) {
    query.filter = finalFilters;
  }

  return query;
};
