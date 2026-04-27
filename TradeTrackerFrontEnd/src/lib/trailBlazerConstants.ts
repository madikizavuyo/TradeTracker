/** Symbols excluded from Retail Sentiment UI/API: MyFXBook often returns placeholder 50/50 with no real positioning. */
export const RETAIL_SENTIMENT_EXCLUDED_SYMBOLS = new Set([
  'USDBRL',
  'JPYZAR',
  'CHFZAR',
  'CADZAR',
  'NZDZAR',
  'AUDZAR',
  'GBPZAR',
]);

export function normalizeInstrumentSymbol(symbol: string | undefined | null): string {
  return (symbol ?? '').replace(/\//g, '').replace(/_/g, '').replace(/\s/g, '').toUpperCase();
}

export function isRetailSentimentExcluded(symbol: string | undefined | null): boolean {
  return RETAIL_SENTIMENT_EXCLUDED_SYMBOLS.has(normalizeInstrumentSymbol(symbol));
}
