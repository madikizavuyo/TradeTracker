import { useState, useEffect } from 'react';
import { useNavigate, useParams, Link } from 'react-router-dom';
import { AppLayout } from '@/components/AppLayout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select } from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import { ArrowLeft } from 'lucide-react';
import { api } from '@/lib/api';
import { Trade, Strategy } from '@/lib/types';

export default function TradeForm() {
  const navigate = useNavigate();
  const { id } = useParams();
  const isEdit = !!id;

  const [formData, setFormData] = useState<Partial<Trade>>({
    instrument: '',
    type: 'Long',
    broker: '',
    strategyId: undefined,
    entryPrice: 0,
    exitPrice: 0,
    stopLoss: 0,
    takeProfit: 0,
    lotSize: 0,
    dateTime: '',
    exitDateTime: '',
    status: 'Open',
    notes: '',
    currency: 'USD',
  });

  const [entryImages, setEntryImages] = useState<File[]>([]);
  const [exitImages, setExitImages] = useState<File[]>([]);
  const [loading, setLoading] = useState(false);
  const [strategies, setStrategies] = useState<Strategy[]>([]);

  useEffect(() => {
    loadStrategies();
    if (isEdit) {
      loadTrade();
    }
  }, [id]);

  const loadStrategies = async () => {
    try {
      const response = await api.getStrategies();
      setStrategies(response.data || response || []);
    } catch (error) {
      console.error('Failed to load strategies:', error);
    }
  };

  const loadTrade = async () => {
    try {
      if (id) {
        const trade = await api.getTradeDetails(parseInt(id));
        setFormData(trade);
      }
    } catch (error) {
      console.error('Failed to load trade:', error);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);

    try {
      if (isEdit && id) {
        await api.updateTrade(parseInt(id), formData, entryImages, exitImages);
      } else {
        await api.createTrade(formData, entryImages, exitImages);
      }
      navigate('/trades');
    } catch (error) {
      console.error('Failed to save trade:', error);
      alert('Failed to save trade. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  const handleInputChange = (field: keyof Trade, value: any) => {
    setFormData((prev) => ({ ...prev, [field]: value }));
  };

  return (
    <AppLayout>
      <div className="space-y-6">
        {/* Header */}
        <div className="flex items-center space-x-4">
          <Link to="/trades">
            <Button variant="ghost" size="icon">
              <ArrowLeft className="h-5 w-5" />
            </Button>
          </Link>
          <div>
            <h1 className="text-3xl font-bold tracking-tight text-primary">
              {isEdit ? 'Edit Trade' : 'Add New Trade'}
            </h1>
            <p className="text-muted-foreground">
              {isEdit ? 'Update trade information' : 'Enter trade details below'}
            </p>
          </div>
        </div>

        <form onSubmit={handleSubmit} className="space-y-6">
          {/* Basic Information */}
          <Card>
            <CardHeader>
              <CardTitle>Basic Information</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid gap-4 md:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="instrument">
                    Instrument <span className="text-destructive">*</span>
                  </Label>
                  <Input
                    id="instrument"
                    value={formData.instrument}
                    onChange={(e) => handleInputChange('instrument', e.target.value)}
                    placeholder="e.g., EURUSD"
                    required
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="type">
                    Type <span className="text-destructive">*</span>
                  </Label>
                  <Select
                    id="type"
                    value={formData.type}
                    onChange={(e) => handleInputChange('type', e.target.value)}
                    required
                  >
                    <option value="Long">Long</option>
                    <option value="Short">Short</option>
                  </Select>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="broker">Broker</Label>
                  <Input
                    id="broker"
                    value={formData.broker}
                    onChange={(e) => handleInputChange('broker', e.target.value)}
                    placeholder="e.g., XM"
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="lotSize">Lot Size</Label>
                  <Input
                    id="lotSize"
                    type="number"
                    step="0.01"
                    value={formData.lotSize}
                    onChange={(e) => handleInputChange('lotSize', parseFloat(e.target.value))}
                    placeholder="0.00"
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="strategyId">Strategy</Label>
                  <Select
                    id="strategyId"
                    value={formData.strategyId || ''}
                    onChange={(e) => handleInputChange('strategyId', e.target.value ? parseInt(e.target.value) : undefined)}
                  >
                    <option value="">No Strategy</option>
                    {strategies.map((strategy) => (
                      <option key={strategy.id} value={strategy.id}>
                        {strategy.name}
                      </option>
                    ))}
                  </Select>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="currency">Currency</Label>
                  <Select
                    id="currency"
                    value={formData.currency}
                    onChange={(e) => handleInputChange('currency', e.target.value)}
                  >
                    <option value="USD">USD - US Dollar</option>
                    <option value="EUR">EUR - Euro</option>
                    <option value="GBP">GBP - British Pound</option>
                    <option value="JPY">JPY - Japanese Yen</option>
                    <option value="ZAR">ZAR - South African Rand</option>
                    <option value="AUD">AUD - Australian Dollar</option>
                    <option value="CAD">CAD - Canadian Dollar</option>
                    <option value="CHF">CHF - Swiss Franc</option>
                  </Select>
                </div>
              </div>
            </CardContent>
          </Card>

          {/* Entry Details */}
          <Card>
            <CardHeader>
              <CardTitle>Entry Details</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid gap-4 md:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="entryPrice">
                    Entry Price <span className="text-destructive">*</span>
                  </Label>
                  <Input
                    id="entryPrice"
                    type="number"
                    step="0.00001"
                    value={formData.entryPrice}
                    onChange={(e) => handleInputChange('entryPrice', parseFloat(e.target.value))}
                    placeholder="0.00000"
                    required
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="dateTime">
                    Entry Date/Time <span className="text-destructive">*</span>
                  </Label>
                  <Input
                    id="dateTime"
                    type="datetime-local"
                    value={formData.dateTime ? new Date(formData.dateTime).toISOString().slice(0, 16) : ''}
                    onChange={(e) => handleInputChange('dateTime', e.target.value)}
                    required
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="stopLoss">Stop Loss</Label>
                  <Input
                    id="stopLoss"
                    type="number"
                    step="0.00001"
                    value={formData.stopLoss}
                    onChange={(e) => handleInputChange('stopLoss', parseFloat(e.target.value))}
                    placeholder="0.00000"
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="takeProfit">Take Profit</Label>
                  <Input
                    id="takeProfit"
                    type="number"
                    step="0.00001"
                    value={formData.takeProfit}
                    onChange={(e) => handleInputChange('takeProfit', parseFloat(e.target.value))}
                    placeholder="0.00000"
                  />
                </div>
              </div>

              <div className="space-y-2">
                <Label htmlFor="entryImages">Entry Images</Label>
                <Input
                  id="entryImages"
                  type="file"
                  accept="image/*"
                  multiple
                  onChange={(e) => setEntryImages(Array.from(e.target.files || []))}
                />
              </div>
            </CardContent>
          </Card>

          {/* Exit Details */}
          <Card>
            <CardHeader>
              <CardTitle>Exit Details</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid gap-4 md:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="exitPrice">Exit Price</Label>
                  <Input
                    id="exitPrice"
                    type="number"
                    step="0.00001"
                    value={formData.exitPrice}
                    onChange={(e) => handleInputChange('exitPrice', parseFloat(e.target.value))}
                    placeholder="0.00000"
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="exitDateTime">Exit Date/Time</Label>
                  <Input
                    id="exitDateTime"
                    type="datetime-local"
                    value={formData.exitDateTime ? new Date(formData.exitDateTime).toISOString().slice(0, 16) : ''}
                    onChange={(e) => handleInputChange('exitDateTime', e.target.value)}
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="status">
                    Status <span className="text-destructive">*</span>
                  </Label>
                  <Select
                    id="status"
                    value={formData.status}
                    onChange={(e) => handleInputChange('status', e.target.value)}
                    required
                  >
                    <option value="Open">Open</option>
                    <option value="Closed">Closed</option>
                    <option value="Cancelled">Cancelled</option>
                  </Select>
                </div>
              </div>

              <div className="space-y-2">
                <Label htmlFor="exitImages">Exit Images</Label>
                <Input
                  id="exitImages"
                  type="file"
                  accept="image/*"
                  multiple
                  onChange={(e) => setExitImages(Array.from(e.target.files || []))}
                />
              </div>
            </CardContent>
          </Card>

          {/* Notes */}
          <Card>
            <CardHeader>
              <CardTitle>Notes</CardTitle>
            </CardHeader>
            <CardContent>
              <div className="space-y-2">
                <Label htmlFor="notes">Trading Notes</Label>
                <Textarea
                  id="notes"
                  value={formData.notes}
                  onChange={(e) => handleInputChange('notes', e.target.value)}
                  placeholder="Add any relevant notes about this trade..."
                  rows={4}
                />
              </div>
            </CardContent>
          </Card>

          {/* Actions */}
          <div className="flex justify-end space-x-4">
            <Link to="/trades">
              <Button type="button" variant="outline">
                Cancel
              </Button>
            </Link>
            <Button type="submit" disabled={loading}>
              {loading ? 'Saving...' : isEdit ? 'Update Trade' : 'Save Trade'}
            </Button>
          </div>
        </form>
      </div>
    </AppLayout>
  );
}


