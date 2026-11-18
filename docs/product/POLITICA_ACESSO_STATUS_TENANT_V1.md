
# Política de acesso por status do tenant — V1

**Data:** 2026-11-18
**Natureza da decisão:** o proprietário do produto delegou explicitamente esta decisão ("Decida tudo da forma mais profissional"). Este documento registra a decisão tomada sob essa delegação. Ela é **reversível**: mudar uma linha da tabela e do teste exaustivo basta. Nenhum deploy, nenhum signup público e nenhum dado real dependem dela.

**Implementação:** `src/Atendee.Domain/Tenants/TenantAccessPolicy.cs` (fonte única da verdade) e `TenantCapability.cs`.
**Teste:** `tests/Atendee.Domain.Checks` (matriz exaustiva status × papel × capacidade, escrita de forma independente) e `tests/Atendee.Api.HttpChecks` (sessões reais).

## Princípios adotados

1. **Negar por padrão.** Status ou papel indefinido, capacidade vazia, composta ou desconhecida: negado.
2. **Sem login, nada.** Nenhuma capacidade é concedida a quem não pode autenticar-se naquele status.
3. **Avaliação a cada requisição.** A política roda no middleware de tenant em toda requisição autenticada; uma mudança de status ou papel vale na requisição seguinte, sem esperar expirar a sessão.
4. **Camada grossa.** A política de status é um filtro grosso. Ela nunca concede uma permissão que o papel não tenha (ver `docs/product/MATRIZ_PERMISSOES_V1.txt`); ela só retira.
5. **Direitos do titular preservados.** Exportação dos próprios dados permanece disponível enquanto o proprietário puder entrar (inclui `Suspended` e `Closing`), alinhado à diretriz já escrita em `ESPEC_IDENTIDADE_LOGIN_CADASTRO_V1.txt`, seção 3, passo 9.
6. **Custo controlado.** Automação (respostas de IA e mensagens ativas, que geram custo de provedor) só roda em estados comercialmente ativos, conforme `DECISOES_COMERCIAIS.txt`.

## Capacidades

| Capacidade | Cobre (permissões atômicas de `MATRIZ_PERMISSOES_V1`) |
|---|---|
| Login | Autenticar e manter sessão operacional |
| CompleteOnboarding | Verificar contato, aceitar termos, concluir dados essenciais do salão |
| ReadData | `*.read`, relatórios, auditoria do próprio salão |
| WriteData | `customer.*`, `appointment.*`, `service.manage`, `professional.manage`, `conversation.reply/assign` |
| Automation | Respostas de IA, `campaign.*`, mensagens ativas |
| ManageTeamAndSettings | `user.*`, `role.*`, `tenant.update`, `ai_config.manage`, `whatsapp.connection.manage` |
| Billing | Ver situação da assinatura e atualizar pagamento |
| ExportData | `customer.export`, `report.export` |

## Matriz decidida

Y = permitido (para os papéis que podem entrar naquele status), — = negado.

| Status | Quem pode entrar | Onboarding | Leitura | Escrita | Automação | Equipe/Config. | Cobrança | Exportação |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| PendingVerification | Proprietário | Y | — | — | — | — | — | — |
| SetupRequired | Proprietário, Gerente | Y | Y | Y | — | Y | Y | — |
| Trial | Todos | — | Y | Y | Y | Y | Y | Y |
| Active | Todos | — | Y | Y | Y | Y | Y | Y |
| PastDue | Todos | — | Y | Y | Y | Y | Y | Y |
| Suspended | Todos | — | Y | — | — | — | Y | Y |
| Closing | Proprietário | — | Y | — | — | — | Y | Y |
| Closed | Ninguém | — | — | — | — | — | — | — |

### Justificativas

- **PendingVerification:** o tenant ainda não provou o contato; só o proprietário conclui a verificação. Não deve existir dado de negócio.
- **SetupRequired:** o salão está sendo configurado; Proprietário e Gerente configuram. Automação fica desligada até o setup terminar, evitando custo e mensagens a clientes com configuração incompleta. Exportação não se aplica (sem dados).
- **Trial / Active:** operação completa. As restrições finas continuam sendo as de papel.
- **PastDue:** **período de tolerância com operação completa.** Cortar o atendimento de clientes finais por atraso de pagamento causa dano desproporcional e abre a porta para disputa. A duração do prazo é parâmetro comercial **a ser aplicado pelo processo de cobrança**, que move o tenant para `Suspended` ao fim do prazo. Valor inicial recomendado: 7 dias corridos (recomendação, **não** imposta por código nesta versão, pois ainda não há coluna nem processo de cobrança).
- **Suspended:** somente leitura, pagamento e exportação. Todos os papéis podem entrar para consultar, mas ninguém altera dados nem dispara automação. Segue a diretriz já escrita na espec de identidade.
- **Closing:** encerramento em andamento. Somente o proprietário entra, para conferir, quitar e exportar. Demais papéis perdem acesso na próxima requisição.
- **Closed:** nenhum acesso autoatendido. Retenção, exclusão e pedidos de exportação posteriores são processo de suporte com auditoria, a definir.

## Impacto no comportamento anterior

- Antes: login bloqueado apenas em `Closing`/`Closed`; sessões existentes em `Closing` continuavam passando; `PendingVerification`, `SetupRequired` e `Suspended` entravam sem restrição de papel.
- Agora: a tabela acima, aplicada no login, no desafio MFA, no enrollment de MFA e em toda requisição.
- Mudança visível: em `Closing` somente o proprietário entra (antes ninguém); em `SetupRequired` e `PendingVerification` papéis operacionais não entram.

## Fora do escopo desta decisão (continuam pendentes)

- Duração do prazo de `PastDue` e processo que transita `PastDue → Suspended`.
- Retenção, exclusão e exportação pós-`Closed`; política de reembolso e créditos no cancelamento (`DECISOES_COMERCIAIS.txt`).
- Quem e como altera o status (fluxo administrativo, exige Administrador Atendee, MFA e auditoria). Nenhum endpoint de alteração existe e nenhum deve ser criado antes disso.
- Auditoria de mudança de status do tenant (hoje só a de membership está auditada).
- Enforcement das capacidades Read/Write/Automation/Team/Billing/Export em endpoints de negócio: ainda não existem endpoints de negócio. Quando existirem, cada um deve declarar sua capacidade e consultar `TenantAccessPolicy.IsAllowed`, e deve ter teste negativo por status.
