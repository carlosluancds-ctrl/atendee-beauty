# Atendee Beauty

**Repositório privado do produto Atendee Beauty.** Este repositório reúne as decisões de produto, arquitetura, segurança, documentação de continuidade e o Starter Kit técnico atual.

> **Estado em 17/11/2026:** baseline Starter Kit v7. Restore bloqueado, build Release e checks de domínio passaram localmente no SDK 10.0.404 (15/15); o runner de persistência compila. A integração real com PostgreSQL 18.7/RLS/pool e o workflow GitHub Actions ainda não foram executados porque a CLI GitHub não está autenticada. Este repositório não representa uma aplicação pronta para produção.

## Conteúdo

- `src/` — API, domínio, aplicação e infraestrutura.
- `tests/` — verificações de domínio e integração de persistência.
- `db/` — migração de laboratório, modelo de papéis e provas de conceito de RLS.
- `.github/workflows/postgres18-persistence.yml` — CI Linux para build e validação de isolamento em PostgreSQL 18.7 descartável.
- `docs/product/` — arquitetura, requisitos, papéis, permissões, modelo multiempresa, ameaças, testes e decisões do produto.
- `docs/technical/` — relatórios técnicos e instruções de validação do Starter Kit.
- `docs/continuity/` — instruções e registros para retomar o projeto em outra sessão ou ambiente.

## Baseline aprovada

- Plataforma multiempresa desde o início.
- Cada usuário operacional pertence a exatamente um salão na primeira versão.
- Perfis definidos: Administrador Atendee, Proprietário, Gerente, Recepcionista e Profissional.
- Separação de dados por tenant; leituras e escritas com escopo de tenant devem passar pela fronteira transacional documentada.
- Preferência de integração WhatsApp: API oficial WhatsApp Cloud API; implementação permanece pendente de validação do fluxo de onboarding e cobrança.

## Portão obrigatório antes de autenticação de produção

Executar o workflow **PostgreSQL 18 persistence integration** em GitHub Actions e revisar todos os resultados, sem `SKIP`:

1. `dotnet restore Atendee.sln --locked-mode`;
2. build Release;
3. verificações de domínio;
4. provisionamento efêmero de PostgreSQL 18.7 e papéis restritos;
5. testes EF Core/Npgsql de isolamento entre tenants e reutilização da conexão física após rollback.

Somente após todos os testes de runtime aprovados deve começar a implementação de autenticação/MFA e do contexto confiável de tenant em produção.

## Build local

Requer .NET SDK 10.0.404 (conforme `global.json`):

```bash
dotnet restore Atendee.sln --locked-mode
dotnet build Atendee.sln --no-restore --configuration Release
dotnet run --project tests/Atendee.Domain.Checks/Atendee.Domain.Checks.csproj --configuration Release
```

Para os testes de persistência, consulte `docs/technical/POSTGRES18_CI_EXECUTION_V1.md` e `docs/technical/POSTGRES18_TEST_HARNESS.md`. Use apenas banco descartável de laboratório; não aponte os testes para produção.

## Segurança

- Não armazene segredos, tokens, senhas, chaves ou arquivos `.env` neste repositório.
- Não publique esta aplicação na internet nem armazene dados reais de clientes enquanto os portões de liberação não estiverem aprovados.
- Não altere o Windows Server de produção para executar a validação; use o runner Linux do GitHub Actions.
- O material em `docs/product/DECISOES_COMERCIAIS.txt` é planejamento e precisa ser revalidado antes de oferta comercial.

## Licença

Nenhuma licença de distribuição foi definida. O repositório deve permanecer privado até decisão explícita do titular.
