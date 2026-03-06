import { useEffect, useState } from 'react';
import { AppLayout } from '@/components/AppLayout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select } from '@/components/ui/select';
import { BarChartComponent } from '@/components/BarChartComponent';
import { PerformanceChart } from '@/components/PerformanceChart';
import { Download, TrendingUp, TrendingDown } from 'lucide-react';
import { api } from '@/lib/api';
import { Strategy } from '@/lib/types';
import { formatCurrency } from '@/lib/utils';

interface ReportMetrics {
  totalTrades: number;
  winRate: number;
  totalProfitLoss: number;
  totalProfitLossDisplay: number;
  profitFactor: number;
  averageWin: number;
  averageWinDisplay: number;
  averageLoss: number;
  averageLossDisplay: number;
  displayCurrency: string;
  displayCurrencySymbol: string;
  monthlyPerformance: { month: string; profitLoss: number; trades: number; wins: number }[];
  strategyPerformance: { strategy: string; trades: number; profitLoss: number; winRate: number }[];
  instrumentPerformance: { instrument: string; trades: number; profitLoss: number; winRate: number }[];
}

export default function Reports() {
  const [loading, setLoading] = useState(true);
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [selectedStrategy, setSelectedStrategy] = useState('all');
  const [strategies, setStrategies] = useState<Strategy[]>([]);
  const [reportData, setReportData] = useState<ReportMetrics | null>(null);

  useEffect(() => {
    loadStrategies();
    loadReportData();
  }, []);

  const loadStrategies = async () => {
    try {
      const data = await api.getStrategies();
      setStrategies(Array.isArray(data) ? data : []);
    } catch (error) {
      console.error('Failed to load strategies:', error);
    }
  };

  const loadReportData = async () => {
    setLoading(true);
    try {
      const data: any = await api.getReportsData();
      if (data) {
        setReportData({
          totalTrades: data.totalTrades ?? 0,
          winRate: data.winRate ?? 0,
          totalProfitLoss: data.totalProfitLoss ?? 0,
          totalProfitLossDisplay: data.totalProfitLossDisplay ?? 0,
          profitFactor: data.profitFactor ?? 0,
          averageWin: data.averageWin ?? 0,
          averageWinDisplay: data.averageWinDisplay ?? 0,
          averageLoss: data.averageLoss ?? 0,
          averageLossDisplay: data.averageLossDisplay ?? 0,
          displayCurrency: data.displayCurrency ?? 'USD',
          displayCurrencySymbol: data.displayCurrencySymbol ?? '$',
          monthlyPerformance: data.monthlyPerformance ?? [],
          strategyPerformance: data.strategyPerformance ?? [],
          instrumentPerformance: data.instrumentPerformance ?? [],
        });
      }
    } catch (error) {
      console.error('Failed to load report data:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleGenerateReport = async () => {
    setLoading(true);
    try {
      const strategyId = selectedStrategy !== 'all' ? parseInt(selectedStrategy) : undefined;
      const data: any = await api.getPerformanceReport(
        startDate || undefined,
        endDate || undefined,
        strategyId
      );
      if (data && reportData) {
        setReportData({
          ...reportData,
          totalTrades: data.totalTrades ?? reportData.totalTrades,
          winRate: data.winRate ?? reportData.winRate,
          totalProfitLoss: data.totalProfitLoss ?? reportData.totalProfitLoss,
          profitFactor: data.profitFactor ?? reportData.profitFactor,
          averageWin: data.averageWin ?? reportData.averageWin,
          averageLoss: data.averageLoss ?? reportData.averageLoss,
        });
      }
    } catch (error) {
      console.error('Failed to generate report:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleExport = async () => {
    try {
      const strategyId = selectedStrategy !== 'all' ? parseInt(selectedStrategy) : undefined;
      const blob = await api.exportTrades('csv', startDate || undefined, endDate || undefined, strategyId);
      const url = window.URL.createObjectURL(new Blob([blob]));
      const a = document.createElement('a');
      a.href = url;
      a.download = `trade-report-${new Date().toISOString().slice(0, 10)}.csv`;
      a.click();
      window.URL.revokeObjectURL(url);
    } catch (error) {
      console.error('Failed to export:', error);
    }
  };

  if (loading && !reportData) {
    return (
      <AppLayout>
        <div className="flex items-center justify-center h-96">
          <div className="text-muted-foreground">Loading reports...</div>
        </div>
      </AppLayout>
    );
  }

  const sym = reportData?.displayCurrencySymbol || '$';
  const monthly = reportData?.monthlyPerformance ?? [];
  const stratPerf = reportData?.strategyPerformance ?? [];
  const instrPerf = reportData?.instrumentPerformance ?? [];

  const equityCurveData = (() => {
    if (monthly.length === 0) return [{ date: 'No Data', profitLoss: 0 }];
    let cumulative = 0;
    return monthly.map((m) => {
      cumulative += m.profitLoss;
      return { date: m.month, profitLoss: cumulative };
    });
  })();

  const strategyChartData = stratPerf.length > 0
    ? stratPerf.map((s) => ({ name: s.strategy, value: s.profitLoss }))
    : [{ name: 'No Data', value: 0 }];

  const instrumentChartData = instrPerf.length > 0
    ? instrPerf.map((i) => ({ name: i.instrument, value: i.profitLoss }))
    : [{ name: 'No Data', value: 0 }];

  return (
    <AppLayout>
      <div className="space-y-6">
        <div>
          <h1 className="text-3xl font-bold tracking-tight text-primary">Reports</h1>
          <p className="text-muted-foreground">Comprehensive trading performance analytics</p>
        </div>

        {/* Filters */}
        <Card>
          <CardHeader>
            <CardTitle>Generate Report</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-4">
              <div className="grid gap-4 md:grid-cols-3">
                <div className="space-y-2">
                  <Label htmlFor="startDate">Start Date</Label>
                  <Input
                    id="startDate"
                    type="date"
                    value={startDate}
                    onChange={(e) => setStartDate(e.target.value)}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="endDate">End Date</Label>
                  <Input
                    id="endDate"
                    type="date"
                    value={endDate}
                    onChange={(e) => setEndDate(e.target.value)}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="strategy">Strategy</Label>
                  <Select
                    id="strategy"
                    value={selectedStrategy}
                    onChange={(e) => setSelectedStrategy(e.target.value)}
                  >
                    <option value="all">All Strategies</option>
                    {strategies.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.name}
                      </option>
                    ))}
                  </Select>
                </div>
              </div>
              <div className="flex space-x-2">
                <Button onClick={handleGenerateReport} disabled={loading}>
                  {loading ? 'Loading...' : 'Generate Report'}
                </Button>
                <Button variant="outline" onClick={handleExport}>
                  <Download className="mr-2 h-4 w-4" />
                  Export CSV
                </Button>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Key Performance Metrics */}
        <Card>
          <CardHeader>
            <CardTitle>Key Performance Metrics</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
              <div className="space-y-1">
                <p className="text-sm text-muted-foreground">Total Trades</p>
                <p className="text-2xl font-bold">{reportData?.totalTrades ?? 0}</p>
              </div>
              <div className="space-y-1">
                <p className="text-sm text-muted-foreground">Win Rate</p>
                <p className="text-2xl font-bold text-success">
                  {reportData?.winRate ? reportData.winRate.toFixed(1) : '0.0'}%
                </p>
              </div>
              <div className="space-y-1">
                <p className="text-sm text-muted-foreground">Total P&L</p>
                <p className={`text-2xl font-bold ${(reportData?.totalProfitLossDisplay ?? 0) >= 0 ? 'text-success' : 'text-destructive'}`}>
                  {formatCurrency(reportData?.totalProfitLossDisplay ?? 0, sym)}
                </p>
              </div>
              <div className="space-y-1">
                <p className="text-sm text-muted-foreground">Profit Factor</p>
                <p className="text-2xl font-bold">
                  {reportData?.profitFactor ? reportData.profitFactor.toFixed(2) : '0.00'}
                </p>
              </div>
              <div className="space-y-1">
                <p className="text-sm text-muted-foreground">Average Win</p>
                <p className="text-2xl font-bold text-success">
                  {formatCurrency(reportData?.averageWinDisplay ?? 0, sym)}
                </p>
              </div>
              <div className="space-y-1">
                <p className="text-sm text-muted-foreground">Average Loss</p>
                <p className="text-2xl font-bold text-destructive">
                  {formatCurrency(reportData?.averageLossDisplay ?? 0, sym)}
                </p>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Monthly Breakdown */}
        <Card>
          <CardHeader>
            <CardTitle>Monthly Breakdown</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-4">
              {monthly.length > 0 ? (
                monthly.map((m) => (
                  <div key={m.month} className="flex items-center justify-between border-b pb-4 last:border-0">
                    <div>
                      <p className="font-semibold">{m.month}</p>
                      <p className="text-sm text-muted-foreground">
                        {m.trades} trades &middot; {m.wins} wins
                      </p>
                    </div>
                    <div className="text-right">
                      <div className="flex items-center space-x-2">
                        {m.profitLoss >= 0 ? (
                          <TrendingUp className="h-4 w-4 text-success" />
                        ) : (
                          <TrendingDown className="h-4 w-4 text-destructive" />
                        )}
                        <span className={`text-lg font-bold ${m.profitLoss >= 0 ? 'text-success' : 'text-destructive'}`}>
                          {formatCurrency(m.profitLoss, sym)}
                        </span>
                      </div>
                    </div>
                  </div>
                ))
              ) : (
                <div className="text-center text-muted-foreground py-8">
                  No monthly data available
                </div>
              )}
            </div>
          </CardContent>
        </Card>

        {/* Performance Charts */}
        <div className="grid gap-6 md:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle>Equity Curve</CardTitle>
            </CardHeader>
            <CardContent>
              <div className="h-64">
                <PerformanceChart data={equityCurveData} currencySymbol={sym} />
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Strategy Performance</CardTitle>
            </CardHeader>
            <CardContent>
              <div className="h-64">
                <BarChartComponent data={strategyChartData} currencySymbol={sym} title="Strategy P&L" />
              </div>
            </CardContent>
          </Card>
        </div>

        {/* Instrument Performance */}
        <Card>
          <CardHeader>
            <CardTitle>Instrument Performance</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="h-64">
              <BarChartComponent data={instrumentChartData} currencySymbol={sym} title="Instrument P&L" />
            </div>
          </CardContent>
        </Card>

        {/* Trading Insights */}
        <Card>
          <CardHeader>
            <CardTitle>Trading Insights</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-4">
              {(reportData?.winRate ?? 0) > 0 && (
                <div className={`rounded-lg p-4 border ${(reportData?.winRate ?? 0) >= 50 ? 'bg-success/10 border-success/20' : 'bg-destructive/10 border-destructive/20'}`}>
                  <p className={`font-semibold mb-2 ${(reportData?.winRate ?? 0) >= 50 ? 'text-success' : 'text-destructive'}`}>
                    Win Rate: {reportData?.winRate?.toFixed(1)}%
                  </p>
                  <p className="text-sm text-muted-foreground">
                    {(reportData?.winRate ?? 0) >= 50
                      ? 'Your win rate is above average. Continue focusing on high-probability setups.'
                      : 'Your win rate is below 50%. Consider reviewing entry criteria and risk management.'}
                  </p>
                </div>
              )}
              {stratPerf.length > 0 && (
                <div className="rounded-lg bg-blue-500/10 p-4 border border-blue-500/20">
                  <p className="font-semibold text-blue-600 mb-2">
                    Best Strategy: {stratPerf.sort((a, b) => b.profitLoss - a.profitLoss)[0]?.strategy}
                  </p>
                  <p className="text-sm text-muted-foreground">
                    {formatCurrency(stratPerf.sort((a, b) => b.profitLoss - a.profitLoss)[0]?.profitLoss ?? 0, sym)} total P&L
                    with {stratPerf.sort((a, b) => b.profitLoss - a.profitLoss)[0]?.winRate?.toFixed(0)}% win rate.
                  </p>
                </div>
              )}
              {instrPerf.length > 0 && (
                <div className="rounded-lg bg-yellow-500/10 p-4 border border-yellow-500/20">
                  <p className="font-semibold text-yellow-600 mb-2">
                    Top Instrument: {instrPerf.sort((a, b) => b.profitLoss - a.profitLoss)[0]?.instrument}
                  </p>
                  <p className="text-sm text-muted-foreground">
                    {formatCurrency(instrPerf.sort((a, b) => b.profitLoss - a.profitLoss)[0]?.profitLoss ?? 0, sym)} total P&L
                    across {instrPerf.sort((a, b) => b.profitLoss - a.profitLoss)[0]?.trades} trades.
                  </p>
                </div>
              )}
              {(reportData?.totalTrades ?? 0) === 0 && (
                <div className="text-center text-muted-foreground py-4">
                  Import trades to see insights here.
                </div>
              )}
            </div>
          </CardContent>
        </Card>
      </div>
    </AppLayout>
  );
}
