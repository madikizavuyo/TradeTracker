import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { api } from '@/lib/api';

const POLL_INTERVAL_MS = 2000;

export type TabStatus = 'loading' | 'error' | 'idle';
type TabStatusMap = Record<string, TabStatus>;

export type RefreshProgress = {
  status: 'idle' | 'running' | 'completed' | 'failed';
  step?: string;
  message?: string;
  current: number;
  total: number;
  percent: number;
  error?: string;
};

type TrailBlazerRefreshContextType = {
  refreshTrigger: number;
  progress: RefreshProgress;
  triggerRefresh: () => Promise<void>;
  onDataReload: (fn: () => void | Promise<void>) => void;
  tabStatus: TabStatusMap;
  setTabStatus: (tab: string, status: TabStatus) => void;
};

const TrailBlazerRefreshContext = createContext<TrailBlazerRefreshContextType | null>(null);

const initialProgress: RefreshProgress = {
  status: 'idle',
  current: 0,
  total: 0,
  percent: 0,
};

export function TrailBlazerRefreshProvider({ children }: { children: React.ReactNode }) {
  const [refreshTrigger, setRefreshTrigger] = useState(0);
  const [progress, setProgress] = useState<RefreshProgress>(initialProgress);
  const [tabStatus, setTabStatusState] = useState<TabStatusMap>({});

  const setTabStatus = useCallback((tab: string, status: TabStatus) => {
    setTabStatusState((prev) => (prev[tab] === status ? prev : { ...prev, [tab]: status }));
  }, []);
  const reloadCallbacks = useRef<Set<() => void | Promise<void>>>(new Set());
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const onDataReload = useCallback((fn: () => void | Promise<void>) => {
    reloadCallbacks.current.add(fn);
    return () => {
      reloadCallbacks.current.delete(fn);
    };
  }, []);

  const runReloadCallbacks = useCallback(() => {
    reloadCallbacks.current.forEach((fn) => {
      try {
        fn();
      } catch (e) {
        console.error('TrailBlazer reload callback error:', e);
      }
    });
    window.dispatchEvent(new CustomEvent('trailblazer-reload-now'));
  }, []);

  const poll = useCallback(async () => {
    try {
      const s = await api.getTrailBlazerRefreshStatus();
      setProgress({
        status: s.status as RefreshProgress['status'],
        step: s.step,
        message: s.message,
        current: s.current ?? 0,
        total: s.total ?? 0,
        percent: s.percent ?? 0,
        error: s.error,
      });
      if (s.status === 'completed') {
        if (pollRef.current) {
          clearInterval(pollRef.current);
          pollRef.current = null;
        }
        runReloadCallbacks();
        setProgress((p) => ({ ...p, status: 'completed', percent: 100 }));
        setTimeout(() => setProgress(initialProgress), 3000);
      } else if (s.status === 'failed') {
        if (pollRef.current) {
          clearInterval(pollRef.current);
          pollRef.current = null;
        }
      }
    } catch {
      // Ignore poll errors
    }
  }, [runReloadCallbacks]);

  const triggerRefresh = useCallback(async () => {
    await api.refreshTrailBlazer();
    setRefreshTrigger((t) => t + 1);
    setProgress({ ...initialProgress, status: 'running', message: 'Starting...' });
    if (pollRef.current) clearInterval(pollRef.current);
    pollRef.current = setInterval(poll, POLL_INTERVAL_MS);
    poll();
  }, [poll]);

  useEffect(() => {
    return () => {
      if (pollRef.current) clearInterval(pollRef.current);
    };
  }, []);

  return (
    <TrailBlazerRefreshContext.Provider
      value={{ refreshTrigger, progress, triggerRefresh, onDataReload, tabStatus, setTabStatus }}
    >
      {children}
    </TrailBlazerRefreshContext.Provider>
  );
}

export function useTrailBlazerRefresh() {
  const ctx = useContext(TrailBlazerRefreshContext);
  if (!ctx) throw new Error('useTrailBlazerRefresh must be used within TrailBlazerRefreshProvider');
  return ctx;
}
