import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Legend, Cell } from 'recharts';

interface BarChartComponentProps {
  data: Array<{
    name: string;
    value: number;
  }>;
  currencySymbol?: string;
  title?: string;
}

export function BarChartComponent({ data, currencySymbol = '$', title }: BarChartComponentProps) {
  const CustomTooltip = ({ active, payload }: any) => {
    if (active && payload && payload.length) {
      const value = payload[0].value;
      return (
        <div className="bg-card border rounded-lg p-3 shadow-lg">
          <p className="text-sm font-medium">{payload[0].payload.name}</p>
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
      <BarChart data={data} margin={{ top: 5, right: 30, left: 20, bottom: 5 }}>
        <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
        <XAxis
          dataKey="name"
          className="text-xs"
          tick={{ fill: 'hsl(var(--muted-foreground))' }}
          angle={-45}
          textAnchor="end"
          height={80}
        />
        <YAxis
          className="text-xs"
          tick={{ fill: 'hsl(var(--muted-foreground))' }}
          tickFormatter={(value) => `${currencySymbol}${value}`}
        />
        <Tooltip content={<CustomTooltip />} />
        <Legend />
        <Bar dataKey="value" name={title || 'Value'} radius={[4, 4, 0, 0]}>
          {data.map((entry, index) => (
            <Cell
              key={`cell-${index}`}
              fill={entry.value >= 0 ? 'hsl(var(--success))' : 'hsl(var(--destructive))'}
            />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  );
}







