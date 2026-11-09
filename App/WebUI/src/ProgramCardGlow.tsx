import React, {useLayoutEffect, useRef, useState} from 'react';

export default function ProgramCardGlow({animate,onComplete}:{animate:boolean,onComplete:()=>void}) {
  const svg = useRef<SVGSVGElement>(null);
  const [outline, setOutline] = useState('');

  useLayoutEffect(() => {
    const card = svg.current?.parentElement;
    if (!card) return;
    const measure = () => {
      const width = card.clientWidth;
      const height = card.clientHeight;
      if (width < 10 || height < 10) return;
      const inset = 1.5;
      const right = width - inset;
      const bottom = height - inset;
      const radius = Math.min(
        Math.max(0, (parseFloat(getComputedStyle(card).borderTopLeftRadius) || 0) - inset),
        right / 2, bottom / 2
      );
      setOutline([
        `M ${width / 2} ${inset} H ${right - radius}`,
        `Q ${right} ${inset} ${right} ${inset + radius} V ${bottom - radius}`,
        `Q ${right} ${bottom} ${right - radius} ${bottom} H ${inset + radius}`,
        `Q ${inset} ${bottom} ${inset} ${bottom - radius} V ${inset + radius}`,
        `Q ${inset} ${inset} ${inset + radius} ${inset} Z`
      ].join(' '));
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(card);
    return () => observer.disconnect();
  }, []);

  return <svg ref={svg} className="program-card-glow" aria-hidden="true" focusable="false">
    {outline && <g className={animate ? 'program-card-glow-animated' : undefined} onAnimationEnd={onComplete}>
      <path className="program-card-glow-halo" d={outline} pathLength={100}/>
      <path d={outline} pathLength={100}/>
    </g>}
  </svg>;
}
