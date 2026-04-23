import { useCallback, useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { ScoreGauge } from '@/components/ScoreGauge';
import { ScoreBar } from '@/components/ScoreBar';
import { StatusDot } from '@/components/StatusDot';
import { Filter, ChevronLeft, ChevronRight, Search, ChevronUp, ChevronDown, HelpCircle } from 'lucide-react';
import { Input } from '@/components/ui/input';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts';
import { api } from '@/lib/api';
import { useTrailBlazerRefresh } from '@/contexts/TrailBlazerRefreshContext';
import { TrailBlazerScore, ScoreHistoryEntry } from '@/lib/types';

type AssetFilter = 'All' | 'ForexMajor' | 'ForexMinor' | 'Index' | 'Metal' | 'Commodity' | 'Bond';
type BiasFilter = 'All' | 'Bullish' | 'Bearish' | 'Neutral';
type SignalFilter = 'All' | 'BUY NOW' | 'SELL NOW' | 'DOUBLE_CONFLUENCE_BUY' | 'DOUBLE_CONFLUENCE_SELL' | 'GOLDEN_ZONE_BUY' | 'GOLDEN_ZONE_SELL' | 'HORIZONTAL_CONFLUENCE_BUY' | 'HORIZONTAL_CONFLUENCE_SELL' | 'TRENDLINE_CONFLUENCE_BUY' | 'TRENDLINE_CONFLUENCE_SELL' | 'STRONG_REVERSAL_BUY' | 'STRONG_REVERSAL_SELL' | 'REVERSAL_BUY' | 'REVERSAL_SELL' | 'RESISTANCE_BUY' | 'RESISTANCE_SELL' | 'TRENDLINE_BUY' | 'TRENDLINE_SELL' | 'BUY' | 'SELL' | 'WATCH' | 'NONE';

type SortKey = 'name' | 'class' | 'score' | 'bias' | 'signal' | 'fundamental' | 'cot' | 'retail' | 'news' | 'currency' | 'technical';
type SortDir = 'asc' | 'desc';

const SIGNAL_TOOLTIPS: Record<string, string> = {
  'BUY NOW': 'Bullish golden zone is active and both horizontal support plus trendline confluence are aligned. Highest-conviction bullish entry.',
  'SELL NOW': 'Bearish golden zone is active and both horizontal resistance plus trendline confluence are aligned. Highest-conviction bearish entry.',
  'DOUBLE_CONFLUENCE_BUY': 'Daily horizontal support and the active 4H trendline are both aligned, but price has not entered the Fib golden zone yet.',
  'DOUBLE_CONFLUENCE_SELL': 'Daily horizontal resistance and the active 4H trendline are both aligned, but price has not entered the Fib golden zone yet.',
  'GOLDEN_ZONE_BUY': 'Price is inside the bullish 50%-61.8% Fib golden zone, but no double confluence is active yet.',
  'GOLDEN_ZONE_SELL': 'Price is inside the bearish 50%-61.8% Fib golden zone, but no double confluence is active yet.',
  'HORIZONTAL_CONFLUENCE_BUY': 'Price is respecting Daily support, but the 4H trendline is not aligned yet.',
  'HORIZONTAL_CONFLUENCE_SELL': 'Price is respecting Daily resistance, but the 4H trendline is not aligned yet.',
  'TRENDLINE_CONFLUENCE_BUY': 'Price is respecting the active 4H trendline, but Daily support is not aligned yet.',
  'TRENDLINE_CONFLUENCE_SELL': 'Price is respecting the active 4H trendline, but Daily resistance is not aligned yet.',
  'STRONG_REVERSAL_BUY': 'Deep (61.8%) Fibonacci retrace of the last down-leg has been touched — classic reversal zone. Direction is BUY from the scanner score.',
  'STRONG_REVERSAL_SELL': 'Deep (61.8%) Fibonacci retrace of the last up-leg has been touched — classic reversal zone. Direction is SELL from the scanner score.',
  'REVERSAL_BUY': 'Shallow (50%) Fibonacci retrace of the last down-leg reached. Weaker reversal zone than 61.8% but still tradeable in a bullish scanner context.',
  'REVERSAL_SELL': 'Shallow (50%) Fibonacci retrace of the last up-leg reached. Weaker reversal than 61.8% but still tradeable in a bearish scanner context.',
  'RESISTANCE_BUY': 'Price is bouncing off horizontal trend support (continuation buy) with score > 6. No Fib touched — trend-follow entry.',
  'RESISTANCE_SELL': 'Price is rejecting horizontal trend resistance (continuation sell) with score < 4. No Fib touched — trend-follow entry.',
  'TRENDLINE_BUY': 'Price is respecting an ascending trendline drawn through the two most-recent higher-lows. Continuation BUY in an uptrend; score > 6. Use the trendline level as your entry zone, stop just beneath it.',
  'TRENDLINE_SELL': 'Price is respecting a descending trendline drawn through the two most-recent lower-highs. Continuation SELL in a downtrend; score < 4. Use the trendline level as your entry zone, stop just above it.',
  'BUY': 'Directional BUY from scanner score > 6. No Fib, horizontal S/R or trendline alignment yet — wait for entry trigger.',
  'SELL': 'Directional SELL from scanner score < 4. No Fib, horizontal S/R or trendline alignment yet — wait for entry trigger.',
  'STRONG_BUY': 'Legacy strong-buy label from older engine. Treat as high-conviction BUY.',
  'STRONG_SELL': 'Legacy strong-sell label from older engine. Treat as high-conviction SELL.',
  'WATCH': 'Scanner score is in the neutral band (4–6). No actionable directional signal — monitor only.',
  'NONE': 'No signal generated (insufficient price history or neutral/unclear structure).',
};

const SIGNAL_STATUS_TEXT: Record<string, string> = {
  'BUY NOW': 'Golden zone + double confluence',
  'SELL NOW': 'Golden zone + double confluence',
  'DOUBLE_CONFLUENCE_BUY': 'Horizontal + trendline',
  'DOUBLE_CONFLUENCE_SELL': 'Horizontal + trendline',
  'GOLDEN_ZONE_BUY': 'Golden zone only',
  'GOLDEN_ZONE_SELL': 'Golden zone only',
  'HORIZONTAL_CONFLUENCE_BUY': 'Horizontal only',
  'HORIZONTAL_CONFLUENCE_SELL': 'Horizontal only',
  'TRENDLINE_CONFLUENCE_BUY': 'Trendline only',
  'TRENDLINE_CONFLUENCE_SELL': 'Trendline only',
  'BUY': 'Directional only',
  'SELL': 'Directional only',
  'WATCH': 'Neutral bias',
  'NONE': 'No setup',
};

export default function TrailBlazerScanner() {
  const navigate = useNavigate();
  const location = useLocation();
  const { setTabStatus } = useTrailBlazerRefresh();
  const [scores, setScores] = useState<TrailBlazerScore[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [assetFilter, setAssetFilter] = useState<AssetFilter>('All');
  const [biasFilter, setBiasFilter] = useState<BiasFilter>('All');
  const [signalFilter, setSignalFilter] = useState<SignalFilter>('All');
  const [highConvictionOnly, setHighConvictionOnly] = useState(false);
  const [alignedOnly, setAlignedOnly] = useState(false);
  const [minScore, setMinScore] = useState<number | ''>('');
  const [maxScore, setMaxScore] = useState<number | ''>('');
  const [searchQuery, setSearchQuery] = useState('');
  const [sortKey, setSortKey] = useState<SortKey>('score');
  const [sortDir, setSortDir] = useState<SortDir>('desc');
  const [scannerPage, setScannerPage] = useState(0);
  const [selectedInstrument, setSelectedInstrument] = useState<TrailBlazerScore | null>(null);
  const [history, setHistory] = useState<ScoreHistoryEntry[]>([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [showGuide, setShowGuide] = useState(false);
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

  useEffect(() => { load(); }, [load]);
  useEffect(() => { setTabStatus('scanner', loading ? 'loading' : error ? 'error' : 'idle'); }, [loading, error, setTabStatus]);
  useEffect(() => {
    const onReload = () => load(true);
    window.addEventListener('trailblazer-reload-now', onReload);
    return () => window.removeEventListener('trailblazer-reload-now', onReload);
  }, [load]);
  useEffect(() => {
    if (selectedInstrument) loadHistory(selectedInstrument.instrumentId);
    else setHistory([]);
  }, [selectedInstrument, loadHistory]);

  const searchLower = searchQuery.trim().toLowerCase();

  const toggleSort = (key: SortKey) => {
    if (sortKey === key) setSortDir(d => d === 'asc' ? 'desc' : 'asc');
    else { setSortKey(key); setSortDir(key === 'name' || key === 'class' || key === 'bias' || key === 'signal' ? 'asc' : 'desc'); }
    setScannerPage(0);
  };

  const sortIcon = (key: SortKey) => {
    if (sortKey !== key) return <span className="inline-block w-3" />;
    return sortDir === 'asc' ? <ChevronUp className="inline h-3 w-3" /> : <ChevronDown className="inline h-3 w-3" />;
  };

  /** True if persisted dataSources JSON array includes this tag. Supports legacy combined tag. */
  const hasDataSource = (ds: string | undefined | null, key: string): boolean => {
    if (!ds || !key) return false;
    try {
      const arr = JSON.parse(ds) as string[];
      if (!Array.isArray(arr)) return false;
      const legacyWebNews = 'GoogleNews/Yahoo/Finnhub/Brave';
      return arr.some((s: string) => {
        if (s === key) return true;
        if (s === legacyWebNews) return key === 'GoogleNews' || key === 'Yahoo/Finnhub/Brave' || key === 'Brave/Finnhub';
        return false;
      });
    } catch { return false; }
  };

  const getComponent = (s: TrailBlazerScore, key: Exclude<SortKey, 'name' | 'class' | 'score' | 'bias' | 'signal'>): number => {
    switch (key) {
      case 'fundamental': return s.fundamentalScore ?? 0;
      case 'cot': return s.cotScore ?? 0;
      case 'retail': return s.retailSentimentScore ?? 0;
      case 'news': return s.newsSentimentScore ?? 5;
      case 'currency': return s.currencyStrengthScore ?? 0;
      case 'technical': return s.technicalScore ?? 0;
    }
  };

  const allFilteredScores = useMemo(() => {
    const rows = scores.filter(s => {
      if (assetFilter !== 'All' && s.assetClass !== assetFilter) return false;
      if (biasFilter !== 'All' && s.bias !== biasFilter) return false;
      if (signalFilter !== 'All') {
        const sig = (s.tradeSetupSignal ?? 'NONE').toUpperCase();
        if (sig !== signalFilter) return false;
      }
      if (searchLower && !s.instrumentName.toLowerCase().includes(searchLower)) return false;
      if (highConvictionOnly && s.overallScore >= 3.5 && s.overallScore <= 6.5) return false;
      if (alignedOnly) {
        const sig = (s.tradeSetupSignal ?? '').toUpperCase();
        if (sig !== 'BUY NOW' && sig !== 'SELL NOW') return false;
      }
      if (minScore !== '' && s.overallScore < Number(minScore)) return false;
      if (maxScore !== '' && s.overallScore > Number(maxScore)) return false;
      return true;
    });

    const dir = sortDir === 'asc' ? 1 : -1;
    rows.sort((a, b) => {
      let cmp = 0;
      switch (sortKey) {
        case 'name': cmp = a.instrumentName.localeCompare(b.instrumentName); break;
        case 'class': cmp = (a.assetClass ?? '').localeCompare(b.assetClass ?? ''); break;
        case 'bias': cmp = (a.bias ?? '').localeCompare(b.bias ?? ''); break;
        case 'signal': cmp = (a.tradeSetupSignal ?? 'z').localeCompare(b.tradeSetupSignal ?? 'z'); break;
        case 'score': cmp = a.overallScore - b.overallScore; break;
        default: cmp = getComponent(a, sortKey) - getComponent(b, sortKey); break;
      }
      if (cmp === 0) cmp = a.instrumentName.localeCompare(b.instrumentName);
      return cmp * dir;
    });
    return rows;
  }, [scores, assetFilter, biasFilter, signalFilter, searchLower, highConvictionOnly, alignedOnly, minScore, maxScore, sortKey, sortDir]);

  const scannerTotalPages = Math.max(1, Math.ceil(allFilteredScores.length / PAGE_SIZE));
  const filteredScores = allFilteredScores.slice(scannerPage * PAGE_SIZE, (scannerPage + 1) * PAGE_SIZE);

  const getBiasVariant = (bias: string) => {
    if (bias === 'Bullish') return 'success' as const;
    if (bias === 'Bearish') return 'destructive' as const;
    return 'outline' as const;
  };

  const formatScoreCell = (score: TrailBlazerScore, key: 'fundamental' | 'cot' | 'retail' | 'news' | 'technical' | 'currency') => {
    const sourceMap = { fundamental: 'FRED', cot: 'CFTC', retail: 'myfxbook', news: 'Brave/Finnhub', technical: 'TwelveData', currency: 'CurrencyStrength' } as const;
    const source = sourceMap[key];
    const hasData = key === 'retail'
      ? hasDataSource(score.dataSources, 'myfxbook') || hasDataSource(score.dataSources, 'load-myfxbook')
      : key === 'technical'
      ? hasDataSource(score.dataSources, 'YahooFinance') || hasDataSource(score.dataSources, 'TwelveData') || hasDataSource(score.dataSources, 'MarketStack') || hasDataSource(score.dataSources, 'iTick') || hasDataSource(score.dataSources, 'EODHD') || hasDataSource(score.dataSources, 'FMP') || hasDataSource(score.dataSources, 'NasdaqDataLink')
      : key === 'news'
      ? hasDataSource(score.dataSources, 'GoogleNews') || hasDataSource(score.dataSources, 'Yahoo/Finnhub/Brave') || hasDataSource(score.dataSources, 'Brave/Finnhub') || hasDataSource(score.dataSources, 'TraderNickTranscript')
      : hasDataSource(score.dataSources, source);
    if (!hasData) return <span className="text-muted-foreground">N/A</span>;
    if (key === 'currency' && (score.currencyStrengthScore == null || score.currencyStrengthScore === undefined)) return <span className="text-muted-foreground">N/A</span>;
    const val = key === 'fundamental' ? score.fundamentalScore : key === 'cot' ? score.cotScore : key === 'retail' ? score.retailSentimentScore : key === 'news' ? (score.newsSentimentScore ?? 5) : key === 'currency' ? score.currencyStrengthScore! : score.technicalScore;
    return val.toFixed(1);
  };

  const formatSetupBadge = (sig: string | null | undefined, detail?: string | null) => {
    if (!sig || sig === 'NONE') return <span className="text-muted-foreground text-xs" title={SIGNAL_TOOLTIPS['NONE']}>—</span>;
    const u = sig.toUpperCase();
    const tip = (detail && detail.trim().length > 0 ? detail : SIGNAL_TOOLTIPS[u]) ?? SIGNAL_TOOLTIPS[u] ?? '';
    const statusText = SIGNAL_STATUS_TEXT[u] ?? 'Signal active';
    const badge = (() => {
      if (u === 'BUY NOW') return <Badge className="bg-green-800 hover:bg-green-800 text-white text-xs">BUY NOW</Badge>;
      if (u === 'SELL NOW') return <Badge className="bg-red-800 hover:bg-red-800 text-white text-xs">SELL NOW</Badge>;
      if (u === 'DOUBLE_CONFLUENCE_BUY') return <Badge className="bg-sky-700 hover:bg-sky-700 text-white text-xs">DOUBLE CONF BUY</Badge>;
      if (u === 'DOUBLE_CONFLUENCE_SELL') return <Badge className="bg-purple-700 hover:bg-purple-700 text-white text-xs">DOUBLE CONF SELL</Badge>;
      if (u === 'GOLDEN_ZONE_BUY') return <Badge className="bg-amber-600 hover:bg-amber-600 text-white text-xs">GOLDEN ZONE BUY</Badge>;
      if (u === 'GOLDEN_ZONE_SELL') return <Badge className="bg-orange-600 hover:bg-orange-600 text-white text-xs">GOLDEN ZONE SELL</Badge>;
      if (u === 'HORIZONTAL_CONFLUENCE_BUY') return <Badge className="bg-lime-700 hover:bg-lime-700 text-white text-xs">HORIZONTAL BUY</Badge>;
      if (u === 'HORIZONTAL_CONFLUENCE_SELL') return <Badge className="bg-yellow-700 hover:bg-yellow-700 text-white text-xs">HORIZONTAL SELL</Badge>;
      if (u === 'TRENDLINE_CONFLUENCE_BUY') return <Badge className="bg-cyan-700 hover:bg-cyan-700 text-white text-xs">TRENDLINE BUY</Badge>;
      if (u === 'TRENDLINE_CONFLUENCE_SELL') return <Badge className="bg-fuchsia-700 hover:bg-fuchsia-700 text-white text-xs">TRENDLINE SELL</Badge>;
      if (u === 'STRONG_BUY') return <Badge className="bg-green-700 hover:bg-green-700 text-white text-xs">STRONG BUY</Badge>;
      if (u === 'STRONG_REVERSAL_BUY') return <Badge className="bg-emerald-700 hover:bg-emerald-700 text-white text-xs">STRONG REVERSAL BUY</Badge>;
      if (u === 'STRONG_REVERSAL_SELL') return <Badge variant="destructive" className="text-xs">STRONG REVERSAL SELL</Badge>;
      if (u === 'REVERSAL_BUY') return <Badge className="bg-teal-600/90 hover:bg-teal-600/90 text-white text-xs">REVERSAL BUY (50%)</Badge>;
      if (u === 'REVERSAL_SELL') return <Badge className="bg-rose-600/90 hover:bg-rose-600/90 text-white text-xs">REVERSAL SELL (50%)</Badge>;
      if (u === 'BUY') return <Badge className="bg-green-600/90 hover:bg-green-600/90 text-white text-xs">BUY</Badge>;
      if (u === 'RESISTANCE_BUY') return <Badge className="bg-lime-700 hover:bg-lime-700 text-white text-xs">RESISTANCE BUY</Badge>;
      if (u === 'TRENDLINE_BUY') return <Badge className="bg-cyan-700 hover:bg-cyan-700 text-white text-xs">TRENDLINE BUY</Badge>;
      if (u === 'STRONG_SELL') return <Badge variant="destructive" className="text-xs">STRONG SELL</Badge>;
      if (u === 'SELL') return <Badge className="bg-red-600/90 hover:bg-red-600/90 text-white text-xs">SELL</Badge>;
      if (u === 'RESISTANCE_SELL') return <Badge className="bg-orange-700 hover:bg-orange-700 text-white text-xs">RESISTANCE SELL</Badge>;
      if (u === 'TRENDLINE_SELL') return <Badge className="bg-fuchsia-700 hover:bg-fuchsia-700 text-white text-xs">TRENDLINE SELL</Badge>;
      if (u === 'WATCH') return <Badge variant="outline" className="text-amber-700 border-amber-600 text-xs">WATCH</Badge>;
      return <span className="text-xs text-muted-foreground">{sig}</span>;
    })();
    return (
      <div className="inline-flex flex-col items-center gap-1" title={tip}>
        {badge}
        <span className="text-[10px] leading-none text-muted-foreground">{statusText}</span>
      </div>
    );
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

  const signalOptions: SignalFilter[] = ['All', 'BUY NOW', 'SELL NOW', 'DOUBLE_CONFLUENCE_BUY', 'DOUBLE_CONFLUENCE_SELL', 'GOLDEN_ZONE_BUY', 'GOLDEN_ZONE_SELL', 'HORIZONTAL_CONFLUENCE_BUY', 'HORIZONTAL_CONFLUENCE_SELL', 'TRENDLINE_CONFLUENCE_BUY', 'TRENDLINE_CONFLUENCE_SELL', 'STRONG_REVERSAL_BUY', 'STRONG_REVERSAL_SELL', 'REVERSAL_BUY', 'REVERSAL_SELL', 'RESISTANCE_BUY', 'RESISTANCE_SELL', 'TRENDLINE_BUY', 'TRENDLINE_SELL', 'BUY', 'SELL', 'WATCH', 'NONE'];

  return (
    <div className="space-y-4">
      <Card className="border-primary/40">
        <CardHeader className="py-3">
          <div className="flex items-center justify-between">
            <CardTitle className="flex items-center gap-2 text-base">
              <HelpCircle className="h-4 w-4 text-primary" />
              How to read TrailBlazer data
            </CardTitle>
            <Button size="sm" variant="outline" className="text-xs h-7" onClick={() => setShowGuide(s => !s)}>
              {showGuide ? 'Hide' : 'Show'} guide
            </Button>
          </div>
        </CardHeader>
        {showGuide && (
          <CardContent className="text-sm space-y-4 leading-relaxed">
            <section>
              <h4 className="font-semibold text-foreground">1. The Overall Score (0–10)</h4>
              <p className="text-muted-foreground">
                Each instrument gets a composite <strong>0–10</strong> score blending fundamentals, COT positioning,
                retail sentiment, news, currency strength and technicals. Read it as a directional bias:
              </p>
              <ul className="list-disc ml-5 text-muted-foreground">
                <li><strong>&gt; 6</strong> → Buy bias. The higher it is (towards 10), the stronger.</li>
                <li><strong>&lt; 4</strong> → Sell bias. The lower it is (towards 0), the stronger.</li>
                <li><strong>4 – 6</strong> → Neutral / WATCH. Avoid new trades; wait for the score to clear the band.</li>
              </ul>
            </section>
            <section>
              <h4 className="font-semibold text-foreground">2. Signal tiers (strongest → weakest)</h4>
              <ul className="list-disc ml-5 text-muted-foreground space-y-1">
                <li><Badge className="bg-green-800 text-white text-xs">BUY NOW</Badge> / <Badge className="bg-red-800 text-white text-xs">SELL NOW</Badge> — Golden zone plus <strong>double confluence</strong>: both horizontal and trendline are aligned. <em>These are the clearest entry signals.</em></li>
                <li><Badge className="bg-sky-700 text-white text-xs">DOUBLE CONF BUY</Badge> / <Badge className="bg-purple-700 text-white text-xs">DOUBLE CONF SELL</Badge> — Horizontal level and trendline are both active, but price is not inside the golden zone yet.</li>
                <li><Badge className="bg-amber-600 text-white text-xs">GOLDEN ZONE BUY</Badge> / <Badge className="bg-orange-600 text-white text-xs">GOLDEN ZONE SELL</Badge> — Price is inside the 50%–61.8% golden zone, but confluence is not fully aligned yet.</li>
                <li><Badge className="bg-lime-700 text-white text-xs">HORIZONTAL BUY</Badge> / <Badge className="bg-yellow-700 text-white text-xs">HORIZONTAL SELL</Badge> — Only Daily support/resistance is active.</li>
                <li><Badge className="bg-cyan-700 text-white text-xs">TRENDLINE BUY</Badge> / <Badge className="bg-fuchsia-700 text-white text-xs">TRENDLINE SELL</Badge> — Only the 4H trendline is active.</li>
                <li><Badge className="bg-emerald-700 text-white text-xs">STRONG REVERSAL BUY</Badge> / <Badge variant="destructive" className="text-xs">STRONG REVERSAL SELL</Badge> — Price tagged the <strong>61.8%</strong> Fibonacci retrace of the last completed leg; classic deep-retrace reversal zone.</li>
                <li><Badge className="bg-teal-600/90 text-white text-xs">REVERSAL BUY (50%)</Badge> / <Badge className="bg-rose-600/90 text-white text-xs">REVERSAL SELL (50%)</Badge> — Shallower 50% Fib retrace. Weaker than 61.8% but still a tradeable zone when the score agrees.</li>
                <li><Badge className="bg-lime-700 text-white text-xs">RESISTANCE BUY</Badge> / <Badge className="bg-orange-700 text-white text-xs">RESISTANCE SELL</Badge> — Trend-continuation at a <strong>horizontal</strong> level: price bouncing off trend support (buy) or rejecting trend resistance (sell).</li>
                <li><Badge className="bg-cyan-700 text-white text-xs">TRENDLINE BUY</Badge> / <Badge className="bg-fuchsia-700 text-white text-xs">TRENDLINE SELL</Badge> — Trend-continuation at a <strong>diagonal</strong> trendline drawn through the two most-recent higher-lows (buy) or lower-highs (sell). Classic trend-follow pullback entry.</li>
                <li><Badge className="bg-green-600/90 text-white text-xs">BUY</Badge> / <Badge className="bg-red-600/90 text-white text-xs">SELL</Badge> — Directional only. The score says BUY/SELL but nothing has aligned on the chart yet; wait for a Fib or continuation trigger.</li>
                <li><Badge variant="outline" className="text-amber-700 border-amber-600 text-xs">WATCH</Badge> — Score is in the 4–6 neutral band. Monitor only.</li>
              </ul>
              <p className="text-muted-foreground mt-1">
                The small line under each badge shows the active status directly, so phone users do not need hover tooltips.
              </p>
            </section>
            <section>
              <h4 className="font-semibold text-foreground">3. Component scores</h4>
              <ul className="list-disc ml-5 text-muted-foreground space-y-1">
                <li><strong>Fundamental (FRED)</strong> — inflation, GDP, rates, PMI vs targets. Higher = more supportive macro backdrop.</li>
                <li><strong>COT (CFTC)</strong> — institutional positioning extremes. High = institutions net long; low = net short.</li>
                <li><strong>Retail (MyFXBook)</strong> — retail crowd positioning (a contrarian signal — extreme crowd longs often precede weakness).</li>
                <li><strong>News</strong> — headline sentiment from Google News, Yahoo, Brave, Finnhub, and Trader Nick transcripts.</li>
                <li><strong>Currency Strength</strong> — relative strength of the base vs quote currency from news + fundamentals.</li>
                <li><strong>Technical (Yahoo + TwelveData)</strong> — price-action signals across RSI, MACD, moving-averages, breakouts.</li>
              </ul>
              <p className="text-muted-foreground mt-1">A hole (<span className="italic">N/A</span>) means that data source wasn't available for this instrument this cycle.</p>
            </section>
            <section>
              <h4 className="font-semibold text-foreground">4. How to trade it</h4>
              <ol className="list-decimal ml-5 text-muted-foreground space-y-1">
                <li>Filter the scanner to <strong>High Conviction</strong> or <strong>Aligned Only</strong> to focus on the best setups.</li>
                <li>Prefer signals where the <strong>Overall Score</strong>, <strong>Bias</strong> and multiple <strong>component scores</strong> all point the same way.</li>
                <li>Click a row to open the <em>Score Breakdown</em> and <em>Score History</em> — confirm the score is holding (not a one-off spike).</li>
                <li>Use the setup detail text as your entry zone (the Fib or support/resistance price). Place your stop just beyond that level.</li>
                <li><strong>Risk only what you can lose</strong>. Scanner signals are inputs, not trade recommendations.</li>
              </ol>
            </section>
          </CardContent>
        )}
      </Card>

      <Card>
        <CardHeader className="flex flex-col gap-3">
          <div className="flex items-center gap-2">
            <StatusDot status="idle" />
            <CardTitle className="flex items-center gap-2">
              <Filter className="h-5 w-5" />
              Asset Scanner
            </CardTitle>
          </div>
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
            <Button
              size="sm"
              variant={alignedOnly ? 'default' : 'outline'}
              onClick={() => { setAlignedOnly(!alignedOnly); setScannerPage(0); }}
              className="text-xs h-7"
              title="Show only BUY NOW / SELL NOW (all-aligned) setups"
            >
              Aligned Only
            </Button>
            <div className="flex items-center gap-1" title="Filter by overall score range">
              <Input type="number" step="0.1" placeholder="Min" value={minScore} onChange={e => { setMinScore(e.target.value === '' ? '' : Number(e.target.value)); setScannerPage(0); }} className="h-7 w-16 text-xs" />
              <span className="text-xs text-muted-foreground">–</span>
              <Input type="number" step="0.1" placeholder="Max" value={maxScore} onChange={e => { setMaxScore(e.target.value === '' ? '' : Number(e.target.value)); setScannerPage(0); }} className="h-7 w-16 text-xs" />
            </div>
            <select
              value={biasFilter}
              onChange={e => { setBiasFilter(e.target.value as BiasFilter); setScannerPage(0); }}
              className="h-7 text-xs rounded-md border border-input bg-background px-2"
              title="Filter by bias"
            >
              {(['All', 'Bullish', 'Bearish', 'Neutral'] as BiasFilter[]).map(b => <option key={b} value={b}>{b === 'All' ? 'All bias' : b}</option>)}
            </select>
            <select
              value={signalFilter}
              onChange={e => { setSignalFilter(e.target.value as SignalFilter); setScannerPage(0); }}
              className="h-7 text-xs rounded-md border border-input bg-background px-2"
              title="Filter by setup signal"
            >
              {signalOptions.map(s => <option key={s} value={s}>{s === 'All' ? 'All signals' : s}</option>)}
            </select>
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
        </CardHeader>
        <CardContent>
          {filteredScores.length === 0 ? (
            <p className="text-center text-muted-foreground py-8">No scores available for this filter.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border select-none">
                    <th className="text-left p-2 cursor-pointer hover:text-primary" onClick={() => toggleSort('name')} title="Instrument symbol. Click to sort.">
                      Instrument {sortIcon('name')}
                    </th>
                    <th className="text-center p-2 cursor-pointer hover:text-primary" onClick={() => toggleSort('class')} title="Asset class. Click to sort.">
                      Class {sortIcon('class')}
                    </th>
                    <th className="text-center p-2 cursor-pointer hover:text-primary" onClick={() => toggleSort('score')} title="Composite 0–10 score. > 6 bullish, < 4 bearish, 4–6 neutral. Click to sort.">
                      Score {sortIcon('score')}
                    </th>
                    <th className="text-center p-2 cursor-pointer hover:text-primary" onClick={() => toggleSort('bias')} title="Overall directional bias derived from the score. Click to sort.">
                      Bias {sortIcon('bias')}
                    </th>
                    <th className="text-center p-2 cursor-pointer hover:text-primary" onClick={() => toggleSort('signal')} title="Trade setup signal. Hover a badge for details. Click to sort.">
                      Setup {sortIcon('signal')}
                    </th>
                    <th className="text-center p-2 cursor-pointer hover:text-primary" onClick={() => toggleSort('fundamental')} title="Fundamental score from FRED (inflation, GDP, rates, PMI). Click to sort.">
                      Fundamental {sortIcon('fundamental')}
                    </th>
                    <th className="text-center p-2 cursor-pointer hover:text-primary" onClick={() => toggleSort('cot')} title="CFTC Commitment of Traders positioning extremes. Click to sort.">
                      COT {sortIcon('cot')}
                    </th>
                    <th className="text-center p-2 cursor-pointer hover:text-primary" onClick={() => toggleSort('retail')} title="MyFXBook retail crowd positioning (contrarian). Click to sort.">
                      Retail {sortIcon('retail')}
                    </th>
                    <th className="text-center p-2 cursor-pointer hover:text-primary" onClick={() => toggleSort('news')} title="News sentiment (Google News / Brave / Finnhub / Trader Nick). Click to sort.">
                      News {sortIcon('news')}
                    </th>
                    <th className="text-center p-2 cursor-pointer hover:text-primary" onClick={() => toggleSort('currency')} title="Currency strength (base vs quote). Click to sort.">
                      Currency {sortIcon('currency')}
                    </th>
                    <th className="text-center p-2 cursor-pointer hover:text-primary" onClick={() => toggleSort('technical')} title="Technical score (RSI, MACD, MAs, breakouts). Click to sort.">
                      Technical {sortIcon('technical')}
                    </th>
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
                        <span
                          className={`font-bold ${
                            s.overallScore >= 7 ? 'text-green-700 dark:text-green-400' :
                            s.overallScore > 6 ? 'text-green-500' :
                            s.overallScore <= 3 ? 'text-red-700 dark:text-red-400' :
                            s.overallScore < 4 ? 'text-red-500' :
                            'text-yellow-500'
                          }`}
                          title={s.overallScore > 6 ? `Bullish bias (${s.overallScore.toFixed(2)} > 6)` : s.overallScore < 4 ? `Bearish bias (${s.overallScore.toFixed(2)} < 4)` : `Neutral (${s.overallScore.toFixed(2)} in 4–6)`}
                        >
                          {s.overallScore.toFixed(1)}
                        </span>
                      </td>
                      <td className="p-2 text-center">
                        <Badge variant={getBiasVariant(s.bias)}>{s.bias}</Badge>
                      </td>
                      <td className="p-2 text-center">{formatSetupBadge(s.tradeSetupSignal, s.tradeSetupDetail)}</td>
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
                    <ScoreBar
                      label="News Sentiment"
                      value={
                        hasDataSource(selectedInstrument.dataSources, 'GoogleNews') ||
                        hasDataSource(selectedInstrument.dataSources, 'Yahoo/Finnhub/Brave') ||
                        hasDataSource(selectedInstrument.dataSources, 'Brave/Finnhub') ||
                        hasDataSource(selectedInstrument.dataSources, 'TraderNickTranscript')
                          ? (selectedInstrument.newsSentimentScore ?? 5)
                          : undefined
                      }
                    />
                    <ScoreBar label="Currency Strength" value={hasDataSource(selectedInstrument.dataSources, 'CurrencyStrength') && selectedInstrument.currencyStrengthScore != null ? selectedInstrument.currencyStrengthScore : undefined} />
                    <ScoreBar label="Technical" value={(hasDataSource(selectedInstrument.dataSources, 'YahooFinance') || hasDataSource(selectedInstrument.dataSources, 'TwelveData') || hasDataSource(selectedInstrument.dataSources, 'MarketStack') || hasDataSource(selectedInstrument.dataSources, 'iTick') || hasDataSource(selectedInstrument.dataSources, 'EODHD') || hasDataSource(selectedInstrument.dataSources, 'FMP') || hasDataSource(selectedInstrument.dataSources, 'NasdaqDataLink')) ? selectedInstrument.technicalScore : undefined} />
                  </div>
                  {(selectedInstrument.tradeSetupSignal && selectedInstrument.tradeSetupSignal !== 'NONE') && (
                    <div className={`mt-4 rounded-lg border p-3 text-sm ${selectedInstrument.tradeSetupSignal.includes('SELL') ? 'border-red-500/50 bg-red-500/5' : selectedInstrument.tradeSetupSignal.includes('BUY') ? 'border-green-600/50 bg-green-600/5' : 'border-amber-500/50 bg-amber-500/5'}`}>
                      <div className="font-semibold mb-1 flex items-center gap-2">Setup {formatSetupBadge(selectedInstrument.tradeSetupSignal, selectedInstrument.tradeSetupDetail)}</div>
                      <p className="text-muted-foreground leading-snug">{selectedInstrument.tradeSetupDetail ?? ''}</p>
                      <p className="text-xs text-muted-foreground mt-2 italic">
                        {SIGNAL_TOOLTIPS[(selectedInstrument.tradeSetupSignal ?? '').toUpperCase()] ?? ''}
                      </p>
                    </div>
                  )}
                </CardContent>
              </Card>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
