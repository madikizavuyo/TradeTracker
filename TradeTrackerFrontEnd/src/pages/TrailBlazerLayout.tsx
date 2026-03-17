import { Outlet, NavLink } from 'react-router-dom';
import { AppLayout } from '@/components/AppLayout';
import { Button } from '@/components/ui/button';
import { Filter, LayoutDashboard, BarChart3, Newspaper, RefreshCw, GitCompare, Clock, Activity } from 'lucide-react';
import { StatusDot } from '@/components/StatusDot';
import { cn } from '@/lib/utils';
import { useState, useEffect } from 'react';
import { useTrailBlazerRefresh } from '@/contexts/TrailBlazerRefreshContext';
import { api } from '@/lib/api';

const tabs = [
  { to: '/trailblazer', label: 'Overview', icon: LayoutDashboard },
  { to: '/trailblazer/scanner', label: 'Asset Scanner', icon: Filter },
  { to: '/trailblazer/strength', label: 'Strength', icon: BarChart3 },
  { to: '/trailblazer/relative-strength', label: 'Relative Strength', icon: Activity },
  { to: '/trailblazer/news-sentiment', label: 'News Sentiment', icon: Newspaper },
  { to: '/trailblazer/bias-changes', label: 'Bias Changes', icon: GitCompare },
];

function formatLastRefresh(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
  } catch {
    return iso;
  }
}

export default function TrailBlazerLayout() {
  const [refreshing, setRefreshing] = useState(false);
  const [lastRefresh, setLastRefresh] = useState<string | null>(null);
  const { triggerRefresh, progress, tabStatus } = useTrailBlazerRefresh();

  const fetchLastRefresh = async () => {
    try {
      const { lastSuccessfulRefresh } = await api.getTrailBlazerLastRefresh();
      setLastRefresh(lastSuccessfulRefresh ?? null);
    } catch {
      setLastRefresh(null);
    }
  };

  useEffect(() => {
    fetchLastRefresh();
  }, []);

  useEffect(() => {
    if (progress.status === 'completed') {
      fetchLastRefresh();
    }
  }, [progress.status]);

  const tabStatusMap: Record<string, string> = {
    '/trailblazer': 'overview',
    '/trailblazer/scanner': 'scanner',
    '/trailblazer/strength': 'strength',
    '/trailblazer/relative-strength': 'relative-strength',
    '/trailblazer/news-sentiment': 'news-sentiment',
    '/trailblazer/bias-changes': 'bias-changes',
  };
  const isActive = progress.status === 'running' || progress.status === 'completed';

  const handleRefresh = async () => {
    setRefreshing(true);
    try {
      await triggerRefresh();
    } finally {
      setRefreshing(false);
    }
  };

  return (
    <AppLayout>
      <div className="space-y-6">
        {isActive && (
          <div className="rounded-lg border border-border bg-muted/30 p-4 space-y-2">
            <div className="flex items-center justify-between text-sm">
              <div className="flex items-center gap-2">
                <StatusDot
                  status={progress.status === 'failed' ? 'error' : progress.status === 'running' ? 'loading' : 'idle'}
                  title={progress.status === 'failed' ? 'Refresh failed' : progress.status === 'running' ? 'Refreshing...' : 'Complete'}
                />
                <span className="font-medium text-foreground">
                {progress.status === 'completed'
                  ? 'Refresh complete'
                  : progress.status === 'failed'
                    ? 'Refresh failed'
                    : 'Refreshing data...'}
                </span>
              </div>
              {progress.status === 'running' && (
                <span className="text-muted-foreground">{progress.message}</span>
              )}
              {progress.status === 'failed' && progress.error && (
                <span className="text-destructive text-xs">{progress.error}</span>
              )}
            </div>
            {(progress.status === 'running' || progress.status === 'completed') && (
              <div className="h-2 w-full rounded-full bg-muted overflow-hidden">
                <div
                  className="h-full bg-primary transition-all duration-500 ease-out"
                  style={{
                    width: progress.total > 0
                      ? `${Math.min(100, progress.percent)}%`
                      : progress.status === 'completed'
                        ? '100%'
                        : '33%',
                  }}
                />
              </div>
            )}
            {progress.total > 0 && progress.status === 'running' && (
              <p className="text-xs text-muted-foreground">
                {progress.current} / {progress.total} instruments
              </p>
            )}
          </div>
        )}
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h1 className="text-2xl sm:text-3xl font-bold tracking-tight text-primary">TrailBlazer</h1>
            <p className="text-sm sm:text-base text-muted-foreground">Market scanner with multi-factor scoring across fundamentals, sentiment, and technicals.</p>
            {lastRefresh && (
              <p className="mt-1 flex items-center gap-1.5 text-xs text-muted-foreground">
                <Clock className="h-3.5 w-3.5" />
                Last refreshed: {formatLastRefresh(lastRefresh)}
              </p>
            )}
          </div>
          <Button onClick={handleRefresh} disabled={refreshing || progress.status === 'running'} variant="outline" className="min-h-[44px] shrink-0">
            <RefreshCw className={`h-4 w-4 mr-2 ${refreshing ? 'animate-spin' : ''}`} />
            {refreshing ? 'Starting...' : progress.status === 'running' ? 'Refreshing...' : 'Refresh Data'}
          </Button>
        </div>

        <nav className="flex gap-1 border-b border-border overflow-x-auto -mx-4 px-4 sm:mx-0 sm:px-0 scrollbar-hide">
          {tabs.map((tab) => {
            const statusKey = tabStatusMap[tab.to];
            const status = (statusKey ? tabStatus[statusKey] ?? 'idle' : 'idle') as 'loading' | 'error' | 'idle';
            return (
              <NavLink
                key={tab.to}
                to={tab.to}
                end={tab.to === '/trailblazer'}
                className={({ isActive }) =>
                  cn(
                    'flex items-center gap-2 px-4 py-3 text-sm font-medium border-b-2 -mb-px transition-colors shrink-0 min-h-[44px]',
                    isActive
                      ? 'border-primary text-primary'
                      : 'border-transparent text-muted-foreground hover:text-foreground'
                  )
                }
              >
                <StatusDot status={status} />
                <tab.icon className="h-4 w-4" />
                {tab.label}
              </NavLink>
            );
          })}
        </nav>

        <Outlet />
      </div>
    </AppLayout>
  );
}
