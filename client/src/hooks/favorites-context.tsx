import React, { createContext, useContext } from 'react';
import { useFavorites } from './useFavorites';

/**
 * Общий контекст избранного — один инстанс useFavorites на всё приложение,
 * чтобы и таблица результатов (сердечко), и попап деталей (звезда "это я")
 * работали с одним состоянием.
 */
type FavoritesValue = ReturnType<typeof useFavorites>;

const FavoritesContext = createContext<FavoritesValue | null>(null);

export function FavoritesProvider({ children }: { children: React.ReactNode }) {
  const favorites = useFavorites();
  return (
    <FavoritesContext.Provider value={favorites}>
      {children}
    </FavoritesContext.Provider>
  );
}

export function useFavoritesContext(): FavoritesValue {
  const ctx = useContext(FavoritesContext);
  if (!ctx) {
    throw new Error('useFavoritesContext must be used within <FavoritesProvider>');
  }
  return ctx;
}
