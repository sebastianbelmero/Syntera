import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { Layers } from "lucide-react";
import { categoryApi } from "../../api/catalog";
import type { CategoryDto } from "../../types";
import { formatDate } from "../../lib/format";
import {
  AppGrid,
  Badge,
  type ColumnProps,
} from "../../components";

/**
 * CategoriesPage — migrated to the in-house AppGrid + AppDynamicForm pattern.
 *
 * Master-detail pattern: each Category row expands to reveal an inner
 * AppGrid showing child categories (filtered client-side by parentId).
 * The inner AppGrid can itself expand if a child has grandchildren —
 * true recursive AppGrid-in-AppGrid nesting.
 *
 * The parent <select> in the form uses an inline lookup array (fetched
 * by react-query) so the dropdown knows which categories are eligible
 * parents. The lookup auto-refreshes when the categories-list query
 * is invalidated after a save.
 */
export default function CategoriesPage() {
  // Fetch full category list once for the parent-lookup dropdown AND
  // for rendering child grids inside the master-detail expansion.
  const parentsQuery = useQuery({
    queryKey: ["categories-list"],
    queryFn: () => categoryApi.page({ pageSize: 200 }),
  });

  const allCategories = useMemo(
    () => parentsQuery.data?.items ?? [],
    [parentsQuery.data]
  );

  // Schema drives both grid display AND AppDynamicForm generation.
  // parentId uses an inline lookup array (filtered to exclude self
  // via formVisible callback isn't possible at schema time, but
  // the API enforces uniqueness server-side anyway).
  const columns: ColumnProps[] = useMemo(() => [
    {
      dataField: "name",
      caption: "Nama",
      editorType: "text",
      validationRules: [{ type: "required", message: "Nama wajib diisi." }],
      cellRender: ({ row }: any) => (
        <div>
          <p className="font-medium text-[var(--card-foreground)]">{row.name}</p>
          <p className="text-xs text-[var(--muted-foreground)]">/{row.slug}</p>
        </div>
      ),
    },
    {
      dataField: "slug",
      caption: "Slug",
      editorType: "text",
      hint: "URL-friendly identifier. Biarkan kosong untuk auto-generate dari nama.",
      visibleInGrid: false,
    },
    {
      dataField: "description",
      caption: "Deskripsi",
      editorType: "textarea",
      hideOnMobile: true,
      cellRender: ({ row }: any) => (
        <span className="text-xs text-[var(--muted-foreground)]">
          {row.description ?? "—"}
        </span>
      ),
    },
    {
      dataField: "parentId",
      caption: "Kategori Induk",
      editorType: "combobox",
      lookup: {
        // Inline array — when the form opens, the dropdown options are
        // already populated from the query cache. LookupSelect's
        // useEffect detects Array.isArray and uses it directly.
        dataSource: allCategories,
        valueExpr: "id",
        displayExpr: "name",
      },
      hideOnMobile: true,
      cellRender: ({ row }: any) => (
        <span className="text-xs">{row.parentName ?? "—"}</span>
      ),
    },
    {
      dataField: "productCount",
      caption: "Produk",
      dataType: "number",
      alignment: "right",
      allowEditing: false,
      visibleInForm: false,
      cellRender: ({ row }: any) => (
        <Badge variant={row.productCount > 0 ? "info" : "neutral"}>
          {row.productCount}
        </Badge>
      ),
    },
    {
      dataField: "createdAt",
      caption: "Dibuat",
      dataType: "date",
      hideOnMobile: true,
      visibleInForm: false,
      allowFiltering: false,
      allowEditing: false,
      cellRender: ({ row }: any) => (
        <span className="text-xs">{formatDate(row.createdAt)}</span>
      ),
    },
  ], [allCategories]);

  // Master-detail: render a simple list of child categories inside the
  // expanded row. Children are filtered client-side by parentId === row.id.
  // Using a compact inline list rather than a nested AppGrid because
  // the typical category tree is small (3-10 children max) and the
  // chrome of a full grid (toolbar/pager/filter) would be visual noise.
  const renderChildGrid = (parent: CategoryDto) => {
    const children = allCategories.filter((c) => c.parentId === parent.id);
    return (
      <div className="bg-[var(--surface)] p-2">
        <p className="mb-2 text-xs font-semibold text-[var(--muted-foreground)]">
          Sub-kategori dari <span className="text-[var(--card-foreground)]">{parent.name}</span> ({children.length})
        </p>
        {children.length === 0 ? (
          <p className="text-xs italic text-[var(--muted-foreground)]">
            Tidak ada sub-kategori.
          </p>
        ) : (
          <ul className="space-y-1">
            {children.map((c) => (
              <li key={c.id} className="flex items-center gap-2 text-xs">
                <Layers size={12} className="text-[var(--primary)]" />
                <span className="font-medium text-[var(--card-foreground)]">{c.name}</span>
                <span className="text-[var(--muted-foreground)]">/{c.slug}</span>
                <Badge variant="info">{c.productCount} produk</Badge>
              </li>
            ))}
          </ul>
        )}
      </div>
    );
  };

  return (
    <div className="flex flex-col gap-4">
      <header>
        <h2 className="text-2xl font-bold tracking-tight text-[var(--card-foreground)]">
          Kategori
        </h2>
        <p className="text-sm text-[var(--muted-foreground)]">
          Klasifikasi produk obat dan kesehatan.
        </p>
      </header>

      <AppGrid
        apiEndpoint="/api/categories/grid"
        enableCrud
        enableFiltering
        enableSorting
        enableColumnChooser
        enablePagination
        enableStatePersistence
        persistenceKey="categories-grid"
        globalFilterFields={["name", "slug", "description", "parentName"]}
        title="Daftar Kategori"
        pageSize={10}
        enableMasterDetail
        renderMasterDetail={renderChildGrid}
        dynamicColumns={columns}
      />
    </div>
  );
}
