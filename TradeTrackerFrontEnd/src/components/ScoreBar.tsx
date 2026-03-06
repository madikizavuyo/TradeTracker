import { StatusDot } from '@/components/StatusDot';

type Status = 'loading' | 'error' | 'idle';

export function ScoreBar({ label, value, status = 'idle' }: { label: string; value?: number; status?: Status }) {
  if (value == null) {
    return (
      <div>
        <div className="flex justify-between items-center text-xs mb-1 gap-2">
          <span className="flex items-center gap-1.5 text-muted-foreground">
            <StatusDot status={status} className="h-1.5 w-1.5" />
            {label}
          </span>
          <span className="text-muted-foreground font-medium">N/A</span>
        </div>
        <div className="h-2 bg-muted rounded-full overflow-hidden">
          <div className="h-full rounded-full bg-muted-foreground/20" style={{ width: '0%' }} />
        </div>
      </div>
    );
  }
  const pct = ((value - 1) / 9) * 100;
  const color = pct <= 35 ? 'bg-red-500' : pct <= 65 ? 'bg-yellow-500' : 'bg-green-500';

  return (
    <div>
      <div className="flex justify-between items-center text-xs mb-1 gap-2">
        <span className="flex items-center gap-1.5 text-muted-foreground">
          <StatusDot status={status} className="h-1.5 w-1.5" />
          {label}
        </span>
        <span className="font-medium">{value.toFixed(1)}</span>
      </div>
      <div className="h-2 bg-muted rounded-full overflow-hidden">
        <div className={`h-full rounded-full ${color} transition-all`} style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}
