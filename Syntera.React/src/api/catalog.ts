/**
 * Catalog API — categories, suppliers, products. Endpoints match the
 * backend controllers under /api/{categories,suppliers,products}.
 * Query string construction for paginated endpoints is centralised in
 * `buildPageQuery` so every list call uses the same param names.
 */

import { del, get, post, put } from "./client";
import type {
  CategoryDto,
  CategoryTreeNodeDto,
  CategoryUpsertDto,
  PageQuery,
  PagedResult,
  ProductDto,
  ProductStockAdjustDto,
  ProductUpsertDto,
  SupplierDto,
  SupplierUpsertDto,
} from "../types";

export function buildPageQuery(q: PageQuery = {}): string {
  const params = new URLSearchParams();
  if (q.page) params.set("page", String(q.page));
  if (q.pageSize) params.set("pageSize", String(q.pageSize));
  if (q.search) params.set("search", q.search);
  const s = params.toString();
  return s ? `?${s}` : "";
}

export const categoryApi = {
  page: (q: PageQuery = {}) =>
    get<PagedResult<CategoryDto>>(`/categories${buildPageQuery(q)}`),
  tree: () => get<CategoryTreeNodeDto[]>("/categories/tree"),
  get: (id: string) => get<CategoryDto>(`/categories/${id}`),
  create: (dto: CategoryUpsertDto) => post<CategoryDto>("/categories", dto),
  update: (id: string, dto: CategoryUpsertDto) =>
    put<CategoryDto>(`/categories/${id}`, dto),
  remove: (id: string) => del(`/categories/${id}`),
};

export const supplierApi = {
  page: (q: PageQuery = {}) =>
    get<PagedResult<SupplierDto>>(`/suppliers${buildPageQuery(q)}`),
  get: (id: string) => get<SupplierDto>(`/suppliers/${id}`),
  create: (dto: SupplierUpsertDto) => post<SupplierDto>("/suppliers", dto),
  update: (id: string, dto: SupplierUpsertDto) =>
    put<SupplierDto>(`/suppliers/${id}`, dto),
  remove: (id: string) => del(`/suppliers/${id}`),
};

export interface ProductSearchQuery extends PageQuery {
  categoryId?: string;
  supplierId?: string;
  activeOnly?: boolean;
}

export const productApi = {
  search: (q: ProductSearchQuery = {}) => {
    const params = new URLSearchParams();
    if (q.page) params.set("page", String(q.page));
    if (q.pageSize) params.set("pageSize", String(q.pageSize));
    if (q.search) params.set("search", q.search);
    if (q.categoryId) params.set("categoryId", q.categoryId);
    if (q.supplierId) params.set("supplierId", q.supplierId);
    if (q.activeOnly !== undefined) params.set("activeOnly", String(q.activeOnly));
    const s = params.toString();
    return get<PagedResult<ProductDto>>(`/products${s ? `?${s}` : ""}`);
  },
  get: (id: string) => get<ProductDto>(`/products/${id}`),
  create: (dto: ProductUpsertDto) => post<ProductDto>("/products", dto),
  update: (id: string, dto: ProductUpsertDto) =>
    put<ProductDto>(`/products/${id}`, dto),
  remove: (id: string) => del(`/products/${id}`),
  adjustStock: (id: string, dto: ProductStockAdjustDto) =>
    post<ProductDto>(`/products/${id}/stock`, dto),
};
