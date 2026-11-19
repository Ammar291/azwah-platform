import { cn } from "cn"

// `bg-muted` is almost the page background (#f5f3ec on #faf8f3), so a loading block could not be seen; the border
// colour shows on both the page and a white card.
function Skeleton({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="skeleton"
      className={cn("animate-pulse rounded-md bg-border", className)}
      {...props}
    />
  )
}

export { Skeleton }
