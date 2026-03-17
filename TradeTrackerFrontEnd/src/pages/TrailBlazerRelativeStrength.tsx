import { useCallback, useEffect, useState } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { StatusDot } from '@/components/StatusDot';
import { Activity } from 'lucide-react';
import { api } from '@/lib/api';
import { useTrailBlazerRefresh } from '@/contexts/TrailBlazerRefreshContext';

type RelativeStrengthItem = { symbol: string; pctChange5d: number; pctChange20d: number };

export default function TrailBlazerRelativeStrength() {
  const { setTabStatus } = useTrailBlazerRefresh();
  const [data, setData] = useState<RelativeStrengthItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const res = await api.getTrailBlazerRelativeStrength();
      setData(res);
      setError(null);
    } catch (err) {
      console.error('Failed to load relative strength:', err);
      setError('Failed to load relative strength. Ensure FMP API key is configured.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    setTabStatus('relative-strength', loading ? 'loading' : error ? 'error' : 'idle');
  }, [loading, error, setTabStatus]);

  useEffect(() => {
    const onReload = () => load();
    window.addEventListener('trailblazer-reload-now', onReload);
    return () => window.removeEventListener('trailblazer-reload-now', onReload);
  }, [load]);

  if (loading) {
    return (
      <div className="flex items-center justify-center h-96 gap-2">
        <StatusDot status="loading" />
        <span className="text-muted-foreground">Loading Relative Strength...</span>
      </div>
    );
  }

  if (error) {
    return (
      <Card>
        <CardContent className="py-8 text-center">
          <StatusDot status="error" />
          <p className="text-muted-foreground mt-2">{error}</p>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <StatusDot status="idle" />
          <Activity className="h-5 w-5" />
          Relative Strength
        </CardTitle>
        <p className="text-sm text-muted-foreground">
          Assets ranked by % price change over 5 and 20 days. Higher = stronger performance.
        </p>
      </CardHeader>
      <CardContent>
        {data.length === 0 ? (
          <p className="text-center text-muted-foreground py-8">No data available.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border">
                  <th className="text-left p-2 font-medium">#</th>
                  <th className="text-left p-2 font-medium">Asset</th>
                  <th className="text-right p-2 font-medium">5d %</th>
                  <th className="text-right p-2 font-medium">20d %</th>
                </tr>
              </thead>
              <tbody>
                {data.map((row, i) => (
                  <tr key={row.symbol} className="border-b border-border/50">
                    <td className="p-2 text-muted-foreground">{i + 1}</td>
                    <td className="p-2 font-medium">{row.symbol}</td>
                    <td className={`p-2 text-right font-medium ${row.pctChange5d >= 0 ? 'text-green-500' : 'text-red-500'}`}>
                      {row.pctChange5d >= 0 ? '+' : ''}{row.pctChange5d.toFixed(2)}%
                    </td>
                    <td className={`p-2 text-right font-medium ${row.pctChange20d >= 0 ? 'text-green-500' : 'text-red-500'}`}>
                      {row.pctChange20d >= 0 ? '+' : ''}{row.pctChange20d.toFixed(2)}%
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
