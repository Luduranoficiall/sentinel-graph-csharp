using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using SentinelGraph.Engine;

namespace SentinelGraph.Connectors;

public sealed record CnpjIngestionResult(
    bool Success,
    string Message,
    string Cnpj,
    GraphNode? CompanyNode,
    IReadOnlyList<GraphNode> PartnerNodes,
    IReadOnlyList<GraphEdge> CreatedEdges,
    bool TransparenciaEnriched,
    int TransparenciaContractsFound
);

public sealed record ExternalConnectorStatus(
    bool Success,
    string Source,
    bool Configured,
    string Message,
    int RecordsIngested
);

public sealed class OpenDataConnectors
{
    private const string BrasilApiBaseUrl = "https://brasilapi.com.br/api/cnpj/v1/";
    private const string TransparenciaBaseUrl = "https://api.portaldatransparencia.gov.br/api-de-dados/";
    private const string CamaraBaseUrl = "https://dadosabertos.camara.leg.br/api/v2/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GraphEngine _graph;
    private readonly ILogger<OpenDataConnectors> _logger;

    public OpenDataConnectors(
        IHttpClientFactory httpClientFactory,
        GraphEngine graph,
        ILogger<OpenDataConnectors> logger)
    {
        _httpClientFactory = httpClientFactory;
        _graph = graph;
        _logger = logger;
    }

    public bool HasTransparenciaKey => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TRANSPARENCIA_API_KEY"));

    /// <summary>
    /// Consulta real assíncrona na BrasilAPI (Receita Federal) por CNPJ, extraindo razão social,
    /// capital social, data de abertura e Quadro de Sócios e Administradores (QSA),
    /// ingerindo automaticamente os nós L2/L1 e cruzando com entidades já presentes no grafo.
    /// </summary>
    public async Task<CnpjIngestionResult> IngestCnpjLiveAsync(string rawCnpj, CancellationToken ct = default)
    {
        var cnpj = GraphEngine.NormalizeDigits(rawCnpj);
        if (cnpj.Length != 14)
        {
            return new CnpjIngestionResult(
                false,
                "CNPJ inválido. Informe exatamente 14 dígitos numéricos.",
                cnpj,
                null,
                Array.Empty<GraphNode>(),
                Array.Empty<GraphEdge>(),
                false,
                0);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("BrasilApi");
            using var response = await client.GetAsync($"{BrasilApiBaseUrl}{cnpj}", ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("BrasilAPI retornou {StatusCode} para CNPJ {Cnpj}: {Body}", response.StatusCode, cnpj, body);
                return new CnpjIngestionResult(
                    false,
                    $"BrasilAPI retornou HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) para o CNPJ {FormatCnpj(cnpj)}.",
                    cnpj,
                    null,
                    Array.Empty<GraphNode>(),
                    Array.Empty<GraphEdge>(),
                    false,
                    0);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var dto = await JsonSerializer.DeserializeAsync<BrasilApiCnpjDto>(stream, JsonOptions, ct);

            if (dto is null || string.IsNullOrWhiteSpace(dto.RazaoSocial))
            {
                return new CnpjIngestionResult(
                    false,
                    "Resposta da BrasilAPI não contém dados válidos da empresa.",
                    cnpj,
                    null,
                    Array.Empty<GraphNode>(),
                    Array.Empty<GraphEdge>(),
                    false,
                    0);
            }

            DateTime? openingDate = DateTime.TryParse(
                dto.DataInicioAtividade,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsedDate)
                ? parsedDate
                : null;

            var companyId = $"L2-CNPJ-{cnpj}";
            var formattedCnpj = FormatCnpj(cnpj);

            var companyNode = _graph.UpsertNode(new GraphNode
            {
                Id = companyId,
                Label = dto.RazaoSocial.Trim(),
                Document = formattedCnpj,
                Layer = NodeLayer.L2_EmpresaCnpj,
                SubType = string.IsNullOrWhiteSpace(dto.DescricaoSituacaoCadastral)
                    ? "Empresa / CNPJ"
                    : $"CNPJ — {dto.DescricaoSituacaoCadastral}",
                FinancialExposure = dto.CapitalSocial,
                CreatedDate = openingDate,
                Source = "BrasilAPI (Receita Federal)",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CNPJ"] = formattedCnpj,
                    ["NomeFantasia"] = string.IsNullOrWhiteSpace(dto.NomeFantasia) ? dto.RazaoSocial : dto.NomeFantasia,
                    ["CapitalSocial"] = dto.CapitalSocial.ToString(CultureInfo.InvariantCulture),
                    ["CapitalSocialFormatado"] = dto.CapitalSocial.ToString("C", CultureInfo.GetCultureInfo("pt-BR")),
                    ["DataAbertura"] = openingDate?.ToString("yyyy-MM-dd") ?? "N/D",
                    ["SituacaoCadastral"] = dto.DescricaoSituacaoCadastral ?? "N/D",
                    ["CNAE"] = dto.CnaeFiscalDescricao ?? "N/D",
                    ["Localizacao"] = $"{dto.Municipio}/{dto.Uf}".Trim('/'),
                    ["Porte"] = dto.Porte ?? "N/D",
                    ["NaturezaJuridica"] = dto.NaturezaJuridica ?? "N/D"
                }
            });

            var partnerNodes = new List<GraphNode>();
            var createdEdges = new List<GraphEdge>();

            if (dto.Qsa is { Count: > 0 })
            {
                foreach (var socio in dto.Qsa)
                {
                    if (string.IsNullOrWhiteSpace(socio.NomeSocio))
                        continue;

                    // Tenta cruzar com entidade já existente no grafo (ex: Agente Público L0 ou Sócio L1)
                    var existingMatch = _graph.FindNodeByNormalizedNameOrDocument(socio.NomeSocio, socio.CnpjCpfDoSocio);
                    string partnerId;
                    GraphNode partnerNode;

                    if (existingMatch is not null)
                    {
                        partnerId = existingMatch.Id;
                        partnerNode = existingMatch;
                    }
                    else
                    {
                        var normKey = GraphEngine.NormalizeText(socio.NomeSocio).Replace(" ", "_");
                        partnerId = $"L1-SOCIO-{normKey[..Math.Min(normKey.Length, 24)]}-{Math.Abs(socio.NomeSocio.GetHashCode()) % 9999}";
                        DateTime? entryDate = DateTime.TryParse(
                            socio.DataEntradaSociedade,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal,
                            out var parsedEntry)
                            ? parsedEntry
                            : null;

                        partnerNode = _graph.UpsertNode(new GraphNode
                        {
                            Id = partnerId,
                            Label = socio.NomeSocio.Trim(),
                            Document = string.IsNullOrWhiteSpace(socio.CnpjCpfDoSocio) ? "CPF/CNPJ Mascarado (QSA)" : socio.CnpjCpfDoSocio,
                            Layer = NodeLayer.L1_FamiliarSocio,
                            SubType = string.IsNullOrWhiteSpace(socio.QualificacaoSocio) ? "Sócio QSA" : socio.QualificacaoSocio,
                            CreatedDate = entryDate,
                            Source = "BrasilAPI (QSA / Receita Federal)",
                            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["Qualificacao"] = socio.QualificacaoSocio ?? "Sócio",
                                ["EntradaSociedade"] = entryDate?.ToString("yyyy-MM-dd") ?? "N/D",
                                ["FaixaEtaria"] = socio.FaixaEtaria ?? "N/D"
                            }
                        });
                    }

                    partnerNodes.Add(partnerNode);

                    var edge = _graph.UpsertEdge(new GraphEdge
                    {
                        Id = $"EDGE-QSA-{partnerId}-{companyId}",
                        SourceId = partnerId,
                        TargetId = companyId,
                        RelationType = "SOCIO_QSA",
                        Weight = 0.85,
                        FinancialValue = 0m,
                        OfficialSource = "BrasilAPI / Receita Federal (QSA)",
                        EventDate = openingDate,
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Qualificacao"] = socio.QualificacaoSocio ?? "Sócio"
                        }
                    });

                    createdEdges.Add(edge);
                }
            }

