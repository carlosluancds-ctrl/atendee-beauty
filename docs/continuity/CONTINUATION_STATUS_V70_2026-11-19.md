# Atendee Beauty — Continuity Status V70 (approved, 2026-11-19)

## Resultado

V70 aprovada após execução integral da CI no SHA `49b8e0bb235905f7d7d3663419fe9c2eb337214c`, branch `security/csrf-webhook-antiforgery-v23`.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44457062380)
- Run: `44457062380`, attempt 1, conclusão `success`.
- Job `Build and verify tenant isolation`: sucesso.
- Frontend/browser, build Release, domínio, PostgreSQL/RLS, Identity/TOTP, HTTP/full-stack, limpeza de credenciais e remoção do banco descartável passaram.

## Escopo V70 — atomicidade da falha de senha e auditoria

O login operacional usa `SignInManager.CheckPasswordSignInAsync(..., lockoutOnFailure: true)`, que altera `AccessFailedCount` em senha incorreta. Antes, o contador podia ser salvo antes da gravação obrigatória de `operational_auth.login_failed`.

Correção:
- Injeta o mesmo `AtendeeIdentityDbContext` scoped usado por UserManager e `PostgresSecurityAuditWriter`.
- Executa verificação de senha e gravação do evento de falha dentro da mesma transação.
- Commit acontece somente após o evento obrigatório; exceções/cancelamentos antes do commit deixam a transação ser revertida.
- A transação é encerrada antes de `PostgresTenantMembershipResolver`, que abre sua própria transação curta no mesmo DbContext.
- Quando a senha é válida, o reset do contador é confirmado antes de resolver o membership.

Regressões HTTP:
- Senha incorreta retorna 401, gera exatamente um `operational_auth.login_failed` e incrementa `AccessFailedCount` para 1.
- Senha válida subsequente retorna o fluxo restrito de setup MFA e zera `AccessFailedCount` para 0.

## Histórico recente preservado

- V69: onboarding registra o Owner autenticado como ator; falhas TOTP/recovery code têm contador e audit atômicos.
- V68: cabeçalhos/query/body forjados não alteram tenant/ator/papel/status; Receptionist não conclui onboarding como Owner.
- V67: atribuição de serviço cross-tenant rejeitada sem mutação parcial.
- V66: descoberta/criação de perfis profissionais e IDs estrangeiros isolados por tenant.
- V65: leitura/atualização de serviços cross-tenant retorna 404.
- V64: antiforgery de sessão validado no full-stack.
- Todos os arquivos de continuidade anteriores permanecem preservados.

## Commits de implementação

- `495abc10160d4210850b9cd5217b96b72e8b96de` — agrupa a verificação de senha, contador de falhas e evento de auditoria na mesma transação.
- `49b8e0bb235905f7d7d3663419fe9c2eb337214c` — adiciona asserções HTTP para incremento e reset do contador.

## Evidência CI

Run `44457062380` passou pelo build Release, testes de domínio, PostgreSQL 18.7/RLS, Identity/TOTP e HTTP/full-stack browser. A etapa HTTP executou as verificações de senha incorreta, login MFA, onboarding audit actor e todos os cenários anteriores; o full-stack browser E2E passou contra Kestrel real e PostgreSQL descartável.

## Próxima etapa

Continuar backend-first auditando outras operações de identidade e segurança de conta que combinam persistência com eventos obrigatórios. Priorizar caminhos de falha e transações que precisam reverter em conjunto, sem enfraquecer lockout ou alterar regras de autenticação.

## Guardrails

- `main` permanece intocada.
- Nenhum deploy ou alteração em produção foi realizado.
- PostgreSQL de CI é descartável e removido ao final.
- Credenciais temporárias são removidas do ambiente do job.
- Gestão administrativa cross-tenant, disable/revoke e transições de status continuam sujeitas à aprovação explícita descrita em `CLAUDE.md`.
