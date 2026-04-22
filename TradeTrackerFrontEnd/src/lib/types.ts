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
  roles?: string[];
  claims?: Array<{ type: string; value: string }>;
}

// Prediction types
export interface PredictionHistory {
  date: string;
  instrument: string;
  score: number;
}

// TrailBlazer types
export interface TrailBlazerScore {
  id: number;
  instrumentId: number;
  instrumentName: string;
  assetClass: string;
  overallScore: number;
  bias: 'Bullish' | 'Bearish' | 'Neutral';
  fundamentalScore: number;
  sentimentScore: number;
  technicalScore: number;
  cotScore: number;
  retailSentimentScore: number;
  newsSentimentScore: number;
  economicScore: number;
  currencyStrengthScore?: number;
  dataSources?: string | null;
  dateComputed: string;
  /** Asset scanner setup signal. Tiers (strongest → weakest):
   *  BUY NOW / SELL NOW                          (all aligned: score + Fib 50/61.8% + continuation S/R or trendline)
   *  STRONG_REVERSAL_BUY / STRONG_REVERSAL_SELL  (61.8% Fib touched)
   *  REVERSAL_BUY / REVERSAL_SELL                (50% Fib touched)
   *  RESISTANCE_BUY / RESISTANCE_SELL            (bounce/reject at horizontal trend support/resistance)
   *  TRENDLINE_BUY / TRENDLINE_SELL              (bounce/reject at ascending/descending trendline)
   *  BUY / SELL                                  (directional from score only)
   *  WATCH  (score in 4–6 neutral band) | NONE | legacy STRONG_BUY/STRONG_SELL
   */
  tradeSetupSignal?: string | null;
  tradeSetupDetail?: string | null;
}

export interface TrailBlazerTopSetups {
  bullish: TrailBlazerScore[];
  bearish: TrailBlazerScore[];
}

export interface HeatmapEntry {
  currency: string;
  indicator: string;
  value: number;
  previousValue: number;
  impact: 'Positive' | 'Negative' | 'Neutral';
  dateCollected: string;
}

export interface COTData {
  symbol: string;
  commercialLong: number;
  commercialShort: number;
  nonCommercialLong: number;
  nonCommercialShort: number;
  openInterest: number;
  netNonCommercial: number;
  reportDate: string;
}

export interface SentimentData {
  symbol: string;
  retailSentimentScore: number;
  longPct: number;
  shortPct: number;
}

export interface ScoreHistoryEntry {
  overallScore: number;
  bias: string;
  fundamentalScore: number;
  sentimentScore: number;
  technicalScore: number;
  dateComputed: string;
}

export interface TrailBlazerNewsItem {
  headline: string;
  summary: string;
  source: string;
  url: string;
  imageUrl: string;
  publishedAt: string;
}

export interface TrailBlazerOutlookItem {
  title: string;
  url: string;
  description: string;
  source: string;
}

export interface TrailBlazerBiasChange {
  instrumentId: number;
  instrumentName: string;
  previousBias: string;
  newBias: string;
  overallScore: number;
  changedAt: string;
}

// Email types
export interface EmailResponse {
  message: string;
}

