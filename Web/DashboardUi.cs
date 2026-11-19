namespace SentinelGraph.Web;

public static class DashboardUi
{
    public static string RenderHtml() => """
<!DOCTYPE html>
<html lang="pt-BR" class="dark">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>SENTINEL GRAPH — Auditoria Pública &amp; Inteligência em Grafos</title>
  <script src="https://cdn.tailwindcss.com"></script>
  <link rel="preconnect" href="https://fonts.googleapis.com" />
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&family=JetBrains+Mono:wght@400;500;600;700&display=swap" rel="stylesheet" />
  <script>
    tailwind.config = {
      darkMode: 'class',
      theme: {
        extend: {
          colors: {
            obsidian: '#0B0D11',
            panel: '#11151C',
            surface: '#161B24',
            stroke: '#222A38',
            amberforge: '#F59E0B',
            crimson: '#EF4444'
          },
          fontFamily: {
            sans: ['Inter', 'system-ui', 'sans-serif'],
            mono: ['JetBrains Mono', 'ui-monospace', 'SFMono-Regular', 'monospace']
          }
        }
      }
    };
  </script>
  <style>
    * { box-sizing: border-box; }
    html, body {
      background-color: #0B0D11;
      color: #E2E8F0;
      margin: 0;
      padding: 0;
      font-family: 'Inter', system-ui, -apple-system, sans-serif;
    }
    .font-mono-num {
      font-family: 'JetBrains Mono', ui-monospace, SFMono-Regular, monospace;
      font-variant-numeric: tabular-nums;
    }
    .custom-scroll::-webkit-scrollbar { width: 6px; height: 6px; }
    .custom-scroll::-webkit-scrollbar-track { background: #0B0D11; }
    .custom-scroll::-webkit-scrollbar-thumb { background: #263042; border-radius: 999px; }
    .custom-scroll::-webkit-scrollbar-thumb:hover { background: #3B4963; }

    .grid-canvas {
      background-color: #0B0D11;
      background-image:
        radial-gradient(circle at 50% 0%, rgba(245, 158, 11, 0.06), transparent 55%),
        linear-gradient(to right, rgba(34, 42, 56, 0.28) 1px, transparent 1px),
        linear-gradient(to bottom, rgba(34, 42, 56, 0.28) 1px, transparent 1px);
      background-size: 100% 100%, 36px 36px, 36px 36px;
    }

    .svg-node { cursor: pointer; transition: opacity 0.2s ease; }
    .svg-node:hover .node-halo { opacity: 0.45; }
    .svg-edge { transition: opacity 0.2s ease, stroke-width 0.2s ease; }

    @keyframes dashFlow {
      to { stroke-dashoffset: -28; }
    }
    .cycle-edge {
      stroke-dasharray: 8 6;
      animation: dashFlow 1.2s linear infinite;
    }
    @keyframes pulseRing {
      0%, 100% { opacity: 0.25; transform: scale(1); }
      50% { opacity: 0.65; transform: scale(1.08); }
    }
    .pulse-ring {
      transform-box: fill-box;
      transform-origin: center;
      animation: pulseRing 2.2s ease-in-out infinite;
    }
  </style>
</head>
<body class="bg-obsidian text-slate-200 min-h-screen flex flex-col overflow-x-hidden">

  <!-- TOP EXECUTIVE HEADER -->
  <header class="border-b border-stroke bg-[#0D1017]/95 backdrop-blur sticky top-0 z-40">
    <div class="max-w-[1920px] mx-auto px-4 py-3 flex flex-wrap items-center justify-between gap-3">
      <div class="flex items-center gap-3">
        <div class="w-9 h-9 rounded-lg bg-gradient-to-br from-amber-500/20 to-red-600/20 border border-amber-500/40 flex items-center justify-center shadow-lg shadow-amber-500/10">
          <svg class="w-5 h-5 text-amber-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="6" cy="6" r="2.5"></circle>
            <circle cx="18" cy="6" r="2.5"></circle>
            <circle cx="12" cy="18" r="2.5"></circle>
            <path d="M8.2 7.2L11 15.6M15.8 7.2L13 15.6M8.5 6H15.5"></path>
          </svg>
        </div>
        <div>
          <div class="flex items-center gap-2">
            <h1 class="text-sm sm:text-base font-extrabold tracking-wider text-white">SENTINEL GRAPH</h1>
            <span class="px-1.5 py-0.5 text-[10px] font-mono-num font-semibold rounded bg-amber-500/15 text-amber-400 border border-amber-500/30">.NET 8 OSINT</span>
          </div>
          <p class="text-[11px] text-slate-400 tracking-wide">Auditoria Pública, Detecção de Anomalias &amp; Inteligência em Grafos L0–L4</p>
        </div>
      </div>

      <!-- LIVE CNPJ CONSULTATION & ENTITY SEARCH -->
      <div class="flex flex-wrap items-center gap-2 flex-1 max-w-3xl justify-end">
        <div class="relative flex-1 min-w-[200px] max-w-[260px]">
          <input id="entity-search-input" type="text" placeholder="Filtrar entidades no grafo..."
            class="w-full bg-panel border border-stroke rounded-lg pl-8 pr-3 py-1.5 text-xs text-slate-200 placeholder-slate-500 focus:outline-none focus:border-amber-500/60" />
          <svg class="w-3.5 h-3.5 text-slate-500 absolute left-2.5 top-2.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-4.35-4.35M11 19a8 8 0 100-16 8 8 0 000 16z"/>
          </svg>
        </div>

        <form id="cnpj-form" class="flex items-center gap-1.5">
          <div class="relative">
            <input id="cnpj-input" type="text" maxlength="18" placeholder="Consultar CNPJ real (BrasilAPI)..."
              class="w-56 bg-panel border border-amber-500/35 rounded-lg pl-3 pr-2 py-1.5 text-xs font-mono-num text-amber-100 placeholder-slate-500 focus:outline-none focus:border-amber-400" />
          </div>
          <button id="cnpj-submit-btn" type="submit"
            class="px-3 py-1.5 rounded-lg bg-amber-500 hover:bg-amber-400 text-obsidian font-bold text-xs transition flex items-center gap-1.5 shadow-md shadow-amber-500/10 whitespace-nowrap">
            <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2.2" d="M12 4v16m8-8H4"/>
            </svg>
            <span>Ingerir CNPJ</span>
          </button>
        </form>

        <div id="health-pill" class="hidden xl:flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg bg-panel border border-stroke text-[11px] font-mono-num text-slate-400">
          <span class="w-2 h-2 rounded-full bg-emerald-400 animate-pulse"></span>
          <span id="health-status-text">Conectando...</span>
        </div>
      </div>
    </div>

    <!-- TOAST / FEEDBACK BAR -->
    <div id="status-banner" class="hidden border-t border-stroke bg-panel/95 px-4 py-1.5 text-xs font-mono-num">
      <div class="max-w-[1920px] mx-auto flex items-center justify-between gap-3">
        <span id="status-banner-msg"></span>
        <button onclick="hideBanner()" class="text-slate-400 hover:text-white text-[11px]">FECHAR ✕</button>
      </div>
    </div>
  </header>

  <!-- EXECUTIVE KPI BAR -->
  <section class="max-w-[1920px] w-full mx-auto px-4 pt-3 pb-2">
    <div class="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-5 gap-2.5">
      <div class="bg-panel border border-stroke rounded-xl px-3.5 py-2.5 relative overflow-hidden">
        <div class="absolute top-0 left-0 w-1 h-full bg-amber-500"></div>
        <div class="text-[10px] uppercase tracking-widest text-slate-400 font-semibold">Exposição Total (R$)</div>
        <div id="kpi-exposure" class="text-lg sm:text-xl font-bold font-mono-num text-amber-400 mt-0.5">R$ 0,00</div>
        <div id="kpi-circuits" class="text-[10px] font-mono-num text-slate-500 mt-0.5">0 circuitos fechados</div>
      </div>

      <div class="bg-panel border border-red-500/30 rounded-xl px-3.5 py-2.5 relative overflow-hidden">
        <div class="absolute top-0 left-0 w-1 h-full bg-red-500"></div>
        <div class="text-[10px] uppercase tracking-widest text-red-300/80 font-semibold">Irregularidades Críticas</div>
        <div id="kpi-critical" class="text-lg sm:text-xl font-bold font-mono-num text-red-400 mt-0.5">0</div>
        <div id="kpi-severity-sub" class="text-[10px] font-mono-num text-slate-400 mt-0.5">0 Alta • 0 Média</div>
      </div>

      <div class="bg-panel border border-stroke rounded-xl px-3.5 py-2.5">
        <div class="text-[10px] uppercase tracking-widest text-slate-400 font-semibold">Entidades Mapeadas</div>
        <div id="kpi-entities" class="text-lg sm:text-xl font-bold font-mono-num text-white mt-0.5">0</div>
        <div class="text-[10px] font-mono-num text-slate-500 mt-0.5">Camadas L0 → L4</div>
      </div>

      <div class="bg-panel border border-stroke rounded-xl px-3.5 py-2.5">
        <div class="text-[10px] uppercase tracking-widest text-slate-400 font-semibold">Conexões Ponderadas</div>
        <div id="kpi-edges" class="text-lg sm:text-xl font-bold font-mono-num text-sky-400 mt-0.5">0</div>
        <div class="text-[10px] font-mono-num text-slate-500 mt-0.5">Vínculos &amp; Fluxos</div>
      </div>

      <div class="bg-panel border border-stroke rounded-xl px-3.5 py-2.5 col-span-2 md:col-span-1">
        <div class="text-[10px] uppercase tracking-widest text-slate-400 font-semibold">Fontes Cruzadas</div>
        <div id="kpi-sources" class="text-lg sm:text-xl font-bold font-mono-num text-emerald-400 mt-0.5">0</div>
        <div id="kpi-sources-list" class="text-[10px] text-slate-500 mt-0.5 truncate" title="">Bases governamentais</div>
      </div>
    </div>
  </section>

  <!-- MAIN 3-COLUMN FORENSIC WORKSPACE -->
  <main class="max-w-[1920px] w-full mx-auto px-4 pb-4 flex-1 grid grid-cols-1 lg:grid-cols-12 gap-3 min-h-[680px]">

    <!-- LEFT COLUMN: INSIGHTS & ANOMALIES -->
    <aside class="lg:col-span-3 bg-panel border border-stroke rounded-xl flex flex-col overflow-hidden max-h-[calc(100vh-195px)] min-h-[540px]">
      <div class="p-3 border-b border-stroke bg-surface/60">
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-2">
            <span class="w-2 h-2 rounded-full bg-red-500"></span>
            <h2 class="text-xs font-bold uppercase tracking-widest text-slate-200">Insights &amp; Anomalias</h2>
          </div>
          <span id="anomaly-counter" class="text-[11px] font-mono-num text-amber-400 bg-amber-500/10 border border-amber-500/25 px-2 py-0.5 rounded">0 alertas</span>
        </div>

        <!-- SEVERITY FILTER BUTTONS -->
        <div class="grid grid-cols-4 gap-1 mt-2.5">
          <button data-sev="ALL" class="sev-filter-btn px-2 py-1 rounded text-[11px] font-semibold bg-amber-500 text-obsidian transition">Todos</button>
          <button data-sev="CRITICO" class="sev-filter-btn px-2 py-1 rounded text-[11px] font-semibold bg-surface text-red-300 border border-stroke hover:border-red-500/50 transition">Crítico</button>
          <button data-sev="ALTA" class="sev-filter-btn px-2 py-1 rounded text-[11px] font-semibold bg-surface text-amber-300 border border-stroke hover:border-amber-500/50 transition">Alta</button>
          <button data-sev="MEDIA" class="sev-filter-btn px-2 py-1 rounded text-[11px] font-semibold bg-surface text-sky-300 border border-stroke hover:border-sky-500/50 transition">Média</button>
        </div>
      </div>

      <div id="anomaly-list" class="flex-1 overflow-y-auto custom-scroll p-2.5 space-y-2.5">
        <div class="text-xs text-slate-500 p-4 text-center font-mono-num">Carregando motor forense...</div>
      </div>
    </aside>

    <!-- CENTER COLUMN: INTERACTIVE SVG KNOWLEDGE GRAPH -->
    <section class="lg:col-span-6 bg-panel border border-stroke rounded-xl flex flex-col overflow-hidden max-h-[calc(100vh-195px)] min-h-[560px]">
      <!-- GRAPH TOOLBAR -->
      <div class="px-3 py-2 border-b border-stroke bg-surface/60 flex flex-wrap items-center justify-between gap-2">
        <div class="flex items-center gap-2">
          <span class="text-xs font-bold uppercase tracking-widest text-slate-200">Mapa Relacional L0–L4</span>
          <span id="graph-mode-badge" class="text-[10px] font-mono-num px-2 py-0.5 rounded bg-obsidian border border-stroke text-slate-400">Visão Completa</span>
        </div>

        <div class="flex items-center gap-1.5">
          <button id="btn-highlight-cycles" type="button"
            class="px-2.5 py-1 rounded-md text-[11px] font-mono-num bg-red-500/15 hover:bg-red-500/25 text-red-300 border border-red-500/35 transition">
            Circuitos Fechados
          </button>
          <button id="btn-reset-focus" type="button"
            class="px-2.5 py-1 rounded-md text-[11px] font-mono-num bg-surface hover:bg-slate-800 text-slate-300 border border-stroke transition">
            Resetar Foco
          </button>
          <div class="flex items-center bg-obsidian border border-stroke rounded-md overflow-hidden ml-1">
            <button id="btn-zoom-out" type="button" title="Diminuir zoom" class="px-2 py-1 text-xs font-mono-num text-slate-300 hover:bg-surface">−</button>
            <span id="zoom-label" class="px-2 py-1 text-[10px] font-mono-num text-amber-400 border-x border-stroke">100%</span>
            <button id="btn-zoom-in" type="button" title="Aumentar zoom" class="px-2 py-1 text-xs font-mono-num text-slate-300 hover:bg-surface">+</button>
            <button id="btn-zoom-fit" type="button" title="Ajustar à tela" class="px-2 py-1 text-[10px] font-mono-num text-slate-300 hover:bg-surface border-l border-stroke">FIT</button>
          </div>
        </div>
      </div>

      <!-- INTERACTIVE SVG VIEWPORT -->
      <div id="graph-container" class="relative flex-1 grid-canvas overflow-hidden select-none cursor-grab active:cursor-grabbing">
        <svg id="graph-svg" class="w-full h-full" viewBox="0 0 1160 660" xmlns="http://www.w3.org/2000/svg">
          <defs>
            <marker id="arrow-default" viewBox="0 0 10 10" refX="26" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
              <path d="M 0 1 L 10 5 L 0 9 z" fill="#475569" />
            </marker>
            <marker id="arrow-amber" viewBox="0 0 10 10" refX="26" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
              <path d="M 0 1 L 10 5 L 0 9 z" fill="#F59E0B" />
            </marker>
            <marker id="arrow-red" viewBox="0 0 10 10" refX="26" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
              <path d="M 0 1 L 10 5 L 0 9 z" fill="#EF4444" />
            </marker>
            <filter id="glow-amber" x="-30%" y="-30%" width="160%" height="160%">
              <feGaussianBlur stdDeviation="4" result="blur" />
              <feMerge>
                <feMergeNode in="blur" />
                <feMergeNode in="SourceGraphic" />
              </feMerge>
            </filter>
          </defs>
          <g id="svg-viewport">
            <g id="svg-lanes"></g>
            <g id="svg-edges"></g>
            <g id="svg-nodes"></g>
          </g>
        </svg>

        <!-- FLOATING HINT -->
        <div class="absolute bottom-2.5 left-3 pointer-events-none flex flex-wrap items-center gap-3 bg-[#0B0D11]/90 border border-stroke rounded-lg px-3 py-1.5 text-[10px] font-mono-num text-slate-400">
          <span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-full bg-[#F59E0B]"></span>L0 Agente</span>
          <span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-full bg-[#A855F7]"></span>L1 Familiar/Sócio</span>
          <span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-full bg-[#38BDF8]"></span>L2 Empresa/CNPJ</span>
          <span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-full bg-[#EF4444]"></span>L3 Contrato/Emenda</span>
          <span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-full bg-[#10B981]"></span>L4 Doador/Órgão</span>
        </div>
        <div class="hidden sm:block absolute bottom-2.5 right-3 pointer-events-none bg-[#0B0D11]/85 border border-stroke rounded-lg px-2.5 py-1 text-[10px] font-mono-num text-slate-500">
          Scroll: Zoom • Arrastar: Pan • Clique: BFS L0–L4
        </div>
      </div>
    </section>

    <!-- RIGHT COLUMN: ENTITY INSPECTOR + GEMINI AI + EXPORT HUB -->
    <aside class="lg:col-span-3 bg-panel border border-stroke rounded-xl flex flex-col overflow-hidden max-h-[calc(100vh-195px)] min-h-[540px]">
      <div class="p-3 border-b border-stroke bg-surface/60 flex items-center justify-between">
        <div class="flex items-center gap-2">
          <span class="w-2 h-2 rounded-full bg-amber-400"></span>
          <h2 class="text-xs font-bold uppercase tracking-widest text-slate-200">Inspeção Forense &amp; IA</h2>
        </div>
        <span id="selected-layer-tag" class="text-[10px] font-mono-num text-slate-400 px-2 py-0.5 rounded bg-obsidian border border-stroke">L0–L4</span>
      </div>

      <div class="flex-1 overflow-y-auto custom-scroll p-3 space-y-3">
        <!-- SELECTED ENTITY DETAILS -->
        <div id="entity-detail-card" class="bg-obsidian border border-stroke rounded-xl p-3">
          <div class="text-xs text-slate-400 font-mono-num">Clique em um nó no grafo ou em uma anomalia à esquerda para abrir a análise detalhada L0–L4.</div>
        </div>

        <!-- BFS MULTI-LEVEL BREAKDOWN -->
        <div id="bfs-card" class="hidden bg-obsidian border border-stroke rounded-xl p-3">
          <div class="flex items-center justify-between mb-2">
            <h3 class="text-[11px] font-bold uppercase tracking-wider text-slate-300">Vizinhança BFS (L0–L4)</h3>
            <span id="bfs-summary-count" class="text-[10px] font-mono-num text-amber-400"></span>
          </div>
          <div id="bfs-layers-container" class="space-y-2"></div>
        </div>

        <!-- GOOGLE GEMINI INVESTIGATIVE OPINION -->
        <div class="bg-obsidian border border-amber-500/30 rounded-xl p-3">
          <div class="flex items-center justify-between gap-2 mb-2">
            <div class="flex items-center gap-1.5">
              <svg class="w-4 h-4 text-amber-400" viewBox="0 0 24 24" fill="currentColor">
                <path d="M12 2l2.4 7.2L22 12l-7.6 2.8L12 22l-2.4-7.2L2 12l7.6-2.8L12 2z"/>
              </svg>
              <h3 class="text-[11px] font-bold uppercase tracking-wider text-amber-300">Parecer Técnico IA Gemini</h3>
            </div>
            <button id="btn-run-gemini" type="button"
              class="px-2.5 py-1 rounded-md bg-amber-500 hover:bg-amber-400 disabled:opacity-50 text-obsidian font-bold text-[11px] transition flex items-center gap-1">
              <span>Gerar Parecer</span>
            </button>
          </div>
          <div id="gemini-output" class="text-xs text-slate-400 leading-relaxed">
            Selecione uma entidade e clique em <strong class="text-amber-300">Gerar Parecer</strong> para produzir uma avaliação executiva das conexões suspeitas e diligências de auditoria.
          </div>
        </div>

        <!-- REAL EXPORT HUB -->
        <div class="bg-obsidian border border-stroke rounded-xl p-3">
          <div class="flex items-center justify-between mb-2">
            <h3 class="text-[11px] font-bold uppercase tracking-wider text-slate-300">Central de Exportação</h3>
            <span class="text-[10px] font-mono-num text-emerald-400">LIVE EXPORT</span>
          </div>
          <div class="grid grid-cols-2 gap-2">
            <button id="btn-export-csv" type="button"
              class="px-2.5 py-2 rounded-lg bg-surface hover:bg-slate-800 border border-stroke hover:border-amber-500/40 text-left transition group">
              <div class="text-[11px] font-bold text-slate-200 group-hover:text-amber-300">CSV Entidades</div>
              <div class="text-[10px] font-mono-num text-slate-500">Excel / UTF-8</div>
            </button>

            <button id="btn-export-json" type="button"
              class="px-2.5 py-2 rounded-lg bg-surface hover:bg-slate-800 border border-stroke hover:border-amber-500/40 text-left transition group">
              <div class="text-[11px] font-bold text-slate-200 group-hover:text-amber-300">JSON Grafo</div>
              <div class="text-[10px] font-mono-num text-slate-500">Nós + Arestas</div>
            </button>

            <button id="btn-export-svg" type="button"
              class="px-2.5 py-2 rounded-lg bg-surface hover:bg-slate-800 border border-stroke hover:border-amber-500/40 text-left transition group">
              <div class="text-[11px] font-bold text-slate-200 group-hover:text-amber-300">SVG Vetorial</div>
              <div class="text-[10px] font-mono-num text-slate-500">Mapa Atual</div>
            </button>

            <button id="btn-export-report" type="button"
              class="px-2.5 py-2 rounded-lg bg-amber-500/15 hover:bg-amber-500/25 border border-amber-500/40 text-left transition group">
              <div class="text-[11px] font-bold text-amber-300">Dossiê HTML / PDF</div>
              <div class="text-[10px] font-mono-num text-amber-200/70">Pronto p/ Impressão</div>
            </button>
          </div>
        </div>
      </div>
    </aside>
  </main>

  <script>
    (() => {
      const state = {
        snapshot: null,
        selectedNodeId: null,
        selectedBfs: null,
        severityFilter: 'ALL',
        searchQuery: '',
        highlightedNodeIds: new Set(),
        highlightedEdgeIds: new Set(),
        cyclesHighlighted: false,
        positions: new Map(),
        transform: { x: 0, y: 0, scale: 1 },
        isPanning: false,
        panStart: { x: 0, y: 0 }
      };

      const LAYER_META = {
        L0_AgentePublico:  { code: 'L0', title: 'L0 • AGENTE PÚBLICO',  color: '#F59E0B', col: 0 },
        L1_FamiliarSocio:  { code: 'L1', title: 'L1 • FAMILIAR / SÓCIO', color: '#A855F7', col: 1 },
        L2_EmpresaCnpj:    { code: 'L2', title: 'L2 • EMPRESA / CNPJ',   color: '#38BDF8', col: 2 },
        L3_ContratoEmenda: { code: 'L3', title: 'L3 • CONTRATO / EMENDA',color: '#EF4444', col: 3 },
        L4_DoadorOrgao:    { code: 'L4', title: 'L4 • DOADOR / ÓRGÃO',   color: '#10B981', col: 4 }
      };

      const brlFmt = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

      function fmtMoney(val) {
        const num = Number(val || 0);
        return brlFmt.format(isNaN(num) ? 0 : num);
      }

      function escHtml(str) {
        return String(str ?? '')
          .replace(/&/g, '&amp;')
          .replace(/</g, '&lt;')
          .replace(/>/g, '&gt;')
          .replace(/"/g, '&quot;')
          .replace(/'/g, '&#39;');
      }

      function truncate(str, max) {
        if (!str) return '';
        return str.length > max ? str.slice(0, max - 1) + '…' : str;
      }

      function severityBadgeClass(sev) {
        switch (sev) {
          case 'CRITICO': return 'bg-red-500/20 text-red-300 border border-red-500/40';
          case 'ALTA':    return 'bg-amber-500/20 text-amber-300 border border-amber-500/40';
          case 'MEDIA':   return 'bg-sky-500/20 text-sky-300 border border-sky-500/40';
          default:        return 'bg-slate-700/40 text-slate-300 border border-slate-600/40';
        }
      }

      function severityBarColor(sev) {
        switch (sev) {
          case 'CRITICO': return 'bg-red-500';
          case 'ALTA':    return 'bg-amber-500';
          case 'MEDIA':   return 'bg-sky-400';
          default:        return 'bg-emerald-500';
        }
      }

      function showBanner(msg, type = 'info') {
        const banner = document.getElementById('status-banner');
        const text = document.getElementById('status-banner-msg');
        banner.classList.remove('hidden', 'text-amber-300', 'text-red-300', 'text-emerald-300');
        if (type === 'error') banner.classList.add('text-red-300');
        else if (type === 'success') banner.classList.add('text-emerald-300');
        else banner.classList.add('text-amber-300');
        text.textContent = msg;
      }

      window.hideBanner = () => {
        document.getElementById('status-banner').classList.add('hidden');
      };

      async function loadHealth() {
        try {
          const res = await fetch('/health');
          if (!res.ok) return;
          const data = await res.json();
          const geminiLive = data?.integrations?.googleGemini?.configured;
          const transpLive = data?.integrations?.portalTransparencia?.configured;
          const label = `BrasilAPI ON • Gemini ${geminiLive ? 'LIVE' : 'LOCAL'}${transpLive ? ' • CGU ON' : ''}`;
          document.getElementById('health-status-text').textContent = label;
        } catch {
          document.getElementById('health-status-text').textContent = 'Modo Offline';
        }
      }

      async function loadGraph(query = '') {
        const url = query ? `/api/graph?q=${encodeURIComponent(query)}` : '/api/graph';
        const res = await fetch(url);
        if (!res.ok) throw new Error('Falha ao carregar grafo.');
        const snapshot = await res.json();
        state.snapshot = snapshot;

        renderKpis(snapshot.metrics);
        renderAnomalies(snapshot.anomalies);
        computeLayout(snapshot.nodes);
        renderSvgGraph();

        // Seleciona automaticamente o nó mais crítico no primeiro carregamento
        if (!state.selectedNodeId && snapshot.nodes.length > 0) {
          await selectNode(snapshot.nodes[0].id, false);
        } else if (state.selectedNodeId && snapshot.nodes.some(n => n.id === state.selectedNodeId)) {
          await selectNode(state.selectedNodeId, false);
        }
      }

      function renderKpis(metrics) {
        if (!metrics) return;
        document.getElementById('kpi-exposure').textContent = fmtMoney(metrics.totalFinancialExposure);
        document.getElementById('kpi-circuits').textContent = `${metrics.closedCircuitsCount} circuito(s) fechado(s) detectado(s)`;
        document.getElementById('kpi-critical').textContent = metrics.criticalIrregularities;
        document.getElementById('kpi-severity-sub').textContent = `${metrics.highIrregularities} Alta • ${metrics.mediumIrregularities} Média`;
        document.getElementById('kpi-entities').textContent = metrics.mappedEntities;
        document.getElementById('kpi-edges').textContent = metrics.totalConnections;
        document.getElementById('kpi-sources').textContent = metrics.crossedSourcesCount;

        const srcText = (metrics.crossedSources || []).join(' • ');
        const srcEl = document.getElementById('kpi-sources-list');
        srcEl.textContent = srcText || 'Bases oficiais';
        srcEl.title = srcText;
      }

      async function fetchFilteredAnomalies(severity) {
        state.severityFilter = severity;
        document.querySelectorAll('.sev-filter-btn').forEach(btn => {
          const active = btn.dataset.sev === severity;
          btn.className = active
            ? 'sev-filter-btn px-2 py-1 rounded text-[11px] font-bold bg-amber-500 text-obsidian transition'
            : 'sev-filter-btn px-2 py-1 rounded text-[11px] font-semibold bg-surface text-slate-300 border border-stroke hover:border-amber-500/40 transition';
        });

        try {
          const url = severity === 'ALL' ? '/api/anomalies' : `/api/anomalies?severity=${encodeURIComponent(severity)}`;
          const res = await fetch(url);
          if (!res.ok) return;
          const data = await res.json();
          renderAnomalies(data.items || []);
        } catch {
          if (state.snapshot) renderAnomalies(state.snapshot.anomalies || []);
        }
      }

      function renderAnomalies(items) {
        const container = document.getElementById('anomaly-list');
        const counter = document.getElementById('anomaly-counter');
        const filtered = (items || []).filter(a => state.severityFilter === 'ALL' || a.severity === state.severityFilter);
        counter.textContent = `${filtered.length} alerta${filtered.length === 1 ? '' : 's'}`;

        if (filtered.length === 0) {
          container.innerHTML = `<div class="p-4 text-center text-xs text-slate-500 font-mono-num border border-dashed border-stroke rounded-lg">Nenhuma anomalia para o filtro selecionado.</div>`;
          return;
        }

        container.innerHTML = filtered.map(a => {
          const badgeCls = severityBadgeClass(a.severity);
          const barCls = severityBarColor(a.severity);
          const activeBorder = state.selectedNodeId === a.primaryNodeId
            ? 'border-amber-500/60 bg-surface'
            : 'border-stroke bg-obsidian/90 hover:border-amber-500/35';

          return `
            <article class="anomaly-card cursor-pointer rounded-xl border ${activeBorder} p-3 transition"
              data-primary-id="${escHtml(a.primaryNodeId)}"
              data-related='${escHtml(JSON.stringify(a.relatedNodeIds || []))}'>
              <div class="flex items-center justify-between gap-2 mb-1.5">
                <span class="px-1.5 py-0.5 rounded text-[10px] font-mono-num font-bold ${badgeCls}">${escHtml(a.severity)}</span>
                <span class="text-[11px] font-mono-num font-semibold text-amber-400">${fmtMoney(a.involvedAmount)}</span>
              </div>

              <h3 class="text-xs font-bold text-slate-100 leading-snug mb-1.5">${escHtml(a.title)}</h3>
              <p class="text-[11px] text-slate-400 leading-relaxed mb-2">${escHtml(a.description)}</p>

              <div class="mb-2">
                <div class="flex items-center justify-between text-[10px] font-mono-num text-slate-400 mb-1">
                  <span>CONFIANÇA DO PADRÃO</span>
                  <span class="text-slate-200 font-semibold">${a.confidencePercent}%</span>
                </div>
                <div class="w-full h-1.5 rounded-full bg-slate-800 overflow-hidden">
                  <div class="h-full ${barCls} rounded-full" style="width:${Math.min(100, a.confidencePercent)}%"></div>
                </div>
              </div>

              <div class="flex flex-wrap items-center justify-between gap-1.5 pt-1.5 border-t border-stroke/70">
                <span class="px-1.5 py-0.5 rounded bg-amber-500/10 border border-amber-500/25 text-[9px] font-mono-num text-amber-300">PATTERN: ${escHtml(a.patternCode)}</span>
                <span class="text-[10px] font-mono-num text-slate-500">${(a.relatedNodeIds || []).length} nós</span>
              </div>
            </article>
          `;
        }).join('');

        container.querySelectorAll('.anomaly-card').forEach(card => {
          card.addEventListener('click', async () => {
            const primaryId = card.dataset.primaryId;
            let related = [];
            try { related = JSON.parse(card.dataset.related || '[]'); } catch {}
            state.cyclesHighlighted = false;
            await selectNode(primaryId, true, related);
          });
        });
      }

      function computeLayout(nodes) {
        state.positions.clear();
        const buckets = [[], [], [], [], []];
        for (const n of nodes) {
          const meta = LAYER_META[n.layer] || { col: 2 };
          buckets[meta.col].push(n);
        }

        const startX = 120;
        const colGap = 230;
        const canvasHeight = 640;

        buckets.forEach((colNodes, colIdx) => {
          colNodes.sort((a, b) => (b.riskScore || 0) - (a.riskScore || 0));
          const count = colNodes.length;
          const topMargin = 85;
          const usableH = canvasHeight - topMargin - 55;
          const stepY = count <= 1 ? 0 : usableH / (count - 1);

          colNodes.forEach((node, idx) => {
            const x = startX + colIdx * colGap + (idx % 2 === 0 ? -8 : 8);
            const y = count === 1 ? topMargin + usableH / 2 : topMargin + idx * stepY;
            state.positions.set(node.id, { x, y });
          });
        });
      }

      function renderSvgGraph() {
        if (!state.snapshot) return;
        const { nodes, edges, cycles } = state.snapshot;
        const lanesG = document.getElementById('svg-lanes');
        const edgesG = document.getElementById('svg-edges');
        const nodesG = document.getElementById('svg-nodes');

        // Swimlanes L0..L4
        const laneEntries = Object.values(LAYER_META);
        lanesG.innerHTML = laneEntries.map(m => {
          const x = 120 + m.col * 230;
          return `
            <g>
              <line x1="${x}" y1="42" x2="${x}" y2="635" stroke="#1E2634" stroke-dasharray="4 6" stroke-width="1" />
              <rect x="${x - 86}" y="12" width="172" height="22" rx="6" fill="#11151C" stroke="${m.color}" stroke-opacity="0.35" />
              <text x="${x}" y="27" text-anchor="middle" fill="${m.color}" font-size="9.5" font-family="JetBrains Mono, monospace" font-weight="700">${m.title}</text>
            </g>
          `;
        }).join('');

        const cycleEdgeSet = new Set();
        (cycles || []).forEach(c => {
          if (c.isDonationContractLoop || state.cyclesHighlighted) {
            (c.edgeIds || []).forEach(eid => cycleEdgeSet.add(eid));
          }
        });

        const hasFocus = state.highlightedNodeIds.size > 0;

        // Render Edges
        edgesG.innerHTML = edges.map(e => {
          const p1 = state.positions.get(e.sourceId);
          const p2 = state.positions.get(e.targetId);
          if (!p1 || !p2) return '';

          const isCycle = cycleEdgeSet.has(e.id);
          const isHighlighted = state.highlightedEdgeIds.has(e.id) ||
            (hasFocus && state.highlightedNodeIds.has(e.sourceId) && state.highlightedNodeIds.has(e.targetId));

          const opacity = hasFocus ? (isHighlighted || isCycle ? 0.95 : 0.14) : (isCycle ? 0.95 : 0.6);
          const strokeColor = isCycle ? '#EF4444' : (isHighlighted ? '#F59E0B' : '#475569');
          const strokeWidth = isCycle ? 2.6 : (isHighlighted ? 2.2 : 1.4);
          const marker = isCycle ? 'url(#arrow-red)' : (isHighlighted ? 'url(#arrow-amber)' : 'url(#arrow-default)');

          const dx = p2.x - p1.x;
          const dy = p2.y - p1.y;
          const curveOffset = Math.abs(dx) < 40 ? 48 : (dy >= 0 ? -24 : 24);
          const cx = (p1.x + p2.x) / 2;
          const cy = (p1.y + p2.y) / 2 + curveOffset;
          const pathD = `M ${p1.x} ${p1.y} Q ${cx} ${cy} ${p2.x} ${p2.y}`;

          const showLabel = isHighlighted || isCycle || e.financialValue >= 1000000;
          const labelText = e.financialValue > 0
            ? `${e.relationType.replace(/_/g, ' ')} • ${fmtMoney(e.financialValue)}`
            : e.relationType.replace(/_/g, ' ');

          const labelSvg = showLabel ? `
            <g opacity="${opacity}">
              <rect x="${cx - 72}" y="${cy - 9}" width="144" height="16" rx="4" fill="#0B0D11" fill-opacity="0.88" stroke="${strokeColor}" stroke-opacity="0.35" stroke-width="0.8" />
              <text x="${cx}" y="${cy + 2}" text-anchor="middle" fill="${isCycle ? '#FCA5A5' : '#CBD5E1'}" font-size="8" font-family="JetBrains Mono, monospace">${escHtml(truncate(labelText, 27))}</text>
            </g>` : '';

          return `
            <g class="svg-edge" opacity="${opacity}">
              <path d="${pathD}" fill="none" stroke="${strokeColor}" stroke-width="${strokeWidth}" marker-end="${marker}" class="${isCycle ? 'cycle-edge' : ''}" />
              ${labelSvg}
            </g>
          `;
        }).join('');

        // Render Nodes
        nodesG.innerHTML = nodes.map(n => {
          const pos = state.positions.get(n.id);
          if (!pos) return '';
          const meta = LAYER_META[n.layer] || { code: 'L?', color: '#94A3B8' };
          const isSelected = state.selectedNodeId === n.id;
          const isHighlighted = !hasFocus || state.highlightedNodeIds.has(n.id);
          const opacity = isHighlighted ? 1 : 0.22;
          const isCritical = n.severity === 'CRITICO';
          const radius = isSelected ? 22 : (isCritical ? 19 : 16);
          const ringColor = isCritical ? '#EF4444' : (n.severity === 'ALTA' ? '#F59E0B' : meta.color);

          return `
            <g class="svg-node" data-node-id="${escHtml(n.id)}" transform="translate(${pos.x}, ${pos.y})" opacity="${opacity}">
              ${isCritical ? `<circle r="${radius + 7}" fill="none" stroke="#EF4444" stroke-width="1.4" class="pulse-ring" />` : ''}
              <circle class="node-halo" r="${radius + 5}" fill="${meta.color}" opacity="${isSelected ? 0.35 : 0.12}" />
              <circle r="${radius}" fill="#11151C" stroke="${ringColor}" stroke-width="${isSelected ? 3.2 : 2.2}" ${isSelected ? 'filter="url(#glow-amber)"' : ''} />
              <text y="-2" text-anchor="middle" fill="${meta.color}" font-size="9.5" font-weight="800" font-family="JetBrains Mono, monospace">${meta.code}</text>
              <text y="9" text-anchor="middle" fill="#F8FAFC" font-size="8.5" font-weight="700" font-family="JetBrains Mono, monospace">${n.riskScore}%</text>

              <rect x="-76" y="${radius + 6}" width="152" height="28" rx="5" fill="#0B0D11" fill-opacity="0.92" stroke="${isSelected ? '#F59E0B' : '#222A38'}" stroke-width="${isSelected ? 1.2 : 0.8}" />
              <text y="${radius + 18}" text-anchor="middle" fill="#F1F5F9" font-size="9.5" font-weight="600" font-family="Inter, sans-serif">${escHtml(truncate(n.label, 24))}</text>
              <text y="${radius + 29}" text-anchor="middle" fill="#94A3B8" font-size="8" font-family="JetBrains Mono, monospace">${escHtml(truncate(n.document || n.subType, 24))}</text>
            </g>
          `;
        }).join('');

        nodesG.querySelectorAll('.svg-node').forEach(el => {
          el.addEventListener('click', async (ev) => {
            ev.stopPropagation();
            state.cyclesHighlighted = false;
            await selectNode(el.dataset.nodeId, true);
          });
        });

        applyTransform();
      }

      async function selectNode(nodeId, highlightSubgraph = true, customRelatedIds = null) {
        if (!nodeId) return;
        state.selectedNodeId = nodeId;

        try {
          const res = await fetch(`/api/graph/bfs/${encodeURIComponent(nodeId)}?depth=4`);
          if (!res.ok) return;
          const bfs = await res.json();
          state.selectedBfs = bfs;

          state.highlightedNodeIds.clear();
          state.highlightedEdgeIds.clear();

          if (highlightSubgraph) {
            if (customRelatedIds && customRelatedIds.length > 0) {
              customRelatedIds.forEach(id => state.highlightedNodeIds.add(id));
              state.highlightedNodeIds.add(nodeId);
            } else {
              (bfs.nodes || []).forEach(n => state.highlightedNodeIds.add(n.id));
              (bfs.edges || []).forEach(e => state.highlightedEdgeIds.add(e.id));
            }
            document.getElementById('graph-mode-badge').textContent = `Foco BFS: ${truncate(bfs.rootNode.label, 22)}`;
          } else {
            document.getElementById('graph-mode-badge').textContent = 'Visão Completa';
          }

          renderEntityDetail(bfs);
          renderSvgGraph();
          if (state.snapshot) renderAnomalies(state.snapshot.anomalies);

          // Reset Gemini box prompt hint for new selection
          document.getElementById('gemini-output').innerHTML = `
            Entidade <strong class="text-slate-200">${escHtml(bfs.rootNode.label)}</strong> pronta para análise forense.
            Clique em <strong class="text-amber-300">Gerar Parecer</strong> para consultar a IA Gemini sobre os ${(bfs.anomalies || []).length} alerta(s) e ${(bfs.edges || []).length} conexão(ões) mapeadas.
          `;
        } catch (err) {
          showBanner(`Erro ao carregar detalhes BFS: ${err.message}`, 'error');
        }
      }

      function renderEntityDetail(bfs) {
        const n = bfs.rootNode;
        const meta = LAYER_META[n.layer] || { code: 'L?', title: n.layer, color: '#94A3B8' };
        document.getElementById('selected-layer-tag').textContent = meta.code;

        const metaRows = Object.entries(n.metadata || {}).map(([k, v]) => `
          <div class="flex justify-between gap-2 text-[11px] py-1 border-b border-stroke/50 last:border-b-0">
            <span class="text-slate-500 font-mono-num">${escHtml(k)}</span>
            <span class="text-slate-200 text-right font-mono-num">${escHtml(v)}</span>
          </div>
        `).join('');

        const factorsHtml = (n.riskFactors || []).map(f => `
          <li class="text-[11px] text-slate-300 leading-snug flex items-start gap-1.5">
            <span class="text-amber-400 mt-0.5">▸</span>
            <span>${escHtml(f)}</span>
          </li>
        `).join('');

        document.getElementById('entity-detail-card').innerHTML = `
          <div class="flex items-start justify-between gap-2 mb-2">
            <div>
              <div class="text-[10px] font-mono-num font-bold uppercase tracking-wider" style="color:${meta.color}">${escHtml(meta.title)}</div>
              <h3 class="text-sm font-extrabold text-white leading-tight mt-0.5">${escHtml(n.label)}</h3>
              <div class="text-[11px] text-slate-400 mt-0.5">${escHtml(n.subType)}</div>
            </div>
            <span class="px-2 py-0.5 rounded text-[10px] font-mono-num font-bold whitespace-nowrap ${severityBadgeClass(n.severity)}">${escHtml(n.severity)}</span>
          </div>

          <div class="grid grid-cols-2 gap-2 my-2.5">
            <div class="bg-panel border border-stroke rounded-lg p-2">
              <div class="text-[9px] uppercase tracking-wider text-slate-500">Score de Risco</div>
              <div class="text-base font-bold font-mono-num text-white">${n.riskScore}%</div>
              <div class="w-full h-1.5 bg-slate-800 rounded-full mt-1 overflow-hidden">
                <div class="h-full ${severityBarColor(n.severity)}" style="width:${n.riskScore}%"></div>
              </div>
            </div>
            <div class="bg-panel border border-stroke rounded-lg p-2">
              <div class="text-[9px] uppercase tracking-wider text-slate-500">Exposição</div>
              <div class="text-xs font-bold font-mono-num text-amber-400 mt-1">${fmtMoney(n.financialExposure)}</div>
              <div class="text-[10px] font-mono-num text-slate-500 mt-0.5 truncate" title="${escHtml(n.document)}">${escHtml(n.document || 'N/D')}</div>
            </div>
          </div>

          <div class="text-[10px] font-mono-num text-slate-400 mb-2">
            <span class="text-slate-500">FONTES:</span> ${escHtml(n.source)}
          </div>

          ${metaRows ? `<div class="bg-panel/80 border border-stroke rounded-lg px-2.5 py-1.5 mb-2.5">${metaRows}</div>` : ''}

          <div>
            <div class="text-[10px] font-bold uppercase tracking-wider text-slate-400 mb-1.5">Fatores de Risco Estruturais</div>
            <ul class="space-y-1">${factorsHtml}</ul>
          </div>
        `;

        // Render BFS Layers L0..L4
        const bfsCard = document.getElementById('bfs-card');
        const bfsContainer = document.getElementById('bfs-layers-container');
        bfsCard.classList.remove('hidden');
        document.getElementById('bfs-summary-count').textContent = `${(bfs.nodes || []).length} nós • ${(bfs.edges || []).length} elos`;

        const layerKeys = ['L0', 'L1', 'L2', 'L3', 'L4'];
        const layerColorMap = { L0: '#F59E0B', L1: '#A855F7', L2: '#38BDF8', L3: '#EF4444', L4: '#10B981' };

        bfsContainer.innerHTML = layerKeys.map(lk => {
          const group = (bfs.nodesByLayer && bfs.nodesByLayer[lk]) ? bfs.nodesByLayer[lk] : [];
          if (group.length === 0) return '';
          return `
            <div>
              <div class="text-[10px] font-mono-num font-bold mb-1" style="color:${layerColorMap[lk]}">${lk} (${group.length})</div>
              <div class="flex flex-wrap gap-1">
                ${group.map(item => `
                  <button type="button" data-jump-id="${escHtml(item.id)}"
                    class="bfs-jump-btn px-2 py-0.5 rounded text-[10px] font-mono-num border ${item.id === n.id ? 'bg-amber-500/20 border-amber-500 text-amber-200' : 'bg-panel border-stroke text-slate-300 hover:border-amber-500/50'} transition">
                    ${escHtml(truncate(item.label, 22))} (${item.riskScore}%)
                  </button>
                `).join('')}
              </div>
            </div>
          `;
        }).join('');

        bfsContainer.querySelectorAll('.bfs-jump-btn').forEach(btn => {
          btn.addEventListener('click', () => selectNode(btn.dataset.jumpId, true));
        });
      }

      async function runGeminiAnalysis() {
        if (!state.selectedNodeId) {
          showBanner('Selecione uma entidade no grafo antes de solicitar o Parecer IA.', 'error');
          return;
        }

        const btn = document.getElementById('btn-run-gemini');
        const out = document.getElementById('gemini-output');
        btn.disabled = true;
        btn.innerHTML = '<span>Analisando...</span>';
        out.innerHTML = `<div class="font-mono-num text-amber-300 animate-pulse py-2">Consultando motor investigativo Gemini &amp; consolidando subgrafo L0–L4...</div>`;

        try {
          const res = await fetch(`/api/analyze/${encodeURIComponent(state.selectedNodeId)}`, { method: 'POST' });
          if (!res.ok) throw new Error(`HTTP ${res.status}`);
          const data = await res.json();

          const modeBadge = data.usedLiveAi
            ? '<span class="px-1.5 py-0.5 rounded bg-emerald-500/15 border border-emerald-500/35 text-emerald-300 text-[9px] font-mono-num">GEMINI LIVE AI</span>'
            : '<span class="px-1.5 py-0.5 rounded bg-amber-500/15 border border-amber-500/35 text-amber-300 text-[9px] font-mono-num">MOTOR FORENSE LOCAL</span>';

          out.innerHTML = `
            <div class="space-y-2.5">
              <div class="flex items-center justify-between gap-2">
                ${modeBadge}
                <span class="text-[10px] font-mono-num text-slate-500 truncate">${escHtml(data.model)}</span>
              </div>
              <p class="text-xs text-slate-200 leading-relaxed">${escHtml(data.executiveSummary)}</p>
              <div class="text-[11px] font-mono-num text-amber-300/90 bg-panel border border-stroke rounded-lg p-2">${escHtml(data.riskDiagnosis)}</div>
              <div>
                <div class="text-[10px] font-bold uppercase tracking-wider text-slate-400 mb-1">Achados-Chave</div>
                <ul class="space-y-1 text-[11px] text-slate-300">
                  ${(data.keyFindings || []).map(f => `<li class="flex gap-1.5"><span class="text-red-400">•</span><span>${escHtml(f)}</span></li>`).join('')}
                </ul>
              </div>
              <div>
                <div class="text-[10px] font-bold uppercase tracking-wider text-slate-400 mb-1">Diligências Recomendadas</div>
                <ul class="space-y-1 text-[11px] text-slate-300">
                  ${(data.recommendedAuditActions || []).map(a => `<li class="flex gap-1.5"><span class="text-emerald-400">✓</span><span>${escHtml(a)}</span></li>`).join('')}
                </ul>
              </div>
            </div>
          `;
        } catch (err) {
          out.innerHTML = `<div class="text-red-300 font-mono-num">Falha ao gerar parecer: ${escHtml(err.message)}</div>`;
        } finally {
          btn.disabled = false;
          btn.innerHTML = '<span>Gerar Parecer</span>';
        }
      }

      async function handleCnpjSubmit(ev) {
        ev.preventDefault();
        const input = document.getElementById('cnpj-input');
        const btn = document.getElementById('cnpj-submit-btn');
        const digits = (input.value || '').replace(/\D/g, '');

        if (digits.length !== 14) {
          showBanner('Informe um CNPJ válido com 14 dígitos (ex: 00.000.000/0001-91).', 'error');
          return;
        }

        btn.disabled = true;
        btn.innerHTML = '<span>Consultando...</span>';
        showBanner(`Consultando BrasilAPI ao vivo para CNPJ ${digits}...`, 'info');

        try {
          const res = await fetch(`/api/cnpj/${encodeURIComponent(digits)}`);
          const data = await res.json();

          if (!res.ok || !data.success) {
            showBanner(data.message || 'Não foi possível consultar o CNPJ informado.', 'error');
            return;
          }

          if (data.snapshot) {
            state.snapshot = data.snapshot;
            renderKpis(data.snapshot.metrics);
            renderAnomalies(data.snapshot.anomalies);
            computeLayout(data.snapshot.nodes);
            renderSvgGraph();
          } else {
            await loadGraph();
          }

          if (data.companyNode && data.companyNode.id) {
            await selectNode(data.companyNode.id, true);
          }

          const extra = data.transparenciaEnriched
            ? ` + ${data.transparenciaContractsFound} contrato(s) CGU ingerido(s).`
            : '';
          showBanner(`${data.message}${extra}`, 'success');
          input.value = '';
        } catch (err) {
          showBanner(`Erro na consulta BrasilAPI: ${err.message}`, 'error');
        } finally {
          btn.disabled = false;
          btn.innerHTML = `
            <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2.2" d="M12 4v16m8-8H4"/>
            </svg>
            <span>Ingerir CNPJ</span>`;
        }
      }

      // ZOOM & PAN CONTROLS
      function applyTransform() {
        const vp = document.getElementById('svg-viewport');
        vp.setAttribute('transform', `translate(${state.transform.x}, ${state.transform.y}) scale(${state.transform.scale})`);
        document.getElementById('zoom-label').textContent = `${Math.round(state.transform.scale * 100)}%`;
      }

      function zoomBy(factor) {
        state.transform.scale = Math.min(2.4, Math.max(0.45, state.transform.scale * factor));
        applyTransform();
      }

      function resetView() {
        state.transform = { x: 0, y: 0, scale: 1 };
        applyTransform();
      }

      function exportSvgVector() {
        const svgEl = document.getElementById('graph-svg');
        const clone = svgEl.cloneNode(true);
        clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');

        const bgRect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
        bgRect.setAttribute('width', '100%');
        bgRect.setAttribute('height', '100%');
        bgRect.setAttribute('fill', '#0B0D11');
        clone.insertBefore(bgRect, clone.firstChild);

        const serializer = new XMLSerializer();
        const svgString = '<?xml version="1.0" encoding="UTF-8"?>\n' + serializer.serializeToString(clone);
        const blob = new Blob([svgString], { type: 'image/svg+xml;charset=utf-8' });
        const url = URL.createObjectURL(blob);

        const a = document.createElement('a');
        a.href = url;
        a.download = `sentinel-graph-vetorial-${new Date().toISOString().slice(0, 16).replace(/[:T]/g, '-')}.svg`;
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(url);
        showBanner('Mapa vetorial SVG exportado com sucesso.', 'success');
      }

      function bindEvents() {
        document.getElementById('cnpj-form').addEventListener('submit', handleCnpjSubmit);
        document.getElementById('btn-run-gemini').addEventListener('click', runGeminiAnalysis);

        // Search debounce
        let searchTimer = null;
        document.getElementById('entity-search-input').addEventListener('input', (ev) => {
          clearTimeout(searchTimer);
          const q = ev.target.value.trim();
          searchTimer = setTimeout(() => {
            state.searchQuery = q;
            state.highlightedNodeIds.clear();
            state.highlightedEdgeIds.clear();
            loadGraph(q);
          }, 260);
        });

        // Severity filter buttons
        document.querySelectorAll('.sev-filter-btn').forEach(btn => {
          btn.addEventListener('click', () => fetchFilteredAnomalies(btn.dataset.sev));
        });

        // Graph toolbar actions
        document.getElementById('btn-zoom-in').addEventListener('click', () => zoomBy(1.18));
        document.getElementById('btn-zoom-out').addEventListener('click', () => zoomBy(0.84));
        document.getElementById('btn-zoom-fit').addEventListener('click', resetView);

        document.getElementById('btn-reset-focus').addEventListener('click', () => {
          state.highlightedNodeIds.clear();
          state.highlightedEdgeIds.clear();
          state.cyclesHighlighted = false;
          document.getElementById('graph-mode-badge').textContent = 'Visão Completa';
          renderSvgGraph();
          resetView();
        });

        document.getElementById('btn-highlight-cycles').addEventListener('click', () => {
          if (!state.snapshot || !state.snapshot.cycles || state.snapshot.cycles.length === 0) {
            showBanner('Nenhum circuito fechado detectado no grafo atual.', 'info');
            return;
          }
          state.cyclesHighlighted = true;
          state.highlightedNodeIds.clear();
          state.highlightedEdgeIds.clear();
          state.snapshot.cycles.forEach(c => {
            (c.nodeIds || []).forEach(id => state.highlightedNodeIds.add(id));
            (c.edgeIds || []).forEach(id => state.highlightedEdgeIds.add(id));
          });
          document.getElementById('graph-mode-badge').textContent = `Circuitos Fechados (${state.snapshot.cycles.length})`;
          renderSvgGraph();
          showBanner(`Destacando ${state.snapshot.cycles.length} circuito(s) fechado(s) de alto risco.`, 'info');
        });

        // SVG wheel zoom & drag pan
        const container = document.getElementById('graph-container');
        container.addEventListener('wheel', (ev) => {
          ev.preventDefault();
          zoomBy(ev.deltaY < 0 ? 1.1 : 0.91);
        }, { passive: false });

        container.addEventListener('mousedown', (ev) => {
          if (ev.target.closest('.svg-node')) return;
          state.isPanning = true;
          state.panStart = { x: ev.clientX - state.transform.x, y: ev.clientY - state.transform.y };
        });

        window.addEventListener('mousemove', (ev) => {
          if (!state.isPanning) return;
          state.transform.x = ev.clientX - state.panStart.x;
          state.transform.y = ev.clientY - state.panStart.y;
          applyTransform();
        });

        window.addEventListener('mouseup', () => { state.isPanning = false; });

        // Real Export Hub bindings
        document.getElementById('btn-export-csv').addEventListener('click', () => {
          window.location.href = '/api/export/csv';
          showBanner('Download do arquivo CSV de entidades iniciado.', 'success');
        });

        document.getElementById('btn-export-json').addEventListener('click', () => {
          window.location.href = '/api/export/json';
          showBanner('Download do snapshot JSON completo do grafo iniciado.', 'success');
        });

        document.getElementById('btn-export-svg').addEventListener('click', exportSvgVector);

        document.getElementById('btn-export-report').addEventListener('click', () => {
          const url = state.selectedNodeId
            ? `/api/export/report?nodeId=${encodeURIComponent(state.selectedNodeId)}`
            : '/api/export/report';
          window.open(url, '_blank', 'noopener');
          showBanner('Dossiê Forense HTML aberto em nova aba — pronto para salvar/imprimir em PDF.', 'success');
        });
      }

      // Bootstrap
      bindEvents();
      loadHealth();
      loadGraph().catch(err => showBanner(`Erro inicial: ${err.message}`, 'error'));
    })();
  </script>
</body>
</html>
""";
                   }
