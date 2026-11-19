using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SentinelGraph.Engine;

namespace SentinelGraph.Intelligence;

public sealed class GeminiAnalyst
{
    private const string DefaultModel = "gemini-2.0-flash";
    private const string GeminiEndpointTemplate = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GraphEngine _graph;
    private readonly AnomalyDetector _anomalyDetector;
    private readonly ILogger<GeminiAnalyst> _logger;

    public GeminiAnalyst(
        IHttpClientFactory httpClientFactory,
        GraphEngine graph,
        AnomalyDetector anomalyDetector,
        ILogger<GeminiAnalyst> logger)
    {
        _httpClientFactory = httpClientFactory;
        _graph = graph;
        _anomalyDetector = anomalyDetector;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY"));

    public string ConfiguredModel =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_MODEL"))
            ? DefaultModel
            : Environment.GetEnvironmentVariable("GEMINI_MODEL")!.Trim();

    /// <summary>
    /// Gera um Parecer Técnico Investigativo em linguagem executiva sobre o nó selecionado,
    /// consultando a API nativa do Google Gemini via HttpClient (usando GEMINI_API_KEY).
    /// Caso a chave não esteja configurada ou haja indisponibilidade externa, aciona automaticamente
    /// o motor determinístico forense estruturado para garantir continuidade operacional.
    /// </summary>
    public async Task<GeminiAnalysisResult?> AnalyzeNodeAsync(string nodeId, CancellationToken ct = default)
    {
        var subgraph = _graph.TraverseBfs(nodeId, maxDepth: 4, bidirectional: true);
        if (subgraph is null)
            return null;

        var targetNode = subgraph.RootNode;
        var nodeAnomalies = _anomalyDetector.DetectForNode(nodeId);
        var nodeCycles = _graph.DetectClosedCircuits()
            .Where(c => c.NodeIds.Contains(nodeId, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BuildDeterministicExecutiveReport(targetNode, subgraph, nodeAnomalies, nodeCycles,
                fallbackReason: "GEMINI_API_KEY não definida no ambiente — parecer gerado pelo motor analítico local.");
        }

        var model = ConfiguredModel;
        var prompt = BuildForensicPrompt(targetNode, subgraph, nodeAnomalies, nodeCycles);

        try
        {
            var client = _httpClientFactory.CreateClient("Gemini");
            var endpoint = string.Format(CultureInfo.InvariantCulture, GeminiEndpointTemplate, Uri.EscapeDataString(model));

            var payload = new
            {
                system_instruction = new
                {
                    parts = new[]
                    {
                        new
                        {
                            text =
                                "Você é um Auditor-Chefe Forense Sênior especialista em controle externo (TCU/CGU), " +
                                "análise de grafos de conhecimento, Lei 14.133/2021 e OSINT governamental no Brasil. " +
                                "Produza pareceres técnicos objetivos, imparciais, em português formal executivo, " +
                                "baseando-se ESTRITAMENTE nos fatos, nós, arestas, valores e alertas fornecidos no contexto. " +
                                "Trate todos os indícios como hipóteses de auditoria que requerem verificação documental. " +
                                "Responda EXCLUSIVAMENTE em JSON válido com o schema: " +
                                "{\"executiveSummary\":\"string\",\"riskDiagnosis\":\"string\",\"keyFindings\":[\"string\"],\"recommendedAuditActions\":[\"string\"]}."
                        }
                    }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    topP = 0.85,
                    maxOutputTokens = 1200,
                    responseMimeType = "application/json"
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-goog-api-key", apiKey.Trim());

            using var response = await client.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini API retornou HTTP {Status}: {Body}", response.StatusCode, responseBody);
                return BuildDeterministicExecutiveReport(targetNode, subgraph, nodeAnomalies, nodeCycles,
                    fallbackReason: $"Fallback acionado (Gemini HTTP {(int)response.StatusCode}).");
            }

            var rawText = ExtractCandidateText(responseBody);
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return BuildDeterministicExecutiveReport(targetNode, subgraph, nodeAnomalies, nodeCycles,
                    fallbackReason: "Resposta vazia da API Gemini — fallback analítico acionado.");
            }

            var parsed = TryParseStructuredOpinion(rawText);
            if (parsed is not null)
            {
                return new GeminiAnalysisResult(
                    NodeId: targetNode.Id,
                    NodeLabel: targetNode.Label,
                    UsedLiveAi: true,
                    Model: model,
                    ExecutiveSummary: parsed.ExecutiveSummary,
                    RiskDiagnosis: parsed.RiskDiagnosis,
                    KeyFindings: parsed.KeyFindings.Count > 0
                        ? parsed.KeyFindings
                        : new[] { "Conexões estruturais avaliadas em profundidade L0–L4." },
                    RecommendedAuditActions: parsed.RecommendedAuditActions.Count > 0
                        ? parsed.RecommendedAuditActions
                        : new[] { "Requisitar processos administrativos e notas fiscais correspondentes." },
                    GeneratedAtUtc: DateTime.UtcNow);
            }

            // Se a IA respondeu texto livre fora do JSON, encapsula de forma segura
            return new GeminiAnalysisResult(
                NodeId: targetNode.Id,
                NodeLabel: targetNode.Label,
                UsedLiveAi: true,
                Model: model,
                ExecutiveSummary: rawText.Trim(),
                RiskDiagnosis: $"Classificação atual: {targetNode.Severity} ({targetNode.RiskScore}%).",
                KeyFindings: targetNode.RiskFactors.ToList(),
                RecommendedAuditActions: BuildRecommendedActions(targetNode, nodeAnomalies, nodeCycles),
                GeneratedAtUtc: DateTime.UtcNow);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Timeout ao consultar API do Google Gemini para o nó {NodeId}", nodeId);
            return BuildDeterministicExecutiveReport(targetNode, subgraph, nodeAnomalies, nodeCycles,
                fallbackReason: "Tempo limite excedido na chamada à API Gemini — parecer gerado localmente.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao consultar API do Google Gemini para o nó {NodeId}", nodeId);
            return BuildDeterministicExecutiveReport(targetNode, subgraph, nodeAnomalies, nodeCycles,
                fallbackReason: $"Falha de comunicação com Gemini ({ex.Message}) — parecer gerado localmente.");
        }
    }

    private static string BuildForensicPrompt(
        GraphNode target,
        SubgraphResult subgraph,
        IReadOnlyList<AnomalyAlert> anomalies,
        IReadOnlyList<CyclePath> cycles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== DOSSIÊ DE ENTRADA — SENTINEL GRAPH ===");
        sb.AppendLine($"Entidade Alvo: {target.Label}");
        sb.AppendLine($"ID: {target.Id} | Camada: {target.Layer} | Subtipo: {target.SubType}");
        sb.AppendLine($"Documento: {target.Document} | Data de Abertura/Registro: {target.CreatedDate?.ToString("yyyy-MM-dd") ?? "N/D"}");
        sb.AppendLine($"Score de Risco Calculado: {target.RiskScore}% ({target.Severity})");
        sb.AppendLine($"Exposição Financeira Associada: {target.FinancialExposure.ToString("C", PtBr)}");
        sb.AppendLine($"Fontes Oficiais: {target.Source}");

        if (target.Metadata.Count > 0)
        {
            sb.AppendLine("Metadados:");
            foreach (var kv in target.Metadata)
                sb.AppendLine($"  - {kv.Key}: {kv.Value}");
        }

        sb.AppendLine("\nFatores de Risco Estruturais:");
        foreach (var factor in target.RiskFactors)
            sb.AppendLine($"  - {factor}");

        sb.AppendLine($"\nSubgrafo BFS (L0–L4): {subgraph.Nodes.Count} entidades e {subgraph.Edges.Count} conexões:");
        var nodeLabels = subgraph.Nodes.ToDictionary(n => n.Id, n => $"{n.Label} [{n.Layer}]", StringComparer.OrdinalIgnoreCase);

        foreach (var edge in subgraph.Edges.Take(35))
        {
            var src = nodeLabels.GetValueOrDefault(edge.SourceId, edge.SourceId);
            var dst = nodeLabels.GetValueOrDefault(edge.TargetId, edge.TargetId);
            var val = edge.FinancialValue > 0 ? $" | Valor: {edge.FinancialValue.ToString("C", PtBr)}" : string.Empty;
            var dt = edge.EventDate.HasValue ? $" | Data: {edge.EventDate.Value:yyyy-MM-dd}" : string.Empty;
            sb.AppendLine($"  - {src} --[{edge.RelationType}{val}{dt}]--> {dst} (Fonte: {edge.OfficialSource})");
        }

        sb.AppendLine($"\nAlertas Determinísticos Relacionados ({anomalies.Count}):");
        foreach (var alert in anomalies)
        {
            sb.AppendLine($"  - [{alert.Severity} | {alert.ConfidencePercent}% | {alert.PatternCode}] {alert.Title}: {alert.Description}");
        }

        if (cycles.Count > 0)
        {
            sb.AppendLine($"\nCircuitos Fechados Detectados ({cycles.Count}):");
            foreach (var c in cycles)
                sb.AppendLine($"  - {c.Summary} | Volume: {c.TotalFinancialVolume.ToString("C", PtBr)}");
        }

        sb.AppendLine("\nElabore um parecer técnico executivo conciso, explicando as conexões suspeitas entre as camadas L0–L4, o diagnóstico de risco e 3 a 5 diligências prioritárias de auditoria.");
        return sb.ToString();
    }

    private static string? ExtractCandidateText(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return null;

        var firstCandidate = candidates[0];
        if (!firstCandidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.GetArrayLength() == 0)
            return null;

        var sb = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var txt))
                sb.Append(txt.GetString());
        }

