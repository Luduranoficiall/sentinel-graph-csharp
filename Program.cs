using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SentinelGraph.Connectors;
using SentinelGraph.Engine;
using SentinelGraph.Intelligence;
using SentinelGraph.Web;

var builder = WebApplication.CreateBuilder(args);

// Suporte nativo à variável PORT (Render, Railway, Fly.io, Cloud Run)
var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(portEnv) && int.TryParse(portEnv, out var port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Serialização JSON corporativa: camelCase + Enums como strings legíveis
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = false;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// HttpClients nomeados com timeouts e User-Agent adequados para APIs públicas brasileiras e Gemini
builder.Services.AddHttpClient("BrasilApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SentinelGraph/1.0 (+AuditoriaPublica-OSINT)");
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

builder.Services.AddHttpClient("Transparencia", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SentinelGraph/1.0 (+AuditoriaPublica-OSINT)");
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

builder.Services.AddHttpClient("Camara", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SentinelGraph/1.0 (+AuditoriaPublica-OSINT)");
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

builder.Services.AddHttpClient("Gemini", client =>
{
    client.Timeout = TimeSpan.FromSeconds(35);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SentinelGraph/1.0 (+GeminiForensicAnalyst)");
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

// Serviços de domínio do Sentinel Graph
builder.Services.AddSingleton<GraphEngine>();
builder.Services.AddSingleton<AnomalyDetector>();
builder.Services.AddSingleton<OpenDataConnectors>();
builder.Services.AddSingleton<GeminiAnalyst>();

var app = builder.Build();

// Cabeçalhos de segurança HTTP
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    await next();
});

// Carrega o dataset forense realista em memória na inicialização
using (var scope = app.Services.CreateScope())
{
    var connectors = scope.ServiceProvider.GetRequiredService<OpenDataConnectors>();
    connectors.SeedRealisticDataset();
}

var ptBr = CultureInfo.GetCultureInfo("pt-BR");
var prettyJsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() }
};

// =========================================================================
// 1. PAINEL WEB FORENSE DARK PREMIUM (GET /)
// =========================================================================
app.MapGet("/", () => Results.Content(DashboardUi.RenderHtml(), "text/html; charset=utf-8"));

// =========================================================================
// 2. HEALTHCHECK & STATUS DOS CONECTORES (GET /health)
// =========================================================================
app.MapGet("/health", (
    GraphEngine graph,
    AnomalyDetector detector,
    OpenDataConnectors connectors,
    GeminiAnalyst gemini) =>
{
    var anomalies = detector.DetectAll();
    var cycles = graph.DetectClosedCircuits();
    var metrics = graph.ComputeMetrics(anomalies, cycles);

    return Results.Ok(new
    {
        status = "healthy",
        service = "SENTINEL GRAPH — Auditoria Pública & Inteligência em Grafos",
        runtime = ".NET 8.0",
        timestampUtc = DateTime.UtcNow,
        integrations = new
        {
            brasilApi = new { configured = true, mode = "live-public" },
            camaraDadosAbertos = new { configured = true, mode = "live-public" },
            portalTransparencia = new { configured = connectors.HasTransparenciaKey, envVar = "TRANSPARENCIA_API_KEY" },
            googleGemini = new { configured = gemini.IsConfigured, model = gemini.ConfiguredModel, envVar = "GEMINI_API_KEY" }
        },
        graph = new
        {
            entities = metrics.MappedEntities,
            connections = metrics.TotalConnections,
            anomalies = anomalies.Count,
            closedCircuits = metrics.ClosedCircuitsCount
        }
    });
});

// =========================================================================
// 3. GRAFO COMPLETO, BUSCA E BFS MULTI-NÍVEL (L0–L4)
// =========================================================================
app.MapGet("/api/graph", (
    string? q,
    GraphEngine graph,
    AnomalyDetector detector) =>
{
    var anomalies = detector.DetectAll();
    var cycles = graph.DetectClosedCircuits();
    var metrics = graph.ComputeMetrics(anomalies, cycles);

    if (!string.IsNullOrWhiteSpace(q))
    {
        var matchedNodes = graph.SearchNodes(q);
        var matchedIds = matchedNodes.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matchedEdges = graph.GetAllEdges()
            .Where(e => matchedIds.Contains(e.SourceId) || matchedIds.Contains(e.TargetId))
            .ToList();

        // Inclui nós adjacentes para manter contexto visual da busca
        var allNodesMap = graph.GetAllNodes().ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var e in matchedEdges)
        {
            matchedIds.Add(e.SourceId);
            matchedIds.Add(e.TargetId);
        }

        var expandedNodes = matchedIds
            .Where(allNodesMap.ContainsKey)
            .Select(id => allNodesMap[id])
            .OrderByDescending(n => n.RiskScore)
            .ToList();

        return Results.Ok(new GraphSnapshot(expandedNodes, matchedEdges, cycles, metrics, anomalies));
    }

    var snapshot = new GraphSnapshot(
        Nodes: graph.GetAllNodes(),
        Edges: graph.GetAllEdges(),
        Cycles: cycles,
        Metrics: metrics,
        Anomalies: anomalies);

    return Results.Ok(snapshot);
});

