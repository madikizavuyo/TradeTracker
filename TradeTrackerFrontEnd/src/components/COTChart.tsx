import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer, Cell } from 'recharts';
import { COTData } from '@/lib/types';

interface COTChartProps {
  data: COTData[];
}

/** Normalize COT item - API may return camelCase or PascalCase */
function normalizeCOT(d: Record<string, unknown>): COTData {
  const get = (camel: string, pascal: string) =>
    (d[camel] ?? d[pascal]) as number | string;
  return {
    symbol: String(get('symbol', 'Symbol') ?? ''),
    commercialLong: Number(get('commercialLong', 'CommercialLong') ?? 0),
    commercialShort: Number(get('commercialShort', 'CommercialShort') ?? 0),
    nonCommercialLong: Number(get('nonCommercialLong', 'NonCommercialLong') ?? 0),
    nonCommercialShort: Number(get('nonCommercialShort', 'NonCommercialShort') ?? 0),
    openInterest: Number(get('openInterest', 'OpenInterest') ?? 0),
    netNonCommercial: Number(get('netNonCommercial', 'NetNonCommercial') ?? 0),
    reportDate: String(get('reportDate', 'ReportDate') ?? ''),
  };
}

export function COTChart({ data }: COTChartProps) {
  const normalized = Array.isArray(data) ? data.map(d => normalizeCOT(d as Record<string, unknown>)) : [];
  const valid = normalized.filter(d => d.symbol && (d.nonCommercialLong > 0 || d.nonCommercialShort > 0));

  if (valid.length === 0) {
    return (
      <div className="flex items-center justify-center h-64 text-muted-foreground">
        No COT data available. Click &quot;Scrape COT&quot; to fetch from CFTC, or run a TrailBlazer refresh.
      </div>
    );
  }

  const chartData = valid.map(d => ({
    symbol: d.symbol,
    long: d.nonCommercialLong,
    short: -d.nonCommercialShort,
    net: d.netNonCommercial,
  }));

  return (
    <div className="space-y-4">
      <details className="rounded-lg border border-border bg-muted/20 px-3 py-2 text-sm">
        <summary className="cursor-pointer font-medium text-foreground select-none">How to read this chart</summary>
        <ul className="mt-2 space-y-1.5 text-muted-foreground list-disc pl-5 text-xs sm:text-sm">
          <li>
            Data is from the <strong>CFTC Commitments of Traders</strong> report (weekly). Bars show <strong>non-commercial</strong> (“large speculator”) <strong>long</strong> contracts (green), <strong>short</strong> contracts (red, plotted downward), and <strong>net</strong> long minus short (blue when net long, amber when net short).
          </li>
          <li>
            <strong>Net positioning</strong> shows whether specs are leaning long or short in futures. Extreme nets can flag a <strong>crowded</strong> trade, but they are not bullish or bearish by themselves—you still need trend, fundamentals, and risk management.
          </li>
          <li>
            <strong>Commercial</strong> hedgers are not plotted here; this view focuses on the positioning most often discussed for “hot money” flow.
          </li>
        </ul>
      </details>
    <ResponsiveContainer width="100%" height={350}>
      <BarChart data={chartData} margin={{ top: 10, right: 30, left: 0, bottom: 5 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" />
        <XAxis
          dataKey="symbol"
          tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 11 }}
          axisLine={{ stroke: 'hsl(var(--border))' }}
        />
        <YAxis
          tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 11 }}
          axisLine={{ stroke: 'hsl(var(--border))' }}
          tickFormatter={(v: number) => `${(v / 1000).toFixed(0)}k`}
        />
        <Tooltip
          contentStyle={{
            backgroundColor: 'hsl(var(--card))',
            border: '1px solid hsl(var(--border))',
            borderRadius: '8px',
            padding: '12px',
          }}
          labelStyle={{ color: 'hsl(var(--foreground))' }}
          formatter={(value: number, name: string) => [
            value.toLocaleString(),
            name === 'long' ? 'Non-Commercial Long' : name === 'short' ? 'Non-Commercial Short' : 'Net Position'
          ]}
        />
        <Legend
          wrapperStyle={{ color: 'hsl(var(--muted-foreground))' }}
          formatter={(value: string) => value === 'long' ? 'Long' : value === 'short' ? 'Short' : 'Net'}
        />
        <Bar dataKey="long" fill="#22c55e" radius={[4, 4, 0, 0]} />
        <Bar dataKey="short" radius={[0, 0, 4, 4]}>
          {chartData.map((_, index) => (
            <Cell key={index} fill="#ef4444" />
          ))}
        </Bar>
        <Bar dataKey="net" radius={[4, 4, 4, 4]}>
          {chartData.map((entry, index) => (
            <Cell key={index} fill={entry.net >= 0 ? '#3b82f6' : '#f59e0b'} />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
    </div>
  );
}