        return sb.ToString();
    }

    private static StructuredOpinionDto? TryParseStructuredOpinion(string rawText)
    {
        try
        {
            var cleaned = rawText.Trim();
            if (cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewLine = cleaned.IndexOf('\n');
                var lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewLine > 0 && lastFence > firstNewLine)
                    cleaned = cleaned[(firstNewLine + 1)..lastFence].Trim();
            }

            var dto = JsonSerializer.Deserialize<StructuredOpinionDto>(cleaned, JsonOptions);
            if (dto is null || string.IsNullOrWhiteSpace(dto.ExecutiveSummary))
                return null;

            return dto;
        }
        catch
        {
            return null;
        }
    }

    private static GeminiAnalysisResult BuildDeterministicExecutiveReport(
        GraphNode target,
        SubgraphResult subgraph,
        IReadOnlyList<AnomalyAlert> anomalies,
        IReadOnlyList<CyclePath> cycles,
        string fallbackReason)
    {
        var totalSubgraphVolume = subgraph.Edges.Sum(e => e.FinancialValue);
        var topAnomalies = anomalies.Take(3).ToList();

        var summary = new StringBuilder();
        summary.Append(
            $"A entidade {target.Label} ({target.SubType}, camada {FormatLayerName(target.Layer)}) apresenta Score de Risco " +
            $"{target.RiskScore}% (severidade {target.Severity}), integrando um subgrafo multi-nível com {subgraph.Nodes.Count} " +
            $"entidades e {subgraph.Edges.Count} vínculos que movimentam {totalSubgraphVolume.ToString("C", PtBr)}. ");

        if (cycles.Any(c => c.IsDonationContractLoop))
        {
            summary.Append(
                "Foi identificado circuito financeiro fechado conectando doação eleitoral, destinação pública e empresa contratada, " +
                "caracterizando sinal de alerta máximo para eventual conflito de interesses ou direcionamento de recursos. ");
        }
        else if (topAnomalies.Count > 0)
        {
            summary.Append(
                $"Foram acionados {anomalies.Count} alerta(s) determinístico(s), com destaque para: " +
                $"{string.Join("; ", topAnomalies.Select(a => $"{a.PatternCode} ({a.ConfidencePercent}%)"))}. ");
        }
        else
        {
            summary.Append(
                "Não foram detectados padrões críticos diretos na topologia atual, recomendando-se monitoramento contínuo de novas contratações e alterações societárias.");
        }

        var diagnosis =
            $"Diagnóstico Consolidado ({target.Severity} — {target.RiskScore}%): " +
            $"{(anomalies.Count > 0 ? $"{anomalies.Count} padrão(ões) de anomalia ativo(s)" : "Baixa densidade de anomalias")} | " +
            $"{cycles.Count} circuito(s) fechado(s) | Nota operacional: {fallbackReason}";

        var findings = new List<string>();
        foreach (var a in anomalies.Take(4))
            findings.Add($"[{a.PatternCode} • {a.ConfidencePercent}%] {a.Title} — {a.Description}");

        if (findings.Count == 0)
        {
            foreach (var f in target.RiskFactors.Take(3))
                findings.Add(f);
        }

        var actions = BuildRecommendedActions(target, anomalies, cycles);

        return new GeminiAnalysisResult(
            NodeId: target.Id,
            NodeLabel: target.Label,
            UsedLiveAi: false,
            Model: "Sentinel Deterministic Forensic Engine (Offline/Fallback)",
            ExecutiveSummary: summary.ToString().Trim(),
            RiskDiagnosis: diagnosis,
            KeyFindings: findings,
            RecommendedAuditActions: actions,
            GeneratedAtUtc: DateTime.UtcNow);
    }

    private static List<string> BuildRecommendedActions(
        GraphNode target,
        IReadOnlyList<AnomalyAlert> anomalies,
        IReadOnlyList<CyclePath> cycles)
    {
        var actions = new List<string>();

        if (cycles.Any(c => c.IsDonationContractLoop))
            actions.Add("Requisitar extratos de subcontratação, notas fiscais eletrônicas e prestação de contas eleitorais (TSE) para verificar eventual retorno financeiro ao doador.");

        if (anomalies.Any(a => a.PatternCode.Equals("DUPLO_VINCULO_SIMULTANEO", StringComparison.OrdinalIgnoreCase)))
            actions.Add("Instaurar sindicância patrimonial e cruzar folha funcional (SIAPE/TCE) com alterações do QSA na Junta Comercial para apurar impedimento legal (Lei 14.133/2021, art. 14).");

        if (anomalies.Any(a => a.PatternCode.Equals("EMPRESA_RECEM_ABERTA", StringComparison.OrdinalIgnoreCase)))
            actions.Add("Verificar atestados de capacidade técnica, sede física, quadro de empregados (RAIS/eSocial) e compatibilidade do capital social da contratada.");

        if (anomalies.Any(a => a.PatternCode.Equals("AUTO_DIRECIONAMENTO_SOCIETARIO", StringComparison.OrdinalIgnoreCase)))
            actions.Add("Auditar trilha de tramitação da emenda/dispensa, pareceres jurídicos e eventuais vínculos de parentesco ou assessoramento prévio no gabinete.");

        if (actions.Count < 3)
        {
            actions.Add($"Consolidar dossiê probatório de {target.Label} com consulta atualizada no PNCP, Portal da Transparência e CEIS/CNEP.");
            actions.Add("Validar execução físico-financeira in loco e compatibilidade de preços unitários com referenciais SINAPI/Painel de Preços.");
        }

        return actions.Take(5).ToList();
    }

    private static string FormatLayerName(NodeLayer layer) => layer switch
    {
        NodeLayer.L0_AgentePublico => "L0 — Agente Público",
        NodeLayer.L1_FamiliarSocio => "L1 — Familiar / Sócio",
        NodeLayer.L2_EmpresaCnpj => "L2 — Empresa / CNPJ",
        NodeLayer.L3_ContratoEmenda => "L3 — Contrato / Emenda",
        NodeLayer.L4_DoadorOrgao => "L4 — Doador / Órgão",
        _ => layer.ToString()
    };

    private sealed class StructuredOpinionDto
    {
        [JsonPropertyName("executiveSummary")]
        public string ExecutiveSummary { get; set; } = string.Empty;

        [JsonPropertyName("riskDiagnosis")]
        public string RiskDiagnosis { get; set; } = string.Empty;

        [JsonPropertyName("keyFindings")]
        public List<string> KeyFindings { get; set; } = new();

        [JsonPropertyName("recommendedAuditActions")]
        public List<string> RecommendedAuditActions { get; set; } = new();
    }
}