app.MapGet("/api/graph/bfs/{nodeId}", (
    string nodeId,
    int? depth,
    GraphEngine graph,
    AnomalyDetector detector) =>
{
    var maxDepth = Math.Clamp(depth ?? 4, 1, 6);
    var subgraph = graph.TraverseBfs(nodeId, maxDepth, bidirectional: true);
    if (subgraph is null)
        return Results.NotFound(new { error = $"Entidade '{nodeId}' não localizada no grafo." });

    var nodeAnomalies = detector.DetectForNode(nodeId);
    var nodeCycles = graph.DetectClosedCircuits()
        .Where(c => c.NodeIds.Contains(nodeId, StringComparer.OrdinalIgnoreCase))
        .ToList();

    return Results.Ok(new
    {
        subgraph.RootNode,
        subgraph.Nodes,
        subgraph.Edges,
        subgraph.Distances,
        subgraph.NodesByLayer,
        anomalies = nodeAnomalies,
        cycles = nodeCycles
    });
});

// =========================================================================
// 4. ANOMALIAS DETERMINÍSTICAS (FILTRÁVEL POR SEVERIDADE / NÓ)
// =========================================================================
app.MapGet("/api/anomalies", (
    string? severity,
    string? nodeId,
    AnomalyDetector detector) =>
{
    IEnumerable<AnomalyAlert> alerts = string.IsNullOrWhiteSpace(nodeId)
        ? detector.DetectAll()
        : detector.DetectForNode(nodeId);

    if (!string.IsNullOrWhiteSpace(severity) &&
        !severity.Equals("ALL", StringComparison.OrdinalIgnoreCase) &&
        !severity.Equals("TODOS", StringComparison.OrdinalIgnoreCase))
    {
        var norm = GraphEngine.NormalizeText(severity).ToUpperInvariant();
        if (Enum.TryParse<RiskSeverity>(norm, ignoreCase: true, out var parsedSeverity))
        {
            alerts = alerts.Where(a => a.Severity == parsedSeverity);
        }
    }

    var list = alerts.ToList();
    return Results.Ok(new
    {
        total = list.Count,
        items = list
    });
});

// =========================================================================
// 5. CONSULTA CNPJ AO VIVO (BRASILAPI + PORTAL DA TRANSPARÊNCIA) E CONECTORES
// =========================================================================
app.MapGet("/api/cnpj/{cnpj}", async (
    string cnpj,
    OpenDataConnectors connectors,
    GraphEngine graph,
    AnomalyDetector detector,
    CancellationToken ct) =>
{
    var result = await connectors.IngestCnpjLiveAsync(cnpj, ct);
    if (!result.Success)
        return Results.BadRequest(result);

    var anomalies = detector.DetectAll();
    var cycles = graph.DetectClosedCircuits();
    var metrics = graph.ComputeMetrics(anomalies, cycles);

    return Results.Ok(new
    {
        result.Success,
        result.Message,
        result.Cnpj,
        result.CompanyNode,
        result.PartnerNodes,
        result.CreatedEdges,
        result.TransparenciaEnriched,
        result.TransparenciaContractsFound,
        snapshot = new GraphSnapshot(graph.GetAllNodes(), graph.GetAllEdges(), cycles, metrics, anomalies)
    });
});

