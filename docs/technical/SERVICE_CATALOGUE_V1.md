# Catálogo de serviços do salão — V1

## Escopo implementado
O primeiro corte cobre apenas o catálogo de serviços do salão:
- criar serviço;
- listar e consultar serviço;
- atualizar os campos;
- arquivar/desarquivar com `isActive`, sem hard delete.

Profissionais e associação profissional-serviço não fazem parte deste corte. O papel Professional não recebe acesso ao catálogo até existir uma relação de serviços executados por cada profissional, para não expor o catálogo inteiro.

## Dados e validação
- `service_id`: UUID gerado pelo servidor.
- `tenant_id`: obtido exclusivamente do contexto autenticado; nunca aceito do cliente.
- `name`: obrigatório, trim, máximo 120 caracteres, sem caracteres de controle.
- `duration_minutes`: inteiro entre 1 e 1440.
- `price_minor_units`: inteiro longo não negativo; nunca ponto flutuante.
- `currency`: código ISO 4217 explícito, validado no domínio; nenhum default comercial.
- `buffer_before_minutes` e `buffer_after_minutes`: inteiros entre 0 e 1440.
- `is_active`: booleano explícito.
- timestamps UTC de criação e atualização.

## API
- `GET /api/v1/services`: Owner, Manager e Receptionist, quando a política de status concede `ReadData`.
- `GET /api/v1/services/{id}`: mesma autorização; recurso fora do tenant retorna 404.
- `POST /api/v1/services`: Owner e Manager, quando a política de status concede `WriteData`.
- `PUT /api/v1/services/{id}`: Owner e Manager, mesma capacidade.
- Campos obrigatórios são enviados integralmente em POST/PUT; entrada incompleta ou inválida retorna erro de validação genérico.
- Não há endpoint DELETE. Arquivamento é atualização de `is_active`.

## Persistência e segurança
- Migration: `db/migrations/013_create_tenant_services.sql`.
- Chave primária composta `(tenant_id, service_id)`.
- Índice iniciado por tenant_id.
- RLS habilitada e FORCE RLS; policy compara `tenant_id` com `app.tenant_id` transaction-local.
- `TenantDbContext` captura o tenant uma única vez e valida propriedade em SaveChanges.
- Toda operação passa por `ITenantTransactionRunner`, que define `atendee_runtime`, tenant e ator dentro da transação.
- Runtime recebe SELECT/INSERT/UPDATE; DELETE/TRUNCATE são revogados.
- `tenant_audit_events` é append-only para runtime. Trigger SECURITY DEFINER registra `service.created`, `service.updated` e `service.archived` com ator e recurso. A mutação falha se não houver ator confiável no contexto transacional.

## Limites conhecidos
- Não há associação a profissionais, especialidades, disponibilidade ou agenda.
- Não há conversão entre moedas, cálculo de impostos, descontos ou arredondamento; valores são mantidos como unidades monetárias menores na moeda indicada.
- A API ainda não oferece paginação, filtros ou ordenação configurável; a lista tem ordenação estável por nome e UUID.
- Nenhum dashboard ou UI de catálogo foi implementado neste incremento.
