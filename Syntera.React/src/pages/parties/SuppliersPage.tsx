import {
  AppGrid,
  Column,
  type ColumnProps,
} from "../../kalventis/ui";
import { Badge } from "../../kalventis/ui";

/**
 * SuppliersPage — migrated to kalventis-ui AppGrid + AppDynamicForm
 * pattern. A single ColumnProps[] schema drives BOTH grid display
 * AND form editing (AppDynamicForm auto-generates form fields from
 * the same Column definitions).
 *
 * What changed vs. the previous hand-rolled page:
 *   - Removed the 100-line SupplierFormModal component — the form
 *     is auto-rendered by AppDynamicForm inside the AppGrid Drawer.
 *   - Removed the manual `load` callback — AppGrid calls the
 *     /api/suppliers/grid endpoint directly via useDevExtremeData.
 *   - Removed the manual per-row Edit/Delete buttons — AppGrid
 *     auto-appends an "Aksi" column when enableCrud is on.
 *   - Gained: per-column filter row, Excel-style header filter
 *     dropdown, global search across multiple fields, column
 *     show/hide + pin chooser, state persistence, mobile card view.
 *   - Gained: focus-trapped Drawer form (Tab cycles only inside
 *     the form; focus restored to trigger button on close).
 *
 * The backend endpoint shape:
 *   - Data load: POST /api/suppliers/grid → { data, totalCount }
 *   - CRUD:      POST/PUT/DELETE /api/suppliers[/id] (envelope-wrapped)
 *     The /grid suffix is auto-stripped by AppGrid to derive the
 *     CRUD endpoint.
 */
const supplierColumns: ColumnProps[] = [
  {
    dataField: "name",
    caption: "Pemasok",
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
    visibleInForm: true,
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
    hideOnMobile: true,
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
    dataField: "licenseNumber",
    caption: "No. Lisensi BPOM",
    editorType: "text",
    visibleInForm: true,
    hideOnMobile: true,
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

export default function SuppliersPage() {
  return (
    <div className="flex flex-col gap-4">
      <header>
        <h2 className="text-2xl font-bold tracking-tight text-[var(--card-foreground)]">
          Pemasok
        </h2>
        <p className="text-sm text-[var(--muted-foreground)]">
          Distributor dan produsen obat.
        </p>
      </header>

      <AppGrid
        apiEndpoint="/api/suppliers/grid"
        enableCrud
        enableFiltering
        enableSorting
        enableColumnChooser
        enablePagination
        enableStatePersistence
        persistenceKey="suppliers-grid"
        globalFilterFields={["name", "contactPerson", "email", "phone", "city", "licenseNumber"]}
        title="Daftar Pemasok"
        pageSize={10}
      >
        {supplierColumns.map((col, idx) => (
          <Column key={col.dataField || `col-${idx}`} {...col} />
        ))}
      </AppGrid>
    </div>
  );
}