app.MapGet("/api/connectors/camara/{nome}", async (
    string nome,
    int? ano,
    OpenDataConnectors connectors,
    CancellationToken ct) =>
{
    var status = await connectors.IngestDeputadoCamaraAsync(nome, ano, ct);
    return status.Success ? Results.Ok(status) : Results.BadRequest(status);
});

app.MapGet("/api/connectors/emendas", async (
    string autor,
    int? ano,
    OpenDataConnectors connectors,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(autor))
        return Results.BadRequest(new { error = "Informe o parâmetro 'autor'." });

    var status = await connectors.QueryTransparenciaEmendasAsync(autor, ano ?? DateTime.UtcNow.Year, ct);
    return status.Success ? Results.Ok(status) : Results.BadRequest(status);
});

// =========================================================================
// 6. PARECER TÉCNICO INVESTIGATIVO — GOOGLE GEMINI API (GET / POST)
// =========================================================================
app.MapGet("/api/analyze/{nodeId}", async (
    string nodeId,
    GeminiAnalyst analyst,
    CancellationToken ct) =>
{
    var report = await analyst.AnalyzeNodeAsync(nodeId, ct);
    return report is null
        ? Results.NotFound(new { error = $"Entidade '{nodeId}' não encontrada para análise." })
        : Results.Ok(report);
});

app.MapPost("/api/analyze/{nodeId}", async (
    string nodeId,
    GeminiAnalyst analyst,
    CancellationToken ct) =>
{
    var report = await analyst.AnalyzeNodeAsync(nodeId, ct);
    return report is null
        ? Results.NotFound(new { error = $"Entidade '{nodeId}' não encontrada para análise." })
        : Results.Ok(report);
});

// =========================================================================
// 7. CENTRAL DE EXPORTAÇÃO REAL (CSV, JSON E DOSSIÊ HTML PARA PDF)
// =========================================================================
app.MapGet("/api/export/csv", (GraphEngine graph) =>
{
    var nodes = graph.GetAllNodes();
    var sb = new StringBuilder();

    sb.AppendLine("ID;Nome;Camada;Subtipo;Documento;ScoreRisco;Severidade;ExposicaoFinanceiraBRL;DataRegistro;FontesOficiais;FatoresRisco");

    foreach (var n in nodes)
    {
        sb.Append(CsvEscape(n.Id)).Append(';')
          .Append(CsvEscape(n.Label)).Append(';')
          .Append(CsvEscape(n.Layer.ToString())).Append(';')
          .Append(CsvEscape(n.SubType)).Append(';')
          .Append(CsvEscape(n.Document)).Append(';')
          .Append(n.RiskScore).Append(';')
          .Append(n.Severity).Append(';')
          .Append(n.FinancialExposure.ToString("F2", ptBr)).Append(';')
          .Append(n.CreatedDate?.ToString("yyyy-MM-dd") ?? string.Empty).Append(';')
          .Append(CsvEscape(n.Source)).Append(';')
          .Append(CsvEscape(string.Join(" | ", n.RiskFactors)))
          .AppendLine();
    }

    // UTF-8 com BOM para abertura direta e sem acentos quebrados no Excel
    var preamble = Encoding.UTF8.GetPreamble();
    var contentBytes = Encoding.UTF8.GetBytes(sb.ToString());
    var finalBytes = new byte[preamble.Length + contentBytes.Length];
    Buffer.BlockCopy(preamble, 0, finalBytes, 0, preamble.Length);
    Buffer.BlockCopy(contentBytes, 0, finalBytes, preamble.Length, contentBytes.Length);

    var fileName = $"sentinel-graph-entidades-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";
    return Results.File(finalBytes, "text/csv; charset=utf-8", fileName);
});

app.MapGet("/api/export/json", (GraphEngine graph, AnomalyDetector detector) =>
{
    var anomalies = detector.DetectAll();
    var cycles = graph.DetectClosedCircuits();
    var metrics = graph.ComputeMetrics(anomalies, cycles);

    var exportPayload = new
    {
        generatedAtUtc = DateTime.UtcNow,
        system = "SENTINEL GRAPH — Auditoria Pública & Inteligência em Grafos",
        metrics,
        nodes = graph.GetAllNodes(),
        edges = graph.GetAllEdges(),
        cycles,
        anomalies
    };

    var json = JsonSerializer.Serialize(exportPayload, prettyJsonOptions);
    var bytes = Encoding.UTF8.GetBytes(json);
    var fileName = $"sentinel-graph-completo-{DateTime.UtcNow:yyyyMMdd-HHmm}.json";

    return Results.File(bytes, "application/json; charset=utf-8", fileName);
});

