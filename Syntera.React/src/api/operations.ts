/**
 * Inventory + Sales + Customers + Dashboard API endpoints.
 */

import { del, get, patch, post, put } from "./client";
import type {
  CustomerDto,
  CustomerUpsertDto,
  DashboardSummaryDto,
  DashboardTrendDto,
  InventoryAdjustmentRequest,
  InventoryMovementDto,
  PageQuery,
  PagedResult,
  SaleCreateDto,
  SaleDto,
  SaleStatus,
  SaleStatusUpdateDto,
} from "../types";
import { buildPageQuery } from "./catalog";

export const inventoryApi = {
  page: (q: PageQuery = {}) =>
    get<PagedResult<InventoryMovementDto>>(`/inventory${buildPageQuery(q)}`),
  history: (productId: string) =>
    get<InventoryMovementDto[]>(`/inventory/product/${productId}`),
  record: (req: InventoryAdjustmentRequest) =>
    post<InventoryMovementDto>("/inventory", req),
};

export const customerApi = {
  page: (q: PageQuery = {}) =>
    get<PagedResult<CustomerDto>>(`/customers${buildPageQuery(q)}`),
  get: (id: string) => get<CustomerDto>(`/customers/${id}`),
  create: (dto: CustomerUpsertDto) => post<CustomerDto>("/customers", dto),
  update: (id: string, dto: CustomerUpsertDto) =>
    put<CustomerDto>(`/customers/${id}`, dto),
  remove: (id: string) => del(`/customers/${id}`),
};

export const saleApi = {
  page: (q: PageQuery = {}) =>
    get<PagedResult<SaleDto>>(`/sales${buildPageQuery(q)}`),
  get: (id: string) => get<SaleDto>(`/sales/${id}`),
  create: (dto: SaleCreateDto) => post<SaleDto>("/sales", dto),
  updateStatus: (id: string, status: SaleStatus) =>
    patch<SaleDto>(`/sales/${id}/status`, { status } satisfies SaleStatusUpdateDto),
};

export const dashboardApi = {
  summary: () => get<DashboardSummaryDto>("/dashboard/summary"),
  trend: () => get<DashboardTrendDto>("/dashboard/trend"),
};
