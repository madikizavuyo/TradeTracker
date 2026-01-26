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
import { formatCurrency } from '@/lib/utils';

export default function Reports() {
  const [loading, setLoading] = useState(true);
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [selectedStrategy, setSelectedStrategy] = useState('all');

  const [reportData, setReportData] = useState({
    totalTrades: 42,
    winRate: 66.67,
    totalProfitLoss: 100284.0,
    profitFactor: 1.94,
    averageWin: 6475.0,
    averageLoss: -3330.0,
    displayCurrencySymbol: 'R',
  });

  useEffect(() => {
    loadReportData();
  }, []);

  const loadReportData = async () => {
    try {
      const data = await api.getReportsData();
      if (data) {
        setReportData({
          totalTrades: data.totalTrades,
          winRate: data.winRate,
          totalProfitLoss: data.totalProfitLossDisplay,
          profitFactor: data.profitFactor,
          averageWin: data.averageWinDisplay,
          averageLoss: data.averageLossDisplay,
          displayCurrencySymbol: data.displayCurrencySymbol,
        });
      }
    } catch (error) {
      console.error('Failed to load report data:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleGenerateReport = () => {
    console.log('Generating report with filters:', { startDate, endDate, selectedStrategy });
    // In real implementation, this would call the API with filters
  };

  if (loading) {
    return (
      <AppLayout>
        <div className="flex items-center justify-center h-96">
          <div className="text-muted-foreground">Loading reports...</div>
        </div>
      </AppLayout>
    );
  }

  const monthlyData = [
    { month: 'January', profitLoss: 12580.0, trades: 8 },
    { month: 'February', profitLoss: 8420.0, trades: 6 },
    { month: 'March', profitLoss: -3250.0, trades: 5 },
    { month: 'April', profitLoss: 15320.0, trades: 9 },
    { month: 'May', profitLoss: 9840.0, trades: 7 },
    { month: 'June', profitLoss: 18650.0, trades: 7 },
  ];

  return (
    <AppLayout>
      <div className="space-y-6">
        {/* Header */}
        <div>
          <h1 className="text-3xl font-bold tracking-tight text-primary">Reports</h1>
          <p className="text-muted-foreground">Comprehensive trading performance analytics</p>
        </div>

        {/* Report Generation Controls */}
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
                    <option value="1">Trend Following</option>
                    <option value="2">Breakout</option>
                    <option value="3">Range Trading</option>
                  </Select>
                </div>
              </div>
              <div className="flex space-x-2">
                <Button onClick={handleGenerateReport}>Generate Report</Button>
                <Button variant="outline">
                  <Download className="mr-2 h-4 w-4" />
                  Export PDF
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
            <div className="space-y-4">
              <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
                <div className="space-y-1">
                  <p className="text-sm text-muted-foreground">Total Trades</p>
                  <p className="text-2xl font-bold">{reportData.totalTrades}</p>
                </div>
                <div className="space-y-1">
                  <p className="text-sm text-muted-foreground">Win Rate</p>
                  <p className="text-2xl font-bold text-success">{reportData?.winRate ? reportData.winRate.toFixed(1) : '0.0'}%</p>
                </div>
                <div className="space-y-1">
                  <p className="text-sm text-muted-foreground">Total P&L</p>
                  <p
                    className={`text-2xl font-bold ${
                      reportData.totalProfitLoss >= 0 ? 'text-success' : 'text-destructive'
                    }`}
                  >
                    {formatCurrency(reportData.totalProfitLoss, reportData.displayCurrencySymbol)}
                  </p>
                </div>
                <div className="space-y-1">
                  <p className="text-sm text-muted-foreground">Profit Factor</p>
                  <p className="text-2xl font-bold">{reportData?.profitFactor ? reportData.profitFactor.toFixed(2) : '0.00'}</p>
                </div>
                <div className="space-y-1">
                  <p className="text-sm text-muted-foreground">Average Win</p>
                  <p className="text-2xl font-bold text-success">
                    {formatCurrency(reportData.averageWin, reportData.displayCurrencySymbol)}
                  </p>
                </div>
                <div className="space-y-1">
                  <p className="text-sm text-muted-foreground">Average Loss</p>
                  <p className="text-2xl font-bold text-destructive">
                    {formatCurrency(reportData.averageLoss, reportData.displayCurrencySymbol)}
                  </p>
                </div>
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
              {monthlyData.map((month) => (
                <div key={month.month} className="flex items-center justify-between border-b pb-4 last:border-0">
                  <div>
                    <p className="font-semibold">{month.month}</p>
                    <p className="text-sm text-muted-foreground">{month.trades} trades</p>
                  </div>
                  <div className="text-right">
                    <div className="flex items-center space-x-2">
                      {month.profitLoss >= 0 ? (
                        <TrendingUp className="h-4 w-4 text-success" />
                      ) : (
                        <TrendingDown className="h-4 w-4 text-destructive" />
                      )}
                      <span
                        className={`text-lg font-bold ${
                          month.profitLoss >= 0 ? 'text-success' : 'text-destructive'
                        }`}
                      >
                        {formatCurrency(month.profitLoss, reportData.displayCurrencySymbol)}
                      </span>
                    </div>
                  </div>
                </div>
              ))}
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
                <PerformanceChart
                  data={[
                    { date: 'Jan', profitLoss: 0 },
                    { date: 'Feb', profitLoss: 21275 },
                    { date: 'Mar', profitLoss: 37400 },
                    { date: 'Apr', profitLoss: 61925 },
                    { date: 'May', profitLoss: 80775 },
                    { date: 'Jun', profitLoss: 100284 },
                  ]}
                  currencySymbol={reportData.displayCurrencySymbol}
                />
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Strategy Performance</CardTitle>
            </CardHeader>
            <CardContent>
              <div className="h-64">
                <BarChartComponent
                  data={[
                    { name: 'Trend', value: 38850 },
                    { name: 'Breakout', value: 25125 },
                    { name: 'Range', value: 31559 },
                    { name: 'Momentum', value: 4750 },
                  ]}
                  currencySymbol={reportData.displayCurrencySymbol}
                  title="Strategy P&L"
                />
              </div>
            </CardContent>
          </Card>
        </div>

        {/* Trading Insights */}
        <Card>
          <CardHeader>
            <CardTitle>Trading Insights</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-4">
              <div className="rounded-lg bg-success/10 p-4 border border-success/20">
                <p className="font-semibold text-success mb-2">Strength: Consistent Win Rate</p>
                <p className="text-sm text-muted-foreground">
                  Your win rate of {reportData?.winRate ? reportData.winRate.toFixed(1) : '0.0'}% is above average. Continue focusing on
                  high-probability setups.
                </p>
              </div>
              <div className="rounded-lg bg-blue-500/10 p-4 border border-blue-500/20">
                <p className="font-semibold text-blue-600 mb-2">Observation: Best Performing Strategy</p>
                <p className="text-sm text-muted-foreground">
                  Trend Following strategy shows the highest profit factor. Consider allocating more capital to this
                  approach.
                </p>
              </div>
              <div className="rounded-lg bg-yellow-500/10 p-4 border border-yellow-500/20">
                <p className="font-semibold text-yellow-600 mb-2">Recommendation: Risk Management</p>
                <p className="text-sm text-muted-foreground">
                  Average loss is manageable, but consider tightening stop losses on momentum trades to improve risk-reward ratio.
                </p>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>
    </AppLayout>
  );
}