app.MapGet("/api/export/report", async (
    string? nodeId,
    bool? download,
    GraphEngine graph,
    AnomalyDetector detector,
    GeminiAnalyst analyst,
    CancellationToken ct) =>
{
    var allNodes = graph.GetAllNodes();
    var targetNode = !string.IsNullOrWhiteSpace(nodeId)
        ? graph.GetNode(nodeId)
        : allNodes.FirstOrDefault();

    var anomalies = targetNode is not null && !string.IsNullOrWhiteSpace(nodeId)
        ? detector.DetectForNode(targetNode.Id)
        : detector.DetectAll();

    var allAnomalies = detector.DetectAll();
    var cycles = graph.DetectClosedCircuits();
    var metrics = graph.ComputeMetrics(allAnomalies, cycles);

    SubgraphResult? subgraph = targetNode is not null
        ? graph.TraverseBfs(targetNode.Id, maxDepth: 4, bidirectional: true)
        : null;

    GeminiAnalysisResult? aiOpinion = targetNode is not null
        ? await analyst.AnalyzeNodeAsync(targetNode.Id, ct)
        : null;

    var html = BuildPrintableDossierHtml(targetNode, subgraph, anomalies, cycles, metrics, aiOpinion, ptBr);

    if (download == true)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        var slug = targetNode is not null ? GraphEngine.NormalizeText(targetNode.Id).Replace(" ", "-") : "geral";
        return Results.File(bytes, "text/html; charset=utf-8", $"sentinel-dossie-{slug}-{DateTime.UtcNow:yyyyMMdd-HHmm}.html");
    }

    return Results.Content(html, "text/html; charset=utf-8");
});

app.Run();

// =========================================================================
// HELPERS DE EXPORTAÇÃO (CSV SEGURO + DOSSIÊ HTML IMPRIMÍVEL EM PDF)
// =========================================================================
static string CsvEscape(string? value)
{
    if (string.IsNullOrEmpty(value))
        return "\"\"";

    var sanitized = value.Replace("\r", " ").Replace("\n", " ");
    // Proteção contra CSV Formula Injection em planilhas
    if (sanitized.Length > 0 && (sanitized[0] == '=' || sanitized[0] == '+' || sanitized[0] == '-' || sanitized[0] == '@'))
        sanitized = "'" + sanitized;

    return $"\"{sanitized.Replace("\"", "\"\"")}\"";
}

static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

