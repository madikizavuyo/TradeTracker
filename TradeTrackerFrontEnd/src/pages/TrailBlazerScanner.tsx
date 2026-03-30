import { useCallback, useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { ScoreGauge } from '@/components/ScoreGauge';
import { ScoreBar } from '@/components/ScoreBar';
import { StatusDot } from '@/components/StatusDot';
import { Filter, ChevronLeft, ChevronRight, Search } from 'lucide-react';
import { Input } from '@/components/ui/input';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts';
import { api } from '@/lib/api';
import { useTrailBlazerRefresh } from '@/contexts/TrailBlazerRefreshContext';
import { TrailBlazerScore, ScoreHistoryEntry } from '@/lib/types';

type AssetFilter = 'All' | 'ForexMajor' | 'ForexMinor' | 'Index' | 'Metal' | 'Commodity' | 'Bond';

export default function TrailBlazerScanner() {
  const navigate = useNavigate();
  const location = useLocation();
  const { setTabStatus } = useTrailBlazerRefresh();
  const [scores, setScores] = useState<TrailBlazerScore[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [assetFilter, setAssetFilter] = useState<AssetFilter>('All');
  const [highConvictionOnly, setHighConvictionOnly] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [sortBy, setSortBy] = useState<'score' | 'name'>('score');
  const [scannerPage, setScannerPage] = useState(0);
  const [selectedInstrument, setSelectedInstrument] = useState<TrailBlazerScore | null>(null);
  const [history, setHistory] = useState<ScoreHistoryEntry[]>([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const PAGE_SIZE = 10;

  const loadHistory = useCallback(async (instrumentId: number) => {
    setHistoryLoading(true);
    try {
      const res = await api.getTrailBlazerHistory(instrumentId);
      setHistory(res);
    } catch {
      setHistory([]);
    } finally {
      setHistoryLoading(false);
    }
  }, []);

  useEffect(() => {
    const fromState = (location.state as { selectedInstrument?: TrailBlazerScore })?.selectedInstrument;
    if (fromState) {
      setSelectedInstrument(fromState);
      navigate(location.pathname, { replace: true, state: {} });
    }
  }, [location.state, navigate, location.pathname]);

  const load = useCallback(async (backgroundRefresh = false) => {
    try {
      if (!backgroundRefresh) setLoading(true);
      const res = await api.getTrailBlazerScores();
      setScores(res);
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
    setTabStatus('scanner', loading ? 'loading' : error ? 'error' : 'idle');
  }, [loading, error, setTabStatus]);

  useEffect(() => {
    const onReload = () => load(true);
    window.addEventListener('trailblazer-reload-now', onReload);
    return () => window.removeEventListener('trailblazer-reload-now', onReload);
  }, [load]);

  useEffect(() => {
    if (selectedInstrument) {
      loadHistory(selectedInstrument.instrumentId);
    } else {
      setHistory([]);
    }
  }, [selectedInstrument, loadHistory]);

  const searchLower = searchQuery.trim().toLowerCase();
  const allFilteredScores = scores
    .filter(s => {
      if (assetFilter !== 'All' && s.assetClass !== assetFilter) return false;
      if (searchLower && !s.instrumentName.toLowerCase().includes(searchLower)) return false;
      if (highConvictionOnly && s.overallScore >= 3.5 && s.overallScore <= 6.5) return false;
      return true;
    })
    .sort((a, b) => sortBy === 'score' ? b.overallScore - a.overallScore : a.instrumentName.localeCompare(b.instrumentName));
  const scannerTotalPages = Math.ceil(allFilteredScores.length / PAGE_SIZE);
  const filteredScores = allFilteredScores.slice(scannerPage * PAGE_SIZE, (scannerPage + 1) * PAGE_SIZE);

  const getBiasVariant = (bias: string) => {
    if (bias === 'Bullish') return 'success' as const;
    if (bias === 'Bearish') return 'destructive' as const;
    return 'outline' as const;
  };

  const hasDataSource = (ds: string | undefined | null, key: string): boolean => {
    if (!ds) return false;
    try {
      const arr = JSON.parse(ds) as string[];
      return Array.isArray(arr) && arr.some((s: string) => s === key);
    } catch {
      return false;
    }
  };

  const formatScoreCell = (score: TrailBlazerScore, key: 'fundamental' | 'cot' | 'retail' | 'news' | 'technical' | 'currency') => {
    const sourceMap = { fundamental: 'FRED', cot: 'CFTC', retail: 'myfxbook', news: 'Brave/Finnhub', technical: 'TwelveData', currency: 'CurrencyStrength' } as const;
    const source = sourceMap[key];
    const hasData = key === 'retail'
      ? hasDataSource(score.dataSources, 'myfxbook') || hasDataSource(score.dataSources, 'load-myfxbook')
      : key === 'technical'
      ? hasDataSource(score.dataSources, 'YahooFinance') || hasDataSource(score.dataSources, 'TwelveData') || hasDataSource(score.dataSources, 'MarketStack') || hasDataSource(score.dataSources, 'iTick') || hasDataSource(score.dataSources, 'EODHD') || hasDataSource(score.dataSources, 'FMP') || hasDataSource(score.dataSources, 'NasdaqDataLink')
      : key === 'news'
      ? hasDataSource(score.dataSources, 'Yahoo/Finnhub/Brave') || hasDataSource(score.dataSources, 'Brave/Finnhub')
      : hasDataSource(score.dataSources, source);
    if (!hasData) return <span className="text-muted-foreground">N/A</span>;
    if (key === 'currency' && (score.currencyStrengthScore == null || score.currencyStrengthScore === undefined)) return <span className="text-muted-foreground">N/A</span>;
    const val = key === 'fundamental' ? score.fundamentalScore : key === 'cot' ? score.cotScore : key === 'retail' ? score.retailSentimentScore : key === 'news' ? (score.newsSentimentScore ?? 5) : key === 'currency' ? score.currencyStrengthScore! : score.technicalScore;
    return val.toFixed(1);
  };

  const formatSetupBadge = (sig: string | null | undefined) => {
    if (!sig || sig === 'NONE') return <span className="text-muted-foreground text-xs">—</span>;
    const u = sig.toUpperCase();
    if (u === 'STRONG_BUY') return <Badge className="bg-green-700 hover:bg-green-700 text-white text-xs">STRONG BUY</Badge>;
    if (u === 'BUY') return <Badge className="bg-green-600/90 hover:bg-green-600/90 text-white text-xs">BUY</Badge>;
    if (u === 'STRONG_SELL') return <Badge variant="destructive" className="text-xs">STRONG SELL</Badge>;
    if (u === 'SELL') return <Badge className="bg-red-600/90 hover:bg-red-600/90 text-white text-xs">SELL</Badge>;
    if (u === 'WATCH') return <Badge variant="outline" className="text-amber-700 border-amber-600 text-xs">WATCH</Badge>;
    return <span className="text-xs text-muted-foreground">{sig}</span>;
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-96 gap-2">
        <StatusDot status="loading" />
        <span className="text-muted-foreground">Loading Asset Scanner...</span>
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
    <Card>
      <CardHeader className="flex flex-row items-center gap-2">
        <StatusDot status="idle" />
        <div className="flex items-center justify-between">
          <CardTitle className="flex items-center gap-2">
            <Filter className="h-5 w-5" />
            Asset Scanner
          </CardTitle>
          <div className="flex flex-wrap items-center gap-2">
            <div className="relative">
              <Search className="absolute left-2 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                placeholder="Search instruments..."
                value={searchQuery}
                onChange={(e) => { setSearchQuery(e.target.value); setScannerPage(0); }}
                className="pl-8 h-7 w-40 text-xs"
              />
            </div>
            <Button
              size="sm"
              variant={highConvictionOnly ? 'default' : 'outline'}
              onClick={() => { setHighConvictionOnly(!highConvictionOnly); setScannerPage(0); }}
              className="text-xs h-7"
              title="Hide Neutral (3.5–6.5)"
            >
              High Conviction
            </Button>
            {(['All', 'ForexMajor', 'ForexMinor', 'Index', 'Metal', 'Commodity', 'Bond'] as AssetFilter[]).map(f => (
              <Button
                key={f}
                size="sm"
                variant={assetFilter === f ? 'default' : 'outline'}
                onClick={() => { setAssetFilter(f); setScannerPage(0); }}
                className="text-xs h-7"
              >
                {f === 'All' ? 'All' : f.replace('Forex', 'FX ')}
              </Button>
            ))}
          </div>
        </div>
      </CardHeader>
      <CardContent>
        {filteredScores.length === 0 ? (
          <p className="text-center text-muted-foreground py-8">No scores available for this filter.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border">
                  <th className="text-left p-2 cursor-pointer hover:text-primary" onClick={() => setSortBy('name')}>
                    Instrument {sortBy === 'name' && '▲'}
                  </th>
                  <th className="text-center p-2">Class</th>
                  <th className="text-center p-2 cursor-pointer hover:text-primary" onClick={() => setSortBy('score')}>
                    Score {sortBy === 'score' && '▼'}
                  </th>
                  <th className="text-center p-2">Bias</th>
                  <th className="text-center p-2" title="Yahoo box breakout + scanner alignment">Setup</th>
                  <th className="text-center p-2">Fundamental</th>
                  <th className="text-center p-2">COT</th>
                  <th className="text-center p-2">Retail</th>
                  <th className="text-center p-2">News</th>
                  <th className="text-center p-2">Currency</th>
                  <th className="text-center p-2">Technical</th>
                </tr>
              </thead>
              <tbody>
                {filteredScores.map((s) => (
                  <tr
                    key={s.id}
                    className="border-b border-border/50 hover:bg-accent/50 cursor-pointer transition-colors"
                    onClick={() => setSelectedInstrument(s)}
                  >
                    <td className="p-2 font-medium">{s.instrumentName}</td>
                    <td className="p-2 text-center">
                      <span className="text-xs text-muted-foreground">{s.assetClass}</span>
                    </td>
                    <td className="p-2 text-center">
                      <span className={`font-bold ${
                        s.overallScore >= 7 ? 'text-green-700 dark:text-green-400' :
                        s.overallScore >= 6.5 ? 'text-green-500' :
                        s.overallScore <= 3 ? 'text-red-700 dark:text-red-400' :
                        s.overallScore <= 3.5 ? 'text-red-500' :
                        'text-yellow-500'
                      }`}>
                        {s.overallScore.toFixed(1)}
                      </span>
                    </td>
                    <td className="p-2 text-center">
                      <Badge variant={getBiasVariant(s.bias)}>{s.bias}</Badge>
                    </td>
                    <td className="p-2 text-center">{formatSetupBadge(s.tradeSetupSignal)}</td>
                    <td className="p-2 text-center">{formatScoreCell(s, 'fundamental')}</td>
                    <td className="p-2 text-center">{formatScoreCell(s, 'cot')}</td>
                    <td className="p-2 text-center">{formatScoreCell(s, 'retail')}</td>
                    <td className="p-2 text-center">{formatScoreCell(s, 'news')}</td>
                    <td className="p-2 text-center">{formatScoreCell(s, 'currency')}</td>
                    <td className="p-2 text-center">{formatScoreCell(s, 'technical')}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {scannerTotalPages > 1 && (
              <div className="flex items-center justify-between mt-4 pt-3 border-t border-border">
                <span className="text-xs text-muted-foreground">
                  Showing {scannerPage * PAGE_SIZE + 1}–{Math.min((scannerPage + 1) * PAGE_SIZE, allFilteredScores.length)} of {allFilteredScores.length}
                </span>
                <div className="flex gap-1">
                  <Button size="sm" variant="outline" className="h-7 w-7 p-0" disabled={scannerPage === 0} onClick={() => setScannerPage(p => p - 1)}>
                    <ChevronLeft className="h-4 w-4" />
                  </Button>
                  {Array.from({ length: scannerTotalPages }, (_, i) => (
                    <Button
                      key={i}
                      size="sm"
                      variant={scannerPage === i ? 'default' : 'outline'}
                      className="h-7 w-7 p-0 text-xs"
                      onClick={() => setScannerPage(i)}
                    >
                      {i + 1}
                    </Button>
                  ))}
                  <Button size="sm" variant="outline" className="h-7 w-7 p-0" disabled={scannerPage >= scannerTotalPages - 1} onClick={() => setScannerPage(p => p + 1)}>
                    <ChevronRight className="h-4 w-4" />
                  </Button>
                </div>
              </div>
            )}
          </div>
        )}

        {selectedInstrument && (
          <div className="mt-6 pt-6 border-t border-border space-y-6">
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <StatusDot status={historyLoading ? 'loading' : 'idle'} />
                  Score History
                </CardTitle>
              </CardHeader>
              <CardContent>
                {history.length === 0 ? (
                  <div className="flex items-center justify-center h-64 text-muted-foreground">
                    No historical data available yet.
                  </div>
                ) : (
                  <ResponsiveContainer width="100%" height={300}>
                    <LineChart data={history} margin={{ top: 5, right: 20, left: 0, bottom: 5 }}>
                      <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" />
                      <XAxis
                        dataKey="dateComputed"
                        tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 10 }}
                        tickFormatter={(d: string) => new Date(d).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}
                      />
                      <YAxis domain={[1, 10]} tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 11 }} />
                      <Tooltip
                        contentStyle={{
                          backgroundColor: 'hsl(var(--card))',
                          border: '1px solid hsl(var(--border))',
                          borderRadius: '8px',
                        }}
                      />
                      <Line type="monotone" dataKey="overallScore" stroke="hsl(var(--primary))" strokeWidth={2} dot={false} name="Overall" />
                      <Line type="monotone" dataKey="fundamentalScore" stroke="#22c55e" strokeWidth={1} dot={false} name="Fundamental" />
                      <Line type="monotone" dataKey="technicalScore" stroke="#3b82f6" strokeWidth={1} dot={false} name="Technical" />
                      <Line type="monotone" dataKey="sentimentScore" stroke="#f59e0b" strokeWidth={1} dot={false} name="Sentiment" />
                    </LineChart>
                  </ResponsiveContainer>
                )}
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <StatusDot status="idle" title="Score breakdown" />
                  {selectedInstrument.instrumentName} — Score Breakdown
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="flex justify-center mb-6">
                  <ScoreGauge score={selectedInstrument.overallScore} size={160} label={selectedInstrument.bias} />
                </div>
                <div className="space-y-3">
                  <ScoreBar label="Fundamental" value={hasDataSource(selectedInstrument.dataSources, 'FRED') ? selectedInstrument.fundamentalScore : undefined} />
                  <ScoreBar label="Institutional COT" value={hasDataSource(selectedInstrument.dataSources, 'CFTC') ? selectedInstrument.cotScore : undefined} />
                  <ScoreBar label="Retail Sentiment" value={(hasDataSource(selectedInstrument.dataSources, 'myfxbook') || hasDataSource(selectedInstrument.dataSources, 'load-myfxbook')) ? selectedInstrument.retailSentimentScore : undefined} />
                  <ScoreBar label="News Sentiment" value={(hasDataSource(selectedInstrument.dataSources, 'Yahoo/Finnhub/Brave') || hasDataSource(selectedInstrument.dataSources, 'Brave/Finnhub')) ? (selectedInstrument.newsSentimentScore ?? 5) : undefined} />
                  <ScoreBar label="Currency Strength" value={hasDataSource(selectedInstrument.dataSources, 'CurrencyStrength') && selectedInstrument.currencyStrengthScore != null ? selectedInstrument.currencyStrengthScore : undefined} />
                  <ScoreBar label="Technical" value={(hasDataSource(selectedInstrument.dataSources, 'YahooFinance') || hasDataSource(selectedInstrument.dataSources, 'TwelveData') || hasDataSource(selectedInstrument.dataSources, 'MarketStack') || hasDataSource(selectedInstrument.dataSources, 'iTick') || hasDataSource(selectedInstrument.dataSources, 'EODHD') || hasDataSource(selectedInstrument.dataSources, 'FMP') || hasDataSource(selectedInstrument.dataSources, 'NasdaqDataLink')) ? selectedInstrument.technicalScore : undefined} />
                </div>
                {(selectedInstrument.tradeSetupSignal && selectedInstrument.tradeSetupSignal !== 'NONE') && (
                  <div className={`mt-4 rounded-lg border p-3 text-sm ${selectedInstrument.tradeSetupSignal.includes('SELL') ? 'border-red-500/50 bg-red-500/5' : selectedInstrument.tradeSetupSignal.includes('BUY') ? 'border-green-600/50 bg-green-600/5' : 'border-amber-500/50 bg-amber-500/5'}`}>
                    <div className="font-semibold mb-1 flex items-center gap-2">Box breakout + scanner {formatSetupBadge(selectedInstrument.tradeSetupSignal)}</div>
                    <p className="text-muted-foreground leading-snug">{selectedInstrument.tradeSetupDetail ?? ''}</p>
                  </div>
                )}
              </CardContent>
            </Card>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
