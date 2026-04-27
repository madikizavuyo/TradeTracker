import { HeatmapEntry } from '@/lib/types';

interface EconomicHeatmapProps {
  data: HeatmapEntry[];
}

const CURRENCIES = ['USD', 'EUR', 'GBP', 'JPY', 'AUD', 'NZD', 'CAD', 'CHF', 'SEK', 'ZAR', 'CNY'];
/** Shown for every currency (FRED-backed per row in API). */
const CORE_INDICATORS = ['GDP', 'CPI', 'Unemployment', 'InterestRate', 'PMI'] as const;
/** US-only series in API; separate block so other currencies are not shown as empty columns. */
const US_ONLY_INDICATORS = ['Treasury10Y', 'DollarIndex', 'PCE', 'JOLTs', 'JoblessClaims'] as const;

function getCellColor(impact: string) {
  switch (impact) {
    case 'Positive': return 'bg-green-500/20 text-green-400 border-green-500/30';
    case 'Negative': return 'bg-red-500/20 text-red-400 border-red-500/30';
    default: return 'bg-yellow-500/10 text-yellow-400 border-yellow-500/20';
  }
}

function formatHeatmapCell(indicator: string, entry: HeatmapEntry) {
  const v = entry.value;
  if (indicator === 'GDP' || indicator === 'CPI' || indicator === 'PCE') return `${v.toFixed(1)}%`;
  if (indicator === 'Treasury10Y') return `${v.toFixed(2)}%`;
  if (indicator === 'JOLTs') return `${(v / 1000).toFixed(1)}M`;
  // ICSA (FRED): units are thousands of persons — e.g. 219 → 219K. Raw weekly counts are much larger.
  if (indicator === 'JoblessClaims') {
    if (v >= 10_000) return `${(v / 1000).toFixed(0)}K`;
    return `${v.toFixed(0)}K`;
  }
  if (indicator === 'DollarIndex') return v.toFixed(1);
  return v.toFixed(1);
}

export function EconomicHeatmap({ data }: EconomicHeatmapProps) {
  const getValue = (currency: string, indicator: string) => {
    return data.find(d => d.currency === currency && d.indicator === indicator);
  };

  if (data.length === 0) {
    return (
      <div className="flex items-center justify-center h-40 text-muted-foreground">
        No heatmap data available. Run a refresh to fetch economic data.
      </div>
    );
  }

  const renderRow = (currency: string, indicators: readonly string[]) => (
    <tr key={currency} className="border-t border-border/50">
      <td className="p-2 font-semibold">{currency}</td>
      {indicators.map(indicator => {
        const entry = getValue(currency, indicator);
        const cellClass = entry ? getCellColor(entry.impact) : 'bg-muted/20 text-muted-foreground';
        const displayValue = entry ? formatHeatmapCell(indicator, entry) : '—';
        return (
          <td key={indicator} className="p-1">
            <div className={`rounded px-2 py-1.5 text-center text-xs font-medium border ${cellClass}`}>
              {displayValue}
            </div>
          </td>
        );
      })}
    </tr>
  );

  return (
    <div className="space-y-6 overflow-x-auto">
      <details className="rounded-lg border border-border bg-muted/20 px-3 py-2 text-sm">
        <summary className="cursor-pointer font-medium text-foreground select-none">How to read this heatmap</summary>
        <ul className="mt-2 space-y-1.5 text-muted-foreground list-disc pl-5 text-xs sm:text-sm">
          <li>
            Each cell is the <strong>latest value</strong> we have for that <strong>currency row</strong> and <strong>indicator column</strong> (mostly FRED-backed macro data used in TrailBlazer fundamentals).
          </li>
          <li>
            <span className="text-green-400 font-medium">Green</span> means the recent print is scored as <strong>supportive</strong> for that currency’s fundamental picture;{' '}
            <span className="text-red-400 font-medium">red</span> as a <strong>headwind</strong>; <span className="text-yellow-400 font-medium">amber</span> is <strong>neutral or mixed</strong>.
          </li>
          <li>
            Read <strong>across a row</strong> to see the broad macro mix for one currency (growth, inflation, labour, rates). The second table is <strong>USD-only</strong> series (Treasury yields, dollar index, PCE, job openings, jobless claims).
          </li>
          <li>
            This is a <strong>snapshot</strong>, not a trade signal: use it as context next to price, policy, and your own thesis.
          </li>
        </ul>
      </details>
      <div>
        <p className="text-xs text-muted-foreground mb-2">Cross-currency economic indicators</p>
        <table className="w-full text-sm">
          <thead>
            <tr>
              <th className="text-left p-2 text-muted-foreground font-medium">Currency</th>
              {CORE_INDICATORS.map(ind => (
                <th key={ind} className="p-2 text-center text-muted-foreground font-medium">{ind}</th>
              ))}
            </tr>
          </thead>
          <tbody>{CURRENCIES.map(c => renderRow(c, CORE_INDICATORS))}</tbody>
        </table>
      </div>
      <div>
        <p className="text-xs text-muted-foreground mb-2">United States — rates, dollar, inflation & labor (USD row only)</p>
        <table className="w-full text-sm">
          <thead>
            <tr>
              <th className="text-left p-2 text-muted-foreground font-medium">Currency</th>
              {US_ONLY_INDICATORS.map(ind => (
                <th key={ind} className="p-2 text-center text-muted-foreground font-medium">{ind}</th>
              ))}
            </tr>
          </thead>
          <tbody>{renderRow('USD', US_ONLY_INDICATORS)}</tbody>
        </table>
      </div>
    </div>
  );
}
