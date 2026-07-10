import React, { useState } from 'react';

type ActivePage = 'home' | 'competitions' | 'groups';

export function SwimHubLogo() {
  return (
    <a href="./home.html" className="flex items-center gap-3 no-underline">
      <span className="flex h-[30px] w-[30px] items-center justify-center rounded-[9px] bg-[linear-gradient(140deg,#38bdf8,#0369a1)] text-[14px] font-black text-[#06263f] lg:h-9 lg:w-9 lg:rounded-[11px] lg:text-[17px]">
        S
      </span>
      <span className="text-[12px] font-black tracking-[0.22em] text-[#f3f8fd] lg:text-[14px]">
        SWIM<span className="text-[#7dd3fc]">HUB</span>
      </span>
    </a>
  );
}

const NAV_LINKS: { label: string; href?: string; key?: ActivePage }[] = [
  { label: 'Home', href: './home.html', key: 'home' },
  { label: 'Competitions', href: './competitions.html', key: 'competitions' },
  { label: 'Groups', href: './groups.html', key: 'groups' },
  { label: 'Normatives' },
  { label: 'Records' },
  { label: 'About', href: './about.html' },
];

function MenuItem({
  href,
  label,
  trailing,
  onSelect,
}: {
  href?: string;
  label: string;
  trailing: React.ReactNode;
  onSelect: () => void;
}) {
  const className =
    'flex items-center justify-between rounded-[13px] px-4 py-[15px] text-[17px] font-extrabold text-[#f3f8fd] no-underline transition-colors hover:bg-[rgba(56,189,248,0.12)] active:bg-[rgba(56,189,248,0.12)]';
  if (!href) {
    return (
      <span className={className}>
        {label}
        {trailing}
      </span>
    );
  }
  return (
    <a href={href} className={className} onClick={onSelect}>
      {label}
      {trailing}
    </a>
  );
}

function HomeHeader({ active }: { active: ActivePage }) {
  const [menuOpen, setMenuOpen] = useState(false);
  const closeMenu = () => setMenuOpen(false);

  return (
    <header className="relative z-30 flex items-center justify-between px-5 py-[18px] lg:px-16 lg:py-[34px]">
      <SwimHubLogo />

      <nav className="hidden items-center gap-[34px] lg:flex" aria-label="Main">
        {NAV_LINKS.map((link) =>
          link.href ? (
            <a
              key={link.label}
              href={link.href}
              className={`text-[14px] font-bold no-underline ${
                link.key === active ? 'text-[#7dd3fc]' : 'text-[#c9dcee] hover:text-[#7dd3fc]'
              }`}
            >
              {link.label}
            </a>
          ) : (
            <span key={link.label} className="cursor-default text-[14px] font-bold text-[#c9dcee]">
              {link.label}
            </span>
          )
        )}
      </nav>

      <button
        type="button"
        className="flex h-11 w-11 flex-col items-end justify-center gap-[5px] lg:hidden"
        aria-label={menuOpen ? 'Close menu' : 'Open menu'}
        aria-expanded={menuOpen}
        onClick={() => setMenuOpen((open) => !open)}
      >
        {menuOpen ? (
          <span className="w-full text-center text-[20px] font-bold leading-none text-[#cfe6f6]">✕</span>
        ) : (
          <>
            <span className="h-[2.5px] w-[22px] rounded-[2px] bg-[#cfe6f6]" />
            <span className="h-[2.5px] w-[15px] rounded-[2px] bg-[#cfe6f6]" />
          </>
        )}
      </button>

      {menuOpen && (
        <div className="hp-menu-panel absolute left-3 right-3 top-[66px] z-40 rounded-[20px] border border-[#7dd3fc]/35 bg-[rgba(4,16,32,0.92)] p-[10px] shadow-[0_28px_60px_rgba(2,10,24,0.7)] backdrop-blur-[18px] lg:hidden">
          <MenuItem
            href="./competitions.html"
            label="Competitions"
            onSelect={closeMenu}
            trailing={
              <span className="hp-mono text-[10px] font-extrabold text-[#38ef8f]">● LIVE</span>
            }
          />
          <MenuItem
            label="Normatives"
            onSelect={closeMenu}
            trailing={<span className="text-[17px] font-bold text-[#7dd3fc]">→</span>}
          />
          <MenuItem
            label="Records"
            onSelect={closeMenu}
            trailing={
              <span className="hp-mono text-[10px] font-extrabold text-[#fbbf24]">★ 3 NEW</span>
            }
          />
          <MenuItem
            href="./about.html"
            label="About"
            onSelect={closeMenu}
            trailing={<span className="text-[17px] font-bold text-[#7dd3fc]">→</span>}
          />
          <div className="mt-[10px] flex items-center justify-between border-t border-[#7dd3fc]/[0.18] px-4 pb-[6px] pt-[14px]">
            <span className="text-[11px] font-bold text-[#cbe0f0]/55">Countries — coming 2026</span>
            <span className="hp-mono rounded-[7px] border border-[#94a3b8]/40 px-2 py-[3px] text-[11px] font-extrabold text-[#94a3b8]">
              SOON
            </span>
          </div>
        </div>
      )}
    </header>
  );
}

export default HomeHeader;
