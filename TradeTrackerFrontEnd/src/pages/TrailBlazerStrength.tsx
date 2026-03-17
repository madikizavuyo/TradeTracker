import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { StatusDot } from '@/components/StatusDot';
import { BarChart3, TrendingUp, Search } from 'lucide-react';
import { Input } from '@/components/ui/input';
import { api } from '@/lib/api';
import { useTrailBlazerRefresh } from '@/contexts/TrailBlazerRefreshContext';
import { TrailBlazerScore } from '@/lib/types';

type CurrencyStrength = { currency: string; strength: number; positiveCount: number; negativeCount: number; totalIndicators: number };

export default function TrailBlazerStrength() {
  const navigate = useNavigate();
  const { setTabStatus } = useTrailBlazerRefresh();
  const [scores, setScores] = useState<TrailBlazerScore[]>([]);
  const [currencyStrength, setCurrencyStrength] = useState<CurrencyStrength[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');

  const load = useCallback(async (backgroundRefresh = false) => {
    try {
      if (!backgroundRefresh) setLoading(true);
      const [res, strengthRes] = await Promise.all([
        api.getTrailBlazerScores(),
        api.getTrailBlazerCurrencyStrength().catch(() => []),
      ]);
      const sorted = [...res].sort((a, b) => b.overallScore - a.overallScore);
      setScores(sorted);
      setCurrencyStrength(Array.isArray(strengthRes) ? strengthRes : []);
      setError(null);
    } catch (err) {
      console.error('Failed to load TrailBlazer scores:', err);
      setError('Failed to load TrailBlazer data.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    setTabStatus('strength', loading ? 'loading' : error ? 'error' : 'idle');
  }, [loading, error, setTabStatus]);

  useEffect(() => {
    const onReload = () => load(true);
    window.addEventListener('trailblazer-reload-now', onReload);
    return () => window.removeEventListener('trailblazer-reload-now', onReload);
  }, [load]);

  const getBiasVariant = (bias: string) => {
    if (bias === 'Bullish') return 'success' as const;
    if (bias === 'Bearish') return 'destructive' as const;
    return 'outline' as const;
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-96 gap-2">
        <StatusDot status="loading" />
        <span className="text-muted-foreground">Loading Strength rankings...</span>
      </div>
    );
  }

  if (error) {
    return (
      <Card>
        <CardContent className="py-8 text-center">
          <div className="flex items-center justify-center gap-2 mb-2">
            <StatusDot status="error" />
            <span className="text-muted-foreground">{error}</span>
          </div>
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-4">
      {currencyStrength.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <TrendingUp className="h-5 w-5" />
              Currency Strength
            </CardTitle>
            <p className="text-sm text-muted-foreground">
              70% global economic news analysis + 30% fundamentals (GDP, CPI, Unemployment, Interest Rate, PMI). Higher = stronger.
            </p>
          </CardHeader>
          <CardContent>
            <div className="flex flex-wrap gap-2">
              {currencyStrength.map((cs) => (
                <Badge
                  key={cs.currency}
                  variant={cs.strength >= 6.5 ? 'default' : cs.strength <= 3.5 ? 'destructive' : 'secondary'}
                  className="px-3 py-1.5 text-sm"
                >
                  {cs.currency} {cs.strength.toFixed(1)}
                </Badge>
              ))}
            </div>
          </CardContent>
        </Card>
      )}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <StatusDot status="idle" />
            <BarChart3 className="h-5 w-5" />
            Instrument Strength / Weakness
          </CardTitle>
          <p className="text-sm text-muted-foreground">
            Ranked by overall score (highest to lowest). Higher scores indicate stronger bullish setups.
          </p>
          <div className="relative mt-2 w-48">
            <Search className="absolute left-2 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
            <Input
              placeholder="Search instruments..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="pl-8 h-8 text-sm"
            />
          </div>
        </CardHeader>
        <CardContent>
          {scores.length === 0 ? (
          <p className="text-center text-muted-foreground py-8">No scores available yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border">
                  <th className="text-left p-2 w-12">#</th>
                  <th className="text-left p-2">Instrument</th>
                  <th className="text-center p-2">Score</th>
                  <th className="text-center p-2">Bias</th>
                  <th className="text-center p-2">Asset Class</th>
                </tr>
              </thead>
              <tbody>
                {scores
                  .filter(s => !searchQuery.trim() || s.instrumentName.toLowerCase().includes(searchQuery.trim().toLowerCase()))
                  .map((s, idx) => (
                  <tr
                    key={s.id}
                    className="border-b border-border/50 hover:bg-accent/50 cursor-pointer transition-colors"
                    onClick={() => navigate('/trailblazer/scanner', { state: { selectedInstrument: s } })}
                  >
                    <td className="p-2 font-medium text-muted-foreground">{idx + 1}</td>
                    <td className="p-2 font-medium">{s.instrumentName}</td>
                    <td className="p-2 text-center">
                      <span className={`font-bold ${s.overallScore >= 6.5 ? 'text-green-500' : s.overallScore <= 3.5 ? 'text-red-500' : 'text-yellow-500'}`}>
                        {s.overallScore.toFixed(1)}
                      </span>
                    </td>
                    <td className="p-2 text-center">
                      <Badge variant={getBiasVariant(s.bias)}>{s.bias}</Badge>
                    </td>
                    <td className="p-2 text-center">
                      <span className="text-xs text-muted-foreground">{s.assetClass}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </CardContent>
    </Card>
    </div>
  );
}
