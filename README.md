# SENTINEL GRAPH — Inteligência em Grafos & Auditoria de Dados Públicos (C# .NET 8)

Plataforma corporativa desenvolvida em **C# (.NET 8) puro** pela **Luduranoficiall** ([luduranoficiall.com](https://www.luduranoficiall.com)) para cruzamento de bases públicas abertas, mapeamento de vínculos societários e financeiros em grafo multi-nível (`L0` a `L4`), detecção automatizada de anomalias e geração de pareceres investigativos com **Google Gemini**.

---

## Visão Executiva & Diferenciais de Engenharia

* **Alta Performance em C# (.NET 8) Puro:** Arquitetura Minimal API com tipagem estrita, processamento assíncrono (`async/await`) e motor de grafos em memória sem depender de bancos de grafos pesados ou servidores de 128 GB de RAM.
* **Inteligência em Grafos (`L0` a `L4`):** Busca em largura/profundidade (`BFS/DFS`) e detecção de ciclos fechados conectando Agentes Públicos (`L0`), Familiares/Sócios (`L1`), Empresas/CNPJs (`L2`), Contratos/Emendas (`L3`) e Doadores/Órgãos (`L4`).
* **Dados Reais ao Vivo:** Consulta instantânea de CNPJs e Quadro Societário (QSA) via **BrasilAPI**, com suporte a chaves do **Portal da Transparência** e **Dados Abertos**.
* **Parecer Investigativo com IA:** Integração nativa com a **API do Google Gemini** para traduzir conexões complexas do grafo em relatórios executivos claros e acionáveis.
* **Central de Exportação Completa:** Download imediato de **Planilha CSV**, **Snapshot JSON do Grafo**, **Grafo Vetorial SVG** e **Dossiê Forense HTML** pronto para impressão em PDF.
* **Segurança por Padrão:** Zero credenciais no código-fonte — todas as chaves (`GEMINI_API_KEY`, `TRANSPARENCIA_API_KEY`) são injetadas via variáveis de ambiente.

---

## Arquitetura do Sistema

```text
[BrasilAPI / Portal da Transparência / Seed]
                     │
                     ▼
        [OpenDataConnectors.cs] ──► Normalização de Entidades & QSA
                     │
                     ▼
           [GraphEngine.cs] ──────► Grafo L0–L4 + BFS/DFS + Detecção de Ciclos
                     │
          ┌──────────┴──────────┐
          ▼                     ▼
[AnomalyDetector.cs]    [GeminiAnalyst.cs]
(Regras & Score 0–100%)  (Parecer Executivo IA)
          └──────────┬──────────┘
                     ▼
         [Program.cs + DashboardUi.cs]
   Painel Web Forense Dark + SVG + Exportação
