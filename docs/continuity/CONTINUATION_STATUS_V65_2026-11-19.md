# Atendee Beauty — Continuity Status V65 (approved, 2026-11-19)

## Resultado

V65 aprovada após execução integral da CI no SHA `0a06ab045691a336c6114bf173f1383e3aafb4ac`, branch `security/csrf-webhook-antiforgery-v23`.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44451964941)
- Run: `44451964941`, attempt 1, conclusão `success`.
- Job: `Build and verify tenant isolation`, conclusão `success`.
- Todos os passos obrigatórios concluídos com sucesso, inclusive limpeza das credenciais descartáveis e remoção do container PostgreSQL.

## Escopo V65 — isolamento de serviços entre dois salões

Fixture de browser E2E cria um segundo Owner com MFA e provisiona um segundo tenant independente usando o fluxo de provisionamento existente. O teste de navegador real mantém Owner A e Owner B em contextos separados e valida:

- Owner B cria serviço próprio e sua listagem contém somente o serviço B.
- Owner B recebe HTTP 404 ao ler serviço A por UUID conhecido.
- Owner B recebe HTTP 404 ao tentar atualizar serviço A.
- Owner A mantém listagem contendo somente serviço A.
- Owner A recebe HTTP 404 ao ler serviço B por UUID conhecido.
- Owner A recebe HTTP 404 ao tentar atualizar serviço B.
- O cenário preserva as verificações antiforgery V64 para token anônimo usado após login e token autenticado reutilizado após logout.

O teste Playwright real terminou com `1 passed (4.4s)`, executado contra API Kestrel real e PostgreSQL descartável. O teste de isolamento de serviços também é complementado pelas verificações de RLS: o log registra que PostgreSQL RLS oculta serviço de outro salão mesmo quando o UUID é conhecido. As verificações de isolamento de notas entre tenants também passaram.

## CI integral observada

- Auditoria de dependências frontend: sucesso.
- Testes frontend: sucesso.
- Build frontend: sucesso.
- Instalação Chromium e 8 testes browser do portal: sucesso.
- Restore dependências .NET: sucesso.
- Build Release: sucesso.
- Testes de domínio: sucesso.
- PostgreSQL 18.7 isolado e provisionamento de banco/roles restritas: sucesso.
- EF Core e PostgreSQL RLS: sucesso.
- Identity, TOTP e tenant context: sucesso.
- HTTP auth, segurança de sessão e full-stack browser E2E: sucesso.
- Limpeza de credenciais e remoção do banco descartável: sucesso.

## Commits da implementação V65

- `0332863` — fixture com segundo Owner e tenant independente.
- `fc1d118` — primeira versão dos cenários bidirecionais.
- Commits subsequentes corrigiram matcher da URL, ordem dos cenários, captura do token e leitura do ID do serviço pela listagem autenticada.
- SHA validado pela CI: `0a06ab045691a336c6114bf173f1383e3aafb4ac`.

## Estado anterior

V64 também foi exercitada no mesmo full-stack browser test aprovado: o teste contém as duas asserções de token antiforgery obsoleto (token anônimo após login e token autenticado após logout), ambas exigindo HTTP 400 `antiforgery_validation_failed`. O documento V64 histórico não foi reescrito para preservar o registro da situação existente à época; esta confirmação fica registrada aqui.

## Próxima etapa

Continuar backend-first. Revisar a matriz de autorização e as superfícies restantes que possam permitir acesso cross-tenant, priorizando testes negativos de leitura e mutação. Ler as decisões de produto/política já aprovadas antes de qualquer mudança de autorização. Não implementar gestão administrativa cross-tenant, disable/revoke, nem alterar status de tenant sem as aprovações explícitas previstas em `CLAUDE.md`.

## Guardrails

- `main` permanece intocada.
- Nenhum deploy ou alteração em produção foi realizado.
- PostgreSQL utilizado na CI foi descartável e removido ao final.
- Nenhuma credencial temporária foi mantida no ambiente do job.
- Preservar todos os checkpoints V anteriores.
