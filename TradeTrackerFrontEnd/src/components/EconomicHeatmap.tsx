import { HeatmapEntry } from '@/lib/types';

interface EconomicHeatmapProps {
  data: HeatmapEntry[];
}

const CURRENCIES = ['USD', 'EUR', 'GBP', 'JPY', 'AUD', 'NZD', 'CAD', 'CHF', 'SEK', 'ZAR', 'CNY'];
const INDICATORS = ['GDP', 'CPI', 'Unemployment', 'InterestRate', 'PMI'];

function getCellColor(impact: string) {
  switch (impact) {
    case 'Positive': return 'bg-green-500/20 text-green-400 border-green-500/30';
    case 'Negative': return 'bg-red-500/20 text-red-400 border-red-500/30';
    default: return 'bg-yellow-500/10 text-yellow-400 border-yellow-500/20';
  }
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

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr>
            <th className="text-left p-2 text-muted-foreground font-medium">Currency</th>
            {INDICATORS.map(ind => (
              <th key={ind} className="p-2 text-center text-muted-foreground font-medium">{ind}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {CURRENCIES.map(currency => (
            <tr key={currency} className="border-t border-border/50">
              <td className="p-2 font-semibold">{currency}</td>
              {INDICATORS.map(indicator => {
                const entry = getValue(currency, indicator);
                const cellClass = entry ? getCellColor(entry.impact) : 'bg-muted/20 text-muted-foreground';
                const displayValue = entry
                  ? (indicator === 'GDP' || indicator === 'CPI')
                    ? `${entry.value.toFixed(1)}%`
                    : entry.value.toFixed(1)
                  : '—';
                return (
                  <td key={indicator} className="p-1">
                    <div className={`rounded px-2 py-1.5 text-center text-xs font-medium border ${cellClass}`}>
                      {displayValue}
                    </div>
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
