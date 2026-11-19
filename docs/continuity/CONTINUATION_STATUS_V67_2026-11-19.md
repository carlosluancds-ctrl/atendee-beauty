# Atendee Beauty — Continuity Status V67 (approved, 2026-11-19)

## Resultado

V67 aprovada após execução integral da CI no SHA `962649067d02f4d65e347025c16b5cd069927565`, branch `security/csrf-webhook-antiforgery-v23`.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44454896396)
- Run: `44454896396`, attempt 1, conclusão `success`.
- Job `Build and verify tenant isolation`: sucesso.
- O job concluiu frontend, browser E2E, build Release, domínio, PostgreSQL/RLS, Identity/TOTP, HTTP/full-stack E2E, limpeza de credenciais e remoção do banco descartável.

## Escopo V67 — isolamento da associação profissional-serviço

O teste full-stack usa dois salões reais de teste, cada um com seu próprio Owner, profissional e serviço. Além dos cenários V65/V66, verifica tentativas de atribuição de serviço estrangeiro a perfil profissional local:

- Owner B tenta atribuir o serviço A ao perfil profissional B: HTTP 400 `professional_services_validation_failed`.
- Após a rejeição, o perfil B continua sem serviços atribuídos (`serviceIds` vazio).
- Manager A tenta atribuir o serviço B ao perfil profissional A: HTTP 400 `professional_services_validation_failed`.
- Após a rejeição, o perfil A continua sem serviços atribuídos (`serviceIds` vazio).
- As duas tentativas usam IDs reais conhecidos do outro salão, não UUIDs aleatórios.
- A atribuição legítima de serviço A ao profissional A continua funcionando depois dos testes negativos.

O log confirma os 8 testes browser do portal aprovados e o teste full-stack real com `1 passed (6.3s)`, contra Kestrel e PostgreSQL descartável.

## CI integral

- Auditoria, testes e build frontend: sucesso.
- 8 browser tests do portal: sucesso.
- Restore .NET, build Release e testes de domínio: sucesso.
- PostgreSQL 18.7, provisionamento e EF Core/RLS: sucesso.
- Identity, TOTP e tenant-context: sucesso.
- HTTP auth, antiforgery, sessão e full-stack browser: sucesso.
- Limpeza de credenciais e remoção do container: sucesso.

## Commits de implementação

- `962649067d02f4d65e347025c16b5cd069927565` — adiciona regressões bidirecionais para atribuição de serviços cross-tenant e confirma ausência de mutação parcial.

## Histórico preservado

- V65: isolamento de serviços por tenant, com leitura/atualização cruzada retornando 404.
- V66: descoberta/criação de perfis profissionais por tenant e bloqueio de IDs de identidade/perfil estrangeiros.
- V64: transições de sessão antiforgery validadas no full-stack.
- Todos os arquivos de continuidade anteriores permanecem preservados.

## Próxima etapa

Continuar backend-first com revisão sistemática dos endpoints e stores existentes, procurando qualquer leitura ou mutação que use ID de recurso, identidade ou associação sem escopo tenant. Não alterar políticas aprovadas nem introduzir administração cross-tenant.

## Guardrails

- `main` permanece intocada.
- Nenhum deploy ou alteração em produção foi realizado.
- PostgreSQL de CI é descartável e removido ao final.
- Credenciais temporárias são removidas do ambiente do job.
- Gestão administrativa cross-tenant, disable/revoke e transições de status continuam sujeitas à aprovação explícita descrita em `CLAUDE.md`.
