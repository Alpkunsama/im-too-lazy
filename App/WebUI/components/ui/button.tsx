import * as React from "react"
import { Slot } from "@radix-ui/react-slot"
import { cva, type VariantProps } from "class-variance-authority"

import { cn } from "@/lib/utils"
import { getGlassStyles, type GlassCustomization } from "@/lib/glass-utils"

const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md text-sm font-medium transition-all disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg:not([class*='size-'])]:size-4 shrink-0 [&_svg]:shrink-0 outline-none focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px] aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 aria-invalid:border-destructive active:scale-[0.98] active:transition-transform",
  {
    variants: {
      variant: {
        default:
          "bg-primary text-primary-foreground shadow-xs hover:bg-primary/90 active:shadow-[inset_0_2px_4px_rgba(0,0,0,0.2)]",
        glass:
          "glass-bg text-foreground hover:opacity-90 transition-all active:opacity-80 active:shadow-[inset_0_2px_4px_rgba(0,0,0,0.15),inset_0_1px_2px_rgba(0,0,0,0.1)] dark:active:shadow-[inset_0_2px_4px_rgba(0,0,0,0.3),inset_0_1px_2px_rgba(0,0,0,0.2)]",
        glassSubtle:
          "glass-bg text-foreground opacity-50 backdrop-blur-[var(--blur-sm)] hover:opacity-60 transition-all active:opacity-70 active:shadow-[inset_0_2px_4px_rgba(0,0,0,0.15)]",
        glassSolid:
          "bg-gradient-to-r from-purple-500/20 to-blue-500/20 backdrop-blur-[var(--blur)] border border-white/30 text-foreground shadow-[var(--glass-shadow)] hover:from-purple-500/30 hover:to-blue-500/30 transition-all active:from-purple-500/25 active:to-blue-500/25 active:shadow-[inset_0_2px_4px_rgba(0,0,0,0.2)]",
        frosted:
          "glass-frosted text-foreground hover:opacity-90 transition-all active:opacity-85 active:shadow-[inset_0_2px_4px_rgba(0,0,0,0.2),inset_0_1px_2px_rgba(0,0,0,0.15)]",
        fluted:
          "glass-fluted text-foreground hover:opacity-90 transition-all active:opacity-85 active:shadow-[inset_0_2px_4px_rgba(0,0,0,0.2)]",
        crystal:
          "glass-crystal text-foreground transition-all active:opacity-90 active:shadow-[inset_0_2px_4px_rgba(0,0,0,0.25),inset_0_1px_2px_rgba(0,0,0,0.2)]",
        destructive:
          "bg-destructive text-white shadow-xs hover:bg-destructive/90 focus-visible:ring-destructive/20 dark:focus-visible:ring-destructive/40 dark:bg-destructive/60 active:shadow-[inset_0_2px_4px_rgba(0,0,0,0.3)]",
        outline:
          "border-2 border-foreground/20 bg-transparent text-foreground shadow-xs hover:bg-foreground/10 hover:border-foreground/40 hover:text-foreground dark:border-white/40 dark:hover:bg-white/5 dark:hover:border-white/60 dark:text-white active:bg-foreground/15 active:border-foreground/50 dark:active:bg-white/10 active:shadow-[inset_0_2px_4px_rgba(0,0,0,0.1)]",
        secondary:
          "bg-secondary text-secondary-foreground shadow-xs hover:bg-secondary/80 active:shadow-[inset_0_2px_4px_rgba(0,0,0,0.15)]",
        ghost:
          "hover:bg-accent hover:text-accent-foreground dark:hover:bg-accent/50 active:bg-accent/80 dark:active:bg-accent/60 active:shadow-[inset_0_1px_2px_rgba(0,0,0,0.1)]",
        link: "text-primary underline-offset-4 hover:underline active:opacity-80",
      },
      size: {
        default: "h-9 px-4 py-2 has-[>svg]:px-3",
        sm: "h-8 rounded-md gap-1.5 px-3 has-[>svg]:px-2.5",
        lg: "h-10 rounded-md px-6 has-[>svg]:px-4",
        icon: "size-9",
      },
    },
    defaultVariants: {
      variant: "glass",
      size: "default",
    },
  }
)

function Button({
  className,
  variant,
  size,
  asChild = false,
  glass,
  style,
  ...props
}: React.ComponentProps<"button"> &
  VariantProps<typeof buttonVariants> & {
    asChild?: boolean
    glass?: GlassCustomization
  }) {
  const Comp = asChild ? Slot : "button"
  
  // Apply glass styles for glass variants when custom glass props are provided
  const hasCustomGlass = glass !== undefined
  const isGlassVariant = variant === "glass" || variant === "glassSubtle" || variant === "glassSolid" || 
                         variant === "frosted" || variant === "fluted" || variant === "crystal"
  
  const glassStyles = isGlassVariant && hasCustomGlass
    ? getGlassStyles(glass)
    : {}

  return (
    <Comp
      data-slot="button"
      className={cn(buttonVariants({ variant, size, className }))}
      style={{
        ...glassStyles,
        ...style,
      }}
      {...props}
    />
  )
}

export { Button, buttonVariants }
