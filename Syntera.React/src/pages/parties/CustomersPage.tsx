import {
  AppGrid,
  Column,
  type ColumnProps,
} from "../../kalventis/ui";
import { Badge } from "../../kalventis/ui";

/**
 * CustomersPage — migrated to kalventis-ui AppGrid + AppDynamicForm.
 * Same pattern as SuppliersPage — single ColumnProps[] schema drives
 * grid display + form editing.
 *
 * Backend endpoint: /api/customers/grid (data) → /api/customers (CRUD).
 */
const customerColumns: ColumnProps[] = [
  {
    dataField: "name",
    caption: "Pelanggan",
    editorType: "text",
    validationRules: [{ type: "required", message: "Nama wajib diisi." }],
    cellRender: ({ row }: any) => (
      <div>
        <p className="font-medium text-[var(--card-foreground)]">{row.name}</p>
        {row.contactPerson && (
          <p className="text-xs text-[var(--muted-foreground)]">{row.contactPerson}</p>
        )}
      </div>
    ),
  },
  {
    dataField: "contactPerson",
    caption: "Narahubung",
    editorType: "text",
    hideOnMobile: true,
  },
  {
    dataField: "email",
    caption: "Email",
    editorType: "email",
    hideOnMobile: true,
  },
  {
    dataField: "phone",
    caption: "Telepon",
    editorType: "tel",
    hideOnMobile: true,
  },
  {
    dataField: "address",
    caption: "Alamat",
    editorType: "text",
    visibleInForm: true,
    visibleInGrid: false,
    hideOnMobile: true,
  },
  {
    dataField: "city",
    caption: "Kota",
    editorType: "text",
  },
  {
    dataField: "postalCode",
    caption: "Kode Pos",
    editorType: "text",
    visibleInForm: true,
    visibleInGrid: false,
    hideOnMobile: true,
  },
  {
    dataField: "taxId",
    caption: "NPWP",
    editorType: "text",
    hideOnMobile: true,
  },
  {
    dataField: "totalOrders",
    caption: "Total Order",
    dataType: "number",
    alignment: "right",
    allowEditing: false,
    visibleInForm: false,
  },
  {
    dataField: "isActive",
    caption: "Status",
    dataType: "boolean",
    editorType: "switch",
    cellRender: ({ row }: any) => (
      <Badge variant={row.isActive ? "success" : "neutral"}>
        {row.isActive ? "Aktif" : "Nonaktif"}
      </Badge>
    ),
  },
  {
    dataField: "createdAt",
    caption: "Bergabung",
    dataType: "date",
    hideOnMobile: true,
    visibleInForm: false,
    allowFiltering: false,
    allowEditing: false,
  },
];

export default function CustomersPage() {
  return (
    <div className="flex flex-col gap-4">
      <header>
        <h2 className="text-2xl font-bold tracking-tight text-[var(--card-foreground)]">
          Pelanggan
        </h2>
        <p className="text-sm text-[var(--muted-foreground)]">
          Apotek, klinik, dan distributor pembeli.
        </p>
      </header>

      <AppGrid
        apiEndpoint="/api/customers/grid"
        enableCrud
        enableFiltering
        enableSorting
        enableColumnChooser
        enablePagination
        enableStatePersistence
        persistenceKey="customers-grid"
        globalFilterFields={["name", "contactPerson", "email", "phone", "city", "taxId"]}
        title="Daftar Pelanggan"
        pageSize={10}
      >
        {customerColumns.map((col, idx) => (
          <Column key={col.dataField || `col-${idx}`} {...col} />
        ))}
      </AppGrid>
    </div>
  );
}
