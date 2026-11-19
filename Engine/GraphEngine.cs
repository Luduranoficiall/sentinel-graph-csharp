using System.Globalization;
using System.Text;

namespace SentinelGraph.Engine;

public enum NodeLayer
{
    L0_AgentePublico = 0,
    L1_FamiliarSocio = 1,
    L2_EmpresaCnpj = 2,
    L3_ContratoEmenda = 3,
    L4_DoadorOrgao = 4
}

public enum RiskSeverity
{
    BAIXA = 0,
    MEDIA = 1,
    ALTA = 2,
    CRITICO = 3
}

public sealed class GraphNode
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public NodeLayer Layer { get; set; }
    public string SubType { get; set; } = string.Empty;
    public decimal FinancialExposure { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string Source { get; set; } = "Base Interna";
    public int RiskScore { get; set; }
    public RiskSeverity Severity { get; set; } = RiskSeverity.BAIXA;
    public List<string> RiskFactors { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public GraphNode Clone() => new()
    {
        Id = Id,
        Label = Label,
        Document = Document,
        Layer = Layer,
        SubType = SubType,
        FinancialExposure = FinancialExposure,
        CreatedDate = CreatedDate,
        Source = Source,
        RiskScore = RiskScore,
        Severity = Severity,
        RiskFactors = new List<string>(RiskFactors),
        Metadata = new Dictionary<string, string>(Metadata, StringComparer.OrdinalIgnoreCase)
    };
}

public sealed class GraphEdge
{
    public string Id { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string RelationType { get; set; } = string.Empty;
    public double Weight { get; set; } = 0.5;
    public decimal FinancialValue { get; set; }
    public string OfficialSource { get; set; } = string.Empty;
    public DateTime? EventDate { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public GraphEdge Clone() => new()
    {
        Id = Id,
        SourceId = SourceId,
        TargetId = TargetId,
        RelationType = RelationType,
        Weight = Weight,
        FinancialValue = FinancialValue,
        OfficialSource = OfficialSource,
        EventDate = EventDate,
        Metadata = new Dictionary<string, string>(Metadata, StringComparer.OrdinalIgnoreCase)
    };
}

public sealed record CyclePath(
    string Id,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> NodeLabels,
    IReadOnlyList<string> EdgeIds,
    decimal TotalFinancialVolume,
    bool IsDonationContractLoop,
    string Summary
);

public sealed record SubgraphResult(
    GraphNode RootNode,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyDictionary<string, int> Distances,
    IReadOnlyDictionary<string, List<GraphNode>> NodesByLayer
);

public sealed record GraphMetrics(
    decimal TotalFinancialExposure,
    int CriticalIrregularities,
    int HighIrregularities,
    int MediumIrregularities,
    int MappedEntities,
    int TotalConnections,
    int CrossedSourcesCount,
    IReadOnlyList<string> CrossedSources,
    int ClosedCircuitsCount
);

public sealed record AnomalyAlert(
    string Id,
    string PatternCode,
    string Title,
    string Description,
    RiskSeverity Severity,
    int ConfidencePercent,
    decimal InvolvedAmount,
    string PrimaryNodeId,
    string PrimaryNodeLabel,
    IReadOnlyList<string> RelatedNodeIds,
    IReadOnlyList<string> Sources,
    DateTime DetectedAtUtc
);

public sealed record GeminiAnalysisResult(
    string NodeId,
    string NodeLabel,
    bool UsedLiveAi,
    string Model,
    string ExecutiveSummary,
    string RiskDiagnosis,
    IReadOnlyList<string> KeyFindings,
    IReadOnlyList<string> RecommendedAuditActions,
    DateTime GeneratedAtUtc
);

public sealed record GraphSnapshot(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<CyclePath> Cycles,
    GraphMetrics Metrics,
    IReadOnlyList<AnomalyAlert> Anomalies
);

public sealed class GraphEngine
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly Dictionary<string, GraphNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GraphEdge> _edges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _outgoing = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _incoming = new(StringComparer.OrdinalIgnoreCase);

    public GraphNode UpsertNode(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (string.IsNullOrWhiteSpace(node.Id))
            throw new ArgumentException("Node Id é obrigatório.", nameof(node));

        _lock.EnterWriteLock();
        try
        {
            if (_nodes.TryGetValue(node.Id, out var existing))
            {
                existing.Label = string.IsNullOrWhiteSpace(node.Label) ? existing.Label : node.Label;
                existing.Document = string.IsNullOrWhiteSpace(node.Document) ? existing.Document : node.Document;
                existing.SubType = string.IsNullOrWhiteSpace(node.SubType) ? existing.SubType : node.SubType;
                if (node.FinancialExposure > existing.FinancialExposure)
                    existing.FinancialExposure = node.FinancialExposure;
                existing.CreatedDate ??= node.CreatedDate;
                if (!string.IsNullOrWhiteSpace(node.Source) && !existing.Source.Contains(node.Source, StringComparison.OrdinalIgnoreCase))
                    existing.Source = $"{existing.Source} | {node.Source}";

                foreach (var kv in node.Metadata)
                    existing.Metadata[kv.Key] = kv.Value;

                return existing.Clone();
            }

            var stored = node.Clone();
            _nodes[stored.Id] = stored;
            _outgoing.TryAdd(stored.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            _incoming.TryAdd(stored.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            return stored.Clone();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public GraphEdge UpsertEdge(GraphEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        if (string.IsNullOrWhiteSpace(edge.SourceId) || string.IsNullOrWhiteSpace(edge.TargetId))
            throw new ArgumentException("SourceId e TargetId são obrigatórios.", nameof(edge));

        _lock.EnterWriteLock();
        try
        {
            if (!_nodes.ContainsKey(edge.SourceId) || !_nodes.ContainsKey(edge.TargetId))
                throw new InvalidOperationException($"Nós de origem ({edge.SourceId}) ou destino ({edge.TargetId}) não existem no grafo.");

            var edgeId = string.IsNullOrWhiteSpace(edge.Id)
                ? $"EDGE:{edge.SourceId}->{edge.TargetId}:{edge.RelationType}"
                : edge.Id;

            var stored = new GraphEdge
            {
                Id = edgeId,
                SourceId = edge.SourceId,
                TargetId = edge.TargetId,
                RelationType = edge.RelationType,
                Weight = Math.Clamp(edge.Weight, 0.05, 1.0),
                FinancialValue = edge.FinancialValue,
                OfficialSource = edge.OfficialSource,
                EventDate = edge.EventDate,
                Metadata = new Dictionary<string, string>(edge.Metadata, StringComparer.OrdinalIgnoreCase)
            };

            _edges[edgeId] = stored;
            _outgoing[stored.SourceId].Add(edgeId);
            _incoming[stored.TargetId].Add(edgeId);

            return stored.Clone();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public GraphNode? GetNode(string id)
    {
        _lock.EnterReadLock();
        try
        {
            return _nodes.TryGetValue(id, out var node) ? node.Clone() : null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public GraphNode? FindNodeByNormalizedNameOrDocument(string? name, string? document)
    {
        var normName = NormalizeText(name);
        var normDoc = NormalizeDigits(document);

        _lock.EnterReadLock();
        try
        {
            foreach (var node in _nodes.Values)
            {
                if (!string.IsNullOrEmpty(normDoc) && normDoc.Length >= 8)
                {
                    var nodeDoc = NormalizeDigits(node.Document);
                    if (!string.IsNullOrEmpty(nodeDoc) && nodeDoc.Equals(normDoc, StringComparison.Ordinal))
                        return node.Clone();
                }

                if (!string.IsNullOrEmpty(normName) && normName.Length >= 5)
                {
                    var nodeName = NormalizeText(node.Label);
                    if (nodeName.Equals(normName, StringComparison.Ordinal))
                        return node.Clone();
                }
            }

            return null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlyList<GraphNode> GetAllNodes()
    {
        _lock.EnterReadLock();
        try
        {
            return _nodes.Values.Select(n => n.Clone()).OrderByDescending(n => n.RiskScore).ThenBy(n => n.Layer).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlyList<GraphEdge> GetAllEdges()
    {
        _lock.EnterReadLock();
        try
        {
            return _edges.Values.Select(e => e.Clone()).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlyList<GraphEdge> GetIncidentEdges(string nodeId)
    {
        _lock.EnterReadLock();
        try
        {
            var result = new List<GraphEdge>();
            if (_outgoing.TryGetValue(nodeId, out var outIds))
            {
                foreach (var eid in outIds)
                    if (_edges.TryGetValue(eid, out var e)) result.Add(e.Clone());
            }

            if (_incoming.TryGetValue(nodeId, out var inIds))
            {
                foreach (var eid in inIds)
                    if (_edges.TryGetValue(eid, out var e) && result.All(x => x.Id != e.Id))
                        result.Add(e.Clone());
            }

            return result;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlyList<GraphEdge> GetOutgoingEdges(string nodeId)
    {
        _lock.EnterReadLock();
        try
        {
            if (!_outgoing.TryGetValue(nodeId, out var ids)) return Array.Empty<GraphEdge>();
            return ids.Where(_edges.ContainsKey).Select(id => _edges[id].Clone()).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlyList<GraphEdge> GetIncomingEdges(string nodeId)
    {
        _lock.EnterReadLock();
        try
        {
            if (!_incoming.TryGetValue(nodeId, out var ids)) return Array.Empty<GraphEdge>();
            return ids.Where(_edges.ContainsKey).Select(id => _edges[id].Clone()).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlyList<GraphNode> SearchNodes(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetAllNodes();

        var normQuery = NormalizeText(query);
        var normDigits = NormalizeDigits(query);

        _lock.EnterReadLock();
        try
        {
            return _nodes.Values
                .Where(n =>
                    NormalizeText(n.Label).Contains(normQuery, StringComparison.Ordinal) ||
                    NormalizeText(n.SubType).Contains(normQuery, StringComparison.Ordinal) ||
                    NormalizeText(n.Id).Contains(normQuery, StringComparison.Ordinal) ||
                    (!string.IsNullOrEmpty(normDigits) && NormalizeDigits(n.Document).Contains(normDigits, StringComparison.Ordinal)) ||
                    n.Metadata.Values.Any(v => NormalizeText(v).Contains(normQuery, StringComparison.Ordinal)))
                .Select(n => n.Clone())
                .OrderByDescending(n => n.RiskScore)
                .ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// BFS Multi-Nível (L0 até L4): explora vizinhança até maxDepth saltos a partir de um nó âncora,
    /// retornando subgrafo completo, distâncias e agrupamento por camada L0-L4.
    /// </summary>
    public SubgraphResult? TraverseBfs(string startNodeId, int maxDepth = 4, bool bidirectional = true)
    {
        _lock.EnterReadLock();
        try
        {
            if (!_nodes.TryGetValue(startNodeId, out var root))
                return null;

            var distances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [startNodeId] = 0 };
            var visitedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { startNodeId };
            var collectedEdgeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<(string NodeId, int Depth)>();
            queue.Enqueue((startNodeId, 0));

            while (queue.Count > 0)
            {
                var (currentId, depth) = queue.Dequeue();
                if (depth >= maxDepth)
                    continue;

                if (_outgoing.TryGetValue(currentId, out var outEdgeIds))
                {
                    foreach (var edgeId in outEdgeIds)
                    {
                        var edge = _edges[edgeId];
                        collectedEdgeIds.Add(edgeId);
                        if (visitedNodes.Add(edge.TargetId))
                        {
                            distances[edge.TargetId] = depth + 1;
                            queue.Enqueue((edge.TargetId, depth + 1));
                        }
                    }
                }

                if (bidirectional && _incoming.TryGetValue(currentId, out var inEdgeIds))
                {
                    foreach (var edgeId in inEdgeIds)
                    {
                        var edge = _edges[edgeId];
                        collectedEdgeIds.Add(edgeId);
                        if (visitedNodes.Add(edge.SourceId))
                        {
                            distances[edge.SourceId] = depth + 1;
                            queue.Enqueue((edge.SourceId, depth + 1));
                        }
                    }
                }
            }

            // Inclui arestas internas entre nós já descobertos no subgrafo
            foreach (var edge in _edges.Values)
            {
                if (visitedNodes.Contains(edge.SourceId) && visitedNodes.Contains(edge.TargetId))
                    collectedEdgeIds.Add(edge.Id);
            }

            var nodes = visitedNodes
                .Select(id => _nodes[id].Clone())
                .OrderBy(n => distances[n.Id])
                .ThenBy(n => n.Layer)
                .ToList();

            var edges = collectedEdgeIds
                .Select(id => _edges[id].Clone())
                .ToList();

            var byLayer = new Dictionary<string, List<GraphNode>>
            {
                ["L0"] = nodes.Where(n => n.Layer == NodeLayer.L0_AgentePublico).ToList(),
                ["L1"] = nodes.Where(n => n.Layer == NodeLayer.L1_FamiliarSocio).ToList(),
                ["L2"] = nodes.Where(n => n.Layer == NodeLayer.L2_EmpresaCnpj).ToList(),
                ["L3"] = nodes.Where(n => n.Layer == NodeLayer.L3_ContratoEmenda).ToList(),
                ["L4"] = nodes.Where(n => n.Layer == NodeLayer.L4_DoadorOrgao).ToList()
            };

            return new SubgraphResult(root.Clone(), nodes, edges, distances, byLayer);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Detecção de ciclos direcionados (circuitos fechados de doação, emenda, contrato e repasse).
    /// Utiliza DFS com normalização canônica para evitar ciclos duplicados por rotação.
    /// </summary>
    public IReadOnlyList<CyclePath> DetectClosedCircuits(int maxCycleLength = 6)
    {
        _lock.EnterReadLock();
        try
        {
            var uniqueSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var detected = new List<CyclePath>();
            var orderedIds = _nodes.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var startId in orderedIds)
            {
                var pathNodes = new List<string> { startId };
                var pathEdges = new List<string>();
                var inPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { startId };

                DfsFindCycles(startId, startId, maxCycleLength, pathNodes, pathEdges, inPath, uniqueSignatures, detected);
            }

            return detected
                .OrderByDescending(c => c.IsDonationContractLoop)
                .ThenByDescending(c => c.TotalFinancialVolume)
                .ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private void DfsFindCycles(
        string startId,
        string currentId,
        int maxDepth,
        List<string> pathNodes,
        List<string> pathEdges,
        HashSet<string> inPath,
        HashSet<string> uniqueSignatures,
        List<CyclePath> results)
    {
        if (!_outgoing.TryGetValue(currentId, out var outEdgeIds))
            return;

        foreach (var edgeId in outEdgeIds)
        {
            var edge = _edges[edgeId];
            var nextId = edge.TargetId;

            if (nextId.Equals(startId, StringComparison.OrdinalIgnoreCase) && pathNodes.Count >= 3)
            {
                var cycleNodes = new List<string>(pathNodes);
                var cycleEdges = new List<string>(pathEdges) { edgeId };
                var canonical = BuildCanonicalCycleKey(cycleNodes);

                if (uniqueSignatures.Add(canonical))
                {
                    var labels = cycleNodes.Select(id => _nodes[id].Label).ToList();
                    var edgesObj = cycleEdges.Select(id => _edges[id]).ToList();
                    var totalFinancial = edgesObj.Sum(e => e.FinancialValue);

                    var hasDonation = edgesObj.Any(e =>
                        e.RelationType.Contains("DOACAO", StringComparison.OrdinalIgnoreCase) ||
                        e.RelationType.Contains("CAMPANHA", StringComparison.OrdinalIgnoreCase));

                    var hasContractOrAmendment = edgesObj.Any(e =>
                        e.RelationType.Contains("CONTRATO", StringComparison.OrdinalIgnoreCase) ||
                        e.RelationType.Contains("EMENDA", StringComparison.OrdinalIgnoreCase) ||
                        e.RelationType.Contains("LICITACAO", StringComparison.OrdinalIgnoreCase) ||
                        e.RelationType.Contains("REPASSE", StringComparison.OrdinalIgnoreCase));

                    var isLoop = hasDonation && hasContractOrAmendment;
                    var summary = isLoop
                        ? $"Circuito fechado Doação ↔ Contrato ({cycleNodes.Count} nós): {string.Join(" → ", labels)} → {labels[0]}"
                        : $"Ciclo estrutural detectado ({cycleNodes.Count} nós): {string.Join(" → ", labels)} → {labels[0]}";

                    results.Add(new CyclePath(
                        Id: $"CYC-{results.Count + 1:D3}",
                        NodeIds: cycleNodes,
                        NodeLabels: labels,
                        EdgeIds: cycleEdges,
                        TotalFinancialVolume: totalFinancial,
                        IsDonationContractLoop: isLoop,
                        Summary: summary));
                }

                continue;
            }

            if (pathNodes.Count >= maxDepth || inPath.Contains(nextId))
                continue;

            // Poda lexicográfica para otimizar busca sem perder ciclos simples
            if (string.Compare(nextId, startId, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            pathNodes.Add(nextId);
            pathEdges.Add(edgeId);
            inPath.Add(nextId);

            DfsFindCycles(startId, nextId, maxDepth, pathNodes, pathEdges, inPath, uniqueSignatures, results);

            inPath.Remove(nextId);
            pathNodes.RemoveAt(pathNodes.Count - 1);
            pathEdges.RemoveAt(pathEdges.Count - 1);
        }
    }

    private static string BuildCanonicalCycleKey(List<string> nodeIds)
    {
        var minIdx = 0;
        for (var i = 1; i < nodeIds.Count; i++)
        {
            if (string.Compare(nodeIds[i], nodeIds[minIdx], StringComparison.OrdinalIgnoreCase) < 0)
                minIdx = i;
        }

        var rotated = new List<string>(nodeIds.Count);
        for (var i = 0; i < nodeIds.Count; i++)
            rotated.Add(nodeIds[(minIdx + i) % nodeIds.Count]);

        return string.Join("->", rotated);
    }

    /// <summary>
    /// Recalcula automaticamente o Score de Risco (0 a 100%) e a Severidade (CRITICO, ALTA, MEDIA, BAIXA)
    /// de todos os nós com base em topologia, ciclos fechados, exposição financeira e sinais forenses.
    /// </summary>
    public void RecalculateAllRisks()
    {
        var cycles = DetectClosedCircuits();

        _lock.EnterWriteLock();
        try
        {
            foreach (var node in _nodes.Values)
            {
                var score = 0;
                var factors = new List<string>();

                var incident = GetIncidentEdgesUnsafe(node.Id);
                var financialVolume = incident.Sum(e => e.FinancialValue);
                if (financialVolume > node.FinancialExposure)
                    node.FinancialExposure = financialVolume;

                var effectiveExposure = Math.Max(node.FinancialExposure, financialVolume);

                // 1. Participação em circuitos fechados (Doação ↔ Contrato ou ciclo geral)
                var nodeCycles = cycles.Where(c => c.NodeIds.Contains(node.Id, StringComparer.OrdinalIgnoreCase)).ToList();
                if (nodeCycles.Any(c => c.IsDonationContractLoop))
                {
                    score += 38;
                    factors.Add("Participação direta em circuito fechado Doação de Campanha ↔ Contrato/Emenda (+38)");
                }
                else if (nodeCycles.Count > 0)
                {
                    score += 22;
                    factors.Add($"Integrante de {nodeCycles.Count} ciclo(s) estrutural(is) fechado(s) (+22)");
                }

                // 2. Exposição financeira acumulada
                if (effectiveExposure >= 5_000_000m)
                {
                    score += 24;
                    factors.Add($"Exposição financeira crítica acima de R$ 5M ({effectiveExposure.ToString("C", CultureInfo.GetCultureInfo("pt-BR"))}) (+24)");
                }
                else if (effectiveExposure >= 1_500_000m)
                {
                    score += 16;
                    factors.Add($"Exposição financeira elevada ({effectiveExposure.ToString("C", CultureInfo.GetCultureInfo("pt-BR"))}) (+16)");
                }
                else if (effectiveExposure >= 300_000m)
                {
                    score += 9;
                    factors.Add($"Movimentação financeira relevante ({effectiveExposure.ToString("C", CultureInfo.GetCultureInfo("pt-BR"))}) (+9)");
                }

                // 3. Empresa recém-aberta recebendo contrato alto
                if (node.Layer == NodeLayer.L2_EmpresaCnpj && node.CreatedDate.HasValue)
                {
                    var contractEdges = incident
                        .Where(e =>
                            e.RelationType.Contains("CONTRATO", StringComparison.OrdinalIgnoreCase) ||
                            e.RelationType.Contains("LICITACAO", StringComparison.OrdinalIgnoreCase) ||
                            e.RelationType.Contains("DISPENSA", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var ce in contractEdges)
                    {
                        var contractDate = ce.EventDate ?? DateTime.UtcNow;
                        var ageDays = (contractDate - node.CreatedDate.Value).TotalDays;
                        if (ageDays >= 0 && ageDays <= 180 && ce.FinancialValue >= 500_000m)
                        {
                            score += 28;
                            factors.Add($"Empresa recém-aberta ({Math.Round(ageDays)} dias antes do contrato de {ce.FinancialValue.ToString("C", CultureInfo.GetCultureInfo("pt-BR"))}) (+28)");
                            break;
                        }
                    }

                    // Capital social desproporcional ao contrato
                    if (node.Metadata.TryGetValue("CapitalSocial", out var capStr) &&
                        decimal.TryParse(capStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var capital) &&
                        capital > 0 && effectiveExposure > capital * 20m)
                    {
                        score += 14;
                        factors.Add($"Contratos superam em {Math.Round(effectiveExposure / capital, 1)}x o Capital Social declarado (+14)");
                    }
                }

                // 4. Duplo vínculo simultâneo (Servidor / Agente Público + Sócio de fornecedor)
                var hasPublicRole = node.Layer == NodeLayer.L0_AgentePublico ||
                                    incident.Any(e => e.RelationType.Contains("SERVIDOR", StringComparison.OrdinalIgnoreCase) ||
                                                      e.RelationType.Contains("ORDENADOR", StringComparison.OrdinalIgnoreCase));
                var hasCompanyStake = incident.Any(e => e.RelationType.Contains("SOCIO", StringComparison.OrdinalIgnoreCase));

                if (hasPublicRole && hasCompanyStake)
                {
                    score += 30;
                    factors.Add("Duplo vínculo simultâneo: agente/servidor público vinculado societariamente a fornecedor (+30)");
                }

                // 5. Proximidade entre Agente Público (L0) e Empresa/Contrato via Familiar/Sócio (L1)
                var hasFamilyLink = incident.Any(e =>
                    e.RelationType.Contains("FAMILIAR", StringComparison.OrdinalIgnoreCase) ||
                    e.RelationType.Contains("PARENTE", StringComparison.OrdinalIgnoreCase));
                if (hasFamilyLink)
                {
                    score += 18;
                    factors.Add("Vínculo familiar/parentesco em cadeia de contratação pública (+18)");
                }

                // 6. Centralidade e concentração de emendas/dispensas
                var dispensaCount = incident.Count(e =>
                    e.RelationType.Contains("DISPENSA", StringComparison.OrdinalIgnoreCase) ||
                    e.RelationType.Contains("EMERGENCIAL", StringComparison.OrdinalIgnoreCase) ||
                    e.RelationType.Contains("ADITIVO", StringComparison.OrdinalIgnoreCase));
                if (dispensaCount > 0)
                {
                    var pts = Math.Min(18, dispensaCount * 10);
                    score += pts;
                    factors.Add($"Presença de contratação emergencial, dispensa ou aditivo concentrado (+{pts})");
                }

                if (incident.Count >= 4)
                {
                    score += 10;
                    factors.Add($"Alta centralidade de grau no grafo ({incident.Count} conexões diretas) (+10)");
                }

                node.RiskScore = Math.Clamp(score, 5, 99);
                node.Severity = ClassifySeverity(node.RiskScore);
                node.RiskFactors = factors.Count > 0
                    ? factors
                    : new List<string> { "Entidade monitorada em conformidade preliminar — sem agravantes estruturais diretos." };
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public static RiskSeverity ClassifySeverity(int score) => score switch
    {
        >= 75 => RiskSeverity.CRITICO,
        >= 45 => RiskSeverity.ALTA,
        >= 20 => RiskSeverity.MEDIA,
        _ => RiskSeverity.BAIXA
    };

    public GraphMetrics ComputeMetrics(IReadOnlyList<AnomalyAlert> anomalies, IReadOnlyList<CyclePath> cycles)
    {
        _lock.EnterReadLock();
        try
        {
            // Evita dupla contagem somando os valores financeiros únicos das arestas contratuais/emendas/doações
            var totalExposure = _edges.Values
                .Where(e => e.FinancialValue > 0)
                .Sum(e => e.FinancialValue);

            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in _nodes.Values)
            {
                foreach (var s in node.Source.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    sources.Add(s);
            }

            foreach (var edge in _edges.Values)
            {
                if (!string.IsNullOrWhiteSpace(edge.OfficialSource))
                    sources.Add(edge.OfficialSource.Trim());
            }

            var criticalCount = anomalies.Count(a => a.Severity == RiskSeverity.CRITICO);
            var highCount = anomalies.Count(a => a.Severity == RiskSeverity.ALTA);
            var mediumCount = anomalies.Count(a => a.Severity == RiskSeverity.MEDIA);

            return new GraphMetrics(
                TotalFinancialExposure: totalExposure,
                CriticalIrregularities: criticalCount,
                HighIrregularities: highCount,
                MediumIrregularities: mediumCount,
                MappedEntities: _nodes.Count,
                TotalConnections: _edges.Count,
                CrossedSourcesCount: sources.Count,
                CrossedSources: sources.OrderBy(s => s).ToList(),
                ClosedCircuitsCount: cycles.Count);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private List<GraphEdge> GetIncidentEdgesUnsafe(string nodeId)
    {
        var list = new List<GraphEdge>();
        if (_outgoing.TryGetValue(nodeId, out var outIds))
            foreach (var id in outIds)
                if (_edges.TryGetValue(id, out var e)) list.Add(e);

        if (_incoming.TryGetValue(nodeId, out var inIds))
            foreach (var id in inIds)
                if (_edges.TryGetValue(id, out var e) && list.All(x => x.Id != e.Id)) list.Add(e);

        return list;
    }

    public static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var formD = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string NormalizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsDigit(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }
}
