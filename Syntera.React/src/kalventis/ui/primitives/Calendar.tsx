import * as React from "react";
import { DayPicker } from "react-day-picker";
import { ChevronLeftIcon, ChevronRightIcon } from "lucide-react";
import { cn } from "../../lib/utils";

export type DatePickerProps = React.ComponentProps<typeof DayPicker>;

/**
 * Calendar / Date Picker component built on top of react-day-picker v10.
 *
 * Wrap with a Popover (Radix) for a full date-picker dropdown experience:
 *
 *   <Popover>
 *     <PopoverTrigger asChild>
 *       <Button variant="outline">{date ? format(date, "PPP") : "Pick a date"}</Button>
 *     </PopoverTrigger>
 *     <PopoverContent>
 *       <Calendar mode="single" selected={date} onSelect={setDate} />
 *     </PopoverContent>
 *   </Popover>
 */
export function Calendar({
  className,
  classNames,
  showOutsideDays = true,
  ...props
}: DatePickerProps) {
  return (
    <DayPicker
      showOutsideDays={showOutsideDays}
      className={cn("p-3", className)}
      classNames={{
        months: "flex flex-col sm:flex-row gap-4",
        month: "flex flex-col gap-4",
        month_caption: "flex justify-center pt-1 relative items-center",
        caption_label: "text-sm font-medium",
        nav: "flex items-center gap-1",
        button_previous: cn(
          "absolute left-1 top-0 inline-flex size-7 items-center justify-center rounded-md border border-border bg-transparent p-0 opacity-70 hover:opacity-100",
          "focus-visible:outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
        ),
        button_next: cn(
          "absolute right-1 top-0 inline-flex size-7 items-center justify-center rounded-md border border-border bg-transparent p-0 opacity-70 hover:opacity-100",
          "focus-visible:outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
        ),
        month_grid: "w-full border-collapse space-y-1",
        weekdays: "flex",
        weekday:
          "text-muted-foreground rounded-md w-8 font-normal text-[0.8rem]",
        week: "flex w-full mt-2",
        day: cn(
          "relative p-0 text-center text-sm focus-within:relative focus-within:z-20",
          "[&>button]:size-8 [&>button]:rounded-md [&>button]:p-0"
        ),
        day_button: cn(
          "inline-flex items-center justify-center gap-2 rounded-md text-sm font-normal whitespace-nowrap transition-colors",
          "hover:bg-muted hover:text-foreground",
          "focus-visible:outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50",
          "disabled:pointer-events-none disabled:opacity-50"
        ),
        range_start: "bg-primary text-primary-foreground rounded-l-md",
        range_end: "bg-primary text-primary-foreground rounded-r-md",
        selected:
          "bg-primary text-primary-foreground hover:bg-primary hover:text-primary-foreground focus:bg-primary focus:text-primary-foreground",
        today: "bg-muted text-foreground",
        outside: "day-outside text-muted-foreground opacity-50",
        disabled: "text-muted-foreground opacity-50",
        range_middle:
          "aria-selected:bg-muted aria-selected:text-muted-foreground rounded-none",
        hidden: "invisible",
        ...classNames,
      }}
      components={{
        Chevron: ({ orientation }) =>
          orientation === "left" ? (
            <ChevronLeftIcon className="size-4" />
          ) : (
            <ChevronRightIcon className="size-4" />
          ),
      }}
      {...props}
    />
  );
}
Calendar.displayName = "Calendar";
