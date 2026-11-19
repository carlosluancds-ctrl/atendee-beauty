# Atendee Beauty — Continuity Status V69 (approved, 2026-11-19)

## Resultado

V69 aprovada após execução integral da CI no SHA `00c4b55955738b06602b8cbb0519f6f3ef60ebf4`, branch `security/csrf-webhook-antiforgery-v23`.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44456485697)
- Run: `44456485697`, attempt 1, conclusão `success`.
- Job `Build and verify tenant isolation`: sucesso.
- Frontend/browser, build Release, domínio, PostgreSQL/RLS, Identity/TOTP, HTTP/full-stack, limpeza de credenciais e remoção do banco descartável passaram.

## Escopo V69 — proveniência do ator de onboarding e atomicidade do audit MFA

### Onboarding e auditoria de status

O endpoint de conclusão de onboarding usa uma transação PostgreSQL própria, fora de `NpgsqlTenantTransactionRunner`. Antes, não propagava `app.actor_id`, de modo que o trigger de status podia gravar a transição sem o ator autenticado.

Correção aplicada:
- Limpa o valor de sessão de `app.actor_id` ao abrir a conexão para evitar herança de estado de conexão pooled.
- Dentro da transação, após assumir `atendee_runtime`, define `app.actor_id` transaction-local usando somente `actorId` obtido de `TrustedRequestContext`.
- Nenhum ator vindo de header, query ou corpo é aceito.

A suíte HTTP agora exige que a transição PendingVerification → SetupRequired registre exatamente `ownerUserId` em `ActorIdentityUserId`, mantendo o evento direcionado ao tenant e sem identity target. Também mantém a verificação de que mudanças feitas pelo migrator, sem ator autenticado, continuam com ator NULL.

### Falhas TOTP e audit duráveis

O caminho de TOTP inválido agora agrupa `AccessFailedAsync` e `operational_mfa.challenge_failed` na mesma transação. A regressão confirma:
- TOTP inválido é rejeitado e não conclui MFA.
- Tentativa TOTP inválida e replay de recovery code geram exatamente um evento de falha cada.
- As duas falhas incrementam o contador de tentativas.
- Um TOTP válido subsequente zera o contador.

## Commits de implementação

- `773fb0f7694643b8c6c95897ff574b99db280539` — define `app.actor_id` transaction-local no endpoint de onboarding usando o ator resolvido pelo middleware.
- `d90d8906d91a68da979be60e02ba5ffeaea5c1e0` — adiciona a regressão HTTP que exige o ID exato do Owner no evento de auditoria.
- `00c4b55955738b06602b8cbb0519f6f3ef60ebf4` — torna atômicos contador de falha TOTP e evento de auditoria, com regressões para TOTP inválido e reset após sucesso.

## Evidência CI

O log do run `44456485697` contém:
- `PASS The onboarding status audit records the authenticated Owner as its trusted actor and targets only the tenant.`
- `PASS An invalid TOTP code is rejected without completing MFA.`
- `PASS Invalid TOTP and replayed recovery-code attempts are each durably audited exactly once.`
- `PASS Invalid TOTP and replayed recovery-code attempts each increment the failed-attempt counter.`
- `PASS A successful TOTP challenge resets the accumulated failed-attempt counter.`
- HTTP authentication checks passed; full-stack browser E2E passed against Kestrel real e PostgreSQL descartável.

## Histórico preservado

- V65: isolamento bidirecional de serviços por tenant.
- V66: isolamento de identidade/perfil profissional entre salões.
- V67: associação profissional-serviço cross-tenant rejeitada sem mutação parcial.
- V68: spoofing de tenant/ator/papel/status rejeitado; Receptionist não consegue concluir onboarding como Owner.
- V64: antiforgery de sessão validado no full-stack.
- Todos os arquivos de continuidade anteriores permanecem preservados.

## Próxima etapa

Continuar backend-first revisando outras operações de autenticação e MFA que combinam alteração de estado com auditoria, assegurando atomicidade e proveniência do ator. Não alterar políticas aprovadas nem introduzir fluxos administrativos cross-tenant.

## Guardrails

- `main` permanece intocada.
- Nenhum deploy ou alteração em produção foi realizado.
- PostgreSQL de CI é descartável e removido ao final.
- Credenciais temporárias são removidas do ambiente do job.
- Gestão administrativa cross-tenant, disable/revoke e transições de status continuam sujeitas à aprovação explícita descrita em `CLAUDE.md`.
