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
  if (indicator === 'JoblessClaims') return `${Math.round(v)}K`;
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
