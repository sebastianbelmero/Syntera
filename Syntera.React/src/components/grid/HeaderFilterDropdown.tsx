import React, { useState, useMemo } from "react";
import { Filter, Loader2, X, Search } from "lucide-react";
import type { AxiosInstance } from "axios";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
  Checkbox,
  Button,
} from "../ui";

export interface HeaderFilterDropdownProps {
  column: any;
  apiEndpoint?: string;
  apiClient?: AxiosInstance;
  isPinned?: any;
}

/**
 * Excel-style checkbox header filter.
 * v2.2.1: Refactored to use Radix Popover + Checkbox primitives.
 *
 * Improvements:
 * - Popover with collision detection (auto-flip if near viewport edge)
 * - Proper focus trap + keyboard nav
 * - Checkbox primitive (consistent styling)
 * - Search input with icon
 * - Clear button to reset filter
 * - Loading spinner via Loader2 (animate-spin)
 * - Z-index managed by Radix Portal
 * - Smooth open/close animation
 */
export const HeaderFilterDropdown: React.FC<HeaderFilterDropdownProps> = ({
  column,
  apiEndpoint,
  apiClient,
}) => {
  const [loading, setLoading] = useState(false);
  const [items, setItems] = useState<any[]>([]);
  const [search, setSearch] = useState("");
  const [hasLoaded, setHasLoaded] = useState(false);

  const filterValue = (column.getFilterValue() as string[]) || [];

  const loadItems = React.useCallback(async () => {
    if (hasLoaded) return;
    setHasLoaded(true);
    setLoading(true);

    try {
      if (apiEndpoint && apiClient) {
        const res = await apiClient.get(apiEndpoint, {
          params: {
            take: 200,
            group: JSON.stringify([{ selector: column.id, isExpanded: false }]),
          },
        });
        if (res.data && Array.isArray(res.data.data)) {
          setItems(
            res.data.data
              .map((d: any) => d.key)
              .filter((k: any) => k !== null && k !== undefined)
          );
        } else if (Array.isArray(res.data)) {
          setItems(
            Array.from(new Set(res.data.map((d: any) => d[column.id]))).filter(
              (k) => k !== null && k !== undefined
            )
          );
        }
      } else {
        const allVals = column
          .getFacetedRowModel()
          .flatRows.map((r: any) => r.getValue(column.id));
        setItems(Array.from(new Set(allVals)).filter((k) => k !== null && k !== undefined));
      }
    } catch (err) {
      console.error("Gagal narik distinct filter", err);
    } finally {
      setLoading(false);
    }
  }, [apiEndpoint, apiClient, column, hasLoaded]);

  const toggleItem = (val: any) => {
    const newArr = filterValue.includes(val)
      ? filterValue.filter((v) => v !== val)
      : [...filterValue, val];
    column.setFilterValue(newArr.length > 0 ? newArr : undefined);
  };

  const clearFilter = () => {
    column.setFilterValue(undefined);
  };

  const filteredItems = useMemo(
    () => items.filter((i) => String(i).toLowerCase().includes(search.toLowerCase())),
    [items, search]
  );

  const isActive = filterValue.length > 0;

  return (
    <Popover>
      <PopoverTrigger asChild>
        <button
          type="button"
          className={`rounded p-0.5 transition-colors ${
            isActive
              ? "text-primary"
              : "text-muted-foreground/40 hover:text-muted-foreground"
          }`}
          title="Filter kolom"
        >
          <Filter className="size-3" fill={isActive ? "currentColor" : "none"} />
        </button>
      </PopoverTrigger>
      <PopoverContent
        align="start"
        className="w-56 p-0"
        onOpenAutoFocus={loadItems}
      >
        {/* Search bar */}
        <div className="border-b border-border p-2">
          <div className="flex items-center gap-1.5 rounded-md border border-input bg-card px-2 py-1">
            <Search className="size-3 text-muted-foreground" />
            <input
              type="text"
              placeholder="Cari..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="w-full bg-transparent text-xs outline-none"
            />
          </div>
        </div>

        {/* Items list */}
        <div className="max-h-[200px] overflow-y-auto py-1">
          {loading && (
            <div className="py-4 text-center">
              <Loader2 className="mx-auto size-4 animate-spin text-muted-foreground" />
            </div>
          )}

          {!loading && (
            <label className="flex cursor-pointer items-center gap-2 px-3 py-1.5 font-semibold hover:bg-muted">
              <Checkbox
                checked={filterValue.length === 0}
                onCheckedChange={() => clearFilter()}
              />
              <span className="text-xs">Semua</span>
            </label>
          )}

          {!loading &&
            filteredItems.map((item, idx) => (
              <label
                key={idx}
                className="flex cursor-pointer items-center gap-2 px-3 py-1.5 hover:bg-muted"
              >
                <Checkbox
                  checked={filterValue.includes(item)}
                  onCheckedChange={() => toggleItem(item)}
                />
                <span className="truncate text-xs">{String(item)}</span>
              </label>
            ))}

          {!loading && filteredItems.length === 0 && items.length > 0 && (
            <div className="py-3 text-center text-xs text-muted-foreground">
              Tidak ditemukan
            </div>
          )}
        </div>

        {/* Footer */}
        {isActive && (
          <div className="border-t border-border p-2">
            <Button
              variant="ghost"
              size="sm"
              className="h-7 w-full text-xs text-danger hover:bg-danger/10"
              onClick={clearFilter}
            >
              <X className="size-3" />
              Hapus Filter
            </Button>
          </div>
        )}
      </PopoverContent>
    </Popover>
  );
};
