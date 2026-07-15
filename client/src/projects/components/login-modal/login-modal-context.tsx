import React, { createContext, useCallback, useContext, useState } from 'react';
import LoginModal from './login-modal';
import { getCurrentUser } from '../../../hooks/useAuth';

// Фаза 4.5: контекст логин-модала для страниц без HomeHeader (results_main.html).
// Монтирует LoginModal один раз и даёт хук openLoginModal() гостевым CTA
// (сердечки в таблице результатов, попап деталей спортсмена).

interface LoginModalContextValue {
  openLoginModal(): void;
}

const LoginModalContext = createContext<LoginModalContextValue | null>(null);

export function LoginModalProvider({ children }: { children: React.ReactNode }) {
  const [open, setOpen] = useState(false);

  const openLoginModal = useCallback(() => setOpen(true), []);
  const closeLoginModal = useCallback(() => setOpen(false), []);
  const handleLoggedIn = useCallback(() => {
    getCurrentUser(true);
  }, []);

  return (
    <LoginModalContext.Provider value={{ openLoginModal }}>
      {children}
      <LoginModal open={open} onClose={closeLoginModal} onLoggedIn={handleLoggedIn} />
    </LoginModalContext.Provider>
  );
}

export function useLoginModal(): LoginModalContextValue {
  const ctx = useContext(LoginModalContext);
  if (!ctx) {
    throw new Error('useLoginModal must be used within <LoginModalProvider>');
  }
  return ctx;
}