static string BuildPrintableDossierHtml(
    GraphNode? target,
    SubgraphResult? subgraph,
    IReadOnlyList<AnomalyAlert> anomalies,
    IReadOnlyList<CyclePath> cycles,
    GraphMetrics metrics,
    GeminiAnalysisResult? aiOpinion,
    CultureInfo ptBr)
{
    var sb = new StringBuilder();
    var reportTitle = target is not null
        ? $"Dossiê Forense — {target.Label}"
        : "Dossiê Consolidado de Auditoria Pública";

    sb.AppendLine("<!DOCTYPE html>");
    sb.AppendLine("<html lang=\"pt-BR\">");
    sb.AppendLine("<head>");
    sb.AppendLine("  <meta charset=\"utf-8\" />");
    sb.AppendLine($"  <title>SENTINEL GRAPH | {H(reportTitle)}</title>");
    sb.AppendLine("  <style>");
    sb.AppendLine("    :root { --bg: #ffffff; --ink: #0f172a; --muted: #475569; --border: #cbd5e1; --crit: #dc2626; --high: #d97706; --med: #2563eb; --accent: #b45309; }");
    sb.AppendLine("    * { box-sizing: border-box; }");
    sb.AppendLine("    body { font-family: 'Segoe UI', system-ui, -apple-system, sans-serif; color: var(--ink); background: #f1f5f9; margin: 0; padding: 24px; line-height: 1.5; }");
    sb.AppendLine("    .page { max-width: 980px; margin: 0 auto; background: var(--bg); padding: 36px 42px; border: 1px solid var(--border); box-shadow: 0 10px 30px rgba(15,23,42,.08); }");
    sb.AppendLine("    .toolbar { max-width: 980px; margin: 0 auto 14px; display: flex; justify-content: space-between; align-items: center; }");
    sb.AppendLine("    .btn { background: #0f172a; color: #f8fafc; border: none; padding: 10px 18px; border-radius: 6px; font-weight: 600; cursor: pointer; font-size: 13px; }");
    sb.AppendLine("    .btn:hover { background: #1e293b; }");
    sb.AppendLine("    header { border-bottom: 3px solid #0f172a; padding-bottom: 16px; margin-bottom: 24px; display: flex; justify-content: space-between; align-items: flex-end; }");
    sb.AppendLine("    .brand { font-size: 11px; letter-spacing: 0.16em; text-transform: uppercase; color: var(--accent); font-weight: 700; }");
    sb.AppendLine("    h1 { margin: 4px 0 0; font-size: 24px; color: #0f172a; }");
    sb.AppendLine("    .meta { text-align: right; font-size: 12px; color: var(--muted); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }");
    sb.AppendLine("    .kpis { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; margin-bottom: 24px; }");
    sb.AppendLine("    .kpi { border: 1px solid var(--border); border-radius: 6px; padding: 12px; background: #f8fafc; }");
    sb.AppendLine("    .kpi-label { font-size: 10px; text-transform: uppercase; letter-spacing: 0.08em; color: var(--muted); font-weight: 700; }");
    sb.AppendLine("    .kpi-value { font-size: 18px; font-weight: 800; margin-top: 4px; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }");
    sb.AppendLine("    h2 { font-size: 14px; text-transform: uppercase; letter-spacing: 0.08em; border-bottom: 1px solid var(--border); padding-bottom: 6px; margin: 26px 0 12px; color: #0f172a; }");
    sb.AppendLine("    .badge { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 11px; font-weight: 700; font-family: ui-monospace, monospace; }");
    sb.AppendLine("    .b-CRITICO { background: #fee2e2; color: #991b1b; border: 1px solid #fca5a5; }");
    sb.AppendLine("    .b-ALTA { background: #fef3c7; color: #92400e; border: 1px solid #fcd34d; }");
    sb.AppendLine("    .b-MEDIA { background: #dbeafe; color: #1e40af; border: 1px solid #93c5fd; }");
    sb.AppendLine("    .b-BAIXA { background: #e2e8f0; color: #334155; border: 1px solid #cbd5e1; }");
    sb.AppendLine("    .card { border: 1px solid var(--border); border-radius: 6px; padding: 14px 16px; margin-bottom: 12px; background: #fff; page-break-inside: avoid; }");
    sb.AppendLine("    .mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px; }");
    sb.AppendLine("    table { width: 100%; border-collapse: collapse; font-size: 12px; margin-top: 8px; }");
    sb.AppendLine("    th, td { border: 1px solid var(--border); padding: 8px 10px; text-align: left; vertical-align: top; }");
    sb.AppendLine("    th { background: #f1f5f9; font-weight: 700; text-transform: uppercase; font-size: 10px; letter-spacing: 0.06em; }");
    sb.AppendLine("    ul { margin: 8px 0; padding-left: 20px; }");
    sb.AppendLine("    li { margin-bottom: 4px; font-size: 13px; }");
    sb.AppendLine("    footer { margin-top: 32px; padding-top: 12px; border-top: 1px solid var(--border); font-size: 11px; color: var(--muted); display: flex; justify-content: space-between; }");
    sb.AppendLine("    @media print { body { background: #fff; padding: 0; } .toolbar { display: none; } .page { border: none; box-shadow: none; padding: 0; max-width: 100%; } }");
    sb.AppendLine("  </style>");
    sb.AppendLine("</head>");
    sb.AppendLine("<body>");
    sb.AppendLine("  <div class=\"toolbar\">");
    sb.AppendLine("    <span class=\"mono\" style=\"color:#475569\">SENTINEL GRAPH // DOSSIÊ PRONTO PARA IMPRESSÃO PDF</span>");
    sb.AppendLine("    <button class=\"btn\" onclick=\"window.print()\">Imprimir / Salvar como PDF</button>");
    sb.AppendLine("  </div>");
    sb.AppendLine("  <div class=\"page\">");
    sb.AppendLine("    <header>");
    sb.AppendLine("      <div>");
    sb.AppendLine("        <div class=\"brand\">SENTINEL GRAPH — AUDITORIA PÚBLICA &amp; INTELIGÊNCIA EM GRAFOS</div>");
    sb.AppendLine($"        <h1>{H(reportTitle)}</h1>");
    sb.AppendLine("      </div>");
    sb.AppendLine("      <div class=\"meta\">");
    sb.AppendLine($"        <div>EMISSÃO (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}</div>");
    sb.AppendLine($"        <div>CLASSIFICAÇÃO: USO RESTRITO / AUDITORIA</div>");
    sb.AppendLine("      </div>");
    sb.AppendLine("    </header>");

    sb.AppendLine("    <section class=\"kpis\">");
    sb.AppendLine($"      <div class=\"kpi\"><div class=\"kpi-label\">Exposição Total</div><div class=\"kpi-value\">{H(metrics.TotalFinancialExposure.ToString("C", ptBr))}</div></div>");
    sb.AppendLine($"      <div class=\"kpi\"><div class=\"kpi-label\">Irregularidades Críticas</div><div class=\"kpi-value\" style=\"color:#dc2626\">{metrics.CriticalIrregularities}</div></div>");
    sb.AppendLine($"      <div class=\"kpi\"><div class=\"kpi-label\">Entidades / Conexões</div><div class=\"kpi-value\">{metrics.MappedEntities} / {metrics.TotalConnections}</div></div>");
    sb.AppendLine($"      <div class=\"kpi\"><div class=\"kpi-label\">Fontes Cruzadas</div><div class=\"kpi-value\">{metrics.CrossedSourcesCount}</div></div>");
    sb.AppendLine("    </section>");

    if (target is not null)
    {
        sb.AppendLine("    <h2>1. Perfil Forense da Entidade Alvo</h2>");
        sb.AppendLine("    <div class=\"card\">");
        sb.AppendLine($"      <div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:8px;\">");
        sb.AppendLine($"        <strong style=\"font-size:16px;\">{H(target.Label)}</strong>");
        sb.AppendLine($"        <span class=\"badge b-{target.Severity}\">{target.Severity} • SCORE {target.RiskScore}%</span>");
        sb.AppendLine("      </div>");
        sb.AppendLine($"      <div class=\"mono\" style=\"color:#475569;margin-bottom:8px;\">ID: {H(target.Id)} | Camada: {H(target.Layer.ToString())} | Documento: {H(target.Document)} | Exposição: {H(target.FinancialExposure.ToString("C", ptBr))}</div>");
        sb.AppendLine($"      <div style=\"font-size:13px;margin-bottom:8px;\"><strong>Subtipo:</strong> {H(target.SubType)} &nbsp;|&nbsp; <strong>Fontes:</strong> {H(target.Source)}</div>");
        sb.AppendLine("      <strong style=\"font-size:12px;\">Fatores de Risco Estruturais:</strong>");
        sb.AppendLine("      <ul>");
        foreach (var factor in target.RiskFactors)
            sb.AppendLine($"        <li>{H(factor)}</li>");
        sb.AppendLine("      </ul>");
        sb.AppendLine("    </div>");
    }

    if (aiOpinion is not null)
    {
        sb.AppendLine("    <h2>2. Parecer Técnico Investigativo (IA Analítica)</h2>");
        sb.AppendLine("    <div class=\"card\">");
        sb.AppendLine($"      <div class=\"mono\" style=\"color:#b45309;margin-bottom:6px;\">MOTOR: {H(aiOpinion.Model)} | LIVE AI: {(aiOpinion.UsedLiveAi ? "SIM (Google Gemini)" : "MOTOR FORENSE LOCAL")}</div>");
        sb.AppendLine($"      <p style=\"margin:6px 0 10px;font-size:13.5px;\">{H(aiOpinion.ExecutiveSummary)}</p>");
        sb.AppendLine($"      <p style=\"margin:0 0 10px;font-size:12.5px;color:#475569;\"><strong>Diagnóstico:</strong> {H(aiOpinion.RiskDiagnosis)}</p>");
        sb.AppendLine("      <strong style=\"font-size:12px;\">Achados-Chave:</strong><ul>");
        foreach (var f in aiOpinion.KeyFindings)
            sb.AppendLine($"        <li>{H(f)}</li>");
        sb.AppendLine("      </ul>");
        sb.AppendLine("      <strong style=\"font-size:12px;\">Diligências de Auditoria Recomendadas:</strong><ul>");
        foreach (var act in aiOpinion.RecommendedAuditActions)
            sb.AppendLine($"        <li>{H(act)}</li>");
        sb.AppendLine("      </ul>");
        sb.AppendLine("    </div>");
    }

    sb.AppendLine($"    <h2>3. Anomalias Determinísticas Detectadas ({anomalies.Count})</h2>");
    if (anomalies.Count == 0)
    {
        sb.AppendLine("    <div class=\"card\">Nenhuma anomalia crítica registrada para o escopo selecionado.</div>");
    }
    else
    {
        foreach (var a in anomalies)
        {
            sb.AppendLine("    <div class=\"card\">");
            sb.AppendLine($"      <div style=\"display:flex;justify-content:space-between;align-items:center;\">");
            sb.AppendLine($"        <strong>{H(a.Title)}</strong>");
            sb.AppendLine($"        <span class=\"badge b-{a.Severity}\">{a.Severity} • {a.ConfidencePercent}%</span>");
            sb.AppendLine("      </div>");
            sb.AppendLine($"      <div class=\"mono\" style=\"color:#475569;margin:4px 0;\">PATTERN: {H(a.PatternCode)} | VALOR ENVOLVIDO: {H(a.InvolvedAmount.ToString("C", ptBr))}</div>");
            sb.AppendLine($"      <p style=\"margin:6px 0 4px;font-size:13px;\">{H(a.Description)}</p>");
            sb.AppendLine($"      <div class=\"mono\" style=\"color:#64748b;\">FONTES: {H(string.Join(" • ", a.Sources))}</div>");
            sb.AppendLine("    </div>");
        }
    }

    if (subgraph is not null)
    {
        sb.AppendLine($"    <h2>4. Cadeia de Relacionamentos Multi-Nível BFS L0–L4 ({subgraph.Edges.Count} conexões)</h2>");
        sb.AppendLine("    <table>");
        sb.AppendLine("      <thead><tr><th>Origem</th><th>Vínculo</th><th>Destino</th><th>Valor (R$)</th><th>Fonte Oficial</th></tr></thead>");
        sb.AppendLine("      <tbody>");
        var labelMap = subgraph.Nodes.ToDictionary(n => n.Id, n => n.Label, StringComparer.OrdinalIgnoreCase);
        foreach (var e in subgraph.Edges)
        {
            var src = labelMap.GetValueOrDefault(e.SourceId, e.SourceId);
            var dst = labelMap.GetValueOrDefault(e.TargetId, e.TargetId);
            var val = e.FinancialValue > 0 ? e.FinancialValue.ToString("C", ptBr) : "—";
            sb.AppendLine($"        <tr><td>{H(src)}</td><td class=\"mono\">{H(e.RelationType)}</td><td>{H(dst)}</td><td class=\"mono\">{H(val)}</td><td>{H(e.OfficialSource)}</td></tr>");
        }
        sb.AppendLine("      </tbody>");
        sb.AppendLine("    </table>");
    }

    if (cycles.Count > 0)
    {
        sb.AppendLine($"    <h2>5. Circuitos Fechados Identificados no Grafo ({cycles.Count})</h2>");
        foreach (var c in cycles)
        {
            sb.AppendLine($"    <div class=\"card\"><div class=\"mono\" style=\"color:#dc2626;font-weight:700;\">{H(c.Id)} | VOLUME: {H(c.TotalFinancialVolume.ToString("C", ptBr))}</div><div style=\"font-size:13px;margin-top:4px;\">{H(c.Summary)}</div></div>");
        }
    }

    sb.AppendLine("    <footer>");
    sb.AppendLine("      <span>SENTINEL GRAPH (.NET 8) — Relatório gerado automaticamente para fins de auditoria e inteligência em grafos.</span>");
    sb.AppendLine("      <span>Os indícios apontados constituem alertas analíticos sujeitos à verificação documental oficial.</span>");
    sb.AppendLine("    </footer>");
    sb.AppendLine("  </div>");
    sb.AppendLine("</body>");
    sb.AppendLine("</html>");

    return sb.ToString();
}
