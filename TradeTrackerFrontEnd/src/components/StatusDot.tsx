import { cn } from '@/lib/utils';

type Status = 'loading' | 'error' | 'idle';

type StatusDotProps = {
  status: Status;
  className?: string;
  title?: string;
};

const statusConfig: Record<Status, { bg: string; pulse?: boolean; title: string }> = {
  loading: { bg: 'bg-gray-500', pulse: true, title: 'Fetching data...' },
  error: { bg: 'bg-red-500', title: 'Failed to load' },
  idle: { bg: 'bg-green-500', title: 'Data loaded' },
};

export function StatusDot({ status, className, title }: StatusDotProps) {
  const config = statusConfig[status];
  return (
    <span
      className={cn(
        'inline-block h-2 w-2 rounded-full shrink-0',
        config.bg,
        config.pulse && 'animate-pulse',
        className
      )}
      title={title ?? config.title}
      aria-label={title ?? config.title}
    />
  );
}
