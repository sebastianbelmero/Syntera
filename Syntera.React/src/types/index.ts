/**
 * Shared domain types — mirror of the backend DTOs. Kept in a single
 * file per aggregate to make the contract easy to scan and update when
 * the backend evolves. Currency is always `number` (Rupiah scale 18,2
 * is small enough to fit safely in JS numbers for daily pharmacy flows).
 */

export interface PageQuery {
  page?: number;
  pageSize?: number;
  search?: string;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface ApiResponse<T> {
  success: boolean;
  data?: T;
  message?: string;
  errorCode?: string;
  fieldErrors?: FieldError[];
}

export interface FieldError {
  field: string;
  message: string;
}

// ── Auth ────────────────────────────────────────────────
export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  tokenType: string;
  expiresAt: string;
  refreshToken: string;
  profile: UserProfile;
}

export interface UserProfile {
  id: string;
  email: string;
  fullName?: string | null;
  roles: string[];
}

// ── Catalog ─────────────────────────────────────────────
export type DrugClass =
  | "OverTheCounter"
  | "RestrictedOTC"
  | "PrescriptionOnly"
  | "PharmacyOnly"
  | "Narcotic";

export interface CategoryDto {
  id: string;
  name: string;
  slug: string;
  description?: string | null;
  parentId?: string | null;
  parentName?: string | null;
  productCount: number;
  createdAt: string;
}

export interface CategoryTreeNodeDto {
  id: string;
  name: string;
  slug: string;
  children: CategoryTreeNodeDto[];
}

export interface CategoryUpsertDto {
  name: string;
  description?: string | null;
  parentId?: string | null;
}

export interface SupplierDto {
  id: string;
  name: string;
  contactPerson?: string | null;
  email?: string | null;
  phone?: string | null;
  address?: string | null;
  city?: string | null;
  postalCode?: string | null;
  licenseNumber?: string | null;
  isActive: boolean;
  productCount: number;
  createdAt: string;
}

export interface SupplierUpsertDto {
  name: string;
  contactPerson?: string | null;
  email?: string | null;
  phone?: string | null;
  address?: string | null;
  city?: string | null;
  postalCode?: string | null;
  licenseNumber?: string | null;
  isActive?: boolean;
}

export interface ProductDto {
  id: string;
  name: string;
  sku: string;
  barcode?: string | null;
  registrationNumber?: string | null;
  genericName?: string | null;
  brandName?: string | null;
  manufacturer?: string | null;
  drugClass: DrugClass;
  potency?: string | null;
  packSize?: string | null;
  costPrice: number;
  sellingPrice: number;
  discountPrice?: number | null;
  reorderLevel: number;
  expiryDate?: string | null;
  batchNumber?: string | null;
  isActive: boolean;
  stock: number;
  isExpired: boolean;
  isLowStock: boolean;
  categoryId: string;
  categoryName: string;
  supplierId: string;
  supplierName: string;
  createdAt: string;
}

export interface ProductUpsertDto {
  name: string;
  sku: string;
  barcode?: string | null;
  registrationNumber?: string | null;
  genericName?: string | null;
  brandName?: string | null;
  manufacturer?: string | null;
  drugClass: DrugClass;
  potency?: string | null;
  packSize?: string | null;
  costPrice: number;
  sellingPrice: number;
  discountPrice?: number | null;
  reorderLevel: number;
  expiryDate?: string | null;
  batchNumber?: string | null;
  isActive: boolean;
  categoryId: string;
  supplierId: string;
}

export interface ProductStockAdjustDto {
  quantity: number;
  note?: string | null;
}

// ── Inventory ──────────────────────────────────────────
export type InventoryMovementType =
  | "Inbound"
  | "Outbound"
  | "Adjustment"
  | "Return"
  | "Damage";

export interface InventoryMovementDto {
  id: string;
  productId: string;
  productName: string;
  productSku: string;
  type: InventoryMovementType;
  quantity: number;
  balanceAfter: number;
  reference?: string | null;
  note?: string | null;
  performedByUserId?: string | null;
  createdAt: string;
}

export interface InventoryAdjustmentRequest {
  productId: string;
  type: InventoryMovementType;
  quantity: number;
  reference?: string | null;
  note?: string | null;
}

// ── Customers ──────────────────────────────────────────
export interface CustomerDto {
  id: string;
  name: string;
  contactPerson?: string | null;
  email?: string | null;
  phone?: string | null;
  address?: string | null;
  city?: string | null;
  postalCode?: string | null;
  taxId?: string | null;
  isActive: boolean;
  totalOrders: number;
  createdAt: string;
}

export interface CustomerUpsertDto {
  name: string;
  contactPerson?: string | null;
  email?: string | null;
  phone?: string | null;
  address?: string | null;
  city?: string | null;
  postalCode?: string | null;
  taxId?: string | null;
  isActive?: boolean;
}

// ── Sales ───────────────────────────────────────────────
export type SaleStatus =
  | "Draft"
  | "Pending"
  | "Paid"
  | "Shipped"
  | "Completed"
  | "Cancelled";

export interface SaleDto {
  id: string;
  invoiceNumber: string;
  status: SaleStatus;
  saleDate?: string | null;
  customerId: string;
  customerName: string;
  cashierUserId?: string | null;
  cashierName?: string | null;
  subTotal: number;
  taxRate: number;
  taxAmount: number;
  discountAmount: number;
  grandTotal: number;
  note?: string | null;
  items: SaleItemDto[];
  createdAt: string;
}

export interface SaleItemDto {
  id: string;
  productId: string;
  productName: string;
  productSku: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  lineTotal: number;
}

export interface SaleItemInput {
  productId: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
}

export interface SaleCreateDto {
  customerId: string;
  saleDate?: string | null;
  taxRate: number;
  discountAmount: number;
  note?: string | null;
  items: SaleItemInput[];
}

export interface SaleStatusUpdateDto {
  status: SaleStatus;
}

// ── Dashboard ──────────────────────────────────────────
export interface DashboardSummaryDto {
  totalProducts: number;
  lowStockProducts: number;
  nearExpiryProducts: number;
  totalCustomers: number;
  totalSuppliers: number;
  todaySalesAmount: number;
  todaySalesCount: number;
  monthSalesAmount: number;
  monthSalesCount: number;
  yearSalesAmount: number;
}

export interface SalesTrendPoint {
  date: string;
  amount: number;
  count: number;
}

export interface TopProductDto {
  productId: string;
  productName: string;
  productSku: string;
  quantitySold: number;
  revenue: number;
}

export interface DashboardTrendDto {
  last14Days: SalesTrendPoint[];
  top5ProductsThisMonth: TopProductDto[];
}