            // Se a chave do Portal da Transparência estiver configurada, enriquece automaticamente com contratos federais
            var transparenciaResult = await EnrichContractsFromTransparenciaAsync(cnpj, companyId, ct);

            _graph.RecalculateAllRisks();
            var updatedCompany = _graph.GetNode(companyId) ?? companyNode;

            return new CnpjIngestionResult(
                true,
                $"CNPJ {formattedCnpj} ({updatedCompany.Label}) ingerido ao vivo com {partnerNodes.Count} sócio(s) do QSA.",
                cnpj,
                updatedCompany,
                partnerNodes,
                createdEdges,
                transparenciaResult.Success && transparenciaResult.Configured,
                transparenciaResult.RecordsIngested);
        }
        catch (TaskCanceledException)
        {
            return new CnpjIngestionResult(false, "Tempo limite excedido ao consultar BrasilAPI.", cnpj, null, Array.Empty<GraphNode>(), Array.Empty<GraphEdge>(), false, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao consultar CNPJ {Cnpj} na BrasilAPI", cnpj);
            return new CnpjIngestionResult(false, $"Falha na consulta à BrasilAPI: {ex.Message}", cnpj, null, Array.Empty<GraphNode>(), Array.Empty<GraphEdge>(), false, 0);
        }
    }

    /// <summary>
    /// Conector preparado para o Portal da Transparência (CGU) — Contratos Federais por CNPJ.
    /// Requer variável de ambiente TRANSPARENCIA_API_KEY (header: chave-api-dados).
    /// </summary>
    public async Task<ExternalConnectorStatus> EnrichContractsFromTransparenciaAsync(
        string rawCnpj,
        string companyNodeId,
        CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("TRANSPARENCIA_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ExternalConnectorStatus(
                false,
                "Portal da Transparência (CGU)",
                false,
                "Variável TRANSPARENCIA_API_KEY não configurada — operando sem consulta federal ao vivo.",
                0);
        }

        var cnpj = GraphEngine.NormalizeDigits(rawCnpj);
        try
        {
            var client = _httpClientFactory.CreateClient("Transparencia");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{TransparenciaBaseUrl}contratos/cpf-cnpj?cpfCnpj={cnpj}&pagina=1");
            req.Headers.Add("chave-api-dados", apiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new ExternalConnectorStatus(false, "Portal da Transparência (CGU)", true, $"HTTP {(int)resp.StatusCode} retornado pela API.", 0);
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new ExternalConnectorStatus(true, "Portal da Transparência (CGU)", true, "Nenhum contrato federal localizado.", 0);

            var count = 0;
            foreach (var item in doc.RootElement.EnumerateArray().Take(10))
            {
                var id = item.TryGetProperty("id", out var idProp) ? idProp.ToString() : Guid.NewGuid().ToString("N")[..8];
                var numero = item.TryGetProperty("numero", out var numProp) ? numProp.GetString() ?? id : id;
                var valor = item.TryGetProperty("valorFinalCompra", out var valProp) && valProp.TryGetDecimal(out var dec) ? dec : 0m;
                var objeto = item.TryGetProperty("objeto", out var objProp) ? objProp.GetString() ?? "Contrato Público Federal" : "Contrato Público Federal";
                var dataAssinatura = item.TryGetProperty("dataAssinatura", out var dtProp) && DateTime.TryParse(dtProp.GetString(), out var dt) ? dt : (DateTime?)null;

                var orgaoNome = "Órgão Federal";
                if (item.TryGetProperty("unidadeGestora", out var ug) &&
                    ug.TryGetProperty("orgaoVinculado", out var ov) &&
                    ov.TryGetProperty("nome", out var on))
                {
                    orgaoNome = on.GetString() ?? orgaoNome;
                }

                var contractNodeId = $"L3-CGU-CTR-{id}";
                var orgaoNodeId = $"L4-ORGAO-{Math.Abs(orgaoNome.GetHashCode())}";

                _graph.UpsertNode(new GraphNode
                {
                    Id = orgaoNodeId,
                    Label = orgaoNome,
                    Document = "Órgão Público Federal",
                    Layer = NodeLayer.L4_DoadorOrgao,
                    SubType = "Órgão Contratante (CGU)",
                    Source = "Portal da Transparência (CGU)"
                });

                _graph.UpsertNode(new GraphNode
                {
                    Id = contractNodeId,
                    Label = $"Contrato Federal #{numero}",
                    Document = $"PROC-{id}",
                    Layer = NodeLayer.L3_ContratoEmenda,
                    SubType = "Contrato Público Federal",
                    FinancialExposure = valor,
                    CreatedDate = dataAssinatura,
                    Source = "Portal da Transparência (CGU)",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Objeto"] = objeto.Length > 240 ? objeto[..240] + "..." : objeto,
                        ["Orgao"] = orgaoNome
                    }
                });

                _graph.UpsertEdge(new GraphEdge
                {
                    Id = $"EDGE-CGU-ORG-{orgaoNodeId}-{contractNodeId}",
                    SourceId = orgaoNodeId,
                    TargetId = contractNodeId,
                    RelationType = "CONTRATO_PUBLICO",
                    Weight = 0.8,
                    FinancialValue = valor,
                    OfficialSource = "Portal da Transparência (CGU)",
                    EventDate = dataAssinatura
                });

                _graph.UpsertEdge(new GraphEdge
                {
                    Id = $"EDGE-CGU-CTR-{contractNodeId}-{companyNodeId}",
                    SourceId = contractNodeId,
                    TargetId = companyNodeId,
                    RelationType = "CONTRATO_PUBLICO",
                    Weight = 0.9,
                    FinancialValue = valor,
                    OfficialSource = "Portal da Transparência (CGU)",
                    EventDate = dataAssinatura
                });

                count++;
            }

            return new ExternalConnectorStatus(true, "Portal da Transparência (CGU)", true, $"{count} contrato(s) federal(is) ingerido(s).", count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao consultar Portal da Transparência para CNPJ {Cnpj}", cnpj);
            return new ExternalConnectorStatus(false, "Portal da Transparência (CGU)", true, ex.Message, 0);
        }
    }

    /// <summary>
    /// Conector preparado para Portal da Transparência (Emendas Parlamentares) e Servidores Públicos.
    /// </summary>
    public async Task<ExternalConnectorStatus> QueryTransparenciaEmendasAsync(string nomeAutor, int ano, CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("TRANSPARENCIA_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ExternalConnectorStatus(
                false,
                "Portal da Transparência — Emendas",
                false,
                "Defina TRANSPARENCIA_API_KEY para consultar emendas parlamentares ao vivo.",
                0);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("Transparencia");
            var url = $"{TransparenciaBaseUrl}emendas?ano={ano}&nomeAutor={Uri.EscapeDataString(nomeAutor)}&pagina=1";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("chave-api-dados", apiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return new ExternalConnectorStatus(false, "Portal da Transparência — Emendas", true, $"HTTP {(int)resp.StatusCode}", 0);

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new ExternalConnectorStatus(true, "Portal da Transparência — Emendas", true, "Nenhuma emenda localizada.", 0);

            var authorId = $"L0-AUTOR-{Math.Abs(GraphEngine.NormalizeText(nomeAutor).GetHashCode())}";
            _graph.UpsertNode(new GraphNode
            {
                Id = authorId,
                Label = nomeAutor.Trim(),
                Document = "Autor de Emenda",
                Layer = NodeLayer.L0_AgentePublico,
                SubType = "Parlamentar / Autor de Emenda",
                Source = "Portal da Transparência (CGU)"
            });

            var ingested = 0;
            foreach (var item in doc.RootElement.EnumerateArray().Take(8))
            {
                var codigo = item.TryGetProperty("codigoEmenda", out var cProp) ? cProp.GetString() ?? Guid.NewGuid().ToString("N")[..6] : Guid.NewGuid().ToString("N")[..6];
                var funcao = item.TryGetProperty("funcao", out var fProp) ? fProp.GetString() ?? "Geral" : "Geral";
                var valorStr = item.TryGetProperty("valorEmpenhado", out var vProp) ? vProp.GetString() : "0";
                decimal.TryParse(valorStr?.Replace(".", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var valor);

                var emendaId = $"L3-EMENDA-{codigo}";
                _graph.UpsertNode(new GraphNode
                {
                    Id = emendaId,
                    Label = $"Emenda #{codigo} ({funcao})",
                    Document = codigo,
                    Layer = NodeLayer.L3_ContratoEmenda,
                    SubType = $"Emenda Parlamentar {ano}",
                    FinancialExposure = valor,
                    Source = "Portal da Transparência (CGU)"
                });

                _graph.UpsertEdge(new GraphEdge
                {
                    Id = $"EDGE-EMENDA-{authorId}-{emendaId}",
                    SourceId = authorId,
                    TargetId = emendaId,
                    RelationType = "EMENDA_DESTINADA",
                    Weight = 0.85,
                    FinancialValue = valor,
                    OfficialSource = "Portal da Transparência (CGU)"
                });

                ingested++;
            }

            _graph.RecalculateAllRisks();
            return new ExternalConnectorStatus(true, "Portal da Transparência — Emendas", true, $"{ingested} emenda(s) ingerida(s).", ingested);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro ao consultar emendas para {Autor}", nomeAutor);
            return new ExternalConnectorStatus(false, "Portal da Transparência — Emendas", true, ex.Message, 0);
        }
    }

    /// <summary>
    /// Conector real para Dados Abertos da Câmara dos Deputados (API v2 pública).
    /// Busca deputado por nome e ingere suas despesas parlamentares (CEAP) e fornecedores no grafo.
    /// </summary>
    public async Task<ExternalConnectorStatus> IngestDeputadoCamaraAsync(string nomeParlamentar, int? ano = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nomeParlamentar))
            return new ExternalConnectorStatus(false, "Dados Abertos da Câmara", true, "Informe o nome do parlamentar.", 0);

        var targetYear = ano ?? DateTime.UtcNow.Year;
        var baseUrl = Environment.GetEnvironmentVariable("CAMARA_API_BASE_URL") ?? CamaraBaseUrl;

        try
        {
            var client = _httpClientFactory.CreateClient("Camara");
            using var resp = await client.GetAsync($"{baseUrl.TrimEnd('/')}/deputados?nome={Uri.EscapeDataString(nomeParlamentar.Trim())}&itens=1", ct);
            if (!resp.IsSuccessStatusCode)
                return new ExternalConnectorStatus(false, "Dados Abertos da Câmara", true, $"HTTP {(int)resp.StatusCode} na busca de deputados.", 0);

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("dados", out var dados) || dados.GetArrayLength() == 0)
                return new ExternalConnectorStatus(false, "Dados Abertos da Câmara", true, $"Nenhum deputado encontrado para '{nomeParlamentar}'.", 0);

            var dep = dados[0];
            var depId = dep.GetProperty("id").GetInt32();
            var depNome = dep.GetProperty("nome").GetString() ?? nomeParlamentar;
            var partido = dep.TryGetProperty("siglaPartido", out var p) ? p.GetString() ?? "N/D" : "N/D";
            var uf = dep.TryGetProperty("siglaUf", out var u) ? u.GetString() ?? "BR" : "BR";

            var depNodeId = $"L0-CAMARA-{depId}";
            _graph.UpsertNode(new GraphNode
            {
                Id = depNodeId,
                Label = $"Dep. {depNome} ({partido}-{uf})",
                Document = $"CAMARA-{depId}",
                Layer = NodeLayer.L0_AgentePublico,
                SubType = "Deputado Federal (Câmara)",
                Source = "Dados Abertos da Câmara dos Deputados",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Partido"] = partido,
                    ["UF"] = uf,
                    ["IdCamara"] = depId.ToString()
                }
            });

            // Consulta despesas CEAP do parlamentar
            using var despResp = await client.GetAsync($"{baseUrl.TrimEnd('/')}/deputados/{depId}/despesas?ano={targetYear}&itens=12&ordem=DESC&ordenarPor=valorLiquido", ct);
            var ingested = 0;

            if (despResp.IsSuccessStatusCode)
            {
                await using var despStream = await despResp.Content.ReadAsStreamAsync(ct);
                using var despDoc = await JsonDocument.ParseAsync(despStream, cancellationToken: ct);
                if (despDoc.RootElement.TryGetProperty("dados", out var despesas))
                {
                    foreach (var d in despesas.EnumerateArray().Take(8))
                    {
                        var fornecedor = d.TryGetProperty("nomeFornecedor", out var fn) ? fn.GetString() ?? "Fornecedor CEAP" : "Fornecedor CEAP";
                        var cnpjFornecedor = GraphEngine.NormalizeDigits(d.TryGetProperty("cnpjCpfFornecedor", out var cf) ? cf.GetString() : string.Empty);
                        var tipo = d.TryGetProperty("tipoDespesa", out var td) ? td.GetString() ?? "Despesa CEAP" : "Despesa CEAP";
                        var valor = d.TryGetProperty("valorLiquido", out var vl) && vl.TryGetDecimal(out var dec) ? dec : 0m;
                        var codDoc = d.TryGetProperty("codDocumento", out var cd) ? cd.ToString() : Guid.NewGuid().ToString("N")[..6];
                        var dataDoc = d.TryGetProperty("dataDocumento", out var dd) && DateTime.TryParse(dd.GetString(), out var parsedDt) ? parsedDt : (DateTime?)null;

                        var supplierId = cnpjFornecedor.Length == 14
                            ? $"L2-CNPJ-{cnpjFornecedor}"
                            : $"L2-FORN-{Math.Abs(GraphEngine.NormalizeText(fornecedor).GetHashCode())}";

                        _graph.UpsertNode(new GraphNode
                        {
                            Id = supplierId,
                            Label = fornecedor,
                            Document = cnpjFornecedor.Length == 14 ? FormatCnpj(cnpjFornecedor) : "CPF/CNPJ CEAP",
                            Layer = NodeLayer.L2_EmpresaCnpj,
                            SubType = "Fornecedor CEAP",
                            Source = "Dados Abertos da Câmara dos Deputados"
                        });

                        var expenseId = $"L3-CEAP-{depId}-{codDoc}";
                        _graph.UpsertNode(new GraphNode
                        {
                            Id = expenseId,
                            Label = $"{tipo[..Math.Min(tipo.Length, 38)]}",
                            Document = $"CEAP-{codDoc}",
                            Layer = NodeLayer.L3_ContratoEmenda,
                            SubType = "Despesa Parlamentar (CEAP)",
                            FinancialExposure = valor,
                            CreatedDate = dataDoc,
                            Source = "Dados Abertos da Câmara dos Deputados"
                        });

                        _graph.UpsertEdge(new GraphEdge
                        {
                            Id = $"EDGE-CEAP-DEP-{depNodeId}-{expenseId}",
                            SourceId = depNodeId,
                            TargetId = expenseId,
                            RelationType = "DESPESA_PARLAMENTAR",
                            Weight = 0.75,
                            FinancialValue = valor,
                            OfficialSource = "Dados Abertos da Câmara dos Deputados",
                            EventDate = dataDoc
                        });

                        _graph.UpsertEdge(new GraphEdge
                        {
                            Id = $"EDGE-CEAP-FORN-{expenseId}-{supplierId}",
                            SourceId = expenseId,
                            TargetId = supplierId,
                            RelationType = "CONTRATO_PUBLICO",
                            Weight = 0.75,
                            FinancialValue = valor,
                            OfficialSource = "Dados Abertos da Câmara dos Deputados",
                            EventDate = dataDoc
                        });

                        ingested++;
                    }
                }
            }

            _graph.RecalculateAllRisks();
            return new ExternalConnectorStatus(true, "Dados Abertos da Câmara", true, $"Deputado {depNome} ingerido com {ingested} despesa(s) CEAP.", ingested);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro ao consultar API da Câmara dos Deputados");
            return new ExternalConnectorStatus(false, "Dados Abertos da Câmara", true, ex.Message, 0);
        }
    }

    /// <summary>
    /// Dataset de demonstração realista (fictício para auditoria forense) cobrindo todas as camadas L0–L4
    /// e acionando todos os padrões determinísticos: circuito fechado, empresa recém-aberta,
    /// auto-direcionamento societário/familiar e duplo vínculo simultâneo.
    /// </summary>
    public void SeedRealisticDataset()
    {
        // =========================================================================
        // CASO 1 (CRÍTICO): Circuito Fechado Doação ↔ Emenda ↔ Contrato + Familiar + Empresa Recém-Aberta
        // =========================================================================
        _graph.UpsertNode(new GraphNode
        {
            Id = "L0-DEP-MARCOS",
            Label = "Dep. Marcos Valério Costa",
            Document = "CPF ***.418.920-**",
            Layer = NodeLayer.L0_AgentePublico,
            SubType = "Deputado Federal — Comissão de Infraestrutura",
            Source = "Câmara dos Deputados | TSE DivulgaCand",
            Metadata = new()
            {
                ["Cargo"] = "Deputado Federal",
                ["UF"] = "MG",
                ["Comissao"] = "Viaçao, Transportes e Infraestrutura",
                ["Mandato"] = "2023-2027"
            }
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L1-RENATA-COSTA",
            Label = "Renata Costa Alencar",
            Document = "CPF ***.772.104-**",
            Layer = NodeLayer.L1_FamiliarSocio,
            SubType = "Familiar (Irmã) & Sócia Administradora",
            Source = "Receita Federal (QSA) | Base Civil",
            Metadata = new()
            {
                ["GrauParentesco"] = "Irmã (2º Grau)",
                ["ParticipacaoSocietaria"] = "85% das cotas"
            }
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L2-VERTICE-INFRA",
            Label = "Vértice Infraestrutura & Logística Ltda",
            Document = "34.812.905/0001-42",
            Layer = NodeLayer.L2_EmpresaCnpj,
            SubType = "CNPJ Ativo — Empresa Recém-Aberta",
            CreatedDate = new DateTime(2025, 01, 14),
            Source = "BrasilAPI (Receita Federal) | PNCP",
            Metadata = new()
            {
                ["CapitalSocial"] = "50000",
                ["CapitalSocialFormatado"] = "R$ 50.000,00",
                ["DataAbertura"] = "2025-01-14",
                ["CNAE"] = "Obras de engenharia civil e logística",
                ["Localizacao"] = "Belo Horizonte/MG"
            }
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L3-EMENDA-8841",
            Label = "Emenda Parlamentar #2025-8841",
            Document = "EM-2025-8841",
            Layer = NodeLayer.L3_ContratoEmenda,
            SubType = "Transferência Especial (Emenda Pix)",
            FinancialExposure = 4_850_000m,
            CreatedDate = new DateTime(2025, 02, 20),
            Source = "Portal da Transparência (CGU)",
            Metadata = new()
            {
                ["Modalidade"] = "Transferência Especial",
                ["Funcao"] = "Urbanismo e Infraestrutura"
            }
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L3-CONTRATO-094",
            Label = "Contrato Emergencial #094/2025",
            Document = "CTR-094/2025-FNOR",
            Layer = NodeLayer.L3_ContratoEmenda,
            SubType = "Dispensa Emergencial de Licitação",
            FinancialExposure = 4_620_000m,
            CreatedDate = new DateTime(2025, 03, 27), // 72 dias após abertura da Vértice
            Source = "Portal da Transparência (CGU) | PNCP",
            Metadata = new()
            {
                ["Modalidade"] = "Dispensa Emergencial (Art. 75)",
                ["PrazoExecucao"] = "180 dias"
            }
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L4-CONSORCIO-HORIZONTE",
            Label = "Consórcio Horizonte Participações S.A.",
            Document = "12.409.881/0001-10",
            Layer = NodeLayer.L4_DoadorOrgao,
            SubType = "Doador de Campanha / Holding Privada",
            Source = "TSE DivulgaCand | COAF/Simulado",
            Metadata = new()
            {
                ["TipoDoador"] = "Grupo Econômico / Sócios Doadores",
                ["Eleicao"] = "2022 / 2024"
            }
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L4-FUNDO-OBRAS",
            Label = "Fundo Nacional de Obras Regionais",
            Document = "UG 530012",
            Layer = NodeLayer.L4_DoadorOrgao,
            SubType = "Órgão Público Repassador",
            Source = "Portal da Transparência (CGU)"
        });

        // Arestas Caso 1 (incluindo circuito fechado direcionado L4 -> L0 -> L3 -> L3 -> L2 -> L4)
        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C1-DOACAO",
            SourceId = "L4-CONSORCIO-HORIZONTE",
            TargetId = "L0-DEP-MARCOS",
            RelationType = "DOACAO_CAMPANHA",
            Weight = 0.95,
            FinancialValue = 420_000m,
            OfficialSource = "TSE — Prestação de Contas Eleitorais",
            EventDate = new DateTime(2024, 09, 12)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C1-FAMILIAR",
            SourceId = "L0-DEP-MARCOS",
            TargetId = "L1-RENATA-COSTA",
            RelationType = "VINCULO_FAMILIAR",
            Weight = 0.90,
            FinancialValue = 0m,
            OfficialSource = "Cruzamento Registral / Base Civil"
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C1-SOCIO",
            SourceId = "L1-RENATA-COSTA",
            TargetId = "L2-VERTICE-INFRA",
            RelationType = "SOCIO_ADMINISTRADOR",
            Weight = 0.95,
            FinancialValue = 0m,
            OfficialSource = "Receita Federal (QSA)",
            EventDate = new DateTime(2025, 01, 14)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C1-EMENDA",
            SourceId = "L0-DEP-MARCOS",
            TargetId = "L3-EMENDA-8841",
            RelationType = "EMENDA_DESTINADA",
            Weight = 0.92,
            FinancialValue = 4_850_000m,
            OfficialSource = "Portal da Transparência (CGU)",
            EventDate = new DateTime(2025, 02, 20)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C1-ORGAO-EMENDA",
            SourceId = "L4-FUNDO-OBRAS",
            TargetId = "L3-EMENDA-8841",
            RelationType = "REPASSE_ORCAMENTARIO",
            Weight = 0.70,
            FinancialValue = 4_850_000m,
            OfficialSource = "SIAFI / Portal da Transparência",
            EventDate = new DateTime(2025, 02, 28)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C1-EMENDA-CONTRATO",
            SourceId = "L3-EMENDA-8841",
            TargetId = "L3-CONTRATO-094",
            RelationType = "FINANCIA_CONTRATO",
            Weight = 0.95,
            FinancialValue = 4_620_000m,
            OfficialSource = "PNCP / Portal da Transparência",
            EventDate = new DateTime(2025, 03, 27)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C1-CONTRATO-EMPRESA",
            SourceId = "L3-CONTRATO-094",
            TargetId = "L2-VERTICE-INFRA",
            RelationType = "CONTRATO_EMERGENCIAL",
            Weight = 0.98,
            FinancialValue = 4_620_000m,
            OfficialSource = "PNCP / Diário Oficial da União",
            EventDate = new DateTime(2025, 03, 27)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C1-SUBCONTRATO-LOOP",
            SourceId = "L2-VERTICE-INFRA",
            TargetId = "L4-CONSORCIO-HORIZONTE",
            RelationType = "REPASSE_SUBCONTRATACAO",
            Weight = 0.92,
            FinancialValue = 1_350_000m,
            OfficialSource = "Notas Fiscais Eletrônicas / Auditoria",
            EventDate = new DateTime(2025, 05, 10)
        });

        // =========================================================================
        // CASO 2 (CRÍTICO): Empresa Recém-Aberta + Capital Ínfimo + Aditivo Concentrado
        // =========================================================================
        _graph.UpsertNode(new GraphNode
        {
            Id = "L1-EDUARDO-TAVARES",
            Label = "Eduardo Henrique Tavares",
            Document = "CPF ***.903.511-**",
            Layer = NodeLayer.L1_FamiliarSocio,
            SubType = "Sócio-Administrador & Ex-Assessor Parlamentar",
            Source = "Receita Federal (QSA) | Folha Câmara",
            Metadata = new()
            {
                ["Historico"] = "Exonerado do gabinete 60 dias antes da abertura da SPE"
            }
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L2-NOVATEC-SPE",
            Label = "NovaTec Soluções Digitais SPE Ltda",
            Document = "51.209.441/0001-88",
            Layer = NodeLayer.L

                Layer = NodeLayer.L2_EmpresaCnpj,
            SubType = "SPE Recém-Aberta — Capital Social Ínfimo",
            CreatedDate = new DateTime(2025, 02, 03),
            Source = "BrasilAPI (Receita Federal) | PNCP",
            Metadata = new()
            {
                ["CapitalSocial"] = "15000",
                ["CapitalSocialFormatado"] = "R$ 15.000,00",
                ["DataAbertura"] = "2025-02-03",
                ["CNAE"] = "Consultoria em tecnologia da informação",
                ["Localizacao"] = "Brasília/DF"
            }
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L3-CONTRATO-TI-218",
            Label = "Contrato TI & Analytics #218/2025",
            Document = "CTR-218/2025-SGI",
            Layer = NodeLayer.L3_ContratoEmenda,
            SubType = "Licitação + Aditivo Emergencial 25%",
            FinancialExposure = 3_240_000m,
            CreatedDate = new DateTime(2025, 04, 15), // 71 dias após abertura da NovaTec SPE
            Source = "Portal da Transparência (CGU) | PNCP",
            Metadata = new()
            {
                ["Modalidade"] = "Pregão Eletrônico + Aditivo Emergencial",
                ["Objeto"] = "Plataforma de gestão e licenciamento corporativo"
            }
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L4-SEC-INOVACAO",
            Label = "Secretaria de Gestão & Inovação Pública",
            Document = "UG 201055",
            Layer = NodeLayer.L4_DoadorOrgao,
            SubType = "Órgão Público Contratante",
            Source = "Portal da Transparência (CGU)"
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C2-EXASSESSOR",
            SourceId = "L0-DEP-MARCOS",
            TargetId = "L1-EDUARDO-TAVARES",
            RelationType = "EX_ASSESSOR_GABINETE",
            Weight = 0.85,
            FinancialValue = 0m,
            OfficialSource = "Câmara dos Deputados — Folha de Gabinete",
            EventDate = new DateTime(2024, 12, 05)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C2-SOCIO",
            SourceId = "L1-EDUARDO-TAVARES",
            TargetId = "L2-NOVATEC-SPE",
            RelationType = "SOCIO_ADMINISTRADOR",
            Weight = 0.94,
            FinancialValue = 0m,
            OfficialSource = "Receita Federal (QSA)",
            EventDate = new DateTime(2025, 02, 03)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C2-ORGAO-CTR",
            SourceId = "L4-SEC-INOVACAO",
            TargetId = "L3-CONTRATO-TI-218",
            RelationType = "LICITACAO_ADITIVO",
            Weight = 0.88,
            FinancialValue = 3_240_000m,
            OfficialSource = "PNCP / Portal da Transparência",
            EventDate = new DateTime(2025, 04, 15)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C2-CTR-NOVATEC",
            SourceId = "L3-CONTRATO-TI-218",
            TargetId = "L2-NOVATEC-SPE",
            RelationType = "CONTRATO_ADITIVO_CONCENTRADO",
            Weight = 0.96,
            FinancialValue = 3_240_000m,
            OfficialSource = "PNCP / Diário Oficial da União",
            EventDate = new DateTime(2025, 04, 15)
        });

        // =========================================================================
        // CASO 3 (ALTA / CRÍTICO): Duplo Vínculo Simultâneo (Ordenador de Despesas + Sócio)
        // =========================================================================
        _graph.UpsertNode(new GraphNode
        {
            Id = "L0-DIR-CLAUDIO",
            Label = "Cláudio Roberto Mendonça",
            Document = "CPF ***.615.208-**",
            Layer = NodeLayer.L0_AgentePublico,
            SubType = "Diretor de Compras / Ordenador de Despesas",
            Source = "SIAPE / Portal da Transparência (CGU)",
            Metadata = new()
            {
                ["Cargo"] = "DAS-5 — Diretor de Suprimentos e Compras",
                ["Orgao"] = "Secretaria Estadual de Saúde Integrada",
                ["SituacaoFuncional"] = "Ativo Permanente"
            }
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L2-MEDPRIME-DIST",
            Label = "MedPrime Distribuidora Hospitalar Ltda",
            Document = "28.940.112/0001-67",
            Layer = NodeLayer.L2_EmpresaCnpj,
            SubType = "Fornecedor Hospitalar — QSA Conflitante",
            CreatedDate = new DateTime(2021, 08, 19),
            Source = "BrasilAPI (Receita Federal) | PNCP",
            Metadata = new()
            {
                ["CapitalSocial"] = "250000",
                ["CapitalSocialFormatado"] = "R$ 250.000,00",
                ["DataAbertura"] = "2021-08-19",
                ["CNAE"] = "Comércio atacadista de instrumentos hospitalares",
                ["Localizacao"] = "São Paulo/SP"
            }
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L3-DISPENSA-412",
            Label = "Dispensa Hospitalar #412/2025",
            Document = "DSP-412/2025-SES",
            Layer = NodeLayer.L3_ContratoEmenda,
            SubType = "Dispensa de Licitação — Insumos Críticos",
            FinancialExposure = 2_180_000m,
            CreatedDate = new DateTime(2025, 05, 04),
            Source = "PNCP | Portal da Transparência",
            Metadata = new()
            {
                ["Modalidade"] = "Dispensa de Licitação",
                ["Justificativa"] = "Aquisição emergencial de insumos hospitalares"
            }
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L4-SEC-SAUDE",
            Label = "Secretaria de Saúde Integrada",
            Document = "UG 360041",
            Layer = NodeLayer.L4_DoadorOrgao,
            SubType = "Órgão Público Contratante",
            Source = "Portal da Transparência (CGU)"
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C3-SERVIDOR",
            SourceId = "L0-DIR-CLAUDIO",
            TargetId = "L4-SEC-SAUDE",
            RelationType = "SERVIDOR_ORDENADOR",
            Weight = 0.90,
            FinancialValue = 0m,
            OfficialSource = "SIAPE / Portal da Transparência",
            EventDate = new DateTime(2023, 03, 01)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C3-DUPLO-SOCIO",
            SourceId = "L0-DIR-CLAUDIO",
            TargetId = "L2-MEDPRIME-DIST",
            RelationType = "SOCIO_OCULTO_QSA",
            Weight = 0.98,
            FinancialValue = 0m,
            OfficialSource = "Receita Federal (QSA) — Cruzamento CPF",
            EventDate = new DateTime(2024, 06, 10)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C3-ORDENA-DISPENSA",
            SourceId = "L0-DIR-CLAUDIO",
            TargetId = "L3-DISPENSA-412",
            RelationType = "ORDENADOR_DISPENSA",
            Weight = 0.95,
            FinancialValue = 2_180_000m,
            OfficialSource = "Diário Oficial / PNCP",
            EventDate = new DateTime(2025, 05, 04)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C3-DISPENSA-MEDPRIME",
            SourceId = "L3-DISPENSA-412",
            TargetId = "L2-MEDPRIME-DIST",
            RelationType = "CONTRATO_DISPENSA",
            Weight = 0.96,
            FinancialValue = 2_180_000m,
            OfficialSource = "PNCP / Notas de Empenho",
            EventDate = new DateTime(2025, 05, 04)
        });

        // =========================================================================
        // CASO 4 (MÉDIA / ALTA): Concentração de Contratos em Fornecedor Ligado a Doador
        // =========================================================================
        _graph.UpsertNode(new GraphNode
        {
            Id = "L0-PREF-SILVIA",
            Label = "Pref. Sílvia Nogueira Prado",
            Document = "CPF ***.204.889-**",
            Layer = NodeLayer.L0_AgentePublico,
            SubType = "Chefe do Executivo Municipal",
            Source = "TSE DivulgaCand | TCE",
            Metadata = new()
            {
                ["Cargo"] = "Prefeita Municipal",
                ["UF"] = "PR",
                ["Mandato"] = "2025-2028"
            }
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L1-RODRIGO-PRADO",
            Label = "Rodrigo Nogueira Barros",
            Document = "CPF ***.531.019-**",
            Layer = NodeLayer.L1_FamiliarSocio,
            SubType = "Sócio Representante Comercial",
            Source = "Receita Federal (QSA)"
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L2-ALFA-PAVIMENTACAO",
            Label = "Alfa Pavimentação & Engenharia S.A.",
            Document = "19.332.710/0001-05",
            Layer = NodeLayer.L2_EmpresaCnpj,
            SubType = "Fornecedor Recorrente de Obras",
            CreatedDate = new DateTime(2016, 04, 11),
            Source = "BrasilAPI (Receita Federal) | TCE",
            Metadata = new()
            {
                ["CapitalSocial"] = "1800000",
                ["CapitalSocialFormatado"] = "R$ 1.800.000,00",
                ["DataAbertura"] = "2016-04-11",
                ["Localizacao"] = "Curitiba/PR"
            }
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L3-LICITACAO-77",
            Label = "Concorrência Pavimentação #077/2025",
            Document = "CONC-077/2025",
            Layer = NodeLayer.L3_ContratoEmenda,
            SubType = "Concorrência Pública — Lote Único",
            FinancialExposure = 1_190_000m,
            CreatedDate = new DateTime(2025, 04, 28),
            Source = "TCE | PNCP"
        });

        _graph.UpsertNode(new GraphNode
        {
            Id = "L3-ADITIVO-77A",
            Label = "Termo Aditivo #077-A/2025",
            Document = "ADT-077A/2025",
            Layer = NodeLayer.L3_ContratoEmenda,
            SubType = "Reequilíbrio Econômico-Financeiro",
            FinancialExposure = 440_000m,
            CreatedDate = new DateTime(2025, 06, 18),
            Source = "TCE | PNCP"
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C4-SOCIO-ALFA",
            SourceId = "L1-RODRIGO-PRADO",
            TargetId = "L2-ALFA-PAVIMENTACAO",
            RelationType = "SOCIO_REPRESENTANTE",
            Weight = 0.78,
            FinancialValue = 0m,
            OfficialSource = "Receita Federal (QSA)"
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C4-DOACAO-PREF",
            SourceId = "L1-RODRIGO-PRADO",
            TargetId = "L0-PREF-SILVIA",
            RelationType = "DOACAO_CAMPANHA",
            Weight = 0.80,
            FinancialValue = 145_000m,
            OfficialSource = "TSE — Prestação de Contas",
            EventDate = new DateTime(2024, 09, 25)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C4-PREF-LIC",
            SourceId = "L0-PREF-SILVIA",
            TargetId = "L3-LICITACAO-77",
            RelationType = "HOMOLOGACAO_LICITACAO",
            Weight = 0.72,
            FinancialValue = 1_190_000m,
            OfficialSource = "TCE / Portal Municipal",
            EventDate = new DateTime(2025, 04, 28)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C4-LIC-ALFA",
            SourceId = "L3-LICITACAO-77",
            TargetId = "L2-ALFA-PAVIMENTACAO",
            RelationType = "CONTRATO_LICITACAO",
            Weight = 0.75,
            FinancialValue = 1_190_000m,
            OfficialSource = "PNCP",
            EventDate = new DateTime(2025, 04, 28)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C4-ADITIVO-ALFA",
            SourceId = "L3-ADITIVO-77A",
            TargetId = "L2-ALFA-PAVIMENTACAO",
            RelationType = "ADITIVO_CONTRATUAL",
            Weight = 0.74,
            FinancialValue = 440_000m,
            OfficialSource = "PNCP",
            EventDate = new DateTime(2025, 06, 18)
        });

        _graph.UpsertEdge(new GraphEdge
        {
            Id = "E-C4-LIC-ADITIVO",
            SourceId = "L3-LICITACAO-77",
            TargetId = "L3-ADITIVO-77A",
            RelationType = "GERA_ADITIVO",
            Weight = 0.70,
            FinancialValue = 440_000m,
            OfficialSource = "PNCP",
            EventDate = new DateTime(2025, 06, 18)
        });

        _graph.RecalculateAllRisks();
    }

    public static string FormatCnpj(string cnpj)
    {
        var digits = GraphEngine.NormalizeDigits(cnpj);
        if (digits.Length != 14) return cnpj;
        return $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-{digits[12..14]}";
    }

    private sealed class BrasilApiCnpjDto
    {
        [JsonPropertyName("razao_social")]
        public string? RazaoSocial { get; set; }

        [JsonPropertyName("nome_fantasia")]
        public string? NomeFantasia { get; set; }

        [JsonPropertyName("capital_social")]
        public decimal CapitalSocial { get; set; }

        [JsonPropertyName("data_inicio_atividade")]
        public string? DataInicioAtividade { get; set; }

        [JsonPropertyName("descricao_situacao_cadastral")]
        public string? DescricaoSituacaoCadastral { get; set; }

        [JsonPropertyName("cnae_fiscal_descricao")]
        public string? CnaeFiscalDescricao { get; set; }

        [JsonPropertyName("municipio")]
        public string? Municipio { get; set; }

        [JsonPropertyName("uf")]
        public string? Uf { get; set; }

        [JsonPropertyName("porte")]
        public string? Porte { get; set; }

        [JsonPropertyName("natureza_juridica")]
        public string? NaturezaJuridica { get; set; }

        [JsonPropertyName("qsa")]
        public List<BrasilApiQsaDto>? Qsa { get; set; }
    }

    private sealed class BrasilApiQsaDto
    {
        [JsonPropertyName("nome_socio")]
        public string? NomeSocio { get; set; }

        [JsonPropertyName("cnpj_cpf_do_socio")]
        public string? CnpjCpfDoSocio { get; set; }

        [JsonPropertyName("qualificacao_socio")]
        public string? QualificacaoSocio { get; set; }

        [JsonPropertyName("data_entrada_sociedade")]
        public string? DataEntradaSociedade { get; set; }

        [JsonPropertyName("faixa_etaria")]
        public string? FaixaEtaria { get; set; }
    }
}
                
