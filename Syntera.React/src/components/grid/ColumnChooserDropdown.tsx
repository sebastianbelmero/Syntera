import React from "react";
import { Columns3, Pin, PinOff } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  Checkbox,
  Button,
  Tooltip,
  TooltipTrigger,
  TooltipContent,
} from "../ui";

export interface ColumnChooserDropdownProps {
  table: any;
}

/**
 * Column chooser — show/hide and pin columns.
 * v2.2.1: Refactored to use Radix DropdownMenu + Checkbox primitives.
 *
 * Improvements:
 * - Proper focus trap + keyboard navigation (Arrow keys, Enter, Escape)
 * - Click-outside detection via Radix (no more onBlur hack)
 * - Smooth open/close animation
 * - Checkbox primitive (consistent with rest of UI)
 * - Tooltip on pin button
 * - Z-index managed by Radix Portal (no conflict with Modal)
 */
export const ColumnChooserDropdown: React.FC<ColumnChooserDropdownProps> = ({ table }) => {
  const allColumns = table.getAllLeafColumns().filter((c: any) => c.id !== "__actions" && c.id !== "__expand");

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" className="h-7 gap-1.5 px-2 text-xs">
          <Columns3 className="size-3.5" />
          Kolom
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuLabel className="text-xs text-muted-foreground">
          Pilih Kolom
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <div className="max-h-[300px] overflow-y-auto py-1">
          {allColumns.map((column: any) => {
            const isPinned = column.getIsPinned() === "left";
            const headerLabel =
              typeof column.columnDef.header === "function"
                ? column.columnDef.header()
                : column.id;

            return (
              <div
                key={column.id}
                className="flex items-center gap-2 px-2 py-1 hover:bg-muted"
              >
                <Checkbox
                  checked={column.getIsVisible()}
                  onCheckedChange={(value) => column.toggleVisibility(!!value)}
                  id={`col-${column.id}`}
                />
                <label
                  htmlFor={`col-${column.id}`}
                  className="flex-1 cursor-pointer truncate text-xs"
                  title={String(headerLabel)}
                >
                  {String(headerLabel)}
                </label>
                <Tooltip>
                  <TooltipTrigger asChild>
                    <button
                      type="button"
                      className={`rounded p-1 transition-colors ${
                        isPinned
                          ? "text-primary"
                          : "text-muted-foreground/40 hover:text-muted-foreground"
                      }`}
                      onClick={(e) => {
                        e.preventDefault();
                        column.pin(isPinned ? false : "left");
                      }}
                    >
                      {isPinned ? (
                        <PinOff className="size-3" />
                      ) : (
                        <Pin className="size-3" />
                      )}
                    </button>
                  </TooltipTrigger>
                  <TooltipContent>
                    {isPinned ? "Lepas pin" : "Pin kolom ke kiri"}
                  </TooltipContent>
                </Tooltip>
              </div>
            );
          })}
        </div>
      </DropdownMenuContent>
    </DropdownMenu>
  );
};
