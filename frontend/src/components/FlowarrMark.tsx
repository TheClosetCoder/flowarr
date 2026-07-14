export function FlowarrMark({ size = 20 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 1024 1024" role="img" aria-hidden="true">
      <g fill="none" stroke="currentColor" strokeWidth="44" strokeLinecap="round" strokeLinejoin="round">
        <path d="M282 245 V365 Q282 402 316 421 L445 493 Q490 518 512 558" />
        <path d="M512 245 V558" />
        <path d="M742 245 V365 Q742 402 708 421 L579 493 Q534 518 512 558" />
        <path d="M512 558 V705" />
        <path d="M450 649 L512 711 L574 649" />
        <path d="M315 705 V758 Q315 787 344 787 H680 Q709 787 709 758 V705" />
      </g>
      <g fill="currentColor">
        <circle cx="282" cy="224" r="37" />
        <circle cx="512" cy="224" r="37" />
        <circle cx="742" cy="224" r="37" />
      </g>
    </svg>
  );
}
