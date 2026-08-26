/**
 * UI primitives barrel — Radix-based components owned in-house by
 * Syntera.React. Each component lives in its own file and is exported
 * here so consumers can `import { Button } from "../../components/ui"`
 * (or via the top-level `../../components` barrel).
 *
 * Only primitives that are actually consumed live here. If you need a
 * shadcn-style primitive that is missing (Dialog, Sheet, Tabs, ...),
 * re-add it in its own file and export it below.
 */
export { Avatar, AvatarImage, AvatarFallback } from "./Avatar";
export {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuCheckboxItem,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuShortcut,
  DropdownMenuGroup,
  DropdownMenuPortal,
  DropdownMenuSub,
  DropdownMenuSubTrigger,
  DropdownMenuSubContent,
} from "./DropdownMenu";
export { Badge, type BadgeProps, type BadgeVariant } from "./Badge";
export { Button, type ButtonProps } from "./Button";
export { Checkbox } from "./Checkbox";
export {
  Popover,
  PopoverTrigger,
  PopoverContent,
  PopoverAnchor,
} from "./Popover";
export {
  Select,
  SelectGroup,
  SelectValue,
  SelectTrigger,
  SelectContent,
  SelectLabel,
  SelectItem,
  SelectSeparator,
} from "./Select";
export {
  Tooltip,
  TooltipTrigger,
  TooltipContent,
  TooltipProvider,
} from "./Tooltip";
export { Skeleton } from "./Skeleton";
