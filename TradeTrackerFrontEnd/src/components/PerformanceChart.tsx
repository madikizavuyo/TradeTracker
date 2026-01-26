import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Legend } from 'recharts';

interface PerformanceChartProps {
  data: Array<{
    date: string;
    profitLoss: number;
  }>;
  currencySymbol?: string;
}

export function PerformanceChart({ data, currencySymbol = '$' }: PerformanceChartProps) {
  const CustomTooltip = ({ active, payload }: any) => {
    if (active && payload && payload.length) {
      const value = payload[0].value;
      return (
        <div className="bg-card border rounded-lg p-3 shadow-lg">
          <p className="text-sm text-muted-foreground">{payload[0].payload.date}</p>
          <p className={`text-lg font-bold ${value >= 0 ? 'text-success' : 'text-destructive'}`}>
            {value >= 0 ? '+' : ''}{currencySymbol}{value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
          </p>
        </div>
      );
    }
    return null;
  };

  return (
    <ResponsiveContainer width="100%" height="100%">
      <LineChart data={data} margin={{ top: 5, right: 30, left: 20, bottom: 5 }}>
        <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
        <XAxis
          dataKey="date"
          className="text-xs"
          tick={{ fill: 'hsl(var(--muted-foreground))' }}
        />
        <YAxis
          className="text-xs"
          tick={{ fill: 'hsl(var(--muted-foreground))' }}
          tickFormatter={(value) => `${currencySymbol}${value}`}
        />
        <Tooltip content={<CustomTooltip />} />
        <Legend />
        <Line
          type="monotone"
          dataKey="profitLoss"
          stroke="hsl(var(--primary))"
          strokeWidth={2}
          dot={{ fill: 'hsl(var(--primary))', r: 4 }}
          activeDot={{ r: 6 }}
          name="Profit & Loss"
        />
      </LineChart>
    </ResponsiveContainer>
  );
}







