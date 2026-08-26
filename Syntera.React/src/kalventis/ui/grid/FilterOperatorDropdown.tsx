import React from "react";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "../primitives";

export const FILTER_OPERATORS = [
  { value: "contains", label: "Contains", hint: "Aa" },
  { value: "equals", label: "Equals", hint: "=" },
  { value: "startsWith", label: "Starts with", hint: "A.." },
  { value: "endsWith", label: "Ends with", hint: "..A" },
];

export interface FilterOperatorDropdownProps {
  mode: string;
  onChange: (mode: string) => void;
}

/**
 * Filter operator selector — choose match mode (contains/equals/startsWith/endsWith).
 * v2.2.1: Refactored to use Radix Select primitive.
 *
 * Improvements:
 * - Shows active operator in trigger (user can see current mode without opening)
 * - Proper keyboard navigation (Arrow keys, Enter, Escape)
 * - Click-outside via Radix (no onBlur hack)
 * - Smooth animation
 * - Compact size matching AppGrid density
 */
export const FilterOperatorDropdown: React.FC<FilterOperatorDropdownProps> = ({
  mode,
  onChange,
}) => {
  const activeOperator = FILTER_OPERATORS.find((op) => op.value === mode);

  return (
    <Select value={mode} onValueChange={onChange}>
      <SelectTrigger size="sm" className="h-6 w-[110px] border-none bg-transparent px-1 text-[11px] shadow-none">
        <SelectValue>
          <span className="flex items-center gap-1">
            <span className="font-mono text-muted-foreground">{activeOperator?.hint}</span>
            <span>{activeOperator?.label}</span>
          </span>
        </SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectGroup>
          <SelectLabel className="text-[10px]">Match Mode</SelectLabel>
          {FILTER_OPERATORS.map((op) => (
            <SelectItem key={op.value} value={op.value} className="text-xs">
              <span className="mr-1.5 font-mono text-muted-foreground">{op.hint}</span>
              {op.label}
            </SelectItem>
          ))}
        </SelectGroup>
      </SelectContent>
    </Select>
  );
};
