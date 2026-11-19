using System.Globalization;
using SentinelGraph.Engine;

namespace SentinelGraph.Intelligence;

public sealed class AnomalyDetector
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly GraphEngine _graph;

    public AnomalyDetector(GraphEngine graph)
    {
        _graph = graph;
    }

    /// <summary>
    /// Executa todas as regras forenses determinísticas sobre o grafo atual e retorna
    /// os alertas ordenados por severidade, confiança e impacto financeiro.
    /// </summary>
    public IReadOnlyList<AnomalyAlert> DetectAll()
    {
        var nodes = _graph.GetAllNodes();
        var edges = _graph.GetAllEdges();
        var cycles = _graph.DetectClosedCircuits();

        var nodeMap = nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var alerts = new List<AnomalyAlert>();
        var dedupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        DetectClosedDonationContractLoops(cycles, nodeMap, edges, alerts, dedupKeys);
        DetectSimultaneousDualLink(nodes, nodeMap, alerts, dedupKeys);
        DetectNewlyOpenedCompanyHighContract(nodes, nodeMap, alerts, dedupKeys);
        DetectSelfDirectionAndPartnerConcentration(nodes, nodeMap, alerts, dedupKeys);
        DetectDonationContractTriangulation(nodes, nodeMap, alerts, dedupKeys);
        DetectContractAmendmentConcentration(nodes, nodeMap, alerts, dedupKeys);

        return alerts
            .OrderByDescending(a => a.Severity)
            .ThenByDescending(a => a.ConfidencePercent)
            .ThenByDescending(a => a.InvolvedAmount)
            .ToList();
    }

    public IReadOnlyList<AnomalyAlert> DetectForNode(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return Array.Empty<AnomalyAlert>();

        return DetectAll()
            .Where(a =>
                a.PrimaryNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase) ||
                a.RelatedNodeIds.Contains(nodeId, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Regra (c): Circuito fechado entre doador de campanha e empresa contratada (Ciclo Direcionado).
    /// </summary>
    private void DetectClosedDonationContractLoops(
        IReadOnlyList<CyclePath> cycles,
        Dictionary<string, GraphNode> nodeMap,
        IReadOnlyList<GraphEdge> allEdges,
        List<AnomalyAlert> alerts,
        HashSet<string> dedupKeys)
    {
        var edgeMap = allEdges.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var cycle in cycles)
        {
            var key = $"CYCLE:{string.Join("|", cycle.NodeIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}";
            if (!dedupKeys.Add(key))
                continue;

            var primaryId = cycle.NodeIds.FirstOrDefault(id =>
                nodeMap.TryGetValue(id, out var n) && n.Layer == NodeLayer.L0_AgentePublico)
                ?? cycle.NodeIds[0];

            var primaryNode = nodeMap[primaryId];
            var cycleEdges = cycle.EdgeIds.Where(edgeMap.ContainsKey).Select(id => edgeMap[id]).ToList();
            var sources = CollectSources(cycle.NodeIds, cycleEdges, nodeMap);

            var severity = cycle.IsDonationContractLoop
                ? RiskSeverity.CRITICO
                : (cycle.TotalFinancialVolume >= 1_000_000m ? RiskSeverity.ALTA : RiskSeverity.MEDIA);

            var confidence = cycle.IsDonationContractLoop ? 96 : 82;

            var title = cycle.IsDonationContractLoop
                ? $"Circuito Fechado Doação ↔ Contrato via {primaryNode.Label}"
                : $"Circuito Estrutural Fechado Envolvendo {primaryNode.Label}";

            var description =
                $"Detectado fluxo cíclico fechado com {cycle.NodeIds.Count} entidades somando " +
                $"{FormatCurrency(cycle.TotalFinancialVolume)}: {string.Join(" → ", cycle.NodeLabels)} → {cycle.NodeLabels[0]}. " +
                "Indício forte de retorno financeiro indireto entre financiador de campanha, destinação orçamentária e contratada.";

            alerts.Add(new AnomalyAlert(
                Id: $"ALR-CYC-{alerts.Count + 1:D3}",
                PatternCode: "CIRCUITO_FECHADO_DOACAO_CONTRATO",
                Title: title,
                Description: description,
                Severity: severity,
                ConfidencePercent: confidence,
                InvolvedAmount: cycle.TotalFinancialVolume,
                PrimaryNodeId: primaryNode.Id,
                PrimaryNodeLabel: primaryNode.Label,
                RelatedNodeIds: cycle.NodeIds.ToList(),
                Sources: sources,
                DetectedAtUtc: DateTime.UtcNow));
        }
    }

    /// <summary>
    /// Regra (d): Duplo vínculo simultâneo (Agente/Servidor Público ou Ordenador de Despesas + Sócio de Fornecedor).
    /// </summary>
    private void DetectSimultaneousDualLink(
        IReadOnlyList<GraphNode> nodes,
        Dictionary<string, GraphNode> nodeMap,
        List<AnomalyAlert> alerts,
        HashSet<string> dedupKeys)
    {
        foreach (var node in nodes)
        {
            var incident = _graph.GetIncidentEdges(node.Id);
            var publicRoleEdges = incident.Where(e =>
                e.RelationType.Contains("SERVIDOR", StringComparison.OrdinalIgnoreCase) ||
                e.RelationType.Contains("ORDENADOR", StringComparison.OrdinalIgnoreCase)).ToList();

            var isPublicAgent = node.Layer == NodeLayer.L0_AgentePublico || publicRoleEdges.Count > 0;
            if (!isPublicAgent)
                continue;

            var partnerEdges = incident.Where(e =>
                e.RelationType.Contains("SOCIO", StringComparison.OrdinalIgnoreCase)).ToList();

            if (partnerEdges.Count == 0)
                continue;

            foreach (var pEdge in partnerEdges)
            {
                var companyId = pEdge.SourceId.Equals(node.Id, StringComparison.OrdinalIgnoreCase)
                    ? pEdge.TargetId
                    : pEdge.SourceId;

                if (!nodeMap.TryGetValue(companyId, out var company) || company.Layer != NodeLayer.L2_EmpresaCnpj)
                    continue;

                var key = $"DUAL:{node.Id}:{company.Id}";
                if (!dedupKeys.Add(key))
                    continue;

                var companyIncident = _graph.GetIncidentEdges(company.Id);
                var publicContracts = companyIncident.Where(IsContractOrAmendmentEdge).ToList();
                var contractVolume = publicContracts.Sum(e => e.FinancialValue);
                var orderedDirectly = incident.Any(e =>
                    e.RelationType.Contains("ORDENADOR", StringComparison.OrdinalIgnoreCase) &&
                    publicContracts.Any(pc => pc.SourceId.Equals(e.TargetId, StringComparison.OrdinalIgnoreCase) ||
                                              pc.TargetId.Equals(e.TargetId, StringComparison.OrdinalIgnoreCase)));

                var relatedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { node.Id, company.Id };
                foreach (var pe in publicRoleEdges)
                {
                    relatedIds.Add(pe.SourceId);
                    relatedIds.Add(pe.TargetId);
                }
                foreach (var ce in publicContracts)
                {
                    relatedIds.Add(ce.SourceId);
                    relatedIds.Add(ce.TargetId);
                }

                var severity = (orderedDirectly || contractVolume >= 1_000_000m)
                    ? RiskSeverity.CRITICO
                    : RiskSeverity.ALTA;

                var confidence = orderedDirectly ? 98 : 89;
                var sources = CollectSources(relatedIds, publicRoleEdges.Concat(publicContracts).Append(pEdge), nodeMap);

                var description =
                    $"O agente público {node.Label} mantém vínculo societário simultâneo ({pEdge.RelationType}) com a empresa " +
                    $"{company.Label} ({company.Document}), que acumula {FormatCurrency(contractVolume)} em contratações públicas" +
                    (orderedDirectly
                        ? ", incluindo dispensa/contrato ordenado diretamente pelo próprio servidor (conflito frontal à Lei 14.133/2021 e Lei 8.112/1990)."
                        : ".");

                alerts.Add(new AnomalyAlert(
                    Id: $"ALR-DUAL-{alerts.Count + 1:D3}",
                    PatternCode: "DUPLO_VINCULO_SIMULTANEO",
                    Title: $"Duplo Vínculo Simultâneo: {node.Label} × {company.Label}",
                    Description: description,
                    Severity: severity,
                    ConfidencePercent: confidence,
                    InvolvedAmount: contractVolume,
                    PrimaryNodeId: node.Id,
                    PrimaryNodeLabel: node.Label,
                    RelatedNodeIds: relatedIds.ToList(),
                    Sources: sources,
                    DetectedAtUtc: DateTime.UtcNow));
            }
        }
    }

    /// <summary>
    /// Regra (b): Empresa recém-aberta ganhando contrato público de alto valor e/ou capital social desproporcional.
    /// </summary>
    private void DetectNewlyOpenedCompanyHighContract(
        IReadOnlyList<GraphNode> nodes,
        Dictionary<string, GraphNode> nodeMap,
        List<AnomalyAlert> alerts,
        HashSet<string> dedupKeys)
    {
        foreach (var company in nodes.Where(n => n.Layer == NodeLayer.L2_EmpresaCnpj && n.CreatedDate.HasValue))
        {
            var incident = _graph.GetIncidentEdges(company.Id);
            var contractEdges = incident.Where(IsContractOrAmendmentEdge).ToList();
            if (contractEdges.Count == 0)
                continue;

            decimal capitalSocial = 0m;
            if (company.Metadata.TryGetValue("CapitalSocial", out var capStr))
                decimal.TryParse(capStr, NumberStyles.Any, CultureInfo.InvariantCulture, out capitalSocial);

            foreach (var ce in contractEdges)
            {
                if (ce.FinancialValue < 350_000m)
                    continue;

                var contractDate = ce.EventDate ?? DateTime.UtcNow;
                var ageDays = (contractDate - company.CreatedDate!.Value).TotalDays;
                if (ageDays < 0 || ageDays > 240)
                    continue;

                var contractNodeId = ce.SourceId.Equals(company.Id, StringComparison.OrdinalIgnoreCase)
                    ? ce.TargetId
                    : ce.SourceId;

                var key = $"NEWCO:{company.Id}:{contractNodeId}";
                if (!dedupKeys.Add(key))
                    continue;

                var relatedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { company.Id, contractNodeId };
                var socioEdges = incident.Where(e => e.RelationType.Contains("SOCIO", StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var se in socioEdges)
                {
                    relatedIds.Add(se.SourceId);
                    relatedIds.Add(se.TargetId);
                }

                var ratioText = capitalSocial > 0
                    ? $" O valor contratado equivale a {Math.Round(ce.FinancialValue / capitalSocial, 1)}x o capital social declarado ({FormatCurrency(capitalSocial)})."
                    : string.Empty;

                var severity = (ageDays <= 120 && ce.FinancialValue >= 1_500_000m)
                    ? RiskSeverity.CRITICO
                    : (ageDays <= 180 ? RiskSeverity.ALTA : RiskSeverity.MEDIA);

                var confidence = ageDays <= 90 ? 94 : 84;
                var contractLabel = nodeMap.TryGetValue(contractNodeId, out var cNode) ? cNode.Label : contractNodeId;
                var sources = CollectSources(relatedIds, socioEdges.Append(ce), nodeMap);

                var description =
                    $"A empresa {company.Label} ({company.Document}), constituída em {company.CreatedDate.Value:dd/MM/yyyy}, " +
                    $"assinou {contractLabel} no valor de {FormatCurrency(ce.FinancialValue)} apenas {Math.Round(ageDays)} dias " +
                    $"após sua abertura.{ratioText} Forte indício de empresa de fachada ou SPE criada sob medida.";

                alerts.Add(new AnomalyAlert(
                    Id: $"ALR-NEW-{alerts.Count + 1:D3}",
                    PatternCode: "EMPRESA_RECEM_ABERTA",
                    Title: $"Empresa Recém-Aberta ({Math.Round(ageDays)} dias) em Contrato de {FormatCurrency(ce.FinancialValue)}",
                    Description: description,
                    Severity: severity,
                    ConfidencePercent: confidence,
                    InvolvedAmount: ce.FinancialValue,
                    PrimaryNodeId: company.Id,
                    PrimaryNodeLabel: company.Label,
                    RelatedNodeIds: relatedIds.ToList(),
                    Sources: sources,
                    DetectedAtUtc: DateTime.UtcNow));
            }
        }
    }

    /// <summary>
    /// Regra (a): Auto-direcionamento / concentração de contratos e emendas para empresas de familiares,
    /// ex-assessores ou sócios ligados a agente público (L0 → L1 → L2 ← L3 ← L0).
    /// </summary>
    private void DetectSelfDirectionAndPartnerConcentration(
        IReadOnlyList<GraphNode> nodes,
        Dictionary<string, GraphNode> nodeMap,
        List<AnomalyAlert> alerts,
        HashSet<string> dedupKeys)
    {
        var agents = nodes.Where(n => n.Layer == NodeLayer.L0_AgentePublico).ToList();

        foreach (var agent in agents)
        {
            var agentIncident = _graph.GetIncidentEdges(agent.Id);

            // Pessoas L1 ligadas ao agente (familiar, ex-assessor, etc.)
            var linkedL1Edges = agentIncident.Where(e =>
                e.RelationType.Contains("FAMILIAR", StringComparison.OrdinalIgnoreCase) ||
                e.RelationType.Contains("PARENTE", StringComparison.OrdinalIgnoreCase) ||
                e.RelationType.Contains("ASSESSOR", StringComparison.OrdinalIgnoreCase)).ToList();

            // Contratos/Emendas alcançáveis a partir do agente (até 2 saltos em L3)
            var agentL3Ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _graph.GetOutgoingEdges(agent.Id))
            {
                if (nodeMap.TryGetValue(e.TargetId, out var t) && t.Layer == NodeLayer.L3_ContratoEmenda)
                {
                    agentL3Ids.Add(t.Id);
                    foreach (var next in _graph.GetOutgoingEdges(t.Id))
                    {
                        if (nodeMap.TryGetValue(next.TargetId, out var t2) && t2.Layer == NodeLayer.L3_ContratoEmenda)
                            agentL3Ids.Add(t2.Id);
                    }
                }
            }

            foreach (var l1Edge in linkedL1Edges)
            {
                var l1Id = l1Edge.SourceId.Equals(agent.Id, StringComparison.OrdinalIgnoreCase)
                    ? l1Edge.TargetId
                    : l1Edge.SourceId;

                if (!nodeMap.TryGetValue(l1Id, out var l1Node))
                    continue;

                var l1CompanyEdges = _graph.GetIncidentEdges(l1Id)
                    .Where(e => e.RelationType.Contains("SOCIO", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var scEdge in l1CompanyEdges)
                {
                    var companyId = scEdge.SourceId.Equals(l1Id, StringComparison.OrdinalIgnoreCase)
                        ? scEdge.TargetId
                        : scEdge.SourceId;

                    if (!nodeMap.TryGetValue(companyId, out var company) || company.Layer != NodeLayer.L2_EmpresaCnpj)
                        continue;

                    var companyContracts = _graph.GetIncidentEdges(company.Id)
                        .Where(IsContractOrAmendmentEdge)
                        .ToList();

                    if (companyContracts.Count == 0)
                        continue;

                    var directFromAgent = companyContracts.Where(ce =>
                        agentL3Ids.Contains(ce.SourceId) || agentL3Ids.Contains(ce.TargetId)).ToList();

                    var totalAmount = (directFromAgent.Count > 0 ? directFromAgent : companyContracts).Sum(e => e.FinancialValue);
                    var key = $"SELFDIR:{agent.Id}:{l1Node.Id}:{company.Id}";
                    if (!dedupKeys.Add(key))
                        continue;

                    var relatedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        agent.Id,
                        l1Node.Id,
                        company.Id
                    };

                    foreach (var l3Id in agentL3Ids)
                        relatedIds.Add(l3Id);
                    foreach (var ce in companyContracts)
                    {
                        relatedIds.Add(ce.SourceId);
                        relatedIds.Add(ce.TargetId);
                    }

                    var isFamily = l1Edge.RelationType.Contains("FAMILIAR", StringComparison.OrdinalIgnoreCase) ||
                                   l1Edge.RelationType.Contains("PARENTE", StringComparison.OrdinalIgnoreCase);

                    var severity = (directFromAgent.Count > 0 && (isFamily || totalAmount >= 2_500_000m))
                        ? RiskSeverity.CRITICO
                        : RiskSeverity.ALTA;

                    var confidence = directFromAgent.Count > 0 ? 95 : 86;
                    var sources = CollectSources(relatedIds, companyContracts.Append(l1Edge).Append(scEdge), nodeMap);

                    var linkDesc = isFamily ? "vínculo familiar direto" : $"vínculo prévio ({l1Edge.RelationType})";
                    var flowDesc = directFromAgent.Count > 0
                        ? $"beneficiada diretamente por emenda/contrato vinculado ao próprio {agent.Label}"
                        : $"contratada pelo poder público sob forte proximidade com o gabinete de {agent.Label}";

                    var description =
                        $"Identificado padrão de auto-direcionamento: {agent.Label} possui {linkDesc} com {l1Node.Label}, " +
                        $"sócio da empresa {company.Label} ({company.Document}), {flowDesc}, movimentando {FormatCurrency(totalAmount)}.";

                    alerts.Add(new AnomalyAlert(
                        Id: $"ALR-DIR-{alerts.Count + 1:D3}",
                        PatternCode: "AUTO_DIRECIONAMENTO_SOCIETARIO",
                        Title: $"Auto-Direcionamento via Sócio Ligado: {l1Node.Label} → {company.Label}",
                        Description: description,
                        Severity: severity,
                        ConfidencePercent: confidence,
                        InvolvedAmount: totalAmount,
                        PrimaryNodeId: agent.Id,
                        PrimaryNodeLabel: agent.Label,
                        RelatedNodeIds: relatedIds.ToList(),
                        Sources: sources,
                        DetectedAtUtc: DateTime.UtcNow));
                }
            }
        }
    }

    /// <summary>
    /// Regra complementar (c2): Triangulação aberta entre doador de campanha (L1/L4) e empresa contratada (L2)
    /// homologada pelo agente público beneficiado pela doação (L0).
    /// </summary>
    private void DetectDonationContractTriangulation(
        IReadOnlyList<GraphNode> nodes,
        Dictionary<string, GraphNode> nodeMap,
        List<AnomalyAlert> alerts,
        HashSet<string> dedupKeys)
    {
        var donationEdges = _graph.GetAllEdges()
            .Where(e => e.RelationType.Contains("DOACAO", StringComparison.OrdinalIgnoreCase) ||
                        e.RelationType.Contains("CAMPANHA", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var dEdge in donationEdges)
        {
            if (!nodeMap.TryGetValue(dEdge.SourceId, out var donor) ||
                !nodeMap.TryGetValue(dEdge.TargetId, out var agent) ||
                agent.Layer != NodeLayer.L0_AgentePublico)
                continue;

            var donorCompanies = _graph.GetIncidentEdges(donor.Id)
                .Where(e => e.RelationType.Contains("SOCIO", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.SourceId.Equals(donor.Id, StringComparison.OrdinalIgnoreCase) ? e.TargetId : e.SourceId)
                .Where(id => nodeMap.TryGetValue(id, out var n) && n.Layer == NodeLayer.L2_EmpresaCnpj)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var agentApprovedL3 = _graph.GetOutgoingEdges(agent.Id)
                .Where(e => nodeMap.TryGetValue(e.TargetId, out var n) && n.Layer == NodeLayer.L3_ContratoEmenda)
                .Select(e => e.TargetId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var compId in donorCompanies)
            {
                var comp = nodeMap[compId];
                var matchingContracts = _graph.GetIncidentEdges(compId)
                    .Where(e => agentApprovedL3.Contains(e.SourceId) || agentApprovedL3.Contains(e.TargetId))
                    .ToList();

                if (matchingContracts.Count == 0)
                    continue;

                var key = $"TRIANG:{donor.Id}:{agent.Id}:{compId}";
                if (!dedupKeys.Add(key))
                    continue;

                var contractVal = matchingContracts.Sum(e => e.FinancialValue);
                var ratio = dEdge.FinancialValue > 0 ? Math.Round(contractVal / dEdge.FinancialValue, 1) : 0m;

                var relatedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { donor.Id, agent.Id, compId };
                foreach (var mc in matchingContracts)
                {
                    relatedIds.Add(mc.SourceId);
                    relatedIds.Add(mc.TargetId);
                }

                var severity = contractVal >= 2_000_000m ? RiskSeverity.CRITICO : RiskSeverity.ALTA;
                var sources = CollectSources(relatedIds, matchingContracts.Append(dEdge), nodeMap);

                var description =
                    $"O doador de campanha {donor.Label} destinou {FormatCurrency(dEdge.FinancialValue)} para {agent.Label}. " +
                    $"Posteriormente, a empresa {comp.Label} (da qual o doador é sócio) recebeu {FormatCurrency(contractVal)} " +
                    $"em contratos homologados pelo mesmo agente (multiplicador de retorno estimado em {ratio}x).";

                alerts.Add(new AnomalyAlert(
                    Id: $"ALR-TRI-{alerts.Count + 1:D3}",
                    PatternCode: "TRIANGULACAO_DOADOR_CONTRATO",
                    Title: $"Retorno sobre Doação Eleitoral: {donor.Label} → {comp.Label}",
                    Description: description,
                    Severity: severity,
                    ConfidencePercent: 88,
                    InvolvedAmount: contractVal + dEdge.FinancialValue,
                    PrimaryNodeId: agent.Id,
                    PrimaryNodeLabel: agent.Label,
                    RelatedNodeIds: relatedIds.ToList(),
                    Sources: sources,
                    DetectedAtUtc: DateTime.UtcNow));
            }
        }
    }

    /// <summary>
    /// Regra complementar (e): Concentração de aditivos / reequilíbrio econômico-financeiro acima do patamar de alerta.
    /// </summary>
    private void DetectContractAmendmentConcentration(
        IReadOnlyList<GraphNode> nodes,
        Dictionary<string, GraphNode> nodeMap,
        List<AnomalyAlert> alerts,
        HashSet<string> dedupKeys)
    {
        foreach (var company in nodes.Where(n => n.Layer == NodeLayer.L2_EmpresaCnpj))
        {
            var incident = _graph.GetIncidentEdges(company.Id);
            var aditivoEdges = incident.Where(e =>
                e.RelationType.Contains("ADITIVO", StringComparison.OrdinalIgnoreCase)).ToList();

            var baseContractEdges = incident.Where(e =>
                (e.RelationType.Contains("CONTRATO", StringComparison.OrdinalIgnoreCase) ||
                 e.RelationType.Contains("LICITACAO", StringComparison.OrdinalIgnoreCase)) &&
                !e.RelationType.Contains("ADITIVO", StringComparison.OrdinalIgnoreCase)).ToList();

            if (aditivoEdges.Count == 0 || baseContractEdges.Count == 0)
                continue;

            var baseVal = baseContractEdges.Sum(e => e.FinancialValue);
            var aditivoVal = aditivoEdges.Sum(e => e.FinancialValue);
            if (baseVal <= 0 || aditivoVal <= 0)
                continue;

            var pct = Math.Round((aditivoVal / baseVal) * 100m, 1);
            if (pct < 20m)
                continue;

            var key = $"ADITIVO:{company.Id}";
            if (!dedupKeys.Add(key))
                continue;

            var relatedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { company.Id };
            foreach (var e in baseContractEdges.Concat(aditivoEdges))
            {
                relatedIds.Add(e.SourceId);
                relatedIds.Add(e.TargetId);
            }

            var severity = pct > 30m ? RiskSeverity.MEDIA : RiskSeverity.MEDIA;
            if (pct >= 45m && (baseVal + aditivoVal) >= 3_000_000m)
                severity = RiskSeverity.ALTA;

            var sources = CollectSources(relatedIds, baseContractEdges.Concat(aditivoEdges), nodeMap);

            var description =
                $"A contratada {company.Label} ({company.Document}) recebeu termo(s) aditivo(s) somando {FormatCurrency(aditivoVal)} " +
                $"sobre contrato base de {FormatCurrency(baseVal)} (+{pct}%), ultrapassando o limite legal ordinário de 25% " +
                "previsto para acréscimos contratuais (art. 125 da Lei 14.133/2021).";

            alerts.Add(new AnomalyAlert(
                Id: $"ALR-ADT-{alerts.Count + 1:D3}",
                PatternCode: "ADITIVO_ACIMA_LIMITE_LEGAL",
                Title: $"Aditivo Contratual de +{pct}% em {company.Label}",
                Description: description,
                Severity: severity,
                ConfidencePercent: 79,
                InvolvedAmount: baseVal + aditivoVal,
                PrimaryNodeId: company.Id,
                PrimaryNodeLabel: company.Label,
                RelatedNodeIds: relatedIds.ToList(),
                Sources: sources,
                DetectedAtUtc: DateTime.UtcNow));
        }
    }

    private static bool IsContractOrAmendmentEdge(GraphEdge edge) =>
        edge.RelationType.Contains("CONTRATO", StringComparison.OrdinalIgnoreCase) ||
        edge.RelationType.Contains("LICITACAO", StringComparison.OrdinalIgnoreCase) ||
        edge.RelationType.Contains("DISPENSA", StringComparison.OrdinalIgnoreCase) ||
        edge.RelationType.Contains("EMENDA", StringComparison.OrdinalIgnoreCase) ||
        edge.RelationType.Contains("ADITIVO", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> CollectSources(
        IEnumerable<string> nodeIds,
        IEnumerable<GraphEdge> edges,
        Dictionary<string, GraphNode> nodeMap)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in nodeIds)
        {
            if (nodeMap.TryGetValue(id, out var node))
            {
                foreach (var src in node.Source.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    set.Add(src);
            }
        }

        foreach (var edge in edges)
        {
            if (!string.IsNullOrWhiteSpace(edge.OfficialSource))
                set.Add(edge.OfficialSource.Trim());
        }

        return set.OrderBy(s => s).ToList();
    }

    private static string FormatCurrency(decimal value) => value.ToString("C", PtBr);
}
