# Atendee Beauty — Continuity Status V66 (approved, 2026-11-19)

## Resultado

V66 aprovada após execução integral da CI no SHA `90c6b6c20a3b881395d07d335dda32eabaff009a`, branch `security/csrf-webhook-antiforgery-v23`.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44454083316)
- Run: `44454083316`, attempt 1, conclusão `success`.
- Job `Build and verify tenant isolation`: sucesso.
- Todos os passos obrigatórios passaram, incluindo frontend, browser E2E, build Release, domínio, PostgreSQL/RLS, Identity/TOTP, HTTP/full-stack E2E, limpeza de credenciais e remoção do banco descartável.

## Escopo V66 — isolamento de profissionais entre dois salões

A fixture full-stack provisiona um segundo profissional com membership ativa apenas no salão B. O browser E2E, contra Kestrel real e PostgreSQL 18.7 descartável, valida:

- A descoberta de membros elegíveis do salão A retorna somente a identidade profissional A.
- A descoberta de membros elegíveis do salão B retorna somente a identidade profissional B.
- O Owner B cria o perfil profissional B e a listagem B contém somente esse perfil.
- O Owner B não consegue criar perfil para a identidade profissional pertencente ao salão A: HTTP 400 `professional_membership_invalid`.
- O Owner A não consegue criar perfil para a identidade profissional pertencente ao salão B: HTTP 400 `professional_membership_invalid`.
- A listagem de perfis A permanece vazia antes de criação local, sem expor o perfil B.
- O Owner A não consegue alterar as atribuições do perfil B usando o ID conhecido: HTTP 404.

As verificações existentes de isolamento de serviços V65 e antiforgery V64 continuam no mesmo fluxo de navegador real. O log da CI confirma `1 passed (5.9s)` para o teste full-stack.

## Commits da implementação V66

- `c1333a70572d8cdcacea3dc6c475bfdb273cf6dd` — adiciona identidade profissional e membership exclusiva do segundo salão à fixture.
- `90c6b6c20a3b881395d07d335dda32eabaff009a` — adiciona asserções bidirecionais de descoberta/criação de perfis e bloqueio de atualização cross-tenant.

## Estado anterior preservado

- V65 aprovada: isolamento bidirecional de serviços entre salões, leitura/atualização por UUID estrangeiro retorna 404.
- V64 antiforgery: token anônimo após login e token autenticado após logout são rejeitados com HTTP 400 `antiforgery_validation_failed`.
- Checkpoints V23–V65 permanecem preservados; nenhum arquivo histórico foi sobrescrito.

## Próxima etapa

Continuar backend-first revisando as superfícies de associação profissional-serviço e os endpoints existentes para confirmar que cada ID de recurso e identidade é resolvido dentro do tenant confiável. Ampliar testes negativos de mutação cruzada, sem introduzir fluxos administrativos cross-tenant nem alterar políticas de status aprovadas.

## Guardrails

- `main` permanece intocada.
- Nenhum deploy ou alteração em produção foi realizado.
- Banco PostgreSQL de CI é descartável e removido ao final.
- Credenciais temporárias são removidas do ambiente do job.
- Não implementar gestão administrativa cross-tenant, disable/revoke ou transições de status sem a aprovação explícita exigida em `CLAUDE.md`.
