# PostgreSQL RLS proof of concept

**Status:** SQL de laboratório, ainda não executado. Nunca aplicar diretamente em produção.

## Requisitos

- PostgreSQL 18 em banco descartável.
- Permissão administrativa para criar papéis e schema.
- Ferramenta `psql`.

## Execução

1. Crie um banco vazio reservado a este teste.
2. Execute `001_create_rls_poc.sql` como administrador.
3. Execute `002_test_rls_isolation.sql` como administrador; o script usa `SET LOCAL ROLE atendee_poc_runtime` e reverte os dados com `ROLLBACK`.
4. O teste deve terminar com a mensagem `PASS` e sem exceções.
5. Descarte o banco de laboratório após registrar evidências.

## O que prova

- O papel runtime de laboratório não é superuser, não tem `BYPASSRLS` e não é dono da tabela.
- RLS está habilitado e forçado.
- Sem contexto de tenant, nenhuma linha é visível.
- O tenant A não consegue ler, atualizar, excluir ou inserir linhas do tenant B, mesmo conhecendo seus UUIDs.

## Limitações que não podem ser omitidas

Este exemplo usa uma variável de configuração customizada (`app.tenant_id`) como contexto de RLS. O PostgreSQL permite que uma sessão defina parâmetros customizados com `set_config`; portanto, um processo que tenha acesso SQL arbitrário com o papel runtime pode tentar alterar esse contexto. **Este POC não prova resistência a SQL injection nem a comprometimento da credencial de banco.**

A variável só pode ser definida pelo backend confiável, após validar a sessão do usuário e obter o tenant a partir do servidor. Nunca aceitar tenant ID do navegador. Consultas parametrizadas e ausência de execução de SQL arbitrário são requisitos. RLS é uma segunda barreira contra bugs de escopo, não substituto da segurança da aplicação. Para ameaças mais fortes, avaliar isolamento por papéis/credenciais por tenant ou uma estratégia de contexto validado criptograficamente, com revisão especializada.

Este teste também não prova isolamento de pool de conexões: isso exige testes de integração da aplicação que alternem tenants em conexões reutilizadas e validem transações/rollback.
