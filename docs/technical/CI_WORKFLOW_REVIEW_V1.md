# Revisão do workflow PostgreSQL 18.7 — Marco V17

Data: 17/11/2026  
Base: backup de continuidade v16 / Starter Kit v7

## Escopo

Revisão estática do workflow de integração `.github/workflows/postgres18-persistence.yml`, dos scripts Bash de validação e do estado necessário para executar a CI. Esta revisão não altera o desenho de autenticação nem libera o portão de persistência.

## Verificações realizadas nesta sessão

- YAML do workflow carregado e interpretado com sucesso.
- Blocos Bash embutidos no workflow passaram em `bash -n`.
- `scripts/test-rls.sh` passou em `bash -n`.
- O workflow continua com os eventos `push`, `pull_request` e `workflow_dispatch`.
- O job mantém permissão mínima `contents: read`.
- A execução usa `actions/checkout@v7`, `actions/setup-dotnet@v6`, SDK `10.0.404` e imagem `postgres:18.7-bookworm`.
- A documentação oficial consultada em 17/11/2026 apresenta `actions/checkout@v7` e exemplos atuais com `actions/setup-dotnet@v6`.

## Limitações do ambiente atual

- `dotnet` não está instalado/disponível.
- `docker` não está instalado/disponível.
- Não há remote Git configurado na pasta de trabalho.
- Não houve conexão autenticada com o repositório GitHub nesta sessão.
- Portanto, não foi possível compilar v7, subir PostgreSQL, aplicar migrations, executar RLS ou confirmar a execução do workflow.

## Resultado

**REVISÃO ESTÁTICA APROVADA; CI E TESTES DE RUNTIME AINDA NÃO EXECUTADOS.**

As evidências históricas do v6 continuam restritas ao v6. Elas não podem ser usadas como prova de build/runtime do v7.

## Próximo passo obrigatório

1. Disponibilizar o Starter Kit v7 como raiz de um repositório GitHub privado (ou colocar o workflow na raiz do repositório existente).
2. Conectar o acesso GitHub autorizado para inspecionar o repositório e iniciar/revisar a execução.
3. Executar `PostgreSQL 18 persistence integration` em runner Linux.
4. Conferir restore em locked mode, build Release, domínio e persistência. O runner `Atendee.Persistence.Checks` não pode emitir `SKIP`; todas as verificações precisam reportar `PASS`.
5. Registrar URL/ID, data, resultado e logs relevantes do run.
6. Corrigir falhas antes de iniciar autenticação/MFA e o `ICurrentTenantContext` de produção.

## Portões e limites preservados

- Nenhuma alteração no Windows Server 2022.
- Não iniciar PostgreSQL 14 existente nem instalar PostgreSQL 18 no servidor.
- Não alterar Docker/WSL2, firewall, portas, serviços, volumes ou dados de produção.
- Não adicionar credenciais, tokens ou segredos ao ZIP.
- Não iniciar autenticação/MFA antes da aprovação do portão PostgreSQL 18.7/RLS/pool.

## Referências oficiais consultadas

- https://github.com/actions/checkout — documentação da action checkout v7.
- https://github.com/actions/setup-dotnet — documentação e releases da action setup-dotnet v6.
- https://github.com/actions/setup-dotnet/releases — release notes da linha v6.
