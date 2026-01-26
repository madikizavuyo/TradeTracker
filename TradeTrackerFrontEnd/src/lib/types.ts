export interface Trade {
  id: number;
  userId?: string;
  strategyId?: number;
  instrument: string;
  entryPrice: number;
  exitPrice?: number;
  stopLoss?: number;
  takeProfit?: number;
  profitLoss?: number;
  profitLossDisplay?: number;
  riskReward?: number;
  dateTime: string;
  exitDateTime?: string;
  notes?: string;
  status: 'Open' | 'Closed' | 'Cancelled';
  type: 'Long' | 'Short';
  lotSize?: number;
  broker?: string;
  currency: string;
  displayCurrency?: string;
  displayCurrencySymbol?: string;
  createdAt?: string;
  updatedAt?: string;
  strategyName?: string;
  tradeImages?: TradeImage[];
}

export interface Strategy {
  id: number;
  name: string;
  description?: string;
  userId?: string;
  createdAt: string;
  updatedAt?: string;
  isActive: boolean;
  totalTrades?: number;
  winningTrades?: number;
  losingTrades?: number;
  totalProfitLoss?: number;
  winRate?: number;
  averageWin?: number;
  averageLoss?: number;
  profitFactor?: number;
  displayCurrency?: string;
  displayCurrencySymbol?: string;
  trades?: Trade[];
}

export interface TradeImage {
  id: number;
  tradeId: number;
  type: 'Entry' | 'Exit';
  originalFileName: string;
  imageData?: Uint8Array;
  fileSizeBytes: number;
  mimeType: string;
}

export interface DashboardData {
  totalTrades: number;
  openTrades: number;
  winningTrades: number;
  losingTrades: number;
  totalProfitLoss: number;
  totalProfitLossDisplay: number;
  winRate: number;
  averageWin: number;
  averageWinDisplay: number;
  averageLoss: number;
  averageLossDisplay: number;
  profitFactor: number;
  displayCurrency: string;
  displayCurrencySymbol: string;
  strategies: Strategy[];
  recentTrades: Trade[];
  monthlyPerformance: MonthlyPerformance[];
  strategyPerformance: StrategyPerformance[];
  instrumentPerformance: InstrumentPerformance[];
}

export interface MonthlyPerformance {
  month: string;
  profitLoss: number;
  trades: number;
  winRate: number;
}

export interface StrategyPerformance {
  strategyName: string;
  profitLoss: number;
  trades: number;
  winRate: number;
}

export interface InstrumentPerformance {
  instrument: string;
  profitLoss: number;
  trades: number;
  winRate: number;
}

export interface TradeFilters {
  search?: string;
  instrument?: string;
  strategyId?: number;
  status?: string;
  startDate?: string;
  endDate?: string;
  sortBy?: string;
  sortOrder?: string;
}

export interface User {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  displayCurrency?: string;
  socialAuthProvider?: string;
  createdAt: string;
  lastLoginAt?: string;
}

// Authentication types
export interface LoginResponse {
  message: string;
  email: string;
  token: string;
  expiresIn: number;
}

export interface AuthCheckResponse {
  authenticated: boolean;
  email: string;
  claims: Array<{ type: string; value: string }>;
}

// Prediction types
export interface PredictionHistory {
  date: string;
  instrument: string;
  score: number;
}

// Email types
export interface EmailResponse {
  message: string;
}

