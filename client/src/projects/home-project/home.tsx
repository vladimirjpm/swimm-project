import React from 'react';
import './home.css';
function Home() {
  const pages = [
    {
      href: './dolphin-masters.html',
      title: 'Dolphin Masters',
      subtitle: '2026 Horef Masters Arena ages: 21+',
      badge: 'М',
    },
    {
      href: './results-masters.html',
      title: 'All Masters',
      subtitle: '2026 Horef Masters Arena ages: 21+',
      badge: 'М',
    },
    {
      href: './results-youth-team.html',
      title: 'Youth Results',
      subtitle: '2026 Horef Isr Champ ages: 8-11',
      badge: '8-11',
    },
    {
      href: './results_main.html',
      title: 'Junior Results',
      subtitle: '2026 Horef Isr Champ ages: 11-14',
      badge: '11-15',
    },
  ];

  return (
    <main className="home-page min-h-screen pt-8 px-4 pb-12 sm:pt-12 sm:px-6 sm:pb-18 text-slate-50">
      <section className="w-full max-w-[980px] mx-auto mb-9">
        <p className="mb-3 text-[0.8rem] font-bold tracking-[0.24em] uppercase text-sky-300">
          Swimm Project
        </p>
        <h1 className="text-[clamp(2.8rem,7vw,5.5rem)] leading-[0.92] tracking-[-0.04em]">
          Competition Hub
        </h1>
        <p className="max-w-[640px] mt-[18px] text-[1.05rem] leading-[1.65] text-slate-200/90">
          Last results, upcoming events, and all the info you need to stay on top of the swimming world. 
          Dive in and explore the latest news, records, and highlights from the pool!
        </p>
      </section>

      <section
        className="grid grid-cols-[repeat(auto-fit,minmax(220px,1fr))] gap-[18px] w-full max-w-[980px] mx-auto"
        aria-label="Competition pages"
      >
        {pages.map((page) => (
          <a
            key={page.href}
            className="home-card relative flex flex-col justify-between min-h-[180px] sm:min-h-[220px] p-6 border border-sky-300/18 rounded-[22px] sm:rounded-[28px] overflow-hidden no-underline text-inherit shadow-[0_18px_50px_rgba(2,8,23,0.24)] backdrop-blur-[18px] transition-all duration-[180ms] ease-out hover:-translate-y-1.5 hover:border-sky-300/45 hover:shadow-[0_24px_60px_rgba(8,47,73,0.45)] focus-visible:-translate-y-1.5 focus-visible:border-sky-300/45 focus-visible:shadow-[0_24px_60px_rgba(8,47,73,0.45)] before:content-[''] before:absolute before:-top-[20%] before:left-[55%] before:w-[120px] before:h-[120px] before:rounded-full before:bg-sky-300/18 before:blur-[8px]"
            href={page.href}
          >
            <div className="relative z-10 flex items-start gap-3">
              <span className="text-[1.75rem] font-bold leading-[1.2] pt-1">
                {page.title}
              </span>
              <span className="shrink-0 inline-flex items-center justify-center min-w-[48px] h-8 px-1 rounded-xl bg-sky-400/15 border border-sky-300/25 text-sky-200 text-xl font-bold leading-none">
                {page.badge}
              </span>
            </div>
            <span className="relative z-10 text-[0.95rem] text-slate-200/[0.84]">
              {page.subtitle}
            </span>
          </a>
        ))}
      </section>
    </main>
  );
}

export default Home;
