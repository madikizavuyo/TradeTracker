interface ScoreGaugeProps {
  score: number;
  size?: number;
  label?: string;
}

export function ScoreGauge({ score, size = 120, label }: ScoreGaugeProps) {
  const clampedScore = Math.max(1, Math.min(10, score));
  const percentage = ((clampedScore - 1) / 9) * 100;
  const angle = (percentage / 100) * 180;

  const getColor = (pct: number) => {
    if (pct <= 35) return '#ef4444';
    if (pct <= 50) return '#f59e0b';
    if (pct <= 65) return '#eab308';
    return '#22c55e';
  };

  const color = getColor(percentage);
  const cx = size / 2;
  const cy = size / 2 + 10;
  const radius = size / 2 - 15;
  const strokeWidth = 12;

  const describeArc = (startAngle: number, endAngle: number) => {
    const startRad = ((180 + startAngle) * Math.PI) / 180;
    const endRad = ((180 + endAngle) * Math.PI) / 180;
    const x1 = cx + radius * Math.cos(startRad);
    const y1 = cy + radius * Math.sin(startRad);
    const x2 = cx + radius * Math.cos(endRad);
    const y2 = cy + radius * Math.sin(endRad);
    const largeArc = endAngle - startAngle > 180 ? 1 : 0;
    return `M ${x1} ${y1} A ${radius} ${radius} 0 ${largeArc} 1 ${x2} ${y2}`;
  };

  return (
    <div className="flex flex-col items-center">
      <svg width={size} height={size / 2 + 30} viewBox={`0 0 ${size} ${size / 2 + 30}`}>
        {/* Background arc */}
        <path
          d={describeArc(0, 180)}
          fill="none"
          stroke="hsl(var(--muted))"
          strokeWidth={strokeWidth}
          strokeLinecap="round"
        />
        {/* Value arc */}
        {angle > 0 && (
          <path
            d={describeArc(0, Math.min(angle, 179.9))}
            fill="none"
            stroke={color}
            strokeWidth={strokeWidth}
            strokeLinecap="round"
          />
        )}
        {/* Score text */}
        <text
          x={cx}
          y={cy - 5}
          textAnchor="middle"
          className="text-2xl font-bold fill-foreground"
          fontSize={size / 4}
        >
          {clampedScore.toFixed(1)}
        </text>
        {/* Scale markers */}
        <text x={15} y={cy + 15} textAnchor="middle" className="fill-muted-foreground" fontSize={10}>1</text>
        <text x={size - 15} y={cy + 15} textAnchor="middle" className="fill-muted-foreground" fontSize={10}>10</text>
      </svg>
      {label && (
        <span className="text-xs text-muted-foreground mt-1">{label}</span>
      )}
    </div>
  );
}
